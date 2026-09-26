namespace CongresoApi.Config;

public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Clave simple que debe conocer el staff para poder escanear. No es un sistema
    /// de usuarios real, solo evita que cualquiera con el link entre a escanear.
    /// </summary>
    public string ClaveStaff { get; set; } = string.Empty;

    /// <summary>
    /// Clave para el panel de administración (reportes, sincronizar, exportar,
    /// enviar correos masivos). Debe ser distinta de ClaveStaff: el staff de la
    /// entrada no debería poder disparar un envío masivo de correos ni descargar
    /// el Excel con datos de todos los asistentes.
    /// </summary>
    public string ClaveAdmin { get; set; } = string.Empty;
}