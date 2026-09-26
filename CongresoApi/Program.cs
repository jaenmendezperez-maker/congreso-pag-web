using System.Threading.RateLimiting;
using CongresoApi.Config;
using CongresoApi.Data;
using CongresoApi.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- Controladores + Swagger (documentación interactiva de la API) ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- Base de datos ---
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// --- Configuración de Google Sheets (se llena desde appsettings.json) ---
builder.Services.Configure<GoogleSheetsOptions>(
    builder.Configuration.GetSection(GoogleSheetsOptions.SectionName));

// --- Configuración del QR (clave secreta de firma) ---
builder.Services.Configure<QrOptions>(
    builder.Configuration.GetSection(QrOptions.SectionName));

// --- Configuración de correo (envío de QR por Gmail) ---
builder.Services.Configure<EmailOptions>(
    builder.Configuration.GetSection(EmailOptions.SectionName));

// --- Clave compartida del staff para la app de escaneo ---
builder.Services.Configure<AuthOptions>(
    builder.Configuration.GetSection(AuthOptions.SectionName));

// --- Servicios propios ---
builder.Services.AddScoped<GoogleSheetsClient>();
builder.Services.AddScoped<SincronizacionService>();
builder.Services.AddSingleton<QrService>();
builder.Services.AddScoped<EmailService>();

// --- Sincronización automática en segundo plano ---
builder.Services.AddHostedService<CongresoApi.Workers.SincronizacionAutomaticaWorker>();

// --- Límite de peticiones: protege la API si muchas tablets/celulares
// disparan peticiones al mismo tiempo, o ante un uso indebido/accidental.
// 60 peticiones por minuto por IP es generoso para el uso real de la app
// (escaneos manuales, uno a la vez) pero frena ráfagas anómalas.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var clave = context.Connection.RemoteIpAddress?.ToString() ?? "desconocido";
        return RateLimitPartition.GetFixedWindowLimiter(clave, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

// --- CORS: permite que el frontend en Next.js (otro puerto/dominio) llame a esta API ---
const string CorsPolicyName = "FrontendPolicy";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:3000",           // Next.js en desarrollo
                "https://TU-APP.vercel.app"         // reemplazar cuando se despliegue
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);
app.UseRateLimiter();

// --- Protección del panel de admin ---
// Todo lo que empiece con /api/Reportes, /api/Sync o /api/Qr son acciones de admin
// (ver datos de todos, exportar, sincronizar, mandar correos masivos, descargar QRs).
// Se exige una clave (header X-Admin-Key, o ?adminKey=... para los links de descarga
// directa, que no pueden mandar headers personalizados). Es deliberadamente simple
// (no JWT, no usuarios): el equipo es chico y el riesgo real es que un extraño con
// el link entre a ver o disparar estas acciones, no un ataque sofisticado.
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    var esRutaAdmin = path.StartsWith("/api/Reportes", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/api/Sync", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/api/Qr", StringComparison.OrdinalIgnoreCase);

    if (esRutaAdmin)
    {
        var claveConfigurada = builder.Configuration["Auth:ClaveAdmin"];
        if (!string.IsNullOrWhiteSpace(claveConfigurada))
        {
            var claveRecibida = context.Request.Headers["X-Admin-Key"].FirstOrDefault()
                ?? context.Request.Query["adminKey"].FirstOrDefault();

            if (claveRecibida != claveConfigurada)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Clave de administrador incorrecta o faltante.");
                return;
            }
        }
    }

    await next();
});

app.UseAuthorization();
app.MapControllers();

app.Run();