using System.IO.Compression;
using CongresoApi.Data;
using CongresoApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CongresoApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QrController : ControllerBase
{
    private readonly QrService _qr;
    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly ILogger<QrController> _logger;

    public QrController(QrService qr, AppDbContext db, EmailService email, ILogger<QrController> logger)
    {
        _qr = qr;
        _db = db;
        _email = email;
        _logger = logger;
    }

    /// <summary>
    /// Envía el QR de UN asistente por correo. Si se pasa destinoPrueba, se manda ahí
    /// en vez de al correo real registrado (para probar sin afectar datos reales),
    /// y en ese caso NO se marca como enviado.
    /// Ejemplo: POST /api/qr/enviar/{id}?destinoPrueba=tucorreo@gmail.com
    /// </summary>
    [HttpPost("enviar/{asistenteId:guid}")]
    public async Task<IActionResult> EnviarUno(Guid asistenteId, [FromQuery] string? destinoPrueba)
    {
        var asistente = await _db.Asistentes.FirstOrDefaultAsync(a => a.Id == asistenteId);
        if (asistente is null) return NotFound("No existe un asistente con ese Id.");

        try
        {
            await _email.EnviarQrAsync(asistente, destinoPrueba);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando QR al asistente {Id}", asistenteId);
            return StatusCode(500, $"No se pudo enviar el correo: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(destinoPrueba))
        {
            asistente.QrEnviadoAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return Ok(new { mensaje = "Correo enviado.", enviadoA = destinoPrueba ?? asistente.Correo, marcadoComoEnviado = string.IsNullOrWhiteSpace(destinoPrueba) });
    }

    /// <summary>
    /// Envía el QR a TODOS los asistentes que todavía no lo han recibido (QrEnviadoAt == null).
    /// Se puede correr las veces que quieras: solo le manda a quien le falte, así que
    /// sirve igual para el envío inicial que para los que se registren después.
    /// Manda uno por uno con una pequeña pausa, para no saturar los límites de Gmail.
    /// </summary>
    /// <summary>
    /// Envía el QR a los asistentes que todavía no lo han recibido (QrEnviadoAt == null),
    /// EN LOTES (por defecto 25 a la vez). Mandar cientos de correos en una sola petición
    /// HTTP tarda varios minutos (por la pausa entre envíos) y el navegador/proxy corta
    /// la conexión antes de terminar — por eso se procesa en tandas cortas. El panel
    /// llama a este endpoint repetidamente hasta que ya no queden pendientes.
    /// </summary>
    [HttpPost("enviar-pendientes")]
    public async Task<IActionResult> EnviarPendientes([FromQuery] int limite = 10)
    {
        var pendientes = await _db.Asistentes
            .Where(a => a.QrEnviadoAt == null && a.Correo != "")
            .OrderBy(a => a.Id)
            .Take(limite)
            .ToListAsync();

        var totalPendientesReal = await _db.Asistentes
            .CountAsync(a => a.QrEnviadoAt == null && a.Correo != "");

        var enviados = 0;
        var fallidos = new List<string>();

        foreach (var asistente in pendientes)
        {
            try
            {
                await _email.EnviarQrAsync(asistente);
                asistente.QrEnviadoAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                enviados++;
                _logger.LogInformation("Correo enviado a {Correo} ({Enviados}/{Limite} de este lote)", asistente.Correo, enviados, limite);
                await Task.Delay(2500);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallo al enviar QR a {Correo}", asistente.Correo);
                fallidos.Add(asistente.Correo);
            }
        }

        var quedanPendientes = totalPendientesReal - enviados;

        return Ok(new
        {
            totalPendientesAlEmpezar = totalPendientesReal,
            enviados,
            fallidos,
            quedanPendientes
        });
    }

    /// <summary>
    /// Genera TODOS los QR de golpe, en un .zip descargable, uno por asistente.
    /// Cada archivo se nombra con matrícula o correo + nombre, para ubicarlos fácil.
    /// Pensado para el botón "Descargar todos los QR" del panel de admin.
    /// </summary>
    [HttpGet("exportar-todos")]
    public async Task<IActionResult> ExportarTodos()
    {
        var asistentes = await _db.Asistentes
            .OrderBy(a => a.Origen)
            .ThenBy(a => a.Nombre)
            .ToListAsync();

        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            var nombresUsados = new HashSet<string>();

            foreach (var a in asistentes)
            {
                var png = _qr.GenerarImagenQrPng(a.Id);

                var identificador = !string.IsNullOrWhiteSpace(a.Matricula) ? a.Matricula : a.Correo;
                var nombreArchivo = SanitizarNombreArchivo($"{identificador}_{a.NombreCompleto}");

                // Por si dos personas terminaran generando el mismo nombre de archivo sanitizado
                var nombreFinal = nombreArchivo;
                var contador = 2;
                while (!nombresUsados.Add(nombreFinal))
                {
                    nombreFinal = $"{nombreArchivo}_{contador++}";
                }

                var entrada = zip.CreateEntry($"{a.Origen}/{nombreFinal}.png", CompressionLevel.Fastest);
                await using var entradaStream = entrada.Open();
                await entradaStream.WriteAsync(png);
            }
        }

        memoria.Position = 0;
        return File(memoria.ToArray(), "application/zip", $"qrs_congreso_{DateTime.Now:yyyyMMdd_HHmm}.zip");
    }

    private static string SanitizarNombreArchivo(string texto)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var limpio = new string(texto.Where(c => !invalidos.Contains(c)).ToArray());
        return limpio.Replace(" ", "_").Trim('_');
    }

    /// <summary>
    /// Regresa la imagen PNG del QR de un asistente, lista para mostrarse o descargarse.
    /// Ejemplo: GET /api/qr/imagen/{id} — se puede pegar directo en el src de una etiqueta img.
    /// </summary>
    [HttpGet("imagen/{asistenteId:guid}")]
    public async Task<IActionResult> Imagen(Guid asistenteId)
    {
        var existe = await _db.Asistentes.AnyAsync(a => a.Id == asistenteId);
        if (!existe) return NotFound("No existe un asistente con ese Id.");

        var png = _qr.GenerarImagenQrPng(asistenteId);
        return File(png, "image/png");
    }

    /// <summary>
    /// DIAGNÓSTICO: genera el contenido del QR para un asistente ya importado.
    /// Ejemplo: GET /api/qr/generar/{id}  (usa un Id real de tu tabla Asistentes)
    /// </summary>
    [HttpGet("generar/{asistenteId:guid}")]
    public async Task<IActionResult> Generar(Guid asistenteId)
    {
        var existe = await _db.Asistentes.AnyAsync(a => a.Id == asistenteId);
        if (!existe) return NotFound("No existe un asistente con ese Id.");

        var contenido = _qr.GenerarContenidoQr(asistenteId);
        return Ok(new { asistenteId, contenidoQr = contenido });
    }

    /// <summary>
    /// DIAGNÓSTICO: valida un contenido de QR (simula lo que hará el escaneo real).
    /// Ejemplo: GET /api/qr/validar?contenido=abc123...def.xyz==
    /// </summary>
    [HttpGet("validar")]
    public IActionResult Validar([FromQuery] string contenido)
    {
        var (esValido, asistenteId) = _qr.ValidarContenidoQr(contenido);
        return Ok(new { esValido, asistenteId });
    }
}