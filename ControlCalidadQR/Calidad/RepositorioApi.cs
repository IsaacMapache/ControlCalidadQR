using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControlCalidadQR.Calidad;

// Este repositorio usa exclusivamente la API; nunca simula un guardado si el servidor falla.
public sealed class RepositorioApi : RepositorioCalidad
{
    public RepositorioApi() : base("") { }
    public override Task<DatosCalidad> LeerAsync() => ConexionCalidad.LeerAsync<DatosCalidad>("api/datos");
    public override Task GuardarLoteAsync(LoteCalidad lote, string usuario) => ConexionCalidad.EnviarAsync("api/lotes", lote);
    public override Task GuardarPlantillaAsync(PlantillaCalidad plantilla, string usuario) => ConexionCalidad.EnviarAsync("api/plantillas", plantilla);
    public override Task GuardarControlAsync(ControlCalidad control, string usuario, bool finalizar) =>
        ConexionCalidad.EnviarAsync("api/controles", new { control, finalizar });
}

public static class ConexionCalidad
{
    private static readonly HttpClient Cliente = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };
    // La sesión permanece solo en memoria: al cerrar la aplicación se requiere iniciar sesión otra vez.
    private static string? token;
    public static string Usuario { get; private set; } = "";
    public static string Rol { get; private set; } = "";
    public static bool PuedeRegistrar => RolesCalidad.PuedeRegistrar(Rol);
    public static bool EsAdministrador => RolesCalidad.PuedeAdministrar(Rol);
    public static string Direccion => Preferences.Default.Get("servidor-calidad", "");
    public static bool Conectado => token is not null;
    public static void Configurar(string direccion)
    {
        direccion = direccion.Trim().TrimEnd('/');
        if (!Uri.TryCreate(direccion, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidOperationException("Escribe una dirección válida del servidor.");
        bool permitida = uri.Scheme == "https";
#if DEBUG
        // Excepción limitada al equipo local y a la dirección del host del emulador Android.
        permitida |= uri.Scheme == "http" && (uri.IsLoopback || uri.Host == "10.0.2.2");
#endif
        if (!permitida) throw new InvalidOperationException("El servidor debe usar HTTPS. En desarrollo se permite HTTP local.");
        Preferences.Default.Set("servidor-calidad", direccion);
        token = null; Usuario = ""; Rol = "";
    }
    public static async Task IniciarAsync(string usuario, string clave)
    {
        token = null; Usuario = ""; Rol = "";
        using var respuesta = await SolicitarAsync(HttpMethod.Post, "api/sesion", new { usuario, clave });
        var sesion = await respuesta.Content.ReadFromJsonAsync<RespuestaSesion>(Json)
            ?? throw new InvalidOperationException("La respuesta del servidor no es válida.");
                if (!RolesCalidad.Valido(sesion.Rol))
            throw new InvalidOperationException("Actualiza la API y prepara los permisos antes de iniciar sesión.");
        token = sesion.Token; Usuario = sesion.Usuario; Rol = sesion.Rol;
    }
    public static async Task CerrarAsync()
    {
        try { if (token is not null) { using var respuesta = await SolicitarAsync(HttpMethod.Delete, "api/sesion"); } }
        finally { token = null; Usuario = ""; Rol = ""; }
    }
    public static async Task<T> LeerAsync<T>(string ruta)
    {
        using var respuesta = await SolicitarAsync(HttpMethod.Get, ruta);
        return await respuesta.Content.ReadFromJsonAsync<T>(Json)
            ?? throw new InvalidOperationException("El servidor devolvió una respuesta vacía.");
    }
    public static async Task EnviarAsync(string ruta, object valor)
    {
        using var respuesta = await SolicitarAsync(HttpMethod.Post, ruta, valor);
    }
    private static async Task<HttpResponseMessage> SolicitarAsync(HttpMethod metodo, string ruta, object? valor = null)
    {
        if (string.IsNullOrWhiteSpace(Direccion))
            throw new InvalidOperationException("Primero configura la dirección del servidor desde el inicio de sesión.");
        using var solicitud = new HttpRequestMessage(metodo, Direccion + "/" + ruta);
        if (token is not null) solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (valor is not null) solicitud.Content = JsonContent.Create(valor, options: Json);
        HttpResponseMessage respuesta;
        try { respuesta = await Cliente.SendAsync(solicitud); }
        catch (HttpRequestException) { throw new InvalidOperationException("No se pudo conectar al servidor. Revisa la dirección, la red y que la API esté encendida."); }
        catch (TaskCanceledException) { throw new InvalidOperationException("El servidor tardó demasiado en responder. Revisa el historial antes de repetir un guardado."); }
        if (respuesta.IsSuccessStatusCode) return respuesta;
        var estado = respuesta.StatusCode;
        string? mensaje = null;
        try { mensaje = (await respuesta.Content.ReadFromJsonAsync<ErrorServidor>(Json))?.Mensaje; }
        catch (JsonException) { }
        finally { respuesta.Dispose(); }
        if (estado == HttpStatusCode.Unauthorized)
        {
            token = null; Usuario = ""; Rol = "";
            throw new InvalidOperationException(mensaje ?? "La sesión venció. Regresa al menú y vuelve a iniciar sesión.");
        }
        if (estado == HttpStatusCode.Forbidden) throw new InvalidOperationException("Tu cuenta no tiene permiso para realizar esta acción.");
        throw new InvalidOperationException(mensaje ?? (estado == HttpStatusCode.TooManyRequests
            ? "Se hicieron demasiados intentos. Espera un minuto." : "El servidor no pudo completar la operación."));
    }
    private sealed record RespuestaSesion(string Token, string Usuario, DateTimeOffset Vence, string Rol);
    private sealed record ErrorServidor(string Mensaje);
}

