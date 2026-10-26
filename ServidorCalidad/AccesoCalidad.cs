using ControlCalidadQR.Calidad;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ServidorCalidad;

public record Credenciales(string Usuario, string Clave);
public record Sesion(string Token, string Usuario, DateTimeOffset Vence, string Rol);

public sealed partial class AccesoCalidad(BaseCalidad datos)
{
    private static readonly PasswordHasher<string> Claves = new();
    private static readonly string ClaveFicticia = Claves.HashPassword("desconocido", Guid.NewGuid().ToString());
    public static string Huella(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<Sesion?> IniciarAsync(Credenciales entrada)
    {
        if (string.IsNullOrWhiteSpace(entrada.Usuario) || entrada.Usuario.Length > 100 ||
            entrada.Clave is null || entrada.Clave.Length is < 1 or > 200) return null;
        await using var conexion = await datos.AbrirAsync();
        using var buscar = BaseCalidad.Comando(conexion, null,
            "SELECT Nombre,Clave,Rol FROM dbo.Usuarios WHERE Nombre=@usuario AND Activo=1", ("@usuario", entrada.Usuario.Trim()));
        string? usuario = null, hash = null; string rol = "";
        using (var lector = await buscar.ExecuteReaderAsync())
            if (await lector.ReadAsync()) { usuario = lector.GetString(0); hash = lector.GetString(1); rol = lector.GetString(2); }
        var resultado = Claves.VerifyHashedPassword(usuario ?? "desconocido", hash ?? ClaveFicticia, entrada.Clave);
        if (usuario is null || resultado == PasswordVerificationResult.Failed) return null;
        var sesion = new Sesion(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), usuario, DateTimeOffset.UtcNow.AddHours(8), rol);
        using var crear = BaseCalidad.Comando(conexion, null, """
            DELETE dbo.Sesiones WHERE Vence <= SYSDATETIMEOFFSET();
            INSERT dbo.Sesiones(Huella,Usuario,Vence) VALUES(@huella,@usuario,@vence);
            """, ("@huella", Huella(sesion.Token)), ("@usuario", usuario), ("@vence", sesion.Vence));
        await crear.ExecuteNonQueryAsync();
        return sesion;
    }

    public async Task<UsuarioCalidad?> ValidarAsync(string token)
    {
        if (token.Length != 64) return null;
        await using var conexion = await datos.AbrirAsync();
        using var buscar = BaseCalidad.Comando(conexion, null,
            "SELECT u.Nombre,u.Rol,u.Activo,u.Version FROM dbo.Sesiones s JOIN dbo.Usuarios u ON u.Nombre=s.Usuario WHERE s.Huella=@huella AND s.Vence>SYSDATETIMEOFFSET() AND u.Activo=1", ("@huella", Huella(token)));
        using var lector = await buscar.ExecuteReaderAsync();
        return await lector.ReadAsync() ? new UsuarioCalidad(lector.GetString(0), lector.GetString(1), lector.GetBoolean(2), lector.GetInt32(3)) : null;
    }

    public async Task CerrarAsync(string token)
    {
        await using var conexion = await datos.AbrirAsync();
        using var borrar = BaseCalidad.Comando(conexion, null,
            "DELETE dbo.Sesiones WHERE Huella=@huella", ("@huella", Huella(token)));
        await borrar.ExecuteNonQueryAsync();
    }
}

// La identidad auditada procede de la sesión verificada, nunca del cuerpo enviado por el teléfono.
public sealed class AutenticacionCalidad(IOptionsMonitor<AuthenticationSchemeOptions> opciones,
    ILoggerFactory logger, UrlEncoder encoder, AccesoCalidad acceso)
    : AuthenticationHandler<AuthenticationSchemeOptions>(opciones, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cabecera = Request.Headers.Authorization.ToString();
        if (!cabecera.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var usuario = await acceso.ValidarAsync(cabecera[7..]);
        if (usuario is null) return AuthenticateResult.Fail("La sesión venció o no es válida.");
        var identidad = new ClaimsIdentity([new Claim(ClaimTypes.Name, usuario.Nombre), new Claim(ClaimTypes.Role, usuario.Rol)], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identidad), Scheme.Name));
    }
}

