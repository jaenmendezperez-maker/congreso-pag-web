using System.Globalization;
using CongresoApi.Config;
using CongresoApi.Data;
using CongresoApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CongresoApi.Services;

public class SincronizacionService
{
    private readonly GoogleSheetsClient _sheets;
    private readonly AppDbContext _db;
    private readonly GoogleSheetsOptions _options;

    public SincronizacionService(GoogleSheetsClient sheets, AppDbContext db, IOptions<GoogleSheetsOptions> options)
    {
        _sheets = sheets;
        _db = db;
        _options = options.Value;
    }

    public async Task<SincronizacionResultado> SincronizarTodoAsync()
    {
        var resultado = new SincronizacionResultado();
        resultado.Combinar(await SincronizarUsepAsync());
        resultado.Combinar(await SincronizarExtranjerosAsync());
        return resultado;
    }

    // ---------------------------------------------------------------
    // USEP — llave: Matrícula
    // Columnas: Marca temporal | Nombre completo | Matrícula | Sección
    //           | Correo institucional | Correo electrónico
    // ---------------------------------------------------------------
    public async Task<SincronizacionResultado> SincronizarUsepAsync()
    {
        var resultado = new SincronizacionResultado();
        var filas = await _sheets.LeerHojaAsync(_options.SpreadsheetIdUsep, _options.HojaUsep);
        if (filas.Count == 0) return resultado;

        var mapa = new HeaderMapper(filas[0]);
        var idxMarca = mapa.Requerido("Marca temporal");
        var idxNombre = mapa.Requerido("Nombre completo");
        var idxMatricula = mapa.Requerido("Matr");
        var idxSeccion = mapa.Opcional("Secci");
        var idxCorreoInst = mapa.Opcional("Escriba su correo institucional");
        var idxCorreoPersonal = mapa.Opcional("Correo electr");

        // Cargar existentes por matrícula para minimizar consultas fila por fila
        var existentesPorMatricula = await _db.Asistentes
            .Where(a => a.Origen == Origen.Usep && a.Matricula != null)
            .ToDictionaryAsync(a => a.Matricula!);

        for (var i = 1; i < filas.Count; i++)
        {
            var fila = filas[i];
            var numeroFilaHumano = i + 1; // +1 porque los sheets son 1-indexados y la fila 1 es encabezado

            var matricula = NormalizarMatricula(Leer(fila, idxMatricula));
            var nombre = Leer(fila, idxNombre);
            var correoInst = NormalizarCorreo(Leer(fila, idxCorreoInst));
            var correoPersonal = NormalizarCorreo(Leer(fila, idxCorreoPersonal));

            if (string.IsNullOrWhiteSpace(matricula) || string.IsNullOrWhiteSpace(nombre))
            {
                resultado.FilasIgnoradas.Add($"USEP fila {numeroFilaHumano}: falta matrícula o nombre.");
                continue;
            }

            // Preferimos el correo personal si existe; si no, el institucional
            var correo = !string.IsNullOrWhiteSpace(correoPersonal) ? correoPersonal : correoInst;
            if (string.IsNullOrWhiteSpace(correo))
            {
                resultado.FilasIgnoradas.Add($"USEP fila {numeroFilaHumano}: sin correo (matrícula {matricula}).");
                continue;
            }

            // Conflicto: ese correo ya existe pero pertenece a otra matrícula/persona
            var conflictoCorreo = await _db.Asistentes
                .Where(a => a.Correo == correo && a.Matricula != matricula)
                .Select(a => a.Matricula ?? a.Correo)
                .FirstOrDefaultAsync();
            if (conflictoCorreo != null)
            {
                resultado.Conflictos.Add(
                    $"USEP fila {numeroFilaHumano}: correo '{correo}' ya está usado por otro registro ({conflictoCorreo}). Se omitió esta fila.");
                continue;
            }

            var fechaRegistro = ParsearFecha(Leer(fila, idxMarca));
            var seccion = idxSeccion.HasValue ? Leer(fila, idxSeccion.Value) : null;

            if (existentesPorMatricula.TryGetValue(matricula, out var existente))
            {
                var cambio = false;
                cambio |= ActualizarSiCambio(() => existente.Nombre, v => existente.Nombre = v, nombre, "Nombre(USEP)", resultado.DiagnosticoCambios);
                cambio |= ActualizarSiCambio(() => existente.Correo, v => existente.Correo = v, correo, "Correo(USEP)", resultado.DiagnosticoCambios);
                cambio |= ActualizarSiCambio(() => existente.CorreoAlterno, v => existente.CorreoAlterno = v, correoInst == correo ? correoPersonal : correoInst, "CorreoAlterno(USEP)", resultado.DiagnosticoCambios);
                cambio |= ActualizarSiCambio(() => existente.Seccion, v => existente.Seccion = v, seccion, "Seccion(USEP)", resultado.DiagnosticoCambios);

                if (cambio)
                {
                    existente.ActualizadoEnSistema = DateTime.UtcNow;
                    resultado.Actualizados++;
                }
                else
                {
                    resultado.SinCambios++;
                }
                // Importante: EntregadoAt / EntregadoPor / Estacion NUNCA se tocan aquí.
            }
            else
            {
                var nuevo = new Asistente
                {
                    Origen = Origen.Usep,
                    Matricula = matricula,
                    Correo = correo,
                    CorreoAlterno = correoInst == correo ? correoPersonal : correoInst,
                    Nombre = nombre,
                    Seccion = seccion,
                    FechaRegistro = fechaRegistro
                };
                _db.Asistentes.Add(nuevo);
                existentesPorMatricula[matricula] = nuevo;
                resultado.Nuevos++;
            }
        }

        await _db.SaveChangesAsync();
        return resultado;
    }

