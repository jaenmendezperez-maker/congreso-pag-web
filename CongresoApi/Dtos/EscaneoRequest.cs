namespace CongresoApi.Dtos;

public class EscaneoRequest
{
    /// <summary>El texto crudo leído por la cámara/lector, tal cual viene del QR.</summary>
    public string ContenidoQr { get; set; } = string.Empty;

    /// <summary>Nombre libre de quien escanea (ej. "Laura"). No es un usuario real, solo trazabilidad.</summary>
    public string? RealizadoPor { get; set; }

    /// <summary>Nombre libre de la estación (ej. "Entrada 1").</summary>
    public string? Estacion { get; set; }

    /// <summary>Clave compartida del staff. Se valida contra Auth:ClaveStaff en el backend.</summary>
    public string? Clave { get; set; }
}

public class EscaneoResponse
{
    public string Resultado { get; set; } = string.Empty; // "Ok" | "Duplicado" | "FirmaInvalida" | "NoExiste"
    public string? NombreCompleto { get; set; }
    public string? Origen { get; set; }
    public string? Universidad { get; set; }
    public string? Seccion { get; set; }
    public string? Matricula { get; set; }

    // Solo se llena si Resultado == "Duplicado"
    public DateTime? EntregadoAtPrevio { get; set; }
    public string? EntregadoPorPrevio { get; set; }
    public string? EstacionPrevia { get; set; }
}

public class DeshacerRequest
{
    public string? RealizadoPor { get; set; }
    public string Motivo { get; set; } = string.Empty;
}