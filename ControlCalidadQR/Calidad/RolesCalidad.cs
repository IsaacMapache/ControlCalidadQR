namespace ControlCalidadQR.Calidad;

// Los nombres se comparten entre servidor y aplicación para evitar permisos inconsistentes.
public static class RolesCalidad
{
    public const string Administrador = "Administrador";
    public const string Inspector = "Inspector";
    public const string Consulta = "Consulta";
    public static bool Valido(string? rol) => rol is Administrador or Inspector or Consulta;
    public static bool PuedeRegistrar(string? rol) => rol is Administrador or Inspector;
    public static bool PuedeAdministrar(string? rol) => rol == Administrador;
    public static void ValidarCambio(UsuarioCalidad actual, string rol, bool activo, int administradores)
    {
        if (!Valido(rol)) throw new InvalidOperationException("Selecciona un rol válido.");
        if (actual.Activo && actual.Rol == Administrador && (!activo || rol != Administrador) && administradores <= 1)
            throw new InvalidOperationException("Debe quedar al menos un administrador activo.");
    }
}
public sealed record UsuarioCalidad(string Nombre, string Rol, bool Activo, int Version);
public sealed record AltaUsuario(string Nombre, string Clave, string Rol);
public sealed record CambioUsuario(string Nombre, string Rol, bool Activo, int Version, string? Clave);
