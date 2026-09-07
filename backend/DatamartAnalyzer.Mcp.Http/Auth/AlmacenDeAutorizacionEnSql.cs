using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace DatamartAnalyzer.Mcp.Http.Auth;

/// <summary>
/// Codigos y tokens en SQL Server — sobrevive a un reinicio del host.
///
/// Una sola tabla para los tres (codigo, access, refresh), distinguidos por Tipo:
/// asi hay un solo lugar donde limpiar lo vencido, igual que el diccionario unico
/// que hubiera sido mas simple en memoria si no fuera porque cada uno expira
/// distinto. El esquema esta en sql/02-create-oauth-store.sql.
///
/// IAlmacenDeAutorizacion es sincrono porque la version en memoria no necesitaba
/// otra cosa; esta implementacion respeta esa firma (ADO.NET sincrono) para no
/// tener que tocar EndpointsDeAutorizacion.cs ni TokenOpacoHandler por este cambio.
/// Si el volumen algun dia lo justifica, el async cruza las dos implementaciones
/// a la vez, no una sola.
///
/// El canje de codigo y la rotacion de refresh usan DELETE ... OUTPUT: es la forma
/// atomica de "sacalo y devolveme lo que sacaste" en SQL Server, el equivalente
/// exacto del TryRemove de ConcurrentDictionary que usa la version en memoria. Sin
/// esto, dos canjes concurrentes del mismo codigo podrian los dos leerlo antes de
/// que ninguno lo borre.
/// </summary>
public sealed class AlmacenDeAutorizacionEnSql(string cadenaConexion, TimeSpan vidaDelAccessToken)
    : IAlmacenDeAutorizacion
{
    private static readonly TimeSpan VidaDelCodigo = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan VidaDelRefresh = TimeSpan.FromDays(30);

    private const int TipoCodigo = 1;
    private const int TipoAccess = 2;
    private const int TipoRefresh = 3;

    private static string NuevoSecreto() =>
        Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] datos) =>
        Convert.ToBase64String(datos).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Indice por hash: el secreto en claro no queda guardado en ningun lado.</summary>
    private static string Indice(string secreto) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secreto)));

    private SqlConnection Abrir()
    {
        var conn = new SqlConnection(cadenaConexion);
        conn.Open();
        return conn;
    }

    private static void Purgar(SqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM dbo.AlmacenAutorizacion WHERE ExpiraUtc <= SYSUTCDATETIME();";
        cmd.ExecuteNonQuery();
    }

    public string EmitirCodigo(CodigoPendiente codigo)
    {
        using var conn = Abrir();
        Purgar(conn);

        var secreto = NuevoSecreto();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO dbo.AlmacenAutorizacion
                (IndiceHash, Tipo, CredencialProtegida, ClienteId, RedirectUri, DesafioPkce, MetodoPkce, ExpiraUtc)
            VALUES
                (@indice, @tipo, @credencial, @clienteId, @redirect, @desafio, @metodo, @expira);
            """;
        cmd.Parameters.AddWithValue("@indice", Indice(secreto));
        cmd.Parameters.AddWithValue("@tipo", TipoCodigo);
        cmd.Parameters.AddWithValue("@credencial", codigo.CredencialProtegida);
        cmd.Parameters.AddWithValue("@clienteId", codigo.ClienteId);
        cmd.Parameters.AddWithValue("@redirect", (object?)codigo.RedirectUri ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@desafio", (object?)codigo.DesafioPkce ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@metodo", (object?)codigo.MetodoPkce ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@expira", DateTime.UtcNow + VidaDelCodigo);
        cmd.ExecuteNonQuery();

        return secreto;
    }

    public CodigoPendiente? CanjearCodigo(string codigo)
    {
        using var conn = Abrir();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM dbo.AlmacenAutorizacion
            OUTPUT deleted.CredencialProtegida, deleted.ClienteId, deleted.RedirectUri,
                   deleted.DesafioPkce, deleted.MetodoPkce, deleted.ExpiraUtc
            WHERE IndiceHash = @indice AND Tipo = @tipo;
            """;
        cmd.Parameters.AddWithValue("@indice", Indice(codigo));
        cmd.Parameters.AddWithValue("@tipo", TipoCodigo);

        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        var expiraUtc = rd.GetDateTime(5);
        if (expiraUtc <= DateTime.UtcNow) return null;

        return new CodigoPendiente(
            CredencialProtegida: rd.GetString(0),
            ClienteId: rd.GetString(1),
            RedirectUri: rd.IsDBNull(2) ? "" : rd.GetString(2),
            DesafioPkce: rd.IsDBNull(3) ? null : rd.GetString(3),
            MetodoPkce: rd.IsDBNull(4) ? null : rd.GetString(4),
            ExpiraUtc: expiraUtc);
    }

    public (string AccessToken, string RefreshToken, int ExpiraEnSegundos) EmitirTokens(
        string credencialProtegida, string clienteId)
    {
        using var conn = Abrir();
        Purgar(conn);
        return EmitirTokensEn(conn, credencialProtegida, clienteId);
    }

    private (string AccessToken, string RefreshToken, int ExpiraEnSegundos) EmitirTokensEn(
        SqlConnection conn, string credencialProtegida, string clienteId)
    {
        var access = NuevoSecreto();
        var refresh = NuevoSecreto();
        var ahora = DateTime.UtcNow;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO dbo.AlmacenAutorizacion (IndiceHash, Tipo, CredencialProtegida, ClienteId, ExpiraUtc)
            VALUES (@indiceAccess, @tipoAccess, @credencial, @clienteId, @expiraAccess),
                   (@indiceRefresh, @tipoRefresh, @credencial, @clienteId, @expiraRefresh);
            """;
        cmd.Parameters.AddWithValue("@indiceAccess", Indice(access));
        cmd.Parameters.AddWithValue("@tipoAccess", TipoAccess);
        cmd.Parameters.AddWithValue("@indiceRefresh", Indice(refresh));
        cmd.Parameters.AddWithValue("@tipoRefresh", TipoRefresh);
        cmd.Parameters.AddWithValue("@credencial", credencialProtegida);
        cmd.Parameters.AddWithValue("@clienteId", clienteId);
        cmd.Parameters.AddWithValue("@expiraAccess", ahora + vidaDelAccessToken);
        cmd.Parameters.AddWithValue("@expiraRefresh", ahora + VidaDelRefresh);
        cmd.ExecuteNonQuery();

        return (access, refresh, (int)vidaDelAccessToken.TotalSeconds);
    }

    public string? ResolverAccessToken(string token)
    {
        using var conn = Abrir();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT CredencialProtegida, ExpiraUtc
            FROM dbo.AlmacenAutorizacion
            WHERE IndiceHash = @indice AND Tipo = @tipo;
            """;
        cmd.Parameters.AddWithValue("@indice", Indice(token));
        cmd.Parameters.AddWithValue("@tipo", TipoAccess);

        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        var expiraUtc = rd.GetDateTime(1);
        return expiraUtc > DateTime.UtcNow ? rd.GetString(0) : null;
    }

    public (string AccessToken, string RefreshToken, int ExpiraEnSegundos)? Refrescar(string refreshToken)
    {
        using var conn = Abrir();

        string credencialProtegida;
        string clienteId;
        DateTime expiraUtc;

        using (var cmd = conn.CreateCommand())
        {
            // Rotacion: el DELETE saca el refresh ANTES de emitir uno nuevo. Si un
            // refresh robado y el legitimo se usan los dos, el segundo llega tarde
            // y no encuentra fila — el problema se nota en vez de funcionar en
            // silencio con las dos copias.
            cmd.CommandText = """
                DELETE FROM dbo.AlmacenAutorizacion
                OUTPUT deleted.CredencialProtegida, deleted.ClienteId, deleted.ExpiraUtc
                WHERE IndiceHash = @indice AND Tipo = @tipo;
                """;
            cmd.Parameters.AddWithValue("@indice", Indice(refreshToken));
            cmd.Parameters.AddWithValue("@tipo", TipoRefresh);

            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) return null;

            credencialProtegida = rd.GetString(0);
            clienteId = rd.GetString(1);
            expiraUtc = rd.GetDateTime(2);
        }

        if (expiraUtc <= DateTime.UtcNow) return null;

        return EmitirTokensEn(conn, credencialProtegida, clienteId);
    }
}