    // ---------------------------------------------------------------
    // Extranjeros — llave real: ClaveExterna = correo + nombre completo
    // (el correo solo no basta: varias personas a veces comparten uno,
    // típicamente cuando un profesor registra a su grupo con su propio correo)
    // Columnas: Marca temporal | Correo | Correo alterno | Nombre(s)
    //           | Apellido Paterno | Apellido Materno | Estado o País
    //           | Universidad | Si seleccionó otra institución especifique
    // ---------------------------------------------------------------
    public async Task<SincronizacionResultado> SincronizarExtranjerosAsync()
    {
        var resultado = new SincronizacionResultado();
        var filas = await _sheets.LeerHojaAsync(_options.SpreadsheetIdExtranjeros, _options.HojaExtranjeros);
        if (filas.Count == 0) return resultado;

        var mapa = new HeaderMapper(filas[0]);
        var idxMarca = mapa.Requerido("Marca temporal");
        var idxCorreo = mapa.Requerido("Direcci");
        var idxCorreoAlterno = mapa.Opcional("Correo alternativo");
        var idxNombres = mapa.Requerido("Nombre(s)");
        var idxApPaterno = mapa.Opcional("Apellido Paterno");
        var idxApMaterno = mapa.Opcional("Apellido Materno");
        var idxEstadoPais = mapa.Opcional("Estado o Pa");
        var idxUniversidad = mapa.Opcional("Universidad a la que");
        var idxUniversidadOtra = mapa.Opcional("Si seleccion");

        var existentesPorClave = await _db.Asistentes
            .Where(a => a.Origen == Origen.Extranjero && a.ClaveExterna != null)
            .ToDictionaryAsync(a => a.ClaveExterna!);

        // --- Paso previo: cuando la misma persona llenó el formulario más de una vez,
        // nos quedamos solo con su fila más reciente (por Marca temporal), descartando
        // las anteriores. Sin esto, cada duplicado se aplicaría en el orden en que aparece
        // en el Sheet (no necesariamente cronológico), generando resultados inconsistentes
        // entre sincronizaciones.
        var filasPorClave = new Dictionary<string, (int numeroFila, IList<object> fila, DateTime fecha)>();
        for (var i = 1; i < filas.Count; i++)
        {
            var fila = filas[i];
            var correoTmp = NormalizarCorreo(Leer(fila, idxCorreo));
            var nombreTmp = Leer(fila, idxNombres);
            if (string.IsNullOrWhiteSpace(correoTmp) || string.IsNullOrWhiteSpace(nombreTmp)) continue;

            var apPatTmp = idxApPaterno.HasValue ? Leer(fila, idxApPaterno.Value) : null;
            var apMatTmp = idxApMaterno.HasValue ? Leer(fila, idxApMaterno.Value) : null;
            var nombreCompletoTmp = string.Join(" ", new[] { nombreTmp, apPatTmp, apMatTmp }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var claveTmp = ConstruirClaveExterna(correoTmp, nombreCompletoTmp);
            var fechaTmp = ParsearFecha(Leer(fila, idxMarca));

            if (!filasPorClave.TryGetValue(claveTmp, out var actualGuardada) || fechaTmp > actualGuardada.fecha)
            {
                filasPorClave[claveTmp] = (i + 1, fila, fechaTmp);
            }
        }

        foreach (var (claveExterna, (numeroFilaHumano, fila, _)) in filasPorClave)
        {
            var correo = NormalizarCorreo(Leer(fila, idxCorreo));
            var nombre = Leer(fila, idxNombres);
            var apellidoPaternoFila = idxApPaterno.HasValue ? Leer(fila, idxApPaterno.Value) : null;
            var apellidoMaternoFila = idxApMaterno.HasValue ? Leer(fila, idxApMaterno.Value) : null;

            if (string.IsNullOrWhiteSpace(correo) || string.IsNullOrWhiteSpace(nombre))
            {
                resultado.FilasIgnoradas.Add($"Extranjeros fila {numeroFilaHumano}: falta correo o nombre.");
                continue;
            }

            var nombreCompletoFila = string.Join(" ", new[] { nombre, apellidoPaternoFila, apellidoMaternoFila }.Where(s => !string.IsNullOrWhiteSpace(s)));

            var conflictoMatricula = await _db.Asistentes
                .Where(a => a.Correo == correo && a.Origen != Origen.Extranjero)
                .Select(a => a.Matricula ?? a.Correo)
                .FirstOrDefaultAsync();
            if (conflictoMatricula != null)
            {
                resultado.Conflictos.Add(
                    $"Extranjeros fila {numeroFilaHumano}: correo '{correo}' ya registrado del lado USEP ({conflictoMatricula}). Se omitió esta fila.");
                continue;
            }

            var universidadOtra = idxUniversidadOtra.HasValue ? Leer(fila, idxUniversidadOtra.Value) : null;
            var universidad = idxUniversidad.HasValue ? Leer(fila, idxUniversidad.Value) : null;
            var universidadFinal = !string.IsNullOrWhiteSpace(universidadOtra) ? universidadOtra : universidad;

            var fechaRegistro = ParsearFecha(Leer(fila, idxMarca));

            if (existentesPorClave.TryGetValue(claveExterna, out var existente))
            {
                var cambio = false;
                cambio |= ActualizarSiCambio(() => existente.CorreoAlterno, v => existente.CorreoAlterno = v, idxCorreoAlterno.HasValue ? NormalizarCorreo(Leer(fila, idxCorreoAlterno.Value)) : null, "CorreoAlterno", resultado.DiagnosticoCambios);
                cambio |= ActualizarSiCambio(() => existente.EstadoPais, v => existente.EstadoPais = v, idxEstadoPais.HasValue ? Leer(fila, idxEstadoPais.Value) : null, "EstadoPais", resultado.DiagnosticoCambios);
                cambio |= ActualizarSiCambio(() => existente.Universidad, v => existente.Universidad = v, universidadFinal, "Universidad", resultado.DiagnosticoCambios);
                // Nombre/apellidos NO se comparan aquí: son parte de la llave (ClaveExterna).
                // Si cambiaran, esta fila ya habría generado una ClaveExterna distinta y sería
                // tratada como una persona "nueva" en vez de una actualización — es lo correcto,
                // porque no podemos saber si es una corrección de nombre o alguien más.

                if (cambio)
                {
                    existente.ActualizadoEnSistema = DateTime.UtcNow;
                    resultado.Actualizados++;
                }
                else
                {
                    resultado.SinCambios++;
                }
            }
            else
            {
                var nuevo = new Asistente
                {
                    Origen = Origen.Extranjero,
                    Correo = correo,
                    ClaveExterna = claveExterna,
                    CorreoAlterno = idxCorreoAlterno.HasValue ? NormalizarCorreo(Leer(fila, idxCorreoAlterno.Value)) : null,
                    Nombre = nombre,
                    ApellidoPaterno = apellidoPaternoFila,
                    ApellidoMaterno = apellidoMaternoFila,
                    EstadoPais = idxEstadoPais.HasValue ? Leer(fila, idxEstadoPais.Value) : null,
                    Universidad = universidadFinal,
                    FechaRegistro = fechaRegistro
                };
                _db.Asistentes.Add(nuevo);
                existentesPorClave[claveExterna] = nuevo;
                resultado.Nuevos++;
            }
        }

        await _db.SaveChangesAsync();
        return resultado;
    }

    /// <summary>
    /// Construye la llave real de identidad para extranjeros: correo + nombre completo,
    /// normalizados. Dos filas con el mismo correo pero nombres distintos generan claves
    /// distintas (personas distintas, ej. un profesor registrando a su grupo); la misma
    /// persona resubmitiendo el formulario con el mismo nombre y correo genera la misma
    /// clave, así que se trata como una actualización en vez de un registro nuevo.
    /// </summary>
    private static string ConstruirClaveExterna(string correo, string nombreCompleto)
    {
        var nombreNormalizado = nombreCompleto
            .Trim()
            .ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormC);
        return $"{correo}|{nombreNormalizado}";
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static string? Leer(IList<object> fila, int? indice)
    {
        if (indice is null || indice.Value >= fila.Count) return null;
        var valor = fila[indice.Value]?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(valor)) return null;

        // Normalizamos Unicode desde el origen (ver nota en ActualizarSiCambio) para que
        // el mismo texto siempre se compare y guarde de forma consistente.
        return valor.Normalize(System.Text.NormalizationForm.FormC);
    }

