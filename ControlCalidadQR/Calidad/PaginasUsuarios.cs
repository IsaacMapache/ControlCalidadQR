namespace ControlCalidadQR.Calidad;

// Esta pantalla solo se ofrece al administrador; la API vuelve a comprobar el permiso.
public sealed class PaginaUsuarios : PaginaCalidad
{
    public PaginaUsuarios(RepositorioCalidad repo, string usuario) : base(repo, usuario, "Usuarios y permisos") { }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Cuerpo.Clear();
        await Seguro(async () =>
        {
            var usuarios = await ConexionCalidad.LeerAsync<List<UsuarioCalidad>>("api/usuarios");
            Cuerpo.Add(Texto("Administrador: gestiona usuarios y plantillas. Inspector: registra lotes y controles. Consulta: revisa información sin modificarla."));
            Boton("Crear usuario", () => Abrir(new PaginaEditarUsuario(Repo, Usuario, null)));
            foreach (var usuario in usuarios)
                Tarjeta(usuario.Nombre, $"{usuario.Rol} · {(usuario.Activo ? "Activo" : "Desactivado")}", fila =>
                    Boton("Editar permisos", () => Abrir(new PaginaEditarUsuario(Repo, Usuario, usuario)), fila));
        });
    }
}

public sealed class PaginaEditarUsuario : PaginaCalidad
{
    public PaginaEditarUsuario(RepositorioCalidad repo, string usuario, UsuarioCalidad? cuenta)
        : base(repo, usuario, cuenta is null ? "Crear usuario" : "Permisos del usuario")
    {
        var nombre = Campo("Nombre de usuario", cuenta?.Nombre ?? "");
        nombre.IsReadOnly = cuenta is not null;
        Cuerpo.Add(Texto("Rol"));
        var rol = new Picker { ItemsSource = new[] { RolesCalidad.Administrador, RolesCalidad.Inspector, RolesCalidad.Consulta },
            SelectedItem = cuenta?.Rol ?? RolesCalidad.Inspector, TextColor = Colors.Black, BackgroundColor = Colors.White };
        Cuerpo.Add(rol);
        var activo = Casilla("Cuenta activa", cuenta?.Activo ?? true);
        if (cuenta is null) activo.IsEnabled = false;
        var clave = Campo(cuenta is null ? "Contraseña (mínimo 12 caracteres)" : "Nueva contraseña (deja vacío para conservarla)");
        clave.IsPassword = true;
        Cuerpo.Add(Texto("Los cambios de permisos y de contraseña cierran las sesiones de esta cuenta. Desactivar conserva sus registros e historial."));
        Boton("Guardar usuario", async () =>
        {
            var elegido = rol.SelectedItem as string ?? "";
            if (!RolesCalidad.Valido(elegido)) throw new InvalidOperationException("Selecciona un rol.");
            if (cuenta is null)
                await ConexionCalidad.EnviarAsync("api/usuarios", new AltaUsuario(nombre.Text?.Trim() ?? "", clave.Text ?? "", elegido));
            else
            {
                if (!await DisplayAlertAsync("Actualizar usuario", "Se cerrarán las sesiones de esta cuenta. ¿Deseas continuar?", "Guardar", "Cancelar")) return;
                await ConexionCalidad.EnviarAsync("api/usuarios/guardar", new CambioUsuario(cuenta.Nombre, elegido, activo.IsToggled, cuenta.Version, clave.Text));
            }
            clave.Text = "";
            if (cuenta is not null && string.Equals(cuenta.Nombre, Usuario, StringComparison.OrdinalIgnoreCase))
            {
                try { await ConexionCalidad.CerrarAsync(); } catch { /* La sesión ya fue revocada al guardar. */ }
                await Shell.Current.GoToAsync("//login");
            }
            else await Navigation.PopAsync();
        });
    }
}
