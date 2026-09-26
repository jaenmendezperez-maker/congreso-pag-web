# Congreso API — Backend

## Requisitos
- .NET SDK 10
- Docker Desktop

## Instalación (primera vez)

1. Clona el repositorio y entra a la carpeta.
2. Restaura los paquetes (normalmente automático al compilar):
   ```
   dotnet restore
   ```
3. Levanta la base de datos:
   ```
   docker compose up -d
   ```
4. Copia `appsettings.example.json` a `appsettings.json` y llena tus propios valores:
   - `ConnectionStrings:Default` (déjalo igual si usas el docker-compose incluido)
   - `GoogleSheets:SpreadsheetIdUsep` / `SpreadsheetIdExtranjeros` (pide los IDs reales)
   - `GoogleSheets:CredencialesPath` → coloca tu archivo `service-account.json` en `credenciales/`
   - `Qr:ClaveSecreta` → genera una propia, larga y aleatoria (no reutilices la de otra persona)
5. Pide el archivo `credenciales/service-account.json` por un canal seguro (NO por Git) y colócalo en `credenciales/`.
6. Aplica las migraciones:
   ```
   dotnet ef database update
   ```
7. Corre la API:
   ```
   dotnet run
   ```
8. Prueba en `http://localhost:5232/swagger`.

## Importante
- Nunca subas `credenciales/` ni tu `appsettings.json` con datos reales a Git — ya están en `.gitignore`.
- Si cambias `Qr:ClaveSecreta`, todos los QR ya generados dejan de ser válidos.
