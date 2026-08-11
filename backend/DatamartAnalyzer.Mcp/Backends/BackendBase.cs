using System.Text.Json;
using DatamartAnalyzer.Core.Charts;
using DatamartAnalyzer.Core.Models;

namespace DatamartAnalyzer.Mcp.Backends;

/// <summary>
/// Lo que los dos backends comparten: el estado de la conversacion. El datamart
/// activo y los filtros viven mientras viva el proceso, que es exactamente lo que
/// dura una sesion del conector en Claude Desktop.
/// </summary>
public abstract class BackendBase : IBackend
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private string? _datamartSesion;
    private Dictionary<string, List<string>> _filtros = new(StringComparer.OrdinalIgnoreCase);

    public abstract string Modo { get; }
    public abstract string Descripcion { get; }

    public IReadOnlyDictionary<string, List<string>> Filtros => _filtros;

    public void FijarDatamart(string nombre) => _datamartSesion = nombre;

    public void FijarFiltros(Dictionary<string, List<string>>? filtros) =>
        _filtros = filtros is null
            ? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, List<string>>(filtros, StringComparer.OrdinalIgnoreCase);

    public void LimpiarFiltros() => _filtros.Clear();

    /// <summary>Lo que se pidio explicitamente gana; despues lo fijado en la sesion.</summary>
    protected string? DatamartDeSesion => _datamartSesion;

    /// <summary>Los filtros a aplicar, o null si no hay ninguno.</summary>
    protected IReadOnlyDictionary<string, List<string>>? FiltrosONull =>
        _filtros.Count > 0 ? _filtros : null;

    /// <summary>Serializa cualquier objeto al JsonElement que devuelven las operaciones.</summary>
    protected static JsonElement AJson(object? valor) =>
        JsonSerializer.SerializeToElement(valor, Json);

    public abstract Task<string> ResolverDatamartAsync(string? pedido = null);
    public abstract Task<JsonElement> ContextoAsync();
    public abstract Task<JsonElement> EstadoAlmacenAsync(string db);
    public abstract Task<JsonElement> EsquemaAsync(string db);
    public abstract Task<JsonElement> ValoresFiltroAsync(string db, string tipo);
    public abstract Task<JsonElement> PreguntarAsync(string db, string pregunta);
    public abstract Task<JsonElement> EjecutarSqlAsync(string db, string sql, bool aplicarFiltros = true);
    public abstract Task<JsonElement> ListarSugeridasAsync(string db, string? modulo);
    public abstract Task<JsonElement> CrearSugeridaAsync(string db, string pregunta, string categoria, string modulo);
    public abstract Task BorrarSugeridaAsync(string db, string id);
    public abstract Task RegistrarUsoSugeridaAsync(string db, string id);
    public abstract Task<JsonElement> ListarCategoriasAsync(string db, string? modulo);
    public abstract Task<JsonElement> CrearCategoriaAsync(string db, string nombre, string modulo);
    public abstract Task BorrarCategoriaAsync(string db, string id);
    public abstract Task<JsonElement> ListarConsultasAsync(string db, string? buscar);
    public abstract Task<JsonElement> ObtenerConsultaAsync(string db, string id);
    public abstract Task<JsonElement> GuardarConsultaAsync(string db, string pregunta, string sql, string tipoRespuesta,
                                                           string? tags, GraficoPedido? grafico);
    public abstract Task BorrarConsultaAsync(string db, string id);
    public abstract Task<JsonElement> ListarDashboardsAsync(string db);
    public abstract Task<JsonElement> VerDashboardAsync(string db, string id);
    public abstract Task<JsonElement> CrearDashboardAsync(string db, string nombre, string? descripcion);
    public abstract Task<JsonElement> ActualizarDashboardAsync(string db, string id, string? nombre, string? descripcion);
    public abstract Task BorrarDashboardAsync(string db, string id);
    public abstract Task<JsonElement> AgregarPanelAsync(string db, string dashboardId, string titulo,
                                                        string? consultaId, string? sql, string tipoRespuesta,
                                                        GraficoPedido? grafico);
    public abstract Task QuitarPanelAsync(string db, string dashboardId, string panelId);

    /// <summary>Un Guid o un error claro; los ids del almacen son Guid.</summary>
    protected static Guid AGuid(string id, string que) =>
        Guid.TryParse(id, out var g) ? g
            : throw new InvalidOperationException($"'{id}' no es un id valido de {que}.");

    // ── Graficos ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Convierte lo que pidio la herramienta en una configuracion guardable, y la
    /// valida contra las columnas REALES que devuelve la consulta.
    ///
    /// Ejecutar el SQL una vez de mas al guardar parece caro, pero es la unica forma
    /// de saber como se llaman las columnas. La alternativa es guardar un grafico que
    /// nombra una columna que no existe: no falla nada, no avisa nada, y el usuario
    /// se entera semanas despues cuando abre el dashboard y ve un recuadro vacio.
    /// Ahi ya no hay como saber que fue lo que se escribio mal.
    ///
    /// Es igual para los dos backends a proposito: un grafico que el modo SQL rechaza
    /// no puede colarse por el modo API.
    /// </summary>
    protected async Task<ConfiguracionGrafico?> PrepararGraficoAsync(
        string db, string? sql, string? consultaId, GraficoPedido? pedido)
    {
        if (pedido is null) return null;

        var g = Graficos.Construir(pedido.Tipo, pedido.EjeX, pedido.EjeY,
                                   pedido.Agrupacion, pedido.Titulo, pedido.Color);

        if (g is null)
        {
            // Pidio algo del grafico pero incompleto. Callarse dejaria un panel que
            // dice "grafico" y no dibuja — el bug que esto viene a cerrar.
            var pidioAlgo = !string.IsNullOrWhiteSpace(pedido.Tipo)
                         || !string.IsNullOrWhiteSpace(pedido.EjeX)
                         || !string.IsNullOrWhiteSpace(pedido.EjeY);

            if (pidioAlgo)
                throw new InvalidOperationException(
                    "Para dibujar un grafico hacen falta los dos ejes: graficoEjeX (la categoria) " +
                    "y graficoEjeY (el valor numerico). Ambos tienen que ser nombres de columna " +
                    "exactos del resultado de la consulta.");

            return null;
        }

        var consulta = sql;
        if (string.IsNullOrWhiteSpace(consulta) && !string.IsNullOrWhiteSpace(consultaId))
        {
            var c = await ObtenerConsultaAsync(db, consultaId);
            if (c.TryGetProperty("sql", out var s)) consulta = s.GetString();
        }

        // Sin SQL no hay contra que validar. Se guarda igual: el front avisa al pintar.
        if (string.IsNullOrWhiteSpace(consulta)) return g;

        var r = await EjecutarSqlAsync(db, consulta, aplicarFiltros: false);
        var (columnas, primeraFila) = PrimeraFila(r);

        // Si la consulta no devolvio filas no se puede saber las columnas. No es motivo
        // para rechazar el grafico: puede que hoy no haya datos y manana si.
        if (columnas.Count == 0) return g;

        if (!Graficos.Validar(g, columnas, primeraFila, out var motivo))
            throw new InvalidOperationException(motivo!);

        return g;
    }

    /// <summary>
    /// Las columnas y la primera fila del resultado, con los valores desenvueltos a
    /// tipos de C#. Sin desenvolver, todo es JsonElement y "¿es numerico?" siempre da
    /// que no.
    /// </summary>
    private static (List<string> Columnas, Dictionary<string, object?>? Fila) PrimeraFila(JsonElement resultado)
    {
        if (!resultado.TryGetProperty("datos", out var datos) ||
            datos.ValueKind != JsonValueKind.Array || datos.GetArrayLength() == 0)
            return ([], null);

        var primera = datos[0];
        if (primera.ValueKind != JsonValueKind.Object) return ([], null);

        var columnas = new List<string>();
        var fila = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in primera.EnumerateObject())
        {
            columnas.Add(p.Name);
            fila[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.Number => p.Value.TryGetDouble(out var d) ? d : null,
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        }

        return (columnas, fila);
    }
}
