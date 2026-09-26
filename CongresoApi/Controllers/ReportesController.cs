using ClosedXML.Excel;
using CongresoApi.Data;
using CongresoApi.Dtos;
using CongresoApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CongresoApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportesController : ControllerBase
{
    private readonly AppDbContext _db;

    public ReportesController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Busca asistentes por nombre, matrícula o correo (coincidencia parcial).
    /// Pensado para el buscador del panel de admin y para casos de entrada manual
    /// (alguien sin QR a la mano).
    /// </summary>
    [HttpGet("buscar")]
    public async Task<IActionResult> Buscar([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(Array.Empty<object>());

        var termino = q.Trim().ToLowerInvariant();

        var resultados = await _db.Asistentes
            .Where(a =>
                EF.Functions.ILike(a.Nombre, $"%{termino}%") ||
                (a.ApellidoPaterno != null && EF.Functions.ILike(a.ApellidoPaterno, $"%{termino}%")) ||
                (a.ApellidoMaterno != null && EF.Functions.ILike(a.ApellidoMaterno, $"%{termino}%")) ||
                (a.Matricula != null && EF.Functions.ILike(a.Matricula, $"%{termino}%")) ||
                EF.Functions.ILike(a.Correo, $"%{termino}%"))
            .OrderBy(a => a.Nombre)
            .Take(20)
            .ToListAsync();

        var respuesta = resultados.Select(a => new
        {
            a.Id,
            NombreCompleto = a.NombreCompleto,
            Origen = a.Origen.ToString(),
            a.Matricula,
            a.Universidad,
            a.Seccion,
            YaEntregado = a.YaEntregado,
            a.EntregadoAt,
            a.EntregadoPor,
            a.Estacion
        });

        return Ok(respuesta);
    }

    /// <summary>
    /// Contadores en vivo para el panel de admin: cuántos hay y cuántos ya entraron,
    /// separado por USEP y extranjeros, más los últimos escaneos exitosos para ver actividad.
    /// </summary>
    [HttpGet("resumen")]
    public async Task<ActionResult<ResumenResponse>> Resumen()
    {
        var porOrigen = await _db.Asistentes
            .GroupBy(a => a.Origen)
            .Select(g => new
            {
                Origen = g.Key,
                Total = g.Count(),
                Entregados = g.Count(a => a.EntregadoAt != null)
            })
            .ToListAsync();

        var usep = porOrigen.FirstOrDefault(x => x.Origen == Origen.Usep);
        var extranjeros = porOrigen.FirstOrDefault(x => x.Origen == Origen.Extranjero);

        var ultimosEscaneosCrudos = await _db.Escaneos
            .Where(e => e.Resultado == ResultadoEscaneo.Ok)
            .OrderByDescending(e => e.CreadoEn)
            .Take(10)
            .Select(e => new
            {
                e.Asistente,
                e.CreadoEn,
                e.Estacion,
                e.RealizadoPor
            })
            .ToListAsync();

        // NombreCompleto es una propiedad calculada en C# (no mapeada a una columna),
        // así que se arma aquí en memoria, después de traer los datos crudos.
        var ultimosEscaneos = ultimosEscaneosCrudos.Select(e => new UltimoEscaneoDto
        {
            NombreCompleto = e.Asistente?.NombreCompleto,
            Origen = e.Asistente?.Origen.ToString(),
            FechaHora = e.CreadoEn,
            Estacion = e.Estacion,
            RealizadoPor = e.RealizadoPor
        }).ToList();

        return Ok(new ResumenResponse
        {
            Usep = new ResumenGrupo { Total = usep?.Total ?? 0, Entregados = usep?.Entregados ?? 0 },
            Extranjeros = new ResumenGrupo { Total = extranjeros?.Total ?? 0, Entregados = extranjeros?.Entregados ?? 0 },
            UltimosEscaneos = ultimosEscaneos
        });
    }

    /// <summary>
    /// Exporta la tabla completa de asistentes a un archivo .xlsx, con una columna
    /// de "Entregado" (Sí/No) y la hora de entrega. Descarga directa.
    /// </summary>
    [HttpGet("exportar")]
    public async Task<IActionResult> Exportar()
    {
        var asistentes = await _db.Asistentes
            .OrderBy(a => a.Origen)
            .ThenBy(a => a.Nombre)
            .ToListAsync();

        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add("Asistentes");

        // Encabezados
        string[] columnas =
        {
            "Origen", "Nombre completo", "Matrícula", "Correo", "Universidad",
            "Sección", "Estado/País", "Entregado", "Fecha entrega", "Estación", "Entregado por"
        };
        for (var i = 0; i < columnas.Length; i++)
        {
            var celda = hoja.Cell(1, i + 1);
            celda.Value = columnas[i];
            celda.Style.Font.Bold = true;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#4472C4");
            celda.Style.Font.FontColor = XLColor.White;
        }

        // Filas de datos
        var fila = 2;
        foreach (var a in asistentes)
        {
            hoja.Cell(fila, 1).Value = a.Origen.ToString();
            hoja.Cell(fila, 2).Value = a.NombreCompleto;
            hoja.Cell(fila, 3).Value = a.Matricula ?? "";
            hoja.Cell(fila, 4).Value = a.Correo;
            hoja.Cell(fila, 5).Value = a.Universidad ?? "";
            hoja.Cell(fila, 6).Value = a.Seccion ?? "";
            hoja.Cell(fila, 7).Value = a.EstadoPais ?? "";
            hoja.Cell(fila, 8).Value = a.YaEntregado ? "Sí" : "No";
            if (a.EntregadoAt.HasValue)
                hoja.Cell(fila, 9).Value = a.EntregadoAt.Value.ToLocalTime();
            hoja.Cell(fila, 10).Value = a.Estacion ?? "";
            hoja.Cell(fila, 11).Value = a.EntregadoPor ?? "";
            fila++;
        }

        hoja.Columns().AdjustToContents();
        hoja.SheetView.FreezeRows(1); // encabezado siempre visible al hacer scroll

        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        stream.Position = 0;

        var nombreArchivo = $"asistentes_congreso_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            nombreArchivo);
    }
}