    private static string? NormalizarCorreo(string? correo) =>
        string.IsNullOrWhiteSpace(correo) ? null : correo.Trim().ToLowerInvariant();

    private static string? NormalizarMatricula(string? matricula) =>
        string.IsNullOrWhiteSpace(matricula) ? null : matricula.Trim().ToUpperInvariant().Replace(" ", "");

    private static DateTime ParsearFecha(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return DateTime.UtcNow;

        // Google Sheets exporta la marca temporal como texto tipo "9/24/2026 10:23:00"
        if (DateTime.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
        {
            // PostgreSQL exige que las fechas tengan Kind=Utc explícito para "timestamp with time zone".
            // La marca temporal del formulario no trae zona horaria, así que la tratamos como si ya fuera UTC.
            return DateTime.SpecifyKind(fecha, DateTimeKind.Utc);
        }

        return DateTime.UtcNow;
    }

    /// <summary>Compara el valor actual contra el nuevo; si difiere, lo asigna y regresa true.</summary>
    private static bool ActualizarSiCambio(
        Func<string?> obtener, Action<string?> asignar, string? nuevoValor,
        string? nombreCampoDiagnostico = null, List<string>? diagnostico = null)
    {
        var actual = obtener();

        var actualNormalizado = actual?.Normalize(System.Text.NormalizationForm.FormC);
        var nuevoNormalizado = nuevoValor?.Normalize(System.Text.NormalizationForm.FormC);

        if (string.Equals(actualNormalizado, nuevoNormalizado, StringComparison.Ordinal)) return false;
        if (string.IsNullOrWhiteSpace(actualNormalizado) && string.IsNullOrWhiteSpace(nuevoNormalizado)) return false;

        if (diagnostico != null && diagnostico.Count < 20)
        {
            diagnostico.Add($"Campo '{nombreCampoDiagnostico}': antes=[{actual}] (len={actual?.Length}) -> nuevo=[{nuevoValor}] (len={nuevoValor?.Length})");
        }

        asignar(nuevoValor);
        return true;
    }
}