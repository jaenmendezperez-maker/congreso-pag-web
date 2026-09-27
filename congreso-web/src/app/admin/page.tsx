"use client";

import { useEffect, useState, useCallback } from "react";
import { api, ResumenResponse, AsistenteBusqueda } from '../../lib/api';

export default function PaginaAdmin() {
  const [autenticado, setAutenticado] = useState(false);
  const [claveInput, setClaveInput] = useState("");
  const [resumen, setResumen] = useState<ResumenResponse | null>(null);
  const [cargando, setCargando] = useState(true);
  const [sincronizando, setSincronizando] = useState(false);
  const [enviandoQr, setEnviandoQr] = useState(false);
  const [mensaje, setMensaje] = useState<string | null>(null);
  const [busqueda, setBusqueda] = useState("");
  const [resultadosBusqueda, setResultadosBusqueda] = useState<AsistenteBusqueda[]>([]);
  const [buscando, setBuscando] = useState(false);

  const cargarResumen = useCallback(async () => {
    try {
      const data = await api.resumen();
      setResumen(data);
    } catch {
      setMensaje("No se pudo conectar con el servidor.");
    }
    setCargando(false);
  }, []);

  // --- TODOS los hooks van aquí arriba, sin ningún "return" en medio,
  // para que siempre se ejecuten en el mismo orden sin importar el estado. ---

  useEffect(() => {
    setAutenticado(api.tieneAdminKey());
  }, []);

  useEffect(() => {
    if (!autenticado) return;
    cargarResumen();
    const intervalo = setInterval(cargarResumen, 30_000);
    return () => clearInterval(intervalo);
  }, [autenticado, cargarResumen]);

  // Busca con un pequeño debounce, para no disparar una petición por cada tecla
  useEffect(() => {
    if (busqueda.trim().length < 2) {
      setResultadosBusqueda([]);
      return;
    }
    setBuscando(true);
    const timeout = setTimeout(async () => {
      try {
        const r = await api.buscar(busqueda.trim());
        setResultadosBusqueda(r);
      } catch {
        // silencioso: si falla, simplemente no se muestran resultados
      }
      setBuscando(false);
    }, 350);
    return () => clearTimeout(timeout);
  }, [busqueda]);

  const entrar = (e: React.FormEvent) => {
    e.preventDefault();
    api.guardarAdminKey(claveInput);
    setAutenticado(true);
  };

  const sincronizarAhora = async () => {
    setSincronizando(true);
    setMensaje(null);
    try {
      const r = await api.sincronizarTodo();
      setMensaje(
        `Sincronizado: ${r.nuevos} nuevos, ${r.actualizados} actualizados` +
          (r.conflictos.length ? `, ${r.conflictos.length} conflictos por revisar` : "")
      );
      await cargarResumen();
    } catch {
      setMensaje("Error al sincronizar. Intenta de nuevo.");
    }
    setSincronizando(false);
  };

  const enviarQrPendientes = async () => {
    if (!confirm("Esto envía el QR por correo a todas las personas que aún no lo han recibido. ¿Continuar?")) return;
    setEnviandoQr(true);
    setMensaje(null);

    let totalEnviados = 0;
    let todosFallidos: string[] = [];

    try {
      // Se manda en lotes chicos y se repite automáticamente hasta terminar, porque
      // mandar cientos de correos en una sola petición tarda demasiado y el navegador
      // corta la conexión antes de que acabe.
      while (true) {
        const r = await api.enviarQrPendientes(10);
        totalEnviados += r.enviados;
        todosFallidos = [...todosFallidos, ...r.fallidos];
        setMensaje(`Enviando… ${totalEnviados} enviados hasta ahora, quedan ${r.quedanPendientes}.`);

        if (r.quedanPendientes <= 0 || r.enviados === 0) break; // terminado, o ya no avanza (evita loop infinito si todos fallan)
      }
      setMensaje(
        `Listo. Total enviados: ${totalEnviados}.` +
          (todosFallidos.length ? ` Fallaron: ${todosFallidos.join(", ")}` : "")
      );
    } catch {
      setMensaje(`Error al enviar los correos (van ${totalEnviados} enviados hasta el corte). Puedes darle de nuevo, no se duplican los ya enviados.`);
    }
    setEnviandoQr(false);
  };

  const marcarEntrada = async (asistente: AsistenteBusqueda) => {
    if (!confirm(`¿Marcar entrada manual para "${asistente.nombreCompleto}"? (llegó sin QR)`)) return;
    try {
      await api.marcarEntradaManual(asistente.id, "Admin", "Entrada manual sin QR");
      setResultadosBusqueda((prev) =>
        prev.map((a) => (a.id === asistente.id ? { ...a, yaEntregado: true, entregadoAt: new Date().toISOString(), entregadoPor: "Admin", estacion: "Manual (admin)" } : a))
      );
      cargarResumen();
    } catch {
      alert("No se pudo marcar la entrada (puede que ya estuviera marcada).");
    }
  };

  const deshacerEntrega = async (asistente: AsistenteBusqueda) => {
    const motivo = prompt(`¿Por qué deshacer la entrada de "${asistente.nombreCompleto}"? (ej. "escaneo de prueba", "error del staff")`);
    if (!motivo) return;
    try {
      await api.deshacerEntrega(asistente.id, "Admin", motivo);
      setResultadosBusqueda((prev) =>
        prev.map((a) => (a.id === asistente.id ? { ...a, yaEntregado: false, entregadoAt: undefined, entregadoPor: undefined, estacion: undefined } : a))
      );
      cargarResumen();
    } catch {
      alert("No se pudo deshacer la entrega.");
    }
  };

  // --- A partir de aquí, los "return" condicionales para decidir qué se ve. ---

  if (!autenticado) {
    return (
      <main className="min-h-screen flex items-center justify-center bg-slate-50 p-6">
        <form onSubmit={entrar} className="w-full max-w-sm bg-white border border-slate-200 rounded-2xl p-8 space-y-4">
          <h1 className="text-xl font-semibold text-slate-900">Panel del congreso</h1>
          <input
            required
            type="password"
            value={claveInput}
            onChange={(e) => setClaveInput(e.target.value)}
            placeholder="Clave de administrador"
            className="w-full rounded-lg border border-slate-400 px-4 py-3 text-lg text-slate-900 placeholder:text-slate-400 outline-none focus:ring-2 focus:ring-slate-400"
          />
          <button type="submit" className="w-full bg-slate-900 text-white rounded-lg py-3 font-medium cursor-pointer">
            Entrar
          </button>
        </form>
      </main>
    );
  }

  if (cargando) {
    return (
      <main className="min-h-screen flex items-center justify-center text-slate-500">
        Cargando…
      </main>
    );
  }

  if (!resumen) {
    return (
      <main className="min-h-screen flex items-center justify-center bg-slate-50 p-6">
        <div className="text-center">
          <p className="text-lg font-medium text-slate-900">No se pudo conectar con el servidor.</p>
          <p className="text-sm text-slate-500 mt-2">Confirma que el backend (dotnet run) esté corriendo, y refresca la página.</p>
        </div>
      </main>
    );
  }

  return (
    <main className="min-h-screen bg-slate-50 p-6 md:p-10">
      <div className="max-w-4xl mx-auto space-y-8">
        <header className="flex items-center justify-between">
          <h1 className="text-2xl font-semibold text-slate-900">Panel del congreso</h1>
          <div className="flex gap-3">
            <button
              onClick={enviarQrPendientes}
              disabled={enviandoQr}
              className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
            >
              {enviandoQr ? "Enviando…" : "Enviar QR pendientes"}
            </button>
            <a
              href={api.qrTodosUrl()}
              className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100"
            >
              Descargar todos los QR
            </a>
            <a
              href={api.exportarUrl()}
              className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100"
            >
              Exportar a Excel
            </a>
            <button
              onClick={sincronizarAhora}
              disabled={sincronizando}
              className="rounded-lg bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:opacity-50"
            >
              {sincronizando ? "Sincronizando…" : "Sincronizar ahora"}
            </button>
          </div>
        </header>

        {mensaje && (
          <p className="text-sm text-slate-600 bg-white border border-slate-200 rounded-lg px-4 py-3">
            {mensaje}
          </p>
        )}

        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
          <TarjetaGrupo titulo="USEP" grupo={resumen!.usep} />
          <TarjetaGrupo titulo="Extranjeros" grupo={resumen!.extranjeros} />
        </div>

        <section className="bg-white border border-slate-200 rounded-xl p-6">
          <h2 className="font-medium text-slate-900 mb-3">Buscar asistente</h2>
          <input
            value={busqueda}
            onChange={(e) => setBusqueda(e.target.value)}
            placeholder="Nombre, matrícula o correo…"
            className="w-full rounded-lg border border-slate-300 px-4 py-2 text-sm text-slate-900 placeholder:text-slate-400 outline-none focus:ring-2 focus:ring-slate-400"
          />
          {buscando && <p className="text-sm text-slate-400 mt-2">Buscando…</p>}
          {resultadosBusqueda.length > 0 && (
            <ul className="mt-4 divide-y divide-slate-100">
              {resultadosBusqueda.map((a) => (
                <li key={a.id} className="py-3 flex items-center justify-between text-sm">
                  <div>
                    <p className="font-medium text-slate-900">{a.nombreCompleto}</p>
                    <p className="text-slate-500">
                      {a.origen} {a.matricula ? `· ${a.matricula}` : ""} {a.universidad ? `· ${a.universidad}` : ""}
                    </p>
                    {a.yaEntregado && (
                      <p className="text-emerald-600 text-xs mt-1">
                        Entregado {a.entregadoAt && new Date(a.entregadoAt).toLocaleString()} · {a.estacion} · {a.entregadoPor}
                      </p>
                    )}
                  </div>
                  {a.yaEntregado ? (
                    <button
                      onClick={() => deshacerEntrega(a)}
                      className="text-red-600 text-xs font-medium hover:underline whitespace-nowrap ml-4 cursor-pointer"
                    >
                      Deshacer entrega
                    </button>
                  ) : (
                    <button
                      onClick={() => marcarEntrada(a)}
                      className="text-emerald-600 text-xs font-medium hover:underline whitespace-nowrap ml-4 cursor-pointer"
                    >
                      Marcar entrada
                    </button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </section>

        <section className="bg-white border border-slate-200 rounded-xl">
          <h2 className="px-6 py-4 border-b border-slate-200 font-medium text-slate-900">
            Últimos escaneos
          </h2>
          <ul className="divide-y divide-slate-100">
            {resumen!.ultimosEscaneos.length === 0 && (
              <li className="px-6 py-4 text-sm text-slate-500">Todavía no hay escaneos.</li>
            )}
            {resumen!.ultimosEscaneos.map((e, i) => (
              <li key={i} className="px-6 py-3 flex items-center justify-between text-sm">
                <div>
                  <p className="font-medium text-slate-900">{e.nombreCompleto}</p>
                  <p className="text-slate-500">
                    {e.origen} · {e.estacion} · {e.realizadoPor}
                  </p>
                </div>
                <span className="text-slate-400">
                  {new Date(e.fechaHora).toLocaleTimeString()}
                </span>
              </li>
            ))}
          </ul>
        </section>
      </div>
    </main>
  );
}

function TarjetaGrupo({ titulo, grupo }: { titulo: string; grupo: { total: number; entregados: number; pendientes: number; qrEnviados: number } }) {
  const porcentaje = grupo.total > 0 ? Math.round((grupo.entregados / grupo.total) * 100) : 0;
  return (
    <div className="bg-white border border-slate-200 rounded-xl p-6">
      <h3 className="text-slate-500 text-sm font-medium mb-2">{titulo}</h3>
      <p className="text-3xl font-semibold text-slate-900">
        {grupo.entregados}
        <span className="text-slate-400 text-xl">/{grupo.total}</span>
      </p>
      <div className="mt-3 h-2 bg-slate-100 rounded-full overflow-hidden">
        <div
          className="h-full bg-emerald-500 transition-all"
          style={{ width: `${porcentaje}%` }}
        />
      </div>
      <p className="mt-2 text-sm text-slate-500">{grupo.pendientes} pendientes de entrada</p>
      <p className="mt-1 text-sm text-slate-500">Correos enviados: {grupo.qrEnviados}/{grupo.total}</p>
    </div>
  );
}