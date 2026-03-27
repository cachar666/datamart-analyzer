using Dapper;
using DatamartAnalyzer.Api.Models;
using Microsoft.Data.SqlClient;

namespace DatamartAnalyzer.Api.Services;

public interface ISchemaService
{
    Task<List<SchemaColumn>> ObtenerSchemaAsync(string database);
    Task<List<ViewInfo>> ObtenerVistasAsync(string database);
    Task<SchemaContext> ObtenerContextoCompletoAsync(string database);
}

public class SchemaService : ISchemaService
{
    private readonly string _baseConnectionString;
    private readonly ILogger<SchemaService> _logger;

    // Cache simple en memoria por base de datos
    private readonly Dictionary<string, (SchemaContext Contexto, DateTime CachedAt)> _cache = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public SchemaService(IConfiguration config, ILogger<SchemaService> logger)
    {
        _baseConnectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string not found.");
        _logger = logger;
    }

    private string BuildConnectionString(string database)
    {
        var b = new SqlConnectionStringBuilder(_baseConnectionString)
        {
            InitialCatalog = database
        };
        return b.ConnectionString;
    }

    public async Task<SchemaContext> ObtenerContextoCompletoAsync(string database)
    {
        if (_cache.TryGetValue(database, out var cached)
            && DateTime.UtcNow - cached.CachedAt < CacheDuration)
        {
            _logger.LogInformation("Cache hit para schema de {database}", database);
            return cached.Contexto;
        }

        var columnas = await ObtenerSchemaAsync(database);
        var vistas = await ObtenerVistasAsync(database);
        var contexto = new SchemaContext(database, columnas, vistas);

        _cache[database] = (contexto, DateTime.UtcNow);
        return contexto;
    }

    public async Task<List<SchemaColumn>> ObtenerSchemaAsync(string database)
    {
        await using var conn = new SqlConnection(BuildConnectionString(database));
        await conn.OpenAsync();

        try
        {
            // Primero obtenemos los nombres reales de columnas de la vista
            const string colsSql = @"
                SELECT COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = 'ADP_DTM_VCONF'
                  AND TABLE_NAME   = 'DefinicionesCamposTabla'
                ORDER BY ORDINAL_POSITION";

            var columnas = (await conn.QueryAsync<string>(colsSql)).ToList();

            if (!columnas.Any())
            {
                _logger.LogError("La vista [ADP_DTM_VCONF].[DefinicionesCamposTabla] no existe o no tiene columnas en {database}.", database);
                return new List<SchemaColumn>();
            }

            _logger.LogInformation("Columnas de DefinicionesCamposTabla en {db}: {cols}", database, string.Join(", ", columnas));

            // Candidatos para cada campo lógico (en orden de preferencia)
            string Resolver(params string[] candidatos) =>
                candidatos.FirstOrDefault(c => columnas.Contains(c, StringComparer.OrdinalIgnoreCase)) ?? "";

            var colEsquema = Resolver("Esquema",       "Schema",        "TipoObjeto",  "ObjectType");
            var colTabla   = Resolver("Nombre Tabla",  "NombreTabla",   "TableName",   "ViewName",   "Tabla");
            var colCampo   = Resolver("Nombre Columna","NombreCampo",   "ColumnName",  "Campo",      "Columna");
            var colTipo    = Resolver("Tipo De Dato",  "TipoCampo",     "DataType",    "Tipo",       "TypeName");
            var colDesc    = Resolver("Descripcion",   "DescripcionCampo","Description","Desc");
            var colLlave   = Resolver("EsLlave",       "IsPrimaryKey",  "IsKey",       "Llave");
            var colFormato = Resolver("Formato",       "Format");

            var selectEsquema = string.IsNullOrEmpty(colEsquema) ? "NULL" : $"[{colEsquema}]";
            var selectTabla   = string.IsNullOrEmpty(colTabla)   ? "''"   : $"[{colTabla}]";
            var selectCampo   = string.IsNullOrEmpty(colCampo)   ? "''"   : $"[{colCampo}]";
            var selectTipo    = string.IsNullOrEmpty(colTipo)    ? "'nvarchar'" : $"[{colTipo}]";
            var selectDesc    = string.IsNullOrEmpty(colDesc)    ? "NULL" : $"[{colDesc}]";
            var selectLlave   = string.IsNullOrEmpty(colLlave)   ? "NULL" : $"[{colLlave}]";
            var selectFormato = string.IsNullOrEmpty(colFormato) ? "NULL" : $"[{colFormato}]";

            var sql = $@"
                SELECT {selectEsquema} AS Esquema,
                       {selectTabla}   AS NombreTabla,
                       {selectCampo}   AS NombreCampo,
                       {selectTipo}    AS TipoCampo,
                       {selectDesc}    AS DescripcionCampo,
                       {selectLlave}   AS EsLlave,
                       {selectFormato} AS Formato
                FROM [ADP_DTM_VCONF].[DefinicionesCamposTabla]
                ORDER BY Esquema, NombreTabla, NombreCampo";

            var rows = await conn.QueryAsync<dynamic>(sql);
            return rows.Select(r =>
            {
                // Esquema ADP_DTM_FACT → HECHO, ADP_DTM_DIM → DIMENSION
                string esquema     = (string)(r.Esquema ?? "");
                string nombreTabla = (string)(r.NombreTabla ?? "");
                string nombreCampo = (string)(r.NombreCampo ?? "");

                string tipoObjeto = esquema.EndsWith("FACT", StringComparison.OrdinalIgnoreCase) ? "HECHO"
                                  : esquema.EndsWith("DIM",  StringComparison.OrdinalIgnoreCase) ? "DIMENSION"
                                  : "HECHO";

                // Nombre completo para SQL: [ADP_DTM_DIM].[Actividades]
                string tablaCompleta = !string.IsNullOrEmpty(esquema)
                    ? $"[{esquema}].[{nombreTabla}]"
                    : nombreTabla;

                // Llave: columna explícita o inferida por convención SkId*
                bool esLlave = r.EsLlave is not null
                    ? Convert.ToBoolean(r.EsLlave)
                    : nombreCampo.StartsWith("SkId", StringComparison.OrdinalIgnoreCase);

                return new SchemaColumn(
                    NombreTabla:      tablaCompleta,
                    NombreCampo:      nombreCampo,
                    TipoCampo:        (string)(r.TipoCampo ?? "nvarchar"),
                    TipoObjeto:       tipoObjeto,
                    DescripcionCampo: (string?)r.DescripcionCampo,
                    EsLlave:          esLlave,
                    Formato:          (string?)r.Formato
                );
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo DefinicionesCamposTabla en {database}.", database);
            return new List<SchemaColumn>();
        }
    }

    public async Task<List<ViewInfo>> ObtenerVistasAsync(string database)
    {
        await using var conn = new SqlConnection(BuildConnectionString(database));
        await conn.OpenAsync();

        const string sql = @"
            SELECT
                TABLE_SCHEMA  AS Esquema,
                TABLE_NAME    AS NombreVista,
                NULL          AS Descripcion
            FROM INFORMATION_SCHEMA.VIEWS
            ORDER BY TABLE_SCHEMA, TABLE_NAME";

        try
        {
            var rows = await conn.QueryAsync<dynamic>(sql);
            return rows.Select(r => new ViewInfo(
                Esquema: (string)(r.Esquema ?? ""),
                NombreVista: (string)(r.NombreVista ?? ""),
                Descripcion: (string?)r.Descripcion
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obteniendo vistas de {database}", database);
            return new List<ViewInfo>();
        }
    }
}
