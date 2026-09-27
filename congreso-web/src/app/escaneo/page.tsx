"use client";

import { useEffect, useRef, useState, useCallback } from "react";
import { api, EscaneoResponse } from '../../lib/api';
type Estado = "config" | "listo" | "procesando" | "resultado";

export default function PaginaEscaneo() {
  const [estado, setEstado] = useState<Estado>("config");
  const [estacion, setEstacion] = useState("");
  const [realizadoPor, setRealizadoPor] = useState("");
  const [clave, setClave] = useState("");
  const [resultado, setResultado] = useState<EscaneoResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const scannerRef = useRef<import("html5-qrcode").Html5Qrcode | null>(null);
  const procesandoRef = useRef(false);
  const [camaraTrasera, setCamaraTrasera] = useState(true);

  // Recupera estación/nombre guardados de una sesión anterior en esta misma tablet.
  useEffect(() => {
    const est = localStorage.getItem("congreso_estacion");
    const nom = localStorage.getItem("congreso_realizadoPor");
    const cla = localStorage.getItem("congreso_clave");
    if (est && nom && cla) {
      setEstacion(est);
      setRealizadoPor(nom);
      setClave(cla);
      setEstado("listo");
    }
  }, []);

  const iniciarConfiguracion = (e: React.FormEvent) => {
    e.preventDefault();
    localStorage.setItem("congreso_estacion", estacion);
    localStorage.setItem("congreso_realizadoPor", realizadoPor);
    localStorage.setItem("congreso_clave", clave);
    setEstado("listo");
  };

  const procesarQr = useCallback(
    async (contenidoQr: string) => {
      if (procesandoRef.current) return; // evita doble-disparo del mismo QR
      procesandoRef.current = true;
      setEstado("procesando");
      try {
        const res = await api.escanear(contenidoQr, realizadoPor, estacion, clave);
        setResultado(res);
        setError(null);
      } catch (e) {
        const esNoAutorizado = e instanceof Error && e.message.includes("401");
        setError(
          esNoAutorizado
            ? "Clave incorrecta. Ve a 'Cambiar' y verifica la clave del staff."
            : "No se pudo conectar con el servidor. Revisa la conexión e intenta de nuevo."
        );
        setResultado(null);
      }
      setEstado("resultado");
    },
    [realizadoPor, estacion, clave]
  );

  // Inicia la cámara cuando estamos listos para escanear
  useEffect(() => {
    if (estado !== "listo") return;

    let activo = true;
    import("html5-qrcode").then(({ Html5Qrcode }) => {
      if (!activo) return;
      const scanner = new Html5Qrcode("lector-qr");
      scannerRef.current = scanner;
      scanner
        .start(
          { facingMode: camaraTrasera ? "environment" : "user" },
          {
            fps: 10,
            // Tamaño del recuadro calculado según la pantalla real (70% del lado más
            // chico de la vista de la cámara), en vez de un valor fijo en píxeles que
            // se ve mal o se corta en celulares angostos.
            qrbox: (viewfinderWidth, viewfinderHeight) => {
              const ladoMenor = Math.min(viewfinderWidth, viewfinderHeight);
              const tamano = Math.floor(ladoMenor * 0.7);
              return { width: tamano, height: tamano };
            },
          },
          (textoDecodificado) => {
            scanner.pause(true);
            procesarQr(textoDecodificado);
          },
          undefined
        )
        .catch(() => setError("No se pudo acceder a la cámara. Revisa los permisos del navegador."));
    });

    return () => {
      activo = false;
      scannerRef.current?.stop().catch(() => {});
    };
  }, [estado, procesarQr, camaraTrasera]);

  const siguienteEscaneo = useCallback(() => {
    setResultado(null);
    setError(null);
    procesandoRef.current = false;
    setEstado("listo");
    scannerRef.current?.resume();
  }, []);

  // Auto-avanza después de mostrar el resultado, para no depender de que alguien
  // toque la pantalla en cada persona. Los casos que requieren más atención
  // (duplicado o código inválido) se quedan un poco más de tiempo en pantalla.
  useEffect(() => {
    if (estado !== "resultado") return;
    const segundos = resultado?.resultado === "Ok" ? 3000 : 5000;
    const timeout = setTimeout(siguienteEscaneo, segundos);
    return () => clearTimeout(timeout);
  }, [estado, resultado, siguienteEscaneo]);

  const cambiarEstacion = () => {
    localStorage.removeItem("congreso_estacion");
    localStorage.removeItem("congreso_realizadoPor");
    localStorage.removeItem("congreso_clave");
    setEstado("config");
  };

  // --- Pantalla 1: configuración inicial (una sola vez por tablet) ---
  if (estado === "config") {
    return (
      <main className="min-h-screen flex items-center justify-center bg-slate-950 p-6">
        <form
          onSubmit={iniciarConfiguracion}
          className="w-full max-w-sm bg-slate-900 rounded-2xl p-8 space-y-5"
        >
          <h1 className="text-xl font-semibold text-white">Configurar esta tablet</h1>
          <div>
            <label className="block text-sm text-slate-300 mb-1">Estación</label>
            <input
              required
              value={estacion}
              onChange={(e) => setEstacion(e.target.value)}
              placeholder="Ej. Entrada 1"
              className="w-full rounded-lg bg-slate-800 text-white px-4 py-3 text-lg outline-none focus:ring-2 focus:ring-emerald-500"
            />
          </div>
          <div>
            <label className="block text-sm text-slate-300 mb-1">Tu nombre</label>
            <input
              required
              value={realizadoPor}
              onChange={(e) => setRealizadoPor(e.target.value)}
              placeholder="Ej. Laura"
              className="w-full rounded-lg bg-slate-800 text-white px-4 py-3 text-lg outline-none focus:ring-2 focus:ring-emerald-500"
            />
          </div>
          <div>
            <label className="block text-sm text-slate-300 mb-1">Clave del staff</label>
            <input
              required
              type="password"
              value={clave}
              onChange={(e) => setClave(e.target.value)}
              placeholder="Pide la clave a tu organizador"
              className="w-full rounded-lg bg-slate-800 text-white px-4 py-3 text-lg outline-none focus:ring-2 focus:ring-emerald-500"
            />
          </div>
          <button
            type="submit"
            className="w-full bg-emerald-500 hover:bg-emerald-400 text-slate-950 font-semibold rounded-lg py-3 text-lg transition"
          >
            Empezar a escanear
          </button>
        </form>
      </main>
    );
  }

  // --- Pantalla 2: cámara activa esperando un QR ---
  if (estado === "listo" || estado === "procesando") {
    return (
      <main className="min-h-screen bg-slate-950 flex flex-col">
        <header className="flex items-center justify-between px-4 py-3 text-slate-300 text-sm">
          <span>{estacion} · {realizadoPor}</span>
          <div className="flex items-center gap-4">
            <button
              onClick={() => setCamaraTrasera((v) => !v)}
              className="underline"
              title="Cambiar cámara"
            >
              Voltear cámara
            </button>
            <button onClick={cambiarEstacion} className="underline">Cambiar</button>
          </div>
        </header>
        <div className="flex-1 flex items-center justify-center p-4">
          <div
            id="lector-qr"
            className="w-full max-w-md aspect-square rounded-2xl overflow-hidden [&_video]:!w-full [&_video]:!h-full [&_video]:object-cover"
          />
        </div>
        {estado === "procesando" && (
          <p className="text-center text-slate-300 pb-8 text-lg">Verificando…</p>
        )}
        {error && (
          <p className="text-center text-red-400 pb-8 px-4">{error}</p>
        )}
      </main>
    );
  }

  // --- Pantalla 3: resultado (verde / rojo / amarillo) ---
  const colorPorResultado: Record<string, string> = {
    Ok: "bg-emerald-600",
    Duplicado: "bg-red-600",
    FirmaInvalida: "bg-amber-500",
    NoExiste: "bg-amber-500",
  };
  const color = error ? "bg-amber-500" : colorPorResultado[resultado?.resultado ?? ""] ?? "bg-slate-700";

  return (
    <main className={`min-h-screen flex flex-col items-center justify-center p-6 text-white ${color} transition-colors`}>
      {error ? (
        <>
          <p className="text-3xl font-bold mb-2">Sin conexión</p>
          <p className="text-lg text-center opacity-90">{error}</p>
        </>
      ) : resultado?.resultado === "Ok" ? (
        <>
          <p className="text-5xl font-bold mb-4">✓</p>
          <p className="text-3xl font-bold text-center">{resultado.nombreCompleto}</p>
          <p className="text-lg opacity-90 mt-2">
            {resultado.origen} {resultado.matricula ? `· ${resultado.matricula}` : ""} {resultado.seccion ? `· Sección ${resultado.seccion}` : ""}
          </p>
          {resultado.universidad && <p className="text-lg opacity-90">{resultado.universidad}</p>}
        </>
      ) : resultado?.resultado === "Duplicado" ? (
        <>
          <p className="text-5xl font-bold mb-4">✕</p>
          <p className="text-3xl font-bold text-center">QR ya utilizado</p>
          <p className="text-xl mt-2">{resultado.nombreCompleto}</p>
          <p className="text-lg opacity-90 mt-4">
            Entregado: {resultado.entregadoAtPrevio && new Date(resultado.entregadoAtPrevio).toLocaleTimeString()}
          </p>
          <p className="text-lg opacity-90">
            Por: {resultado.entregadoPorPrevio} · {resultado.estacionPrevia}
          </p>
        </>
      ) : (
        <>
          <p className="text-5xl font-bold mb-4">?</p>
          <p className="text-3xl font-bold text-center">
            {resultado?.resultado === "FirmaInvalida" ? "Código no válido" : "No encontrado en la lista"}
          </p>
          <p className="text-lg opacity-90 mt-2">Busca a esta persona manualmente o llama a un supervisor.</p>
        </>
      )}

      <button
        onClick={siguienteEscaneo}
        className="mt-10 bg-white/20 hover:bg-white/30 rounded-xl px-8 py-4 text-xl font-semibold"
      >
        Siguiente
      </button>
    </main>
  );
}