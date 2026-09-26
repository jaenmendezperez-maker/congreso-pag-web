using System.Security.Cryptography;
using System.Text;
using CongresoApi.Config;
using Microsoft.Extensions.Options;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CongresoApi.Services;

/// <summary>
/// Genera y valida el contenido del QR de cada asistente.
///
/// Formato del QR: "{guid}.{firma}"
/// donde firma = HMAC-SHA256(guid, ClaveSecreta), codificado en Base64Url.
///
/// Por qué así:
/// - El QR NUNCA contiene el nombre ni ningún dato personal, solo el GUID.
///   El nombre se consulta en la base de datos al escanear.
/// - Nadie puede fabricar un QR válido sin conocer la ClaveSecreta, que
///   solo vive en el servidor. Un GUID inventado nunca va a tener una
///   firma que coincida.
/// - Si alguien intenta modificar el GUID de un QR real (para hacerse
///   pasar por otra persona), la firma ya no coincide y se rechaza.
/// </summary>
public class QrService
{
    private readonly byte[] _claveSecreta;

    public QrService(IOptions<QrOptions> options)
    {
        // QuestPDF requiere declarar el tipo de licencia. "Community" es gratuita
        // para este caso de uso (empresas/organizaciones pequeñas, sin fines de lucro
        // por el uso del PDF en sí). Se hace una sola vez.
        QuestPDF.Settings.License = LicenseType.Community;

        var clave = options.Value.ClaveSecreta;
        if (string.IsNullOrWhiteSpace(clave) || clave.Length < 16)
            throw new InvalidOperationException(
                "Qr:ClaveSecreta no está configurada o es demasiado corta (mínimo 16 caracteres). " +
                "Revisa appsettings.json o las variables de entorno.");

        _claveSecreta = Encoding.UTF8.GetBytes(clave);
    }

    /// <summary>Genera el contenido que debe ir codificado dentro de la imagen del QR.</summary>
    public string GenerarContenidoQr(Guid asistenteId)
    {
        var guidTexto = asistenteId.ToString("N"); // sin guiones, más corto
        var firma = Firmar(guidTexto);
        return $"{guidTexto}.{firma}";
    }

    /// <summary>
    /// Valida el contenido leído de un QR escaneado.
    /// Regresa (true, asistenteId) si la firma es correcta.
    /// Regresa (false, null) si el formato es inválido o la firma no coincide,
    /// SIN dar pistas de cuál fue el problema (para no ayudar a quien intente falsificar QRs).
    /// </summary>
    public (bool esValido, Guid? asistenteId) ValidarContenidoQr(string? contenidoEscaneado)
    {
        if (string.IsNullOrWhiteSpace(contenidoEscaneado))
            return (false, null);

        var partes = contenidoEscaneado.Trim().Split('.');
        if (partes.Length != 2)
            return (false, null);

        var guidTexto = partes[0];
        var firmaRecibida = partes[1];

        if (!Guid.TryParseExact(guidTexto, "N", out var asistenteId))
            return (false, null);

        var firmaEsperada = Firmar(guidTexto);

        // Comparación en tiempo constante: evita que alguien deduzca la firma correcta
        // midiendo cuánto tarda la respuesta carácter por carácter (timing attack).
        var coincide = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(firmaEsperada),
            Encoding.UTF8.GetBytes(firmaRecibida));

        return coincide ? (true, asistenteId) : (false, null);
    }

    /// <summary>
    /// Genera un PDF de una página con el nombre de la persona, su QR y las instrucciones.
    /// Pensado como adjunto de correo: más confiable de descargar y más fácil de imprimir
    /// que una imagen suelta, y evita el problema de imágenes incrustadas bloqueadas por
    /// clientes de correo institucionales.
    /// </summary>
    public byte[] GenerarPdfQr(Guid asistenteId, string nombreCompleto, string? origen = null)
    {
        var png = GenerarImagenQrPng(asistenteId, pixelesPorModulo: 15);

        var documento = QuestPDF.Fluent.Document.Create(contenedor =>
        {
            contenedor.Page(pagina =>
            {
                pagina.Size(PageSizes.A5);
                pagina.Margin(30);
                pagina.DefaultTextStyle(x => x.FontSize(12).FontFamily(Fonts.Lato));

                pagina.Content().Column(col =>
                {
                    col.Spacing(12);

                    col.Item().AlignCenter().Text("Congreso").FontSize(16).SemiBold();
                    col.Item().AlignCenter().Text(nombreCompleto).FontSize(18).Bold();
                    if (!string.IsNullOrWhiteSpace(origen))
                        col.Item().AlignCenter().Text(origen).FontSize(11).FontColor(Colors.Grey.Darken1);

                    col.Item().AlignCenter().PaddingVertical(10).Width(220).Image(png);

                    col.Item().AlignCenter().Text("Presenta este código al llegar para recoger tu gafete.")
                        .FontSize(11).FontColor(Colors.Grey.Darken2);
                    col.Item().AlignCenter().Text("Este código es personal e intransferible, y solo puede usarse una vez.")
                        .FontSize(10).FontColor(Colors.Grey.Darken1);
                });
            });
        });

        return documento.GeneratePdf();
    }

    /// <summary>
    /// Genera la imagen PNG del QR (lista para enviar por correo o mostrar en pantalla)
    /// a partir del Id del asistente. Internamente genera el contenido firmado y lo
    /// convierte en una imagen escaneable.
    /// </summary>
    public byte[] GenerarImagenQrPng(Guid asistenteId, int pixelesPorModulo = 10)
    {
        var contenido = GenerarContenidoQr(asistenteId);

        using var generador = new QRCodeGenerator();
        // Nivel de corrección de errores Q: el QR sigue siendo legible aunque se ensucie,
        // se doble o el logo/gafete lo tape parcialmente. Buen balance para impresión física.
        using var datosQr = generador.CreateQrCode(contenido, QRCodeGenerator.ECCLevel.Q);
        using var qrPng = new PngByteQRCode(datosQr);
        return qrPng.GetGraphic(pixelesPorModulo);
    }

    private string Firmar(string guidTexto)
    {
        using var hmac = new HMACSHA256(_claveSecreta);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(guidTexto));
        return Convert.ToBase64String(hash)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('='); // Base64Url, seguro para meter en un QR/URL sin caracteres raros
    }
}