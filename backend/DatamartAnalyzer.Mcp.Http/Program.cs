using DatamartAnalyzer.Core.Prebuilt;
using DatamartAnalyzer.Core.Rag;
using DatamartAnalyzer.Core.Schema;
using DatamartAnalyzer.Core.Sql;
using DatamartAnalyzer.Core.Store;
using DatamartAnalyzer.Mcp;
using DatamartAnalyzer.Mcp.Backends;
using DatamartAnalyzer.Mcp.Http;
using DatamartAnalyzer.Mcp.Http.Auth;

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

// ── Autenticacion ──────────────────────────────────────────────────────────────
// La identidad del usuario ES la credencial de su datamart: no hay directorio
// corporativo detras. Eso permite atender a clientes fuera de SINCO el primer dia.
// El servidor de autorizacion vive en Auth/EndpointsDeAutorizacion.cs.
builder.Services.AddSingleton(new ProtectorDeCredenciales(
    // La llave NO sale de appsettings: ese archivo se versiona y se copia.
    Environment.GetEnvironmentVariable("DATAMART_LLAVE_CIFRADO")));

builder.Services.AddSingleton<IAlmacenDeAutorizacion>(_ => new AlmacenDeAutorizacionEnMemoria(
    TimeSpan.FromHours(builder.Configuration.GetValue("Auth:HorasDeToken", 8))));

builder.Services.AddScoped<ValidadorDeCredencial>();

builder.Services
    .AddAuthentication(TokenOpacoDefaults.Esquema)
    .AddScheme<TokenOpacoOptions, TokenOpacoHandler>(TokenOpacoDefaults.Esquema, _ => { });

builder.Services.AddAuthorization();

// ── El datamart ────────────────────────────────────────────────────────────────
// Sale de la credencial del token. El respaldo por configuracion existe solo para
// poder levantar el host sin OAuth durante el desarrollo; en cualquier otro entorno,
// una llamada sin credencial autenticada no tiene datamart y debe fallar.
builder.Services.AddScoped(sp =>
{
    var credencial = CredencialDeLaSolicitud.De(sp.GetRequiredService<IHttpContextAccessor>().HttpContext);

    if (credencial is not null)
        return new ConexionDatamartDirecta(
            credencial.Servidor, credencial.Base, credencial.Usuario, credencial.Clave);

    var cfg = sp.GetRequiredService<IConfiguration>();
    var servidor = cfg["Datamart:Servidor"];
    var basedatos = cfg["Datamart:Base"];

    if (string.IsNullOrWhiteSpace(servidor) || string.IsNullOrWhiteSpace(basedatos))
        throw new InvalidOperationException(
            "Esta llamada no trae credencial autenticada y no hay datamart de respaldo " +
            "configurado. Conectate primero con el flujo de OAuth.");

    return new ConexionDatamartDirecta(servidor, basedatos, cfg["Datamart:Usuario"], cfg["Datamart:Clave"]);
});

builder.Services.AddScoped<IConexionDatamart>(sp => sp.GetRequiredService<ConexionDatamartDirecta>());

// ── Lifetimes: aca esta TODO el aislamiento entre sesiones ─────────────────────
//
// El conector stdio registra todo esto como Singleton, y hace bien: hay un proceso
// por cliente, asi que el proceso ES la sesion. Aca hay muchas sesiones en un
// proceso, y lo que alli era correcto se vuelve un cruce silencioso de datos.
//
// SCOPED — tienen estado de sesion, o dependen de algo que lo tiene:
//
//   IBackend               El decorador BackendConEstadoPorToken. Scoped porque se
//   BackendSql             hidrata en cada llamada desde el almacen de estado: el
//                          scope de DI se recrea por request, asi que el BackendSql
//                          interno nace vacio cada vez (medido en el spike de A1).
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
// El estado se guarda aparte, indexado por la IDENTIDAD DE LA CREDENCIAL y no por la
// sesion MCP: sobrevive al modo stateless que la revision 2026-07-28 del protocolo
// (SEP-2567) vuelve obligatorio al eliminar Mcp-Session-Id. Se usa la credencial y no
// el token porque el token rota al refrescarse, y colgar el estado de el perderia los
// filtros en cada refresco sin ninguna razon visible para el usuario.
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
    .AddMcpServer(o =>
    {
        // Sin esto, el nombre que viaja en el handshake de "initialize" es el del
        // ensamblado (DatamartAnalyzer.Mcp.Http) — no dice nada al usuario final.
        //
        // OJO con lo que esto NO garantiza: es el nombre que lleva el PROTOCOLO MCP,
        // no necesariamente la etiqueta que Claude muestra en Customize > Connectors.
        // Verificado contra la documentacion de Anthropic: sin coincidencia en su
        // Connectors Directory (curado por Anthropic, no algo que se autoregistre),
        // esa lista muestra la etiqueta generica "Custom". Fijarlo aca es la unica
        // palanca que tenemos del lado del codigo — no cuesta nada, y si el cliente
        // llega a leerlo, ya esta puesto.
        o.ServerInfo = new() { Name = "adpro-datamart-analyzer", Title = "ADPRO Datamart Analyzer", Version = "1.0.0" };
    })
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

app.UseAuthentication();
app.UseAuthorization();

// El servidor de autorizacion: /authorize, /token, /register y su metadata.
app.MapAutorizacion();

// ── El endpoint MCP ────────────────────────────────────────────────────────────
//
// RequireAuthorization() de verdad, no declarativo. La API de este repo llama a
// UseAuthorization() sin registrar ningun esquema y sin un solo [Authorize]: parece
// protegida y no lo esta. Ese es exactamente el error que aca no se puede repetir,
// porque este host SI se va a publicar.
//
// En desarrollo se deja abierto para poder probar contra localhost sin montar el
// flujo de OAuth completo — y por eso el host escucha solo en 127.0.0.1.
var mcp = app.MapMcp();

if (!app.Environment.IsDevelopment())
    mcp.RequireAuthorization();

app.Run();
