using DatamartAnalyzer.Core.Prebuilt;
using DatamartAnalyzer.Core.Rag;
using DatamartAnalyzer.Core.Schema;
using DatamartAnalyzer.Core.Sql;
using DatamartAnalyzer.Core.Store;
using DatamartAnalyzer.Mcp;
using DatamartAnalyzer.Mcp.Backends;
using DatamartAnalyzer.Mcp.Http;

// ═══════════════════════════════════════════════════════════════════════════════
//  Datamart Analyzer — servidor MCP sobre HTTP.
//
//  El hermano remoto del conector stdio. Existe porque los conectores locales no
//  existen en Claude web ni en movil: para consultar el datamart desde el celular
//  la conexion tiene que salir de un servidor publicado, no del PC del usuario.
//
//  CONVIVE con el conector local, no lo reemplaza. Los dos escriben el mismo
//  almacen ADP_DTM_APP, asi que un dashboard creado desde el escritorio se ve
//  desde el telefono.
//
//  Lo que este host NO hace: tocar DatamartAnalyzer.Mcp. Se referencia tal cual y
//  se reusan sus herramientas y sus backends. Todo lo que aca tiene que ser
//  distinto se resuelve abajo, con las lifetimes de DI.
// ═══════════════════════════════════════════════════════════════════════════════

var builder = WebApplication.CreateBuilder(args);

// A diferencia del conector stdio, aca stdout NO transporta el protocolo: se puede
// loguear a consola sin romper nada. La regla de "todo a stderr" es del otro host
// y se queda alli — por eso los dos Program.cs estan separados.

// ── El datamart ────────────────────────────────────────────────────────────────
// FASE A2: sale de configuracion, un datamart fijo, para poder probar el transporte.
// FASE A3: sale del token de la sesion, y esta misma clase se construye por sesion
//          con la credencial que cargo el usuario en la pantalla de OAuth. Por eso
//          ya se registra Scoped: cuando llegue el token, cambia el origen del dato
//          y no la forma de registrarlo.
var servidor = builder.Configuration["Datamart:Servidor"];
var basedatos = builder.Configuration["Datamart:Base"];

if (string.IsNullOrWhiteSpace(servidor) || string.IsNullOrWhiteSpace(basedatos))
    throw new InvalidOperationException(
        "Faltan Datamart:Servidor y Datamart:Base en la configuracion. " +
        "Cargalos en appsettings.Development.json para probar el host.");

builder.Services.AddScoped(_ => new ConexionDatamartDirecta(
    servidor,
    basedatos,
    builder.Configuration["Datamart:Usuario"],   // vacio = autenticacion de Windows
    builder.Configuration["Datamart:Clave"]));

builder.Services.AddScoped<IConexionDatamart>(sp => sp.GetRequiredService<ConexionDatamartDirecta>());

// ── Lifetimes: aca esta TODO el aislamiento entre sesiones ─────────────────────
//
// El conector stdio registra todo esto como Singleton, y hace bien: hay un proceso
// por cliente, asi que el proceso ES la sesion. Aca hay muchas sesiones en un
// proceso, y lo que alli era correcto se vuelve un cruce silencioso de datos.
//
// SCOPED — tienen estado de sesion, o dependen de algo que lo tiene:
//
//   IBackend (BackendSql)  El datamart activo y los filtros son campos de INSTANCIA
//                          de BackendBase (no estaticos). Un scope por sesion basta
//                          para aislarlos: por eso no hizo falta refactorizar nada.
//
//   ISchemaService         Su cache es un Dictionary NO concurrente indexado solo
//                          por nombre de base. Con varias sesiones eso son dos
//                          problemas: escrituras concurrentes sin proteccion, y dos
//                          clientes con bases homonimas en servidores distintos
//                          compartiendo esquema. En stdio nunca se vio porque hay
//                          una sola sesion.
//
//   IStoreMigrator         Mismo problema de clave: marca "verificada" una base por
//                          nombre, y otra credencial podria no poder abrirla.
//
//   ISqlExecutor           No tiene estado propio, pero depende de IConexionDatamart,
//   IStoreRepository       que si es Scoped. Un Singleton que depende de un Scoped es
//                          una captive dependency y el contenedor la rechaza.
//
// SINGLETON — sin estado por sesion y sin dependencia de la conexion:
builder.Services.AddSingleton(new SqlLimites { TimeoutSegundos = 60, MaxFilas = 5000 });
builder.Services.AddSingleton<IPrebuiltQueriesService, PrebuiltQueriesService>();
builder.Services.AddSingleton(new RagOptions());   // sin ruta: corpus del ERP embebido en Core
builder.Services.AddSingleton<IDocumentService, DocumentService>();

