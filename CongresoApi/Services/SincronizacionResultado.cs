namespace CongresoApi.Services;

public class SincronizacionResultado
{
    public int Nuevos { get; set; }
    public int Actualizados { get; set; }
    public int SinCambios { get; set; }
    public List<string> Conflictos { get; set; } = new();
    public List<string> FilasIgnoradas { get; set; } = new();
    public List<string> DiagnosticoCambios { get; set; } = new(); // TEMPORAL: para investigar falsos positivos

    public int TotalProcesadas => Nuevos + Actualizados + SinCambios;

    public void Combinar(SincronizacionResultado otro)
    {
        Nuevos += otro.Nuevos;
        Actualizados += otro.Actualizados;
        SinCambios += otro.SinCambios;
        Conflictos.AddRange(otro.Conflictos);
        FilasIgnoradas.AddRange(otro.FilasIgnoradas);
        DiagnosticoCambios.AddRange(otro.DiagnosticoCambios);
    }
}