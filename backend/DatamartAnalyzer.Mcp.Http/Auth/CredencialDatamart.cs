using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DatamartAnalyzer.Mcp.Http.Auth;

/// <summary>
/// Los cuatro datos que hoy se cargan al instalar el .mcpb, y que en el conector
/// remoto se escriben una vez en la pantalla de OAuth.
///
/// La identidad del usuario ES esta credencial: no hay un directorio detras. Eso es
/// deliberado — permite atender a clientes fuera de SINCO el primer dia, sin depender
/// de que exista un tenant corporativo — y tiene un limite que hay que decir en voz
/// alta: dos personas que compartan credencial son la misma persona para el almacen.
/// </summary>
public sealed record CredencialDatamart(string Servidor, string Base, string? Usuario, string? Clave)
{
    /// <summary>Sin usuario se cae a autenticacion de Windows, igual que en el conector local.</summary>
    public bool UsaAutenticacionSql => !string.IsNullOrWhiteSpace(Usuario);

    /// <summary>Lo que se puede mostrar. Nunca incluye la clave.</summary>
    public string Descripcion => $"{Servidor}/{Base}";

    public string ASerializado() => JsonSerializer.Serialize(this);

    public static CredencialDatamart DeSerializado(string json) =>
        JsonSerializer.Deserialize<CredencialDatamart>(json)
        ?? throw new InvalidOperationException("Credencial ilegible.");
}

/// <summary>
/// Cifra las credenciales que quedan guardadas del lado del servidor.
///
/// Por que existe: en el .mcpb la clave del datamart vive cifrada en el equipo de
/// cada usuario, y una brecha nuestra no expone credenciales de nadie. En el conector
/// remoto pasamos a ser CUSTODIOS de las credenciales de todos los clientes, y el
/// radio de dano de una brecha del host central pasa a ser *todos los datamarts*.
/// Eso no se resuelve con codigo, se resuelve declarandolo en la revision de
/// seguridad — pero lo minimo defendible es que en reposo no esten en claro.
///
/// AES-256-GCM: cifra y autentica a la vez, asi que un ciphertext manipulado falla al
/// descifrar en vez de producir basura silenciosa.
///
/// LA LLAVE NO VIVE EN appsettings. Se lee de una variable de entorno para que el
/// archivo de configuracion —que se versiona, se copia y se manda por chat— nunca la
/// contenga. Y NO se usa DPAPI de maquina a proposito: ata el cifrado al servidor, y
/// el dia que esto se mueva a Azure obligaria a que todos vuelvan a autenticarse.
/// Con una llave movible, migrar es mover la llave.
/// </summary>
public sealed class ProtectorDeCredenciales
{
    private const int TamanoLlave = 32;   // AES-256
    private const int TamanoNonce = 12;   // el que recomienda GCM
    private const int TamanoTag = 16;

    private readonly byte[] _llave;

    public ProtectorDeCredenciales(string? llaveBase64)
    {
        if (string.IsNullOrWhiteSpace(llaveBase64))
            throw new InvalidOperationException(
                "Falta la llave de cifrado de credenciales. Generar una y ponerla en la variable " +
                "de entorno DATAMART_LLAVE_CIFRADO (32 bytes en base64):\n" +
                "  [Convert]::ToBase64String((1..32 | %{ Get-Random -Max 256 }))\n" +
                "No ponerla en appsettings.json: ese archivo se versiona.");

        byte[] llave;
        try { llave = Convert.FromBase64String(llaveBase64); }
        catch (FormatException) { throw new InvalidOperationException("DATAMART_LLAVE_CIFRADO no es base64 valido."); }

        if (llave.Length != TamanoLlave)
            throw new InvalidOperationException(
                $"DATAMART_LLAVE_CIFRADO tiene {llave.Length} bytes y hacen falta {TamanoLlave}.");

        _llave = llave;
    }

    /// <summary>Devuelve nonce | tag | ciphertext en un solo base64.</summary>
    public string Cifrar(string claro)
    {
        var datos = Encoding.UTF8.GetBytes(claro);
        var nonce = RandomNumberGenerator.GetBytes(TamanoNonce);
        var tag = new byte[TamanoTag];
        var cifrado = new byte[datos.Length];

        using var aes = new AesGcm(_llave, TamanoTag);
        aes.Encrypt(nonce, datos, cifrado, tag);

        var salida = new byte[TamanoNonce + TamanoTag + cifrado.Length];
        nonce.CopyTo(salida, 0);
        tag.CopyTo(salida, TamanoNonce);
        cifrado.CopyTo(salida, TamanoNonce + TamanoTag);

        return Convert.ToBase64String(salida);
    }

    public string Descifrar(string protegido)
    {
        var todo = Convert.FromBase64String(protegido);

        if (todo.Length < TamanoNonce + TamanoTag)
            throw new CryptographicException("El dato protegido esta truncado.");

        var nonce = todo.AsSpan(0, TamanoNonce);
        var tag = todo.AsSpan(TamanoNonce, TamanoTag);
        var cifrado = todo.AsSpan(TamanoNonce + TamanoTag);
        var claro = new byte[cifrado.Length];

        using var aes = new AesGcm(_llave, TamanoTag);
        aes.Decrypt(nonce, cifrado, tag, claro);   // lanza si el tag no valida

        return Encoding.UTF8.GetString(claro);
    }
}
