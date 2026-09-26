using CongresoApi.Config;
using CongresoApi.Data;
using CongresoApi.Dtos;
using CongresoApi.Models;
using CongresoApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CongresoApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EscaneoController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly QrService _qr;
    private readonly ILogger<EscaneoController> _logger;
    private readonly AuthOptions _auth;

    public EscaneoController(AppDbContext db, QrService qr, ILogger<EscaneoController> logger, IOptions<AuthOptions> auth)
    {
        _db = db;
        _qr = qr;
        _logger = logger;
        _auth = auth.Value;
    }

    [HttpPost]
    public async Task<ActionResult<EscaneoResponse>> Escanear([FromBody] EscaneoRequest request)
    {
        // La clave se valida también aquí, no solo en la pantalla de configuración del
        // frontend: así, aunque alguien intente llamar a la API directo sin pasar por
        // la app, sigue sin poder escanear sin conocer la clave del staff.
        if (!string.IsNullOrWhiteSpace(_auth.ClaveStaff) && request.Clave != _auth.ClaveStaff)
            return Unauthorized("Clave incorrecta.");

        // --- Paso 1: validar la firma del QR ---
        // Si esto falla, ni siquiera tocamos la base de datos: es un QR falsificado o corrupto.
        var (esValido, asistenteId) = _qr.ValidarContenidoQr(request.ContenidoQr);

        if (!esValido || asistenteId is null)
        {
            await RegistrarEscaneo(null, ResultadoEscaneo.FirmaInvalida, request);
            return Ok(new EscaneoResponse { Resultado = "FirmaInvalida" });
        }

        // --- Paso 2: buscar al asistente ---
        var asistente = await _db.Asistentes.FirstOrDefaultAsync(a => a.Id == asistenteId);
        if (asistente is null)
        {
            // Firma válida pero no existe en la base: pudo haber sido borrado, o es de otro evento.
            await RegistrarEscaneo(asistenteId, ResultadoEscaneo.NoExiste, request);
            return Ok(new EscaneoResponse { Resultado = "NoExiste" });
        }

        // --- Paso 3: marcar la entrega de forma ATÓMICA ---
        // ExecuteUpdateAsync genera un solo UPDATE en la base de datos con la condición
        // "EntregadoAt IS NULL" incluida en el WHERE. Si dos tablets escanean el mismo QR
        // al mismo tiempo, la base de datos garantiza que solo UNA de las dos consultas
        // afecte una fila (filasAfectadas == 1); la otra llega tarde y afecta 0 filas.
        // Esto evita el problema de "leer, decidir, escribir" en pasos separados, que sí
        // sería vulnerable a que ambas lean "disponible" antes de que la primera escriba.
        var ahora = DateTime.UtcNow;
        var filasAfectadas = await _db.Asistentes
            .Where(a => a.Id == asistenteId && a.EntregadoAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.EntregadoAt, ahora)
                .SetProperty(a => a.EntregadoPor, request.RealizadoPor)
                .SetProperty(a => a.Estacion, request.Estacion));

        if (filasAfectadas == 0)
        {
            // Alguien ya lo había marcado (aquí mismo, hace un instante, o antes).
            // Releemos el registro para mostrar CUÁNDO y DÓNDE se usó, útil para resolver reclamos en el momento.
            await _db.Entry(asistente).ReloadAsync();
            await RegistrarEscaneo(asistenteId, ResultadoEscaneo.Duplicado, request);

            return Ok(new EscaneoResponse
            {
                Resultado = "Duplicado",
                NombreCompleto = asistente.NombreCompleto,
                Origen = asistente.Origen.ToString(),
                Universidad = asistente.Universidad,
                Seccion = asistente.Seccion,
                Matricula = asistente.Matricula,
                EntregadoAtPrevio = asistente.EntregadoAt,
                EntregadoPorPrevio = asistente.EntregadoPor,
                EstacionPrevia = asistente.Estacion
            });
        }

        // --- Paso 4: éxito ---
        await RegistrarEscaneo(asistenteId, ResultadoEscaneo.Ok, request);

        return Ok(new EscaneoResponse
        {
            Resultado = "Ok",
            NombreCompleto = asistente.NombreCompleto,
            Origen = asistente.Origen.ToString(),
            Universidad = asistente.Universidad,
            Seccion = asistente.Seccion,
            Matricula = asistente.Matricula
        });
    }

    /// <summary>
    /// Marca la entrada de alguien que llegó SIN QR (se le olvidó, no le llegó el correo, etc.),
    /// encontrado por búsqueda manual desde el admin. Usa la misma condición atómica que el
    /// escaneo normal, para que tampoco se pueda marcar dos veces por accidente.
    /// </summary>
    [HttpPost("{asistenteId:guid}/manual")]
    public async Task<IActionResult> MarcarEntradaManual(Guid asistenteId, [FromBody] DeshacerRequest request)
    {
        if (!ValidarClaveAdmin(out var errorAdmin)) return errorAdmin!;

        var asistente = await _db.Asistentes.FirstOrDefaultAsync(a => a.Id == asistenteId);
        if (asistente is null) return NotFound("No existe un asistente con ese Id.");

        var ahora = DateTime.UtcNow;
        var filasAfectadas = await _db.Asistentes
            .Where(a => a.Id == asistenteId && a.EntregadoAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.EntregadoAt, ahora)
                .SetProperty(a => a.EntregadoPor, request.RealizadoPor)
                .SetProperty(a => a.Estacion, "Manual (admin)"));

        if (filasAfectadas == 0)
            return Conflict("Esta persona ya tenía una entrada registrada.");

        _db.Escaneos.Add(new Escaneo
        {
            AsistenteId = asistenteId,
            Resultado = ResultadoEscaneo.ManualSinRegistro,
            RealizadoPor = request.RealizadoPor,
            Estacion = "Manual (admin)",
            Motivo = request.Motivo
        });
        await _db.SaveChangesAsync();

        return Ok(new { mensaje = "Entrada manual registrada.", asistenteId });
    }

    /// <summary>
    /// Deshace un check-in por error (ej. una prueba, o un escaneo equivocado).
    /// Pensado para el panel de admin, NO para la app de escaneo del staff.
    /// Deja constancia en la bitácora de quién lo deshizo y por qué.
    /// </summary>
    [HttpPost("{asistenteId:guid}/deshacer")]
    public async Task<IActionResult> DeshacerEntrega(Guid asistenteId, [FromBody] DeshacerRequest request)
    {
        if (!ValidarClaveAdmin(out var errorAdmin)) return errorAdmin!;

        var asistente = await _db.Asistentes.FirstOrDefaultAsync(a => a.Id == asistenteId);
        if (asistente is null) return NotFound("No existe un asistente con ese Id.");

        if (asistente.EntregadoAt is null)
            return BadRequest("Este asistente no tiene una entrega marcada, no hay nada que deshacer.");

        asistente.EntregadoAt = null;
        asistente.EntregadoPor = null;
        asistente.Estacion = null;

        _db.Escaneos.Add(new Escaneo
        {
            AsistenteId = asistenteId,
            Resultado = ResultadoEscaneo.ExcepcionSupervisor,
            RealizadoPor = request.RealizadoPor,
            Motivo = $"Entrega deshecha desde el panel de admin. Motivo: {request.Motivo}"
        });

        await _db.SaveChangesAsync();
        return Ok(new { mensaje = "Entrega deshecha correctamente.", asistenteId });
    }

    private bool ValidarClaveAdmin(out IActionResult? error)
    {
        if (string.IsNullOrWhiteSpace(_auth.ClaveAdmin))
        {
            error = null;
            return true; // sin clave configurada, no se exige (solo para desarrollo local)
        }

        var recibida = Request.Headers["X-Admin-Key"].FirstOrDefault()
            ?? Request.Query["adminKey"].FirstOrDefault();

        if (recibida != _auth.ClaveAdmin)
        {
            error = Unauthorized("Clave de administrador incorrecta o faltante.");
            return false;
        }

        error = null;
        return true;
    }

    private async Task RegistrarEscaneo(Guid? asistenteId, ResultadoEscaneo resultado, EscaneoRequest request)
    {
        _db.Escaneos.Add(new Escaneo
        {
            AsistenteId = asistenteId,
            Resultado = resultado,
            Estacion = request.Estacion,
            RealizadoPor = request.RealizadoPor
        });
        await _db.SaveChangesAsync();
    }
}