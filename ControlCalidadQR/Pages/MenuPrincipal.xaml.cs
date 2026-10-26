using ControlCalidadQR.Calidad;

namespace ControlCalidadQR.Pages;

public partial class MenuPrincipal : ContentPage, IQueryAttributable
{
    // Todas las pantallas comparten los datos centrales mediante la API.
    private readonly RepositorioCalidad repositorio = new RepositorioApi();
    private string usuario = "";
    private bool abriendo;
    public MenuPrincipal() { InitializeComponent(); }
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        usuario = query.TryGetValue("usuario", out var valor) ? valor?.ToString() ?? "" : "";
        BienvenidaLabel.Text = string.IsNullOrWhiteSpace(usuario) ? "Bienvenido" : $"Bienvenido, {usuario} · {ConexionCalidad.Rol}";
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();
        NuevoControlButton.IsVisible = ConexionCalidad.PuedeRegistrar;
        MostrarOpciones();
    }
    private int columnas;
    private void OnOpcionesSizeChanged(object? sender, EventArgs e)
    {
        int nuevas = OpcionesGrid.Width >= 620 ? 2 : 1;
        if (nuevas != columnas) { columnas = nuevas; MostrarOpciones(); }
    }
    private void MostrarOpciones()
    {
        var opciones = new List<(string Titulo, string Descripcion, string Ruta, string Accion)>
        {
            ("Lotes", "Registra productos y consulta su trazabilidad.", "lotes", "Explorar lotes"),
            ("Controles y reportes", "Revisa evaluaciones y genera reportes PDF.", "historial", "Ver historial"),
            ("Leer un QR", "Encuentra un lote con una imagen, cámara o código.", "qr", "Abrir lector"),
            ("Plantillas", "Consulta los criterios de cada evaluación.", "plantillas", "Ver plantillas"),
            ("Resumen de calidad", "Consulta resultados y controles pendientes.", "resumen", "Ver resumen")
        };
        if (ConexionCalidad.EsAdministrador)
            opciones.Add(("Usuarios y permisos", "Administra cuentas y accesos del equipo.", "usuarios", "Administrar"));
        OpcionesGrid.Clear(); OpcionesGrid.RowDefinitions.Clear(); OpcionesGrid.ColumnDefinitions.Clear();
        int totalColumnas = Math.Max(1, columnas);
        for (int c = 0; c < totalColumnas; c++) OpcionesGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (int i = 0; i < opciones.Count; i++)
        {
            if (i % totalColumnas == 0) OpcionesGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var opcion = opciones[i];
            var contenido = new VerticalStackLayout { Spacing = 10, Padding = 20 };
            contenido.Add(new Label { Text = opcion.Titulo, FontSize = 19, FontAttributes = FontAttributes.Bold, TextColor = TemaCalidad.Color("CabeceraCalidad") });
            contenido.Add(new Label { Text = opcion.Descripcion, FontSize = 14, TextColor = TemaCalidad.Color("TextoSecundarioCalidad"), MinimumHeightRequest = 38 });
            var boton = new Button { Text = opcion.Accion, CommandParameter = opcion.Ruta, BackgroundColor = TemaCalidad.Color("Secondary"),
                TextColor = TemaCalidad.Color("SecondaryDarkText"), CornerRadius = 11, MinimumHeightRequest = 44, FontAttributes = FontAttributes.Bold };
            boton.Clicked += OnAbrirApartadoClicked;
            contenido.Add(boton);
            var tarjeta = new Border { Content = contenido, BackgroundColor = Colors.White, Stroke = TemaCalidad.Color("BordeCalidad"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 } };
            OpcionesGrid.Add(tarjeta, i % totalColumnas, i / totalColumnas);
        }
    }
    private async void OnAbrirApartadoClicked(object? sender, EventArgs e)
    {
        if (abriendo || sender is not Button boton) return;
        abriendo = true;
        try
        {
            if (!ConexionCalidad.Conectado)
            {
                await Shell.Current.GoToAsync("//login"); return;
            }
            Page pagina = boton.CommandParameter?.ToString() switch
            {
                "usuarios" when ConexionCalidad.EsAdministrador => new PaginaUsuarios(repositorio, usuario),
                "qr" => new PaginaLeerQr(repositorio, usuario),
                "lotes" => new PaginaLotes(repositorio, usuario),
                "plantillas" => new PaginaPlantillas(repositorio, usuario),
                "nuevo" => new PaginaNuevoControl(repositorio, usuario),
                "historial" => new PaginaHistorialControles(repositorio, usuario),
                _ => new PaginaResumenCalidad(repositorio, usuario)
            };
            await Navigation.PushAsync(pagina);
        }
        catch (Exception ex) { await DisplayAlertAsync("No se pudo abrir", ex.Message, "Aceptar"); }
        finally { abriendo = false; }
    }
    private async void OnCerrarSesionClicked(object? sender, EventArgs e)
    {
        if (!await DisplayAlertAsync("Cerrar sesión", "¿Deseas regresar al inicio de sesión?", "Sí", "Cancelar")) return;
        try { await ConexionCalidad.CerrarAsync(); } catch (Exception ex) { await DisplayAlertAsync("Sesión cerrada en este dispositivo", ex.Message, "Aceptar"); }
        usuario = ""; BienvenidaLabel.Text = "Bienvenido";
        await Shell.Current.GoToAsync("//login");
    }
}







