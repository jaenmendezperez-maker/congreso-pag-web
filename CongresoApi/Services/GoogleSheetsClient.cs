using CongresoApi.Config;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Options;

namespace CongresoApi.Services;

public class GoogleSheetsClient
{
    private readonly SheetsService _service;
    private readonly GoogleSheetsOptions _options;

    public GoogleSheetsClient(IOptions<GoogleSheetsOptions> options)
    {
        _options = options.Value;

        var credential = GoogleCredential
            .FromFile(_options.CredencialesPath)
            .CreateScoped(SheetsService.Scope.Spreadsheets);

        _service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "CongresoAcceso"
        });
    }

    /// <summary>Diagnóstico: regresa los nombres EXACTOS de las pestañas de un spreadsheet, tal como Google los tiene guardados.</summary>
    public async Task<IList<string>> ListarNombresDeHojasAsync(string spreadsheetId)
    {
        var request = _service.Spreadsheets.Get(spreadsheetId);
        request.Fields = "sheets.properties.title";
        var response = await request.ExecuteAsync();
        return response.Sheets.Select(s => s.Properties.Title).ToList();
    }

    /// <summary>Lee todas las filas de una hoja de un archivo (spreadsheet) específico, incluyendo el encabezado en la posición 0.</summary>
    public async Task<IList<IList<object>>> LeerHojaAsync(string spreadsheetId, string nombreHoja)
    {
        // Notación de columnas completas (sin número de fila) para traer todas las filas existentes.
        var rango = $"'{nombreHoja}'!A:ZZ";
        var request = _service.Spreadsheets.Values.Get(spreadsheetId, rango);
        var response = await request.ExecuteAsync();
        return response.Values ?? new List<IList<object>>();
    }

    /// <summary>
    /// Agrega una fila al final de la pestaña Control, en el spreadsheet donde viva esa pestaña.
    /// Se usa Append (no Update) para no tener que calcular en qué fila escribir.
    /// </summary>
    public async Task EscribirFilaControlAsync(IList<object> valores)
    {
        var spreadsheetControl = string.IsNullOrWhiteSpace(_options.SpreadsheetIdControl)
            ? _options.SpreadsheetIdUsep
            : _options.SpreadsheetIdControl;

        var rango = $"{_options.HojaControl}!A1";
        var valueRange = new ValueRange { Values = new List<IList<object>> { valores } };

        var request = _service.Spreadsheets.Values.Append(valueRange, spreadsheetControl, rango);
        request.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
        request.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;

        await request.ExecuteAsync();
    }
}