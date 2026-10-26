using System.Threading.RateLimiting;
using ControlCalidadQR.Calidad;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using ServidorCalidad;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 2 * 1024 * 1024);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddSingleton<BaseCalidad>();
builder.Services.AddSingleton<AccesoCalidad>();
builder.Services.AddAuthentication("Sesion").AddScheme<AuthenticationSchemeOptions, AutenticacionCalidad>("Sesion", null);
builder.Services.AddAuthorization(PoliticasCalidad.Configurar);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    // Un límite global acota intentos incluso cuando cambia la dirección de origen.
    o.AddFixedWindowLimiter("inicio", l => { l.PermitLimit = 10; l.Window = TimeSpan.FromMinutes(1); l.QueueLimit = 0; });
});
var app = builder.Build();
if (args.Contains("--inicializar"))
{
    await app.Services.GetRequiredService<BaseCalidad>().InicializarAsync();
    Console.WriteLine("Base de datos y tablas preparadas.");
    return;
}
var administrador = Array.IndexOf(args, "--administrador");
if (administrador >= 0)
{
    if (administrador + 1 >= args.Length) throw new InvalidOperationException("Indica el usuario que será administrador.");
    await app.Services.GetRequiredService<AccesoCalidad>().HacerAdministradorAsync(args[administrador + 1]);
    Console.WriteLine("Administrador configurado. Vuelve a iniciar sesión en la aplicación.");
    return;
}
var importar = Array.IndexOf(args, "--importar");
if (importar >= 0)
{
    if (importar + 1 >= args.Length) throw new InvalidOperationException("Indica el archivo exportado.");
    await ImportacionCalidad.ImportarAsync(app.Services.GetRequiredService<BaseCalidad>(), args[importar + 1]);
    Console.WriteLine("Registros importados. Conserva el archivo como respaldo.");
    return;
}
var alta = Array.IndexOf(args, "--crear-usuario");
if (alta >= 0)
{
    if (alta + 1 >= args.Length) throw new InvalidOperationException("Indica el nombre del usuario.");
    var clave = Environment.GetEnvironmentVariable("CALIDAD_CLAVE_INICIAL");
    if (string.IsNullOrEmpty(clave))
    {
        Console.Write("Contraseña (mínimo 12 caracteres): ");
        var caracteres = new List<char>();
        ConsoleKeyInfo tecla;
        while ((tecla = Console.ReadKey(true)).Key != ConsoleKey.Enter)
        {
            if (tecla.Key == ConsoleKey.Backspace) { if (caracteres.Count > 0) caracteres.RemoveAt(caracteres.Count - 1); }
            else if (!char.IsControl(tecla.KeyChar)) caracteres.Add(tecla.KeyChar);
        }
        Console.WriteLine();
        clave = new string(caracteres.ToArray());
    }
        var indiceRol = Array.IndexOf(args, "--rol");
    var rol = indiceRol >= 0 && indiceRol + 1 < args.Length ? args[indiceRol + 1] : RolesCalidad.Inspector;
    await app.Services.GetRequiredService<AccesoCalidad>().CrearUsuarioAsync(args[alta + 1], clave, rol);
    Console.WriteLine("Usuario creado.");
    return;
}
app.Use(async (contexto, siguiente) =>
{
    try
    {
        // El HTTP de desarrollo solo sirve para pruebas locales y emulador.
        if (!app.Environment.IsDevelopment() && !contexto.Request.IsHttps)
        {
            contexto.Response.StatusCode = 400;
            await contexto.Response.WriteAsJsonAsync(new { mensaje = "Se requiere HTTPS." });
            return;
        }
        await siguiente();
    }
    catch (InvalidOperationException ex)
    {
        contexto.Response.StatusCode = 409;
        await contexto.Response.WriteAsJsonAsync(new { mensaje = ex.Message });
    }
    catch (SqlException ex)
    {
        app.Logger.LogError(ex, "Error de acceso a SQL Server.");
        contexto.Response.StatusCode = 503;
        await contexto.Response.WriteAsJsonAsync(new { mensaje = "SQL Server no está disponible. Revisa la configuración del servidor." });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        app.Logger.LogError(ex, "No se pudo completar la operación.");
        contexto.Response.StatusCode = 500;
        await contexto.Response.WriteAsJsonAsync(new { mensaje = "No se pudo completar la operación. Revisa el registro del servidor." });
    }
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapPost("/api/sesion", async (Credenciales entrada, AccesoCalidad acceso) =>
{
    var sesion = await acceso.IniciarAsync(entrada);
    return sesion is null ? Results.Json(new { mensaje = "Usuario o contraseña incorrectos." }, statusCode: 401) : Results.Ok(sesion);
}).RequireRateLimiting("inicio");
var api = app.MapGroup("/api").RequireAuthorization();
api.MapDelete("/sesion", async (HttpContext contexto, AccesoCalidad acceso) =>
{
    await acceso.CerrarAsync(contexto.Request.Headers.Authorization.ToString()[7..]);
    return Results.NoContent();
});
api.MapGet("/datos", (BaseCalidad datos) => datos.LeerAsync());
api.MapPost("/lotes", async (LoteCalidad lote, HttpContext contexto, BaseCalidad datos) =>
{
    await datos.CambiarAsync(repo => repo.GuardarLoteAsync(lote, contexto.User.Identity!.Name!));
    return Results.NoContent();
}).RequireAuthorization(PoliticasCalidad.Registrar);
api.MapPost("/plantillas", async (PlantillaCalidad plantilla, HttpContext contexto, BaseCalidad datos) =>
{
    await datos.CambiarAsync(repo => repo.GuardarPlantillaAsync(plantilla, contexto.User.Identity!.Name!));
    return Results.NoContent();
}).RequireAuthorization(PoliticasCalidad.Administrar);
api.MapPost("/controles", async (PeticionControl peticion, HttpContext contexto, BaseCalidad datos) =>
{
    peticion.Control.Responsable = contexto.User.Identity!.Name!;
    await datos.CambiarAsync(repo => repo.GuardarControlAsync(peticion.Control, contexto.User.Identity.Name!, peticion.Finalizar));
    return Results.NoContent();
}).RequireAuthorization(PoliticasCalidad.Registrar);
var usuarios = api.MapGroup("/usuarios").RequireAuthorization(PoliticasCalidad.Administrar);
usuarios.MapGet("", (AccesoCalidad acceso) => acceso.ListarAsync());
usuarios.MapPost("", async (AltaUsuario alta, HttpContext contexto, AccesoCalidad acceso) =>
{
    await acceso.CrearUsuarioAsync(alta.Nombre, alta.Clave, alta.Rol, contexto.User.Identity!.Name!);
    return Results.NoContent();
});
usuarios.MapPost("/guardar", async (CambioUsuario cambio, HttpContext contexto, AccesoCalidad acceso) =>
{
    await acceso.CambiarUsuarioAsync(cambio, contexto.User.Identity!.Name!);
    return Results.NoContent();
});
await app.RunAsync();
public record PeticionControl(ControlCalidad Control, bool Finalizar);


