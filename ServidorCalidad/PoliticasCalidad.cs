using ControlCalidadQR.Calidad;
using Microsoft.AspNetCore.Authorization;

namespace ServidorCalidad;

public static class PoliticasCalidad
{
    public const string Registrar = "RegistrarCalidad";
    public const string Administrar = "AdministrarCalidad";
    public static void Configurar(AuthorizationOptions opciones)
    {
        opciones.AddPolicy(Registrar, politica => politica.RequireAuthenticatedUser().RequireRole(RolesCalidad.Administrador, RolesCalidad.Inspector));
        opciones.AddPolicy(Administrar, politica => politica.RequireAuthenticatedUser().RequireRole(RolesCalidad.Administrador));
    }
}
