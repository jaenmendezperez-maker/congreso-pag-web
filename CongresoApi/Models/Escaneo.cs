using System.ComponentModel.DataAnnotations;

namespace CongresoApi.Models;

public enum ResultadoEscaneo
{
    Ok = 0,
    Duplicado = 1,
    FirmaInvalida = 2,
    NoExiste = 3,
    ManualSinRegistro = 4,       // caso especial: entrada manual, no estaba en la lista
    ExcepcionSupervisor = 5      // reintento autorizado sobre un QR ya usado
}

public class Escaneo
{
    [Key]
    public long Id { get; set; }

    // Null cuando el resultado es NoExiste o FirmaInvalida (no hay asistente real que referenciar)
    public Guid? AsistenteId { get; set; }
    public Asistente? Asistente { get; set; }

    public ResultadoEscaneo Resultado { get; set; }

    public string? Estacion { get; set; }
    public string? RealizadoPor { get; set; } // texto libre, nombre de quien escaneó

    public string? Motivo { get; set; } // requerido para ManualSinRegistro / ExcepcionSupervisor

    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}