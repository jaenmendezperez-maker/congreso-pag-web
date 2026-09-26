using CongresoApi.Services;

namespace CongresoApi.Workers;

/// <summary>
/// Corre en segundo plano mientras la API esté prendida, sincronizando ambas hojas
/// de Google Sheets cada IntervaloMinutos. Así los registros nuevos entran solos,
/// sin que nadie tenga que darle clic a "Sincronizar" manualmente.
/// </summary>
public class SincronizacionAutomaticaWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SincronizacionAutomaticaWorker> _logger;
    private readonly int _intervaloMinutos;

    public SincronizacionAutomaticaWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<SincronizacionAutomaticaWorker> logger,
        IConfiguration configuracion)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _intervaloMinutos = configuracion.GetValue<int?>("GoogleSheets:IntervaloSincronizacionMinutos") ?? 15;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Sincronización automática iniciada. Intervalo: {Minutos} minutos.", _intervaloMinutos);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Se crea un "scope" nuevo en cada vuelta porque AppDbContext es Scoped,
                // y este worker vive todo el tiempo que la app esté prendida (Singleton).
                using var scope = _scopeFactory.CreateScope();
                var sincronizacion = scope.ServiceProvider.GetRequiredService<SincronizacionService>();

                var resultado = await sincronizacion.SincronizarTodoAsync();

                _logger.LogInformation(
                    "Sincronización automática completada: {Nuevos} nuevos, {Actualizados} actualizados, {Conflictos} conflictos.",
                    resultado.Nuevos, resultado.Actualizados, resultado.Conflictos.Count);
            }
            catch (Exception ex)
            {
                // Si falla (sin internet, Google caído, etc.), no tumbamos la API entera.
                // Solo se registra el error y se reintenta en la siguiente vuelta.
                _logger.LogError(ex, "Error durante la sincronización automática. Se reintentará en {Minutos} minutos.", _intervaloMinutos);
            }

            await Task.Delay(TimeSpan.FromMinutes(_intervaloMinutos), stoppingToken);
        }
    }
}