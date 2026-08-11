using DatamartAnalyzer.Core.Prebuilt;
using DatamartAnalyzer.Core.Rag;
using DatamartAnalyzer.Core.Schema;
using DatamartAnalyzer.Core.Sql;
using DatamartAnalyzer.Core.Store;
using DatamartAnalyzer.Mcp;
using DatamartAnalyzer.Mcp.Backends;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ═══════════════════════════════════════════════════════════════════════════════
//  Conector MCP de Datamart Analyzer. Dos modos, las mismas herramientas.
//
//  SQL DIRECTO (el del instalador para usuario final)
//    Los cuatro datos que ya se le entregan al usuario:
//      DATAMART_SERVIDOR   datamart.sincoerp.com,4263
//      DATAMART_BASE       SincoComercial_NuevaDW
//      DATAMART_USUARIO    UsuSincoComercial_NuevaDW
//      DATAMART_CLAVE      ********
//    No necesita que haya nada corriendo: el conector aplica las reglas de Core
//    (seguridad SQL, filtros, almacen, prebuilts, contexto ERP embebido) y habla
//    con SQL Server. La IA es Claude, que ya esta del otro lado.
//
//  VIA API (cuando el Datamart Analyzer ya esta desplegado, tipico dentro del ERP)
//      DATAMART_API        http://localhost:5000
//      DATAMART_DB         datamart por defecto (opcional)
//    Hereda el modo ERP: el datamart sale de dbo.ADPConfig.
//
//  Si estan los datos de SQL, gana SQL directo. Si no, se usa la API.
// ═══════════════════════════════════════════════════════════════════════════════

var builder = Host.CreateApplicationBuilder(args);

// CRÍTICO en stdio: por stdout viaja el protocolo MCP. Cualquier log que salga
// por ahí corrompe los mensajes y el cliente ve el servidor como roto.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Los avisos de configuracion se juntan y se emiten cuando el logger ya existe.
var avisos = new List<string>();

/// <summary>
/// Lee una variable de entorno tratando como AUSENTE un placeholder que quedo sin
/// sustituir.
///
/// Por que: en el .mcpb las variables se declaran como "${user_config.usuario}" y
/// Claude Desktop las reemplaza por lo que cargo el usuario. Un campo OPCIONAL que
/// el usuario nunca lleno puede llegar con el texto del placeholder intacto — no
/// vacio, literal. Sin este filtro, el conector intentaba loguearse a SQL Server
/// con el usuario "${user_config.usuario}" y fallaba con un error de red que no
/// tenia nada que ver con la causa.
/// </summary>
// Patron EXACTO del placeholder, no cualquier ${...}: una clave real podria — con
// muy poca probabilidad, pero podria — empezar con ${ y terminar con }.
var placeholder = new System.Text.RegularExpressions.Regex(
    @"^\$\{user_config\.[A-Za-z0-9_]+\}$", System.Text.RegularExpressions.RegexOptions.Compiled);

string? Leer(string nombre, bool recortar)
{
    var v = Environment.GetEnvironmentVariable(nombre);
    if (string.IsNullOrWhiteSpace(v)) return null;

    if (placeholder.IsMatch(v.Trim()))
    {
        avisos.Add($"{nombre} llego sin sustituir ({v.Trim()}); se toma como no configurada.");
        return null;
    }

    // La clave NO se recorta: un espacio al principio o al final puede ser parte de ella.
    return recortar ? v.Trim() : v;
}

string? Var(string nombre) => Leer(nombre, recortar: true);

var servidor = Var("DATAMART_SERVIDOR");
var basedatos = Var("DATAMART_BASE");
var modoSqlDirecto = servidor is not null && basedatos is not null;

if (modoSqlDirecto)
{
    var conexion = new ConexionDatamartDirecta(
        servidor!, basedatos!, Var("DATAMART_USUARIO"), Leer("DATAMART_CLAVE", recortar: false));

    builder.Services.AddSingleton(conexion);
    builder.Services.AddSingleton<IConexionDatamart>(conexion);

    // Los limites del conector son fijos: un usuario final no tiene appsettings.
    builder.Services.AddSingleton(new SqlLimites { TimeoutSegundos = 60, MaxFilas = 5000 });
    builder.Services.AddSingleton<ISqlExecutor, SqlExecutor>();
    builder.Services.AddSingleton<ISchemaService, SchemaService>();
    builder.Services.AddSingleton<IStoreRepository, StoreRepository>();
    builder.Services.AddSingleton<IStoreMigrator, StoreMigrator>();
    builder.Services.AddSingleton<IPrebuiltQueriesService, PrebuiltQueriesService>();

    // Sin ruta: DocumentService usa el corpus del ERP embebido en Core. En la
    // maquina del usuario final no existe la carpeta docs\ del repo.
    builder.Services.AddSingleton(new RagOptions());
    builder.Services.AddSingleton<IDocumentService, DocumentService>();

    builder.Services.AddSingleton<IBackend, BackendSql>();
}
else
{
    var apiUrl = (Var("DATAMART_API") ?? "http://localhost:5000").TrimEnd('/');

    builder.Services.AddSingleton(new McpOptions(
        ApiUrl: apiUrl,
        DatamartPorDefecto: Var("DATAMART_DB")));

    builder.Services.AddHttpClient<DatamartApi>(c =>
    {
        c.BaseAddress = new Uri(apiUrl + "/api/");
        // Una pregunta con IA puede tardar; el timeout por defecto de 100s se queda
        // corto cuando ademas hay que ejecutar el SQL resultante.
        c.Timeout = TimeSpan.FromMinutes(3);
    });

    builder.Services.AddSingleton<IBackend, BackendApi>();
}

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var host = builder.Build();

// Una linea por stderr al arrancar: cuando algo no anda, lo primero que hay que
// saber es contra que se conecto el conector. La clave nunca sale por aca.
var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatamartAnalyzer.Mcp");

// Los avisos van ANTES: si un campo llego sin sustituir, esa es la causa de todo lo
// que falle despues, y tiene que estar arriba en el log y no perdido al final.
foreach (var aviso in avisos)
    log.LogWarning("Configuracion: {aviso}", aviso);

log.LogInformation("Conector listo — {desc}", host.Services.GetRequiredService<IBackend>().Descripcion);

await host.RunAsync();

/// <summary>Configuración del modo API, por variables de entorno.</summary>
public record McpOptions(string ApiUrl, string? DatamartPorDefecto);
