using CongresoApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace CongresoApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly SincronizacionService _sincronizacion;
    private readonly GoogleSheetsClient _sheetsClient;

    public SyncController(SincronizacionService sincronizacion, GoogleSheetsClient sheetsClient)
    {
        _sincronizacion = sincronizacion;
        _sheetsClient = sheetsClient;
    }

    /// <summary>
    /// DIAGNÓSTICO: regresa los nombres reales de las pestañas de un spreadsheet.
    /// Úsalo para confirmar el nombre exacto que debe ir en HojaUsep / HojaExtranjeros.
    /// Ejemplo: GET /api/sync/hojas?spreadsheetId=TU_ID_AQUI
    /// </summary>
    [HttpGet("hojas")]
    public async Task<IActionResult> ListarHojas([FromQuery] string spreadsheetId)
    {
        var nombres = await _sheetsClient.ListarNombresDeHojasAsync(spreadsheetId);
        return Ok(nombres);
    }

    /// <summary>Sincroniza solo la hoja de USEP. Útil para probar esa conexión de forma aislada.</summary>
    [HttpPost("usep")]
    public async Task<IActionResult> SincronizarUsep()
    {
        var resultado = await _sincronizacion.SincronizarUsepAsync();
        return Ok(resultado);
    }

    /// <summary>Sincroniza solo la hoja de extranjeros.</summary>
    [HttpPost("extranjeros")]
    public async Task<IActionResult> SincronizarExtranjeros()
    {
        var resultado = await _sincronizacion.SincronizarExtranjerosAsync();
        return Ok(resultado);
    }

    /// <summary>Sincroniza ambas hojas. Este es el que usará el botón "Sincronizar" del panel de admin.</summary>
    [HttpPost("todo")]
    public async Task<IActionResult> SincronizarTodo()
    {
        var resultado = await _sincronizacion.SincronizarTodoAsync();
        return Ok(resultado);
    }
}