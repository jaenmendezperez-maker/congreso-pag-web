namespace CongresoApi.Config;

public class GoogleSheetsOptions
{
    public const string SectionName = "GoogleSheets";

    // Ruta al JSON de la cuenta de servicio (o el JSON completo como string si prefieren variable de entorno)
    public string CredencialesPath { get; set; } = string.Empty;

    // Son DOS archivos de Sheet distintos (dos Google Forms separados)
    public string SpreadsheetIdUsep { get; set; } = string.Empty;
    public string SpreadsheetIdExtranjeros { get; set; } = string.Empty;

    // Nombre exacto de la pestaña de respuestas dentro de cada archivo
    public string HojaUsep { get; set; } = "Form_Responses";
    public string HojaExtranjeros { get; set; } = "Form_Responses";

    // La pestaña Control vive dentro del Sheet de extranjeros o del de USEP; hay que decidir dónde.
    // Por defecto se escribe en el mismo Sheet de USEP, pero es configurable.
    public string SpreadsheetIdControl { get; set; } = string.Empty;
    public string HojaControl { get; set; } = "Control";
}