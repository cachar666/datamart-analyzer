using DatamartAnalyzer.Api.Services;

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
builder.Services.AddSingleton<IDocumentService, DocumentService>();
builder.Services.AddSingleton<IPrebuiltQueriesService, PrebuiltQueriesService>();
builder.Services.AddSingleton<IStoreRepository, StoreRepository>();
builder.Services.AddSingleton<IErpContextService, ErpContextService>();
builder.Services.AddSingleton<IDatamartConnectionResolver, DatamartConnectionResolver>();
builder.Services.AddSingleton<IStoreMigrator, StoreMigrator>();
builder.Services.AddHttpClient<IAnthropicService, AnthropicService>();

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
