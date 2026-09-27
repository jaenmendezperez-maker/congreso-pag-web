namespace CongresoApi.Config;

public class EmailOptions
{
    public const string SectionName = "Email";

    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;

    /// <summary>Tu correo de Gmail (el remitente).</summary>
    public string Usuario { get; set; } = string.Empty;

    /// <summary>
    /// Contraseña de APLICACIÓN de Gmail (no tu contraseña normal).
    /// Se genera en https://myaccount.google.com/apppasswords, requiere
    /// tener la verificación en dos pasos activada en la cuenta.
    /// </summary>
    public string ContrasenaApp { get; set; } = string.Empty;

    public string NombreRemitente { get; set; } = "Congreso";
    public string AsuntoCorreo { get; set; } = "Tu código de acceso al congreso";

    /// <summary>
    /// URL pública del PDF con el programa del congreso. Si se configura, se descarga
    /// una sola vez (se guarda en memoria) y se adjunta en cada correo, además de
    /// incluir el link como texto por si alguien prefiere verlo en el navegador.
    /// </summary>
    public string? ProgramaPdfUrl { get; set; }
}