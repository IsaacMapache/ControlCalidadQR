using ControlCalidadQR.Calidad;
namespace ControlCalidadQR.Pages;

public partial class Login : ContentPage
{
    // Evita abrir el menú varias veces si se pulsa el botón repetidamente.
    private bool iniciandoSesion;

    public Login()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        EstadoServidorLabel.Text = string.IsNullOrWhiteSpace(ConexionCalidad.Direccion) ? "Configura el servidor antes de tu primer acceso." : "Servidor: " + ConexionCalidad.Direccion;
        // Al regresar del menú no se conservan las credenciales en pantalla.
        UsuarioEntry.Text = string.Empty;
        PasswordEntry.Text = string.Empty;
        MostrarClaveCheck.IsChecked = false;
    }

    // Al regresar a esta pantalla, la contraseña vuelve a estar oculta.
    private void OnMostrarClaveChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (PasswordEntry != null) PasswordEntry.IsPassword = !e.Value;
    }
    private void OnUsuarioCompleted(object? sender, EventArgs e) => PasswordEntry.Focus();

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        if (iniciandoSesion)
            return;

        // La validación local evita enviar campos vacíos; la API verifica las credenciales.
        string usuario = (UsuarioEntry.Text ?? "").Trim();
        string contraseña = PasswordEntry.Text ?? "";
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(contraseña))
        {
            await DisplayAlertAsync("Atención",
                "Debes ingresar tu usuario y contraseña.", "Aceptar");
            return;
        }

        iniciandoSesion = true; IniciarButton.IsEnabled = false; IniciarButton.Text = "Conectando...";
        try
        {
            await ConexionCalidad.IniciarAsync(usuario, contraseña);
            await Shell.Current.GoToAsync("//menuPrincipal", new Dictionary<string, object>
            {
                ["usuario"] = ConexionCalidad.Usuario
            });
            PasswordEntry.Text = string.Empty;
        MostrarClaveCheck.IsChecked = false;
        }
        catch (Exception ex) { await DisplayAlertAsync("No se pudo iniciar sesión", ex.Message, "Aceptar"); }
        finally
        {
            iniciandoSesion = false; IniciarButton.IsEnabled = true; IniciarButton.Text = "Entrar a mi espacio";
        }
    }

    private async void OnConfigurarServidorClicked(object? sender, EventArgs e)
    {
        if (iniciandoSesion) return;
        var direccion = await DisplayPromptAsync("Servidor de calidad",
            "Dirección de la API. Para el emulador Android en este equipo: http://10.0.2.2:5080",
            "Guardar", "Cancelar", initialValue: ConexionCalidad.Direccion);
        if (direccion is null) return;
        try
        {
            ConexionCalidad.Configurar(direccion);
            EstadoServidorLabel.Text = "Servidor: " + ConexionCalidad.Direccion;
            await DisplayAlertAsync("Servidor configurado", "Ya puedes iniciar sesión con tu cuenta del servidor.", "Aceptar");
        }
        catch (Exception ex) { await DisplayAlertAsync("Revisa la dirección", ex.Message, "Aceptar"); }
    }
    private async void OnExportarDatosClicked(object? sender, EventArgs e)
    {
        try
        {
            var original = Path.Combine(FileSystem.AppDataDirectory, "calidad-v1.json");
            if (!File.Exists(original))
            {
                await DisplayAlertAsync("Datos anteriores", "Este dispositivo no tiene registros locales para exportar.", "Aceptar");
                return;
            }
            // Se comparte una copia; el archivo original nunca se borra durante la migración.
            var copia = Path.Combine(FileSystem.CacheDirectory, "calidad-anterior.json");
            File.Copy(original, copia, true);
            await Share.Default.RequestAsync(new ShareFileRequest("Exportar datos anteriores", new ShareFile(copia)));
        }
        catch (Exception ex) { await DisplayAlertAsync("No se pudo exportar", ex.Message, "Aceptar"); }
    }}



