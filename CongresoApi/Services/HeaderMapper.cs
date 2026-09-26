namespace CongresoApi.Services;

/// <summary>
/// Los formularios de Google Forms traen encabezados largos y variables
/// ("Nombre completo. Cuide que sea correcto pc..."). Esta clase busca la
/// columna por el PREFIJO del encabezado, para que un cambio de redacción
/// en el formulario no rompa la importación.
/// </summary>
public class HeaderMapper
{
    private readonly Dictionary<string, int> _indicePorPrefijoEncontrado = new();
    private readonly IList<object> _encabezados;

    public HeaderMapper(IList<object> filaEncabezados)
    {
        _encabezados = filaEncabezados;
    }

    /// <summary>
    /// Busca el índice de la primera columna cuyo encabezado empieza con el prefijo dado
    /// (comparación sin distinguir mayúsculas/acentos simples).
    /// Lanza excepción si no la encuentra, porque sin esa columna la importación no debe continuar.
    /// </summary>
    public int Requerido(string prefijo)
    {
        var idx = BuscarIndice(prefijo);
        if (idx is null)
            throw new InvalidOperationException(
                $"No se encontró la columna que empieza con '{prefijo}'. " +
                $"Encabezados disponibles: {string.Join(" | ", _encabezados)}");
        return idx.Value;
    }

    /// <summary>Igual que Requerido, pero regresa null en vez de lanzar excepción.</summary>
    public int? Opcional(string prefijo) => BuscarIndice(prefijo);

    private int? BuscarIndice(string prefijo)
    {
        if (_indicePorPrefijoEncontrado.TryGetValue(prefijo, out var cacheado))
            return cacheado;

        var prefijoNormalizado = Normalizar(prefijo);

        for (var i = 0; i < _encabezados.Count; i++)
        {
            var encabezado = Normalizar(_encabezados[i]?.ToString() ?? "");
            if (encabezado.StartsWith(prefijoNormalizado))
            {
                _indicePorPrefijoEncontrado[prefijo] = i;
                return i;
            }
        }

        return null;
    }

    private static string Normalizar(string texto) =>
        texto.Trim().ToLowerInvariant()
            .Replace("á", "a").Replace("é", "e").Replace("í", "i")
            .Replace("ó", "o").Replace("ú", "u");
}