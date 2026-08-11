using DatamartAnalyzer.Api.Services;
using DatamartAnalyzer.Core.Prebuilt;
using DatamartAnalyzer.Core.Rag;
using DatamartAnalyzer.Core.Schema;
using DatamartAnalyzer.Core.Sql;
using DatamartAnalyzer.Core.Store;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Datamart Analyzer API", Version = "v1" });
});

// App Services
builder.Services.AddSingleton<ISqlServerService, SqlServerService>();
builder.Services.AddSingleton<ISchemaService, SchemaService>();
builder.Services.AddSingleton<IPrebuiltQueriesService, PrebuiltQueriesService>();

// Ejecutor de consultas: valida, inyecta filtros, acota filas. Lo comparte el conector.
builder.Services.AddSingleton(new SqlLimites
{
    TimeoutSegundos = builder.Configuration.GetValue("QuerySettings:TimeoutSeconds", 30),
    MaxFilas = builder.Configuration.GetValue("QuerySettings:MaxRows", 5000),
});
builder.Services.AddSingleton<ISqlExecutor, SqlExecutor>();
builder.Services.AddSingleton<IStoreRepository, StoreRepository>();
builder.Services.AddSingleton<IErpContextService, ErpContextService>();
builder.Services.AddSingleton<IStoreMigrator, StoreMigrator>();
builder.Services.AddHttpClient<IAnthropicService, AnthropicService>();

// El corpus del ERP: en la API se lee del archivo del repo (editarlo se ve al
// reiniciar, sin recompilar). Si la ruta no existe, DocumentService cae al recurso
// embebido en Core — el mismo que usa el conector MCP.
builder.Services.AddSingleton(new RagOptions
{
    DocumentPath = builder.Configuration["Rag:DocumentPath"]
});
builder.Services.AddSingleton<IDocumentService, DocumentService>();

// Una sola implementación, dos contratos: el resolutor completo para la API y el
// contrato mínimo IConexionDatamart que consume el almacén en Core.
builder.Services.AddSingleton<IDatamartConnectionResolver, DatamartConnectionResolver>();
builder.Services.AddSingleton<IConexionDatamart>(sp =>
    sp.GetRequiredService<IDatamartConnectionResolver>());

// CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173" };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Forzar carga del documento ERP al iniciar
app.Services.GetRequiredService<IDocumentService>();

// En desarrollo el frontend usa el proxy de Vite hacia http://localhost:5000.
// Redirigir a HTTPS provoca un 307 hacia https://localhost:7001 que el navegador
// rechaza con ERR_CERT_AUTHORITY_INVALID (certificado de dev no confiado).
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseAuthorization();
app.MapControllers();

app.Run();
