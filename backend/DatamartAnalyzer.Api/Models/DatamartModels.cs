using Microsoft.Data.SqlClient;

namespace DatamartAnalyzer.Api.Models;

// ═══════════════════════════════════════════════════════════════════════════════
//  Conexión a un datamart — UN SOLO modelo para todos los caminos.
//
//  Los datos que se entregan al conectar un datamart son siempre estos cuatro:
//
//      Servidor SQL = datamart.sincoerp.com,4263
//      Base Datos   = SincoEthussTCoindustrialDW
//      Usuario      = UsuSincoEthussTCoindustrialDW
//      Clave        = ********
//
//  Se declaran igual estén donde estén:
//    · appsettings → sección "Datamarts" (modo standalone y desarrollo)
//    · dbo.ADPConfig del ERP → claves DATAMARTSRV / DATAMART / DATAMARTUSR / DATAMARTPWD
//
//  El conector MCP no maneja credenciales: le pide los datamarts a la API por
//  nombre y la API resuelve la conexión. Una sola implementación de la consulta.
// ═══════════════════════════════════════════════════════════════════════════════

public record DatamartConexion(
    string Nombre,
    string Servidor,
    string Base,
    string? Usuario = null,
    string? Clave = null,
    string? Origen = null
)
{
    /// <summary>Sin usuario se asume autenticación de Windows (caso YOSEMITE en red local).</summary>
    public bool UsaAutenticacionSql => !string.IsNullOrWhiteSpace(Usuario);

    public string ConstruirConnectionString(int timeoutSegundos = 30)
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = Servidor,
            InitialCatalog = Base,
            CommandTimeout = timeoutSegundos,
            // Los datamarts publicados usan certificado propio; sin esto la conexión
            // falla con "The certificate chain was issued by an authority that is not trusted".
            TrustServerCertificate = true
        };

        if (UsaAutenticacionSql)
        {
            b.UserID = Usuario;
            b.Password = Clave ?? "";
        }
        else
        {
            b.IntegratedSecurity = true;
        }

        return b.ConnectionString;
    }

    public bool EsValida(out string? error)
    {
        if (string.IsNullOrWhiteSpace(Servidor)) { error = "Falta el servidor."; return false; }
        if (string.IsNullOrWhiteSpace(Base)) { error = "Falta la base de datos."; return false; }
        if (UsaAutenticacionSql && string.IsNullOrWhiteSpace(Clave))
        {
            error = $"El datamart '{Nombre}' declara usuario '{Usuario}' pero no tiene clave. " +
                    "Ponela en appsettings.Development.json o en user-secrets.";
            return false;
        }
        error = null;
        return true;
    }
}

/// <summary>
/// Lo que se le muestra al cliente. NUNCA incluye usuario ni clave.
/// </summary>
public record DatamartInfoDto(
    string Nombre,
    string Servidor,
    string Base,
    string Origen,
    bool Alcanzable,
    string? Mensaje
);

/// <summary>Sección "Datamarts" de appsettings.</summary>
public class DatamartOptions
{
    public string Nombre { get; set; } = "";
    public string Servidor { get; set; } = "";
    public string Base { get; set; } = "";
    public string? Usuario { get; set; }
    public string? Clave { get; set; }
}
