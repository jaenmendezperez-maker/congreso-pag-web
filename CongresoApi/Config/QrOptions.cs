namespace CongresoApi.Config;

public class QrOptions
{
    public const string SectionName = "Qr";

    /// <summary>
    /// Clave secreta usada para firmar los QR. Debe ser larga y aleatoria,
    /// y NUNCA debe compartirse ni subirse a un repositorio público.
    /// Si esta clave cambia, todos los QR ya generados/enviados dejan de ser válidos.
    /// </summary>
    public string ClaveSecreta { get; set; } = string.Empty;
}