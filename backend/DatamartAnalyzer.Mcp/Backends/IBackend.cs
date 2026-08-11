using System.Text.Json;

namespace DatamartAnalyzer.Mcp.Backends;

/// <summary>
/// Lo que una herramienta pide dibujar. Son los datos sueltos que llegan del
/// modelo; recien al validarlos contra las columnas reales de la consulta se
/// convierten en una ConfiguracionGrafico guardable.
/// </summary>
public sealed record GraficoPedido(
    string? Tipo,
    string? EjeX,
    string? EjeY,
    string? Agrupacion,
    string? Titulo,
    string? Color);

/// <summary>
/// De donde saca los datos el conector. Hay dos caminos y las herramientas no
/// distinguen entre ellos:
///
///   API  — hay un Datamart Analyzer corriendo (tipico dentro del ERP). El conector
///          le pregunta a el, y hereda su modo ERP, su IA y su almacen.
///   SQL  — el usuario final solo tiene los cuatro datos que le entregan: servidor,
///          base, usuario y clave. El conector habla directo con SQL Server usando
///          las MISMAS reglas de Core que aplicaria la API.
///
/// Todo devuelve JsonElement a proposito: asi las herramientas leen la respuesta
/// igual venga de donde venga, y no hay dos formas del mismo dato.
/// </summary>
public interface IBackend
{
    /// <summary>"api" o "sql". Se reporta en la herramienta estado.</summary>
    string Modo { get; }

    /// <summary>Una linea legible de contra que se esta trabajando. Nunca la clave.</summary>
    string Descripcion { get; }

    // Sesion
    Task<string> ResolverDatamartAsync(string? pedido = null);
    void FijarDatamart(string nombre);
    IReadOnlyDictionary<string, List<string>> Filtros { get; }
    void FijarFiltros(Dictionary<string, List<string>>? filtros);
    void LimpiarFiltros();

    // Contexto
    Task<JsonElement> ContextoAsync();
    Task<JsonElement> EstadoAlmacenAsync(string db);
    Task<JsonElement> EsquemaAsync(string db);
    Task<JsonElement> ValoresFiltroAsync(string db, string tipo);

    // Consultar
    Task<JsonElement> PreguntarAsync(string db, string pregunta);

    /// <summary>Ejecuta SELECT. Los filtros de sesion se aplican salvo que se pida lo contrario.</summary>
    Task<JsonElement> EjecutarSqlAsync(string db, string sql, bool aplicarFiltros = true);

    // Preguntas sugeridas — el catalogo que ve la aplicacion web, para que Claude
    // sepa que se le puede preguntar a este datamart sin tener que adivinarlo.
    Task<JsonElement> ListarSugeridasAsync(string db, string? modulo);
    Task<JsonElement> CrearSugeridaAsync(string db, string pregunta, string categoria, string modulo);
    Task BorrarSugeridaAsync(string db, string id);
    Task RegistrarUsoSugeridaAsync(string db, string id);
    Task<JsonElement> ListarCategoriasAsync(string db, string? modulo);
    Task<JsonElement> CrearCategoriaAsync(string db, string nombre, string modulo);
    Task BorrarCategoriaAsync(string db, string id);

    // Consultas guardadas
    Task<JsonElement> ListarConsultasAsync(string db, string? buscar);
    Task<JsonElement> ObtenerConsultaAsync(string db, string id);
    Task<JsonElement> GuardarConsultaAsync(string db, string pregunta, string sql, string tipoRespuesta,
                                           string? tags, GraficoPedido? grafico);
    Task BorrarConsultaAsync(string db, string id);

    // Dashboards
    Task<JsonElement> ListarDashboardsAsync(string db);
    Task<JsonElement> VerDashboardAsync(string db, string id);
    Task<JsonElement> CrearDashboardAsync(string db, string nombre, string? descripcion);
    Task<JsonElement> ActualizarDashboardAsync(string db, string id, string? nombre, string? descripcion);
    Task BorrarDashboardAsync(string db, string id);
    Task<JsonElement> AgregarPanelAsync(string db, string dashboardId, string titulo,
                                        string? consultaId, string? sql, string tipoRespuesta,
                                        GraficoPedido? grafico);
    Task QuitarPanelAsync(string db, string dashboardId, string panelId);
}
