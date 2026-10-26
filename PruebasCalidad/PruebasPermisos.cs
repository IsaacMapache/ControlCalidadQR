using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ControlCalidadQR.Calidad;
using ServidorCalidad;
// Comprobaciones reproducibles sin alterar la base SQL de trabajo.
public static class PruebasPermisos
{
    public static async Task EjecutarAsync()
    {
        await Task.CompletedTask;
var servicios = new ServiceCollection();
servicios.AddLogging(); servicios.AddAuthorization(PoliticasCalidad.Configurar);
using var proveedor = servicios.BuildServiceProvider();
var autorizacion = proveedor.GetRequiredService<IAuthorizationService>();
int correctas = 0;
void Comprobar(bool condicion, string texto) { if (!condicion) throw new Exception(texto); Console.WriteLine("OK: " + texto); correctas++; }
foreach (var rol in new[] { "Administrador", "Inspector", "Consulta", "Otro", "" })
{
    var identidad = new ClaimsIdentity([new Claim(ClaimTypes.Name, "prueba"), new Claim(ClaimTypes.Role, rol)], "Prueba");
    var usuario = new ClaimsPrincipal(identidad);
    var registrar = (await autorizacion.AuthorizeAsync(usuario, null, PoliticasCalidad.Registrar)).Succeeded;
    var administrar = (await autorizacion.AuthorizeAsync(usuario, null, PoliticasCalidad.Administrar)).Succeeded;
    Comprobar(registrar == (rol is "Administrador" or "Inspector"), $"Permiso de registro: {rol}");
    Comprobar(administrar == (rol == "Administrador"), $"Permiso administrativo: {rol}");
}
var anonimo = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Administrador")]));
Comprobar(!(await autorizacion.AuthorizeAsync(anonimo, null, PoliticasCalidad.Administrar)).Succeeded, "Un rol sin autenticar no autoriza");
void DebeRechazar(Action accion, string mensaje)
{
    try { accion(); } catch (InvalidOperationException) { Comprobar(true, mensaje); return; }
    throw new Exception(mensaje);
}
var ultimo = new UsuarioCalidad("administrador", "Administrador", true, 1);
DebeRechazar(() => RolesCalidad.ValidarCambio(ultimo, "Consulta", true, 1), "No se degrada al último administrador");
DebeRechazar(() => RolesCalidad.ValidarCambio(ultimo, "Administrador", false, 1), "No se desactiva al último administrador");
DebeRechazar(() => RolesCalidad.ValidarCambio(ultimo, "Inventado", true, 2), "Se rechaza un rol desconocido");
RolesCalidad.ValidarCambio(ultimo, "Consulta", true, 2);
Comprobar(true, "Se permite cambiar cuando queda otro administrador");
RolesCalidad.ValidarCambio(ultimo, "Administrador", true, 1);
Comprobar(true, "Se permite conservar el último administrador");
Console.WriteLine($"TOTAL: {correctas} comprobaciones correctas.");
    }
}
