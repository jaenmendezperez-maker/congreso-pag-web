# Sistema de Acceso — Congreso

Sistema de control de acceso con QR para el congreso: sincroniza los registros desde Google Sheets (USEP y extranjeros), genera códigos QR firmados, permite escanearlos en la entrada para marcar la entrega de gafetes, y ofrece un panel de administración.

## Estructura del repositorio

```
Congreso/
├── CongresoApi/     ← Backend: ASP.NET Core + PostgreSQL
└── congreso-web/    ← Frontend: Next.js (panel de admin + app de escaneo)
```

Son dos proyectos independientes que se corren por separado, cada uno con su propio `README.md` con instrucciones detalladas:

- **[CongresoApi/README.md](./CongresoApi/README.md)** — instalación del backend, base de datos, Google Sheets, correo, migraciones.
- **congreso-web** — instrucciones abajo.

---

## Backend (CongresoApi)

Ver [CongresoApi/README.md](./CongresoApi/README.md) para la instalación completa. En resumen:

```
cd CongresoApi
dotnet restore
docker compose up -d
dotnet ef database update
dotnet run
```

Corre en `http://localhost:5232` (Swagger en `/swagger`).

## Frontend (congreso-web)

Requiere Node.js. Con el backend ya corriendo:

```
cd congreso-web
npm install
npm run dev
```

Corre en `http://localhost:3000`. Rutas principales:
- `/admin` — panel de administración (pide clave de admin).
- `/escaneo` — app de escaneo para el staff en la entrada (pide estación, nombre y clave de staff).

Next.js reenvía automáticamente las llamadas a `/api/*` hacia el backend en `localhost:5232` (configurado en `next.config.ts`), así que no hace falta configurar CORS para desarrollo local.

## Cómo funciona, en breve

1. **Sincronización**: el backend lee dos Google Sheets (USEP y extranjeros) y guarda a los asistentes en PostgreSQL, sin duplicar a nadie y sin resetear a quien ya entró.
2. **QR**: cada asistente tiene un código QR firmado (HMAC), imposible de falsificar sin la clave secreta del servidor.
3. **Escaneo**: la app de escaneo valida el QR, y marca la entrega de forma atómica (evita que el mismo QR se use dos veces, incluso si dos tablets escanean al mismo tiempo).
4. **Admin**: contadores en vivo, búsqueda de asistentes, exportar a Excel, descargar todos los QR, enviar los QR por correo, deshacer entregas por error, y sincronizar manualmente.

## Seguridad

- Dos claves compartidas simples (no hay sistema de usuarios): una para el staff que escanea, otra para el panel de admin.
- Rate limiting básico en la API.
- Las credenciales de Google y la configuración con datos reales (`appsettings.json`) nunca se suben al repositorio — ver `.gitignore`.