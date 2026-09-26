using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CongresoApi.Models;

public class Asistente
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Origen Origen { get; set; }

    // Llave única para USEP. Null para extranjeros.
    public string? Matricula { get; set; }

    // Llave única para extranjeros. Siempre normalizado a minúsculas/sin espacios.
    [Required]
    public string Correo { get; set; } = string.Empty;

    public string? CorreoAlterno { get; set; }

    /// <summary>
    /// Llave real de unicidad para extranjeros: "correo|nombre completo normalizado".
    /// Existe porque varias personas distintas a veces comparten el mismo correo
    /// (ej. un profesor registra a todo su grupo con su propio correo), así que el
    /// correo por sí solo no basta para identificar a una persona única. Para USEP
    /// se deja en null, porque ahí la Matrícula ya cumple ese papel.
    /// </summary>
    public string? ClaveExterna { get; set; }

    [Required]
    public string Nombre { get; set; } = string.Empty; // nombre completo o Nombre(s)

    public string? ApellidoPaterno { get; set; } // solo extranjero
    public string? ApellidoMaterno { get; set; } // solo extranjero

    public string? Universidad { get; set; }     // solo extranjero
    public string? Seccion { get; set; }          // solo USEP
    public string? EstadoPais { get; set; }       // solo extranjero

    public DateTime FechaRegistro { get; set; } // Marca temporal del formulario

    // --- Estado de entrega ---
    public DateTime? EntregadoAt { get; set; }
    public string? EntregadoPor { get; set; }     // estación/nombre libre, no usuario real
    public string? Estacion { get; set; }

    // --- Envío del QR por correo ---
    // Null = todavía no se le ha mandado su QR. Se llena la primera vez que se envía
    // exitosamente, para que el envío masivo nunca reenvíe a quien ya lo recibió,
    // incluso si se corre varias veces conforme llegan registros nuevos.
    public DateTime? QrEnviadoAt { get; set; }

    [NotMapped]
    public bool YaEntregado => EntregadoAt.HasValue;

    // --- Auditoría de importación ---
    public DateTime CreadoEnSistema { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoEnSistema { get; set; } = DateTime.UtcNow;

    public string NombreCompleto =>
        Origen == Origen.Extranjero
            ? string.Join(" ", new[] { Nombre, ApellidoPaterno, ApellidoMaterno }.Where(s => !string.IsNullOrWhiteSpace(s)))
            : Nombre;
}