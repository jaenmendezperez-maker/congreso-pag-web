namespace CongresoApi.Dtos;

public class ResumenGrupo
{
    public int Total { get; set; }
    public int Entregados { get; set; }
    public int Pendientes => Total - Entregados;
    public int QrEnviados { get; set; }
}

public class ResumenResponse
{
    public ResumenGrupo Usep { get; set; } = new();
    public ResumenGrupo Extranjeros { get; set; } = new();
    public List<UltimoEscaneoDto> UltimosEscaneos { get; set; } = new();
}

public class UltimoEscaneoDto
{
    public string? NombreCompleto { get; set; }
    public string? Origen { get; set; }
    public DateTime FechaHora { get; set; }
    public string? Estacion { get; set; }
    public string? RealizadoPor { get; set; }
}