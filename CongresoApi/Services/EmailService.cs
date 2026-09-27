using System.Net;
using System.Net.Mail;
using CongresoApi.Config;
using CongresoApi.Models;
using Microsoft.Extensions.Options;

namespace CongresoApi.Services;

public class EmailService
{
    private readonly EmailOptions _options;
    private readonly QrService _qr;
    private readonly IHttpClientFactory _httpClientFactory;

    // Caché en memoria del PDF del programa: se descarga una sola vez (la primera
    // vez que se manda un correo) y se reutiliza para todos los envíos siguientes,
    // en vez de descargarlo de internet en cada correo.
    private static byte[]? _programaPdfCache;
    private static readonly SemaphoreSlim _programaPdfLock = new(1, 1);

    public EmailService(IOptions<EmailOptions> options, QrService qr, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _qr = qr;
        _httpClientFactory = httpClientFactory;
    }

    private async Task<byte[]?> ObtenerProgramaPdfAsync()
    {
        if (string.IsNullOrWhiteSpace(_options.ProgramaPdfUrl)) return null;
        if (_programaPdfCache != null) return _programaPdfCache;

        await _programaPdfLock.WaitAsync();
        try
        {
            if (_programaPdfCache != null) return _programaPdfCache; // otro hilo ya lo descargó mientras esperábamos

            var cliente = _httpClientFactory.CreateClient();
            _programaPdfCache = await cliente.GetByteArrayAsync(_options.ProgramaPdfUrl);
            return _programaPdfCache;
        }
        finally
        {
            _programaPdfLock.Release();
        }
    }

    /// <summary>
    /// Envía el correo con el QR incrustado a un destinatario. El parámetro
    /// destinoOverride permite mandarlo a un correo distinto al del asistente
    /// (útil para pruebas, sin tener que cambiar datos reales en la base).
    /// </summary>
    public async Task EnviarQrAsync(Asistente asistente, string? destinoOverride = null)
    {
        var destino = destinoOverride ?? asistente.Correo;

        var png = _qr.GenerarImagenQrPng(asistente.Id);

        using var mensaje = new MailMessage();
        mensaje.From = new MailAddress(_options.Usuario, _options.NombreRemitente);
        mensaje.To.Add(destino);
        mensaje.Subject = _options.AsuntoCorreo;
        mensaje.IsBodyHtml = true;

        // La imagen va incrustada con un Content-ID (cid), así el QR se ve directo
        // en el cuerpo del correo en vez de llegar como un adjunto aparte que
        // muchas personas no abren.
        using var streamImagen = new MemoryStream(png);
        var recursoQr = new LinkedResource(streamImagen, "image/png") { ContentId = "qrcode" };

        var linkPrograma = !string.IsNullOrWhiteSpace(_options.ProgramaPdfUrl)
            ? $"""
                <tr>
                  <td style="padding:0 32px 28px;">
                    <a href="{_options.ProgramaPdfUrl}" style="display:inline-block;background:#0f172a;color:#ffffff;text-decoration:none;padding:12px 22px;border-radius:8px;font-size:14px;font-weight:600;">
                      Ver programa del congreso
                    </a>
                    <p style="margin:10px 0 0;color:#94a3b8;font-size:12px;">También lo encontrarás adjunto en este correo.</p>
                  </td>
                </tr>
              """
            : "";

        var html = $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f1f5f9;padding:32px 0;font-family:Arial,Helvetica,sans-serif;">
              <tr>
                <td align="center">
                  <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:16px;overflow:hidden;">
                    <tr>
                      <td style="background:#0f172a;padding:24px 32px;">
                        <p style="margin:0;color:#ffffff;font-size:13px;letter-spacing:1px;text-transform:uppercase;opacity:0.7;">Congreso</p>
                        <p style="margin:4px 0 0;color:#ffffff;font-size:20px;font-weight:700;">Tu acceso está listo</p>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:28px 32px 4px;">
                        <p style="margin:0 0 4px;color:#64748b;font-size:13px;">¡Hola!</p>
                        <p style="margin:0;color:#0f172a;font-size:22px;font-weight:700;">{WebUtility.HtmlEncode(asistente.NombreCompleto)}</p>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:12px 32px 24px;">
                        <p style="margin:0;color:#475569;font-size:15px;line-height:1.5;">
                          Este es tu código de acceso. Preséntalo en tu celular o impreso al llegar para recoger tu gafete.
                        </p>
                      </td>
                    </tr>
                    <tr>
                      <td align="center" style="padding:0 32px 28px;">
                        <table role="presentation" cellpadding="0" cellspacing="0" style="border:1px solid #e2e8f0;border-radius:12px;padding:20px;background:#f8fafc;">
                          <tr>
                            <td>
                              <img src="cid:qrcode" alt="Código QR" width="220" height="220" style="display:block;border-radius:6px;" />
                            </td>
                          </tr>
                        </table>
                      </td>
                    </tr>
                    {linkPrograma}
                    <tr>
                      <td style="padding:0 32px 28px;">
                        <p style="margin:0;color:#94a3b8;font-size:12px;line-height:1.5;border-top:1px solid #e2e8f0;padding-top:16px;">
                          Este código es personal e intransferible, y solo puede usarse una vez.
                        </p>
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>
            """;

        var vistaHtml = AlternateView.CreateAlternateViewFromString(html, null, "text/html");
        vistaHtml.LinkedResources.Add(recursoQr);
        mensaje.AlternateViews.Add(vistaHtml);

        // Se adjunta un PDF (nombre + QR + instrucciones) además de la vista incrustada
        // en el cuerpo del correo. El PDF es más confiable de descargar que un PNG suelto
        // (algunos clientes de correo lo bajan sin extensión) y más fácil de imprimir.
        var pdf = _qr.GenerarPdfQr(asistente.Id, asistente.NombreCompleto, asistente.Origen.ToString());
        var streamAdjunto = new MemoryStream(pdf);
        var nombreAdjunto = $"QR_{Sanear(asistente.NombreCompleto)}.pdf";
        mensaje.Attachments.Add(new Attachment(streamAdjunto, nombreAdjunto, "application/pdf"));

        // Adjunta también el programa del congreso, si está configurado.
        var programaPdf = await ObtenerProgramaPdfAsync();
        if (programaPdf != null)
        {
            var streamPrograma = new MemoryStream(programaPdf);
            mensaje.Attachments.Add(new Attachment(streamPrograma, "Programa_Congreso.pdf", "application/pdf"));
        }

        using var cliente = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            Credentials = new NetworkCredential(_options.Usuario, _options.ContrasenaApp),
            EnableSsl = true
        };

        await cliente.SendMailAsync(mensaje);
    }

    private static string Sanear(string texto)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var limpio = new string(texto.Where(c => !invalidos.Contains(c)).ToArray());
        return limpio.Replace(" ", "_").Trim('_');
    }
}