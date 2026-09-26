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

    public EmailService(IOptions<EmailOptions> options, QrService qr)
    {
        _options = options.Value;
        _qr = qr;
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

        var html = $"""
            <div style="font-family: Arial, sans-serif; max-width: 480px; margin: 0 auto;">
              <h2>¡Hola, {WebUtility.HtmlEncode(asistente.NombreCompleto)}!</h2>
              <p>Este es tu código de acceso al congreso. Preséntalo (en tu celular o impreso) al llegar para recoger tu gafete.</p>
              <div style="text-align: center; margin: 24px 0;">
                <img src="cid:qrcode" alt="Código QR" style="width: 240px; height: 240px;" />
              </div>
              <p style="color: #555; font-size: 14px;">Este código es personal e intransferible, y solo puede usarse una vez.</p>
            </div>
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