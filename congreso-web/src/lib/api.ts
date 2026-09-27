// Vacío por defecto: las peticiones van a rutas relativas (/api/...), que Next.js
// reenvía internamente al backend (ver next.config.ts). Así, en el celular solo se
// necesita exponer el frontend con ngrok — el backend nunca sale a internet directamente.
const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "";

// URL directa al backend, SIN pasar por el proxy de Next.js. El proxy corta las
// peticiones muy largas (el envío masivo de correos tarda varios minutos), así
// que esa llamada específica le habla directo al backend en vez de pasar por
// next.config.ts. El CORS del backend ya permite localhost:3000.
const BACKEND_DIRECTO = process.env.NEXT_PUBLIC_BACKEND_DIRECTO ?? "http://localhost:5232";

function obtenerAdminKey(): string {
  if (typeof window === "undefined") return "";
  return localStorage.getItem("congreso_adminKey") ?? "";
}

/** Agrega la clave de admin como query param, para usarla en links <a href> (descargas). */
function conAdminKey(url: string): string {
  const key = obtenerAdminKey();
  const separador = url.includes("?") ? "&" : "?";
  return key ? `${url}${separador}adminKey=${encodeURIComponent(key)}` : url;
}

function headersAdmin(): HeadersInit {
  return { "X-Admin-Key": obtenerAdminKey() };
}

export type ResultadoEscaneo = "Ok" | "Duplicado" | "FirmaInvalida" | "NoExiste";

export interface EscaneoResponse {
  resultado: ResultadoEscaneo;
  nombreCompleto?: string;
  origen?: string;
  universidad?: string;
  seccion?: string;
  matricula?: string;
  entregadoAtPrevio?: string;
  entregadoPorPrevio?: string;
  estacionPrevia?: string;
}

export interface ResumenGrupo {
  total: number;
  entregados: number;
  pendientes: number;
  qrEnviados: number;
}

export interface UltimoEscaneo {
  nombreCompleto?: string;
  origen?: string;
  fechaHora: string;
  estacion?: string;
  realizadoPor?: string;
}

export interface ResumenResponse {
  usep: ResumenGrupo;
  extranjeros: ResumenGrupo;
  ultimosEscaneos: UltimoEscaneo[];
}

export interface AsistenteBusqueda {
  id: string;
  nombreCompleto: string;
  origen: string;
  matricula?: string;
  universidad?: string;
  seccion?: string;
  yaEntregado: boolean;
  entregadoAt?: string;
  entregadoPor?: string;
  estacion?: string;
}

export interface SincronizacionResultado {
  nuevos: number;
  actualizados: number;
  sinCambios: number;
  conflictos: string[];
  filasIgnoradas: string[];
  totalProcesadas: number;
}

async function manejarRespuesta<T>(res: Response): Promise<T> {
  if (!res.ok) {
    const texto = await res.text().catch(() => "");
    throw new Error(`Error ${res.status}: ${texto || res.statusText}`);
  }
  return res.json();
}

export const api = {
  escanear: (contenidoQr: string, realizadoPor: string, estacion: string, clave: string) =>
    fetch(`${API_URL}/api/Escaneo`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ contenidoQr, realizadoPor, estacion, clave }),
    }).then((r) => manejarRespuesta<EscaneoResponse>(r)),

  marcarEntradaManual: (asistenteId: string, realizadoPor: string, motivo: string) =>
    fetch(`${API_URL}/api/Escaneo/${asistenteId}/manual`, {
      method: "POST",
      headers: { "Content-Type": "application/json", ...headersAdmin() },
      body: JSON.stringify({ realizadoPor, motivo }),
    }).then((r) => manejarRespuesta<{ mensaje: string }>(r)),

  deshacerEntrega: (asistenteId: string, realizadoPor: string, motivo: string) =>
    fetch(`${API_URL}/api/Escaneo/${asistenteId}/deshacer`, {
      method: "POST",
      headers: { "Content-Type": "application/json", ...headersAdmin() },
      body: JSON.stringify({ realizadoPor, motivo }),
    }).then((r) => manejarRespuesta<{ mensaje: string }>(r)),

  buscar: (q: string) =>
    fetch(`${API_URL}/api/Reportes/buscar?q=${encodeURIComponent(q)}`, { headers: headersAdmin() }).then((r) =>
      manejarRespuesta<AsistenteBusqueda[]>(r)
    ),

  resumen: () =>
    fetch(`${API_URL}/api/Reportes/resumen`, { cache: "no-store", headers: headersAdmin() }).then((r) =>
      manejarRespuesta<ResumenResponse>(r)
    ),

  exportarUrl: () => conAdminKey(`${API_URL}/api/Reportes/exportar`),

  qrTodosUrl: () => conAdminKey(`${API_URL}/api/Qr/exportar-todos`),

  enviarQrPendientes: (limite = 10) =>
    fetch(`${BACKEND_DIRECTO}/api/Qr/enviar-pendientes?limite=${limite}`, { method: "POST", headers: headersAdmin() }).then((r) =>
      manejarRespuesta<{ totalPendientesAlEmpezar: number; enviados: number; fallidos: string[]; quedanPendientes: number }>(r)
    ),

  sincronizarTodo: () =>
    fetch(`${API_URL}/api/Sync/todo`, { method: "POST", headers: headersAdmin() }).then((r) =>
      manejarRespuesta<SincronizacionResultado>(r)
    ),

  guardarAdminKey: (key: string) => localStorage.setItem("congreso_adminKey", key),
  tieneAdminKey: () => obtenerAdminKey().length > 0,
};