builder.Services.AddScoped<ISqlExecutor, SqlExecutor>();
builder.Services.AddScoped<ISchemaService, SchemaService>();
builder.Services.AddScoped<IStoreRepository, StoreRepository>();
builder.Services.AddScoped<IStoreMigrator, StoreMigrator>();

// ── El estado de sesion, que sobre HTTP no existe solo ─────────────────────────
//
// Medido en el spike de A1: el scope de DI se recrea en CADA tool call, incluso con
// Mcp-Session-Id estable y Stateless = false. Un BackendSql Scoped nace vacio cada
// vez, asi que fijar_filtros no tendria efecto sobre la llamada siguiente.
//
// El estado se guarda aparte, indexado por el TOKEN y no por la sesion MCP: es lo
// unico estable que acompana a todas las llamadas de un usuario, y sobrevive al modo
// stateless que la revision 2026-07-28 del protocolo (SEP-2567) vuelve obligatorio
// al eliminar Mcp-Session-Id.
//
// Caduca por inactividad a proposito. Un filtro de empresa olvidado sigue recortando
// cifras sin ninguna senal visible: es preferible que haya que volver a fijarlo.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IEstadoDeSesiones>(_ => new EstadoDeSesiones(
    TimeSpan.FromMinutes(builder.Configuration.GetValue("Sesiones:ExpiracionMinutos", 45))));

builder.Services.AddScoped<ClaveDeSesion>();
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IEstadoDeSesiones>()
      .Obtener(sp.GetRequiredService<ClaveDeSesion>().Resolver()));

// BackendSql se registra concreto para que el decorador pueda pedirlo. La unica
// implementacion de IBackend que ven las herramientas es la decorada.
builder.Services.AddScoped<BackendSql>();
builder.Services.AddScoped<IBackend, BackendConEstadoPorToken>();

// Solo para el spike de la fase A1. Ver DiagnosticoTools.
builder.Services.AddScoped<MarcaDeScope>();

// ── MCP ────────────────────────────────────────────────────────────────────────
// DatamartTools viene del proyecto del conector local, sin modificar: el mismo
// catalogo de 28 herramientas, con los mismos nombres y las mismas firmas.
builder.Services
    .AddMcpServer()
    // Se deja el DEFAULT (Stateless = true) a proposito, y no es indiferencia:
    //
    // Se probaron las dos ramas. Con Stateless = false el SDK si emite un
    // Mcp-Session-Id estable, pero el scope de DI se recrea igual en cada llamada,
    // asi que NO resuelve el problema del estado — solo agrega afinidad de sesion.
    // Y la revision 2026-07-28 del protocolo (SEP-2567) elimina Mcp-Session-Id: el
    // modo con sesion es la rama heredada, no la correcta.
    //
    // Stateless ademas permite balancear carga sin afinidad, que es justo lo que hace
    // falta el dia que esto se mueva del servidor central a Azure.
    .WithHttpTransport()
    .WithTools<DatamartTools>();

// diag_scope NO forma parte del contrato: son 28 herramientas, no 29. Se expone solo
// en desarrollo para poder repetir la medicion del spike; en cualquier otro entorno
// el catalogo tiene que ser identico al del conector local.
if (builder.Environment.IsDevelopment())
    builder.Services.AddMcpServer().WithTools<DiagnosticoTools>();

var app = builder.Build();

app.MapMcp();

app.Run();
