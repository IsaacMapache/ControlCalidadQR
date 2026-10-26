using System.Globalization;
using System.Text.Json;

namespace ControlCalidadQR.Calidad;

// Componentes compartidos para mantener formularios legibles en móvil y escritorio.
public abstract class PaginaCalidad : ContentPage
{
    protected readonly RepositorioCalidad Repo;
    protected readonly string Usuario;
    protected readonly VerticalStackLayout Cuerpo = new() { Spacing = 14, Padding = 24, MaximumWidthRequest = 820 };
    protected PaginaCalidad(RepositorioCalidad repo, string usuario, string titulo)
    {
        Repo = repo; Usuario = usuario; Title = titulo;
        BackgroundColor = TemaCalidad.Color("FondoCalidad");
        Content = new ScrollView { Content = Cuerpo };
    }
    protected virtual bool PuedeEditar => true;
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!PuedeEditar) SoloConsulta(Cuerpo);
    }
    private static void SoloConsulta(View vista)
    {
        if (vista is Entry entrada) entrada.IsReadOnly = true;
        if (vista is Editor editor) editor.IsReadOnly = true;
        if (vista is Picker or DatePicker or Switch) vista.IsEnabled = false;
        if (vista is Layout layout)
            foreach (var hijo in layout.Children.OfType<View>()) SoloConsulta(hijo);
        if (vista is Border borde && borde.Content is View contenido) SoloConsulta(contenido);
    }
    protected Label Texto(string texto, bool titulo = false) => new()
    {
        Text = texto, TextColor = TemaCalidad.Color(titulo ? "Primary" : "TextoCalidad"),
        FontSize = titulo ? 22 : 15, FontAttributes = titulo ? FontAttributes.Bold : FontAttributes.None
    };
    protected Entry Campo(string etiqueta, string valor = "", bool numero = false)
    {
        Cuerpo.Add(Texto(etiqueta));
        var entrada = new Entry { Text = valor, TextColor = Colors.Black, BackgroundColor = Colors.White,
            Keyboard = numero ? Keyboard.Numeric : Keyboard.Default };
        Cuerpo.Add(new Border { Content = entrada, BackgroundColor = Colors.White, Stroke = TemaCalidad.Color("BordeCalidad"), Padding = new Thickness(12, 4), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 } }); return entrada;
    }
    protected Editor Area(string etiqueta, string valor = "")
    {
        Cuerpo.Add(Texto(etiqueta));
        var editor = new Editor { Text = valor, AutoSize = EditorAutoSizeOption.TextChanges,
            MinimumHeightRequest = 90, TextColor = Colors.Black, BackgroundColor = Colors.White };
        Cuerpo.Add(new Border { Content = editor, BackgroundColor = Colors.White, Stroke = TemaCalidad.Color("BordeCalidad"), Padding = 10, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 } }); return editor;
    }
    protected DatePicker Fecha(string etiqueta, DateTime valor)
    {
        Cuerpo.Add(Texto(etiqueta));
        var fecha = new DatePicker { Date = valor, Format = "dd/MM/yyyy", TextColor = Colors.Black, BackgroundColor = Colors.White };
        Cuerpo.Add(fecha); return fecha;
    }
    protected Switch Casilla(string etiqueta, bool valor)
    {
        var control = new Switch { IsToggled = valor, OnColor = TemaCalidad.Color("Primary") };
        var fila = new HorizontalStackLayout { Spacing = 12, Children = { control, Texto(etiqueta) } };
        Cuerpo.Add(fila); return control;
    }
    protected Button Boton(string texto, Func<Task> accion, Layout? destino = null)
    {
        var boton = new Button { Text = texto, BackgroundColor = TemaCalidad.Color("Primary"), TextColor = Colors.White,
            MinimumHeightRequest = 48, CornerRadius = 12 };
        boton.Clicked += async (_, _) =>
        {
            boton.IsEnabled = false;
            try { await Seguro(accion); }
            finally { boton.IsEnabled = true; }
        };
        (destino ?? Cuerpo).Add(boton); return boton;
    }
    protected async Task Seguro(Func<Task> accion)
    {
        try { await accion(); }
        catch (Exception ex) { await DisplayAlertAsync("No se pudo completar", ex.Message, "Aceptar"); }
    }
    protected Task Abrir(Page pagina) => Navigation.PushAsync(pagina);
    protected static decimal Numero(string? texto, string etiqueta)
    {
        if (!RepositorioCalidad.LeerNumero(texto, out var valor))
            throw new InvalidOperationException($"Escribe un número válido en {etiqueta}, usando el separador decimal de tu equipo.");
        return valor;
    }
    protected static decimal? Limite(string? texto, string etiqueta) => string.IsNullOrWhiteSpace(texto) ? null : Numero(texto, etiqueta);
    protected static string Formato(decimal? valor) => valor?.ToString(CultureInfo.CurrentCulture) ?? "";
    protected void Tarjeta(string titulo, string detalle, Action<VerticalStackLayout> acciones, Layout? destino = null)
    {
        var contenido = new VerticalStackLayout { Spacing = 10, Padding = 16 };
        contenido.Add(Texto(titulo, true)); contenido.Add(Texto(detalle)); acciones(contenido);
        (destino ?? Cuerpo).Add(new Border { Content = contenido, BackgroundColor = Colors.White,
            Stroke = TemaCalidad.Color("BordeCalidad"), StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 } });
    }
    protected void VerCambios(string entidad, string id) =>
        _ = Seguro(() => Abrir(new PaginaHistorialCambios(Repo, Usuario, entidad, id)));
}

// Permite registrar y buscar lotes, así como consultar sus controles asociados.
public sealed class PaginaLotes : PaginaCalidad
{
    private DatosCalidad datos = new();
    private readonly VerticalStackLayout lista = new() { Spacing = 14 };
    private readonly SearchBar buscar = new() { Placeholder = "Buscar lote o producto", TextColor = Colors.Black };
    private readonly Switch archivados;
    public PaginaLotes(RepositorioCalidad repo, string usuario) : base(repo, usuario, "Lotes")
    {
        Cuerpo.Add(Texto("Cada lote conserva su identidad y el historial de cambios."));
        if (ConexionCalidad.PuedeRegistrar) Boton("Registrar lote", () => Abrir(new PaginaEditarLote(Repo, Usuario, new())));
        Cuerpo.Add(buscar); archivados = Casilla("Mostrar también archivados", false); Cuerpo.Add(lista);
        buscar.TextChanged += (_, _) => Mostrar(); archivados.Toggled += (_, _) => Mostrar();
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); await Seguro(async () => { datos = await Repo.LeerAsync(); Mostrar(); });
    }
    private void Mostrar()
    {
        lista.Clear(); string filtro = buscar.Text?.Trim() ?? "";
        var lotes = datos.Lotes.Where(x => (archivados.IsToggled || !x.Archivado) &&
            $"{x.Numero} {x.Producto}".Contains(filtro, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.Fecha).ToList();
        if (lotes.Count == 0)
            lista.Add(Texto(filtro.Length > 0 ? "No hay lotes que coincidan. Prueba otro número o producto."
                : datos.Lotes.Count > 0 ? "No hay lotes activos. Activa Mostrar también archivados para consultarlos."
                : ConexionCalidad.PuedeRegistrar ? "Todavía no hay lotes. Pulsa Registrar lote para empezar."
                : "Todavía no hay lotes. Un inspector o administrador puede registrar el primero."));
        foreach (var lote in lotes)
            Tarjeta(lote.ToString(), $"{lote.Fecha:dd/MM/yyyy} · {lote.Cantidad} {lote.Unidad} · {(lote.Archivado ? "Archivado" : "Activo")}\nIdentificador: {lote.Id}", fila =>
            {
                Boton(ConexionCalidad.PuedeRegistrar ? "Ver / editar lote" : "Ver lote", () => Abrir(new PaginaEditarLote(Repo, Usuario, RepositorioCalidad.Copia(lote))), fila);
                Boton("Ver QR del lote", () => Abrir(new PaginaQrLote(Repo, Usuario, lote.Id)), fila);
                Boton("Controles de este lote", () => Abrir(new PaginaHistorialControles(Repo, Usuario, lote.Id)), fila);
            }, lista);
    }
}

// Edita los datos del lote y conserva su identificador permanente y su historial.
public sealed class PaginaEditarLote : PaginaCalidad
{
    protected override bool PuedeEditar => ConexionCalidad.PuedeRegistrar;
    public PaginaEditarLote(RepositorioCalidad repo, string usuario, LoteCalidad lote) : base(repo, usuario,
        lote.Version == 0 ? "Registrar lote" : "Datos del lote")
    {
        Cuerpo.Add(Texto($"Identificador permanente: {lote.Id}"));
        var numero = Campo("Número de lote *", lote.Numero);
        var producto = Campo("Producto o proceso *", lote.Producto);
        var fecha = Fecha("Fecha del lote", lote.Fecha);
        var cantidad = Campo("Cantidad *", lote.Cantidad == 0 ? "" : Formato(lote.Cantidad), true);
        var unidad = Campo("Unidad *", lote.Unidad);
        var observaciones = Area("Observaciones", lote.Observaciones);
        var archivado = Casilla("Lote archivado", lote.Archivado);
        Cuerpo.Add(Texto("Archivar conserva los registros y bloquea nuevos controles. Puedes reactivar el lote después."));
        if (ConexionCalidad.PuedeRegistrar) Boton("Guardar lote", async () =>
        {
            lote.Numero = numero.Text ?? ""; lote.Producto = producto.Text ?? ""; lote.Fecha = fecha.Date ?? DateTime.Today;
            lote.Cantidad = Numero(cantidad.Text, "cantidad"); lote.Unidad = unidad.Text ?? "";
            lote.Observaciones = observaciones.Text ?? ""; lote.Archivado = archivado.IsToggled;
            await Repo.GuardarLoteAsync(lote, Usuario); await Navigation.PopAsync();
        });
        if (lote.Version > 0)
            {
            Boton("Ver QR del lote guardado", () => Abrir(new PaginaQrLote(Repo, Usuario, lote.Id)));
            Boton("Historial de cambios del lote", () => Abrir(new PaginaHistorialCambios(Repo, Usuario, "Lote", lote.Id)));
        }
    }
}

// Muestra las plantillas disponibles y permite crear o modificar sus criterios.
public sealed class PaginaPlantillas : PaginaCalidad
{
    public PaginaPlantillas(RepositorioCalidad repo, string usuario) : base(repo, usuario, "Plantillas de control") { }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); await Seguro(async () =>
        {
            var datos = await Repo.LeerAsync(); Cuerpo.Clear();
            Cuerpo.Add(Texto("Define qué se revisa. Los controles conservan los criterios de la versión utilizada."));
            if (ConexionCalidad.EsAdministrador) Boton("Crear plantilla", () => Abrir(new PaginaEditarPlantilla(Repo, Usuario, new())));
            foreach (var plantilla in datos.Plantillas.OrderBy(x => x.Archivada).ThenBy(x => x.Nombre))
                Tarjeta(plantilla.ToString(), $"{plantilla.Criterios.Count} criterios · {(plantilla.Archivada ? "Archivada" : "Activa")}\n{plantilla.Descripcion}", fila =>
                    Boton(ConexionCalidad.EsAdministrador ? "Ver / editar plantilla" : "Ver plantilla", () => Abrir(new PaginaEditarPlantilla(Repo, Usuario, RepositorioCalidad.Copia(plantilla))), fila));
        });
    }
}

// Reúne los criterios de una plantilla; sus cambios se guardan como una nueva versión.
public sealed class PaginaEditarPlantilla : PaginaCalidad
{
    protected override bool PuedeEditar => ConexionCalidad.EsAdministrador;
    private readonly PlantillaCalidad plantilla;
    private readonly VerticalStackLayout criterios = new() { Spacing = 12 };
    private readonly Entry nombre;
    private readonly Editor descripcion;
    private readonly Switch archivada;
    public PaginaEditarPlantilla(RepositorioCalidad repo, string usuario, PlantillaCalidad valor) : base(repo, usuario, "Configurar plantilla")
    {
        plantilla = valor;
        nombre = Campo("Nombre de plantilla *", valor.Nombre);
        descripcion = Area("Descripción / instrucciones", valor.Descripcion);
        archivada = Casilla("Plantilla archivada", valor.Archivada);
        Cuerpo.Add(Texto("Criterios de evaluación", true)); Cuerpo.Add(criterios);
        if (ConexionCalidad.EsAdministrador) Boton("Agregar criterio", () => AbrirCriterio(new(), true));
        if (ConexionCalidad.EsAdministrador) Boton("Guardar plantilla", async () =>
        {
            plantilla.Nombre = nombre.Text ?? ""; plantilla.Descripcion = descripcion.Text ?? "";
            plantilla.Archivada = archivada.IsToggled;
            await Repo.GuardarPlantillaAsync(plantilla, Usuario); await Navigation.PopAsync();
        });
        if (valor.Version > 0) Boton("Historial de la plantilla", () => Abrir(new PaginaHistorialCambios(Repo, Usuario, "Plantilla", valor.Id)));
        MostrarCriterios();
    }
    private Task AbrirCriterio(CriterioCalidad criterio, bool nuevo) => Abrir(new PaginaEditarCriterio(Repo, Usuario,
        RepositorioCalidad.Copia(criterio), editado =>
        {
            if (!nuevo) plantilla.Criterios.RemoveAll(x => x.Id == editado.Id);
            // Conservamos el orden al editar un criterio existente.
            plantilla.Criterios.Insert(nuevo ? plantilla.Criterios.Count : Math.Min(posicion, plantilla.Criterios.Count), editado);
            MostrarCriterios();
        }));
    private int posicion;
    private void MostrarCriterios()
    {
        criterios.Clear();
        if (plantilla.Criterios.Count == 0) criterios.Add(Texto("Aún no hay criterios. Agrega el primero."));
        foreach (var criterio in plantilla.Criterios)
            Tarjeta(criterio.Nombre, $"{RepositorioCalidad.TipoTexto(criterio.Tipo)} · {(criterio.Obligatorio ? "Obligatorio" : "Opcional")}" +
                (criterio.Tipo == TipoCriterio.Numero ? $"\nLímites inclusivos: {Formato(criterio.Minimo)} a {Formato(criterio.Maximo)} {criterio.Unidad}" : ""), fila =>
            {
                if (ConexionCalidad.EsAdministrador) Boton("Editar criterio", () => { posicion = plantilla.Criterios.IndexOf(criterio); return AbrirCriterio(criterio, false); }, fila);
                if (ConexionCalidad.EsAdministrador) Boton("Quitar criterio", async () =>
                {
                    if (await DisplayAlertAsync("Quitar criterio", $"¿Quitar «{criterio.Nombre}» de esta plantilla?", "Quitar", "Cancelar"))
                    { plantilla.Criterios.Remove(criterio); MostrarCriterios(); }
                }, fila);
            }, criterios);
    }
}

// Configura el tipo de respuesta, la obligatoriedad y los límites de un criterio.
public sealed class PaginaEditarCriterio : PaginaCalidad
{
    protected override bool PuedeEditar => ConexionCalidad.EsAdministrador;
    public PaginaEditarCriterio(RepositorioCalidad repo, string usuario, CriterioCalidad criterio, Action<CriterioCalidad> aceptar)
        : base(repo, usuario, "Criterio de calidad")
    {
        var nombre = Campo("Qué se revisará *", criterio.Nombre);
        Cuerpo.Add(Texto("Tipo de respuesta"));
        var tipo = new Picker { TextColor = Colors.Black, BackgroundColor = Colors.White,
            ItemsSource = new[] { "Cumple / No cumple", "Número", "Texto" }, SelectedIndex = (int)criterio.Tipo };
        Cuerpo.Add(tipo);
        var obligatorio = Casilla("Respuesta obligatoria al finalizar", criterio.Obligatorio);
        var bloque = new VerticalStackLayout { Spacing = 10 };
        bloque.Add(Texto("Unidad (por ejemplo °C, mm, kg)"));
        var unidad = new Entry { Text = criterio.Unidad, TextColor = Colors.Black, BackgroundColor = Colors.White }; bloque.Add(unidad);
        bloque.Add(Texto("Mínimo permitido (opcional)"));
        var minimo = new Entry { Text = Formato(criterio.Minimo), Keyboard = Keyboard.Numeric, TextColor = Colors.Black, BackgroundColor = Colors.White }; bloque.Add(minimo);
        bloque.Add(Texto("Máximo permitido (opcional)"));
        var maximo = new Entry { Text = Formato(criterio.Maximo), Keyboard = Keyboard.Numeric, TextColor = Colors.Black, BackgroundColor = Colors.White }; bloque.Add(maximo);
        bloque.Add(Texto("Los límites se incluyen. Sin límites, el valor solo se registra y no determina aprobación."));
        Cuerpo.Add(bloque); bloque.IsVisible = tipo.SelectedIndex == 1;
        tipo.SelectedIndexChanged += (_, _) => bloque.IsVisible = tipo.SelectedIndex == 1;
        if (ConexionCalidad.EsAdministrador) Boton("Aceptar criterio", async () =>
        {
            if (string.IsNullOrWhiteSpace(nombre.Text)) throw new InvalidOperationException("Escribe el nombre del criterio.");
            criterio.Nombre = nombre.Text.Trim(); criterio.Tipo = (TipoCriterio)tipo.SelectedIndex;
            criterio.Obligatorio = obligatorio.IsToggled;
            criterio.Unidad = criterio.Tipo == TipoCriterio.Numero ? unidad.Text?.Trim() ?? "" : "";
            criterio.Minimo = criterio.Tipo == TipoCriterio.Numero ? Limite(minimo.Text, "mínimo") : null;
            criterio.Maximo = criterio.Tipo == TipoCriterio.Numero ? Limite(maximo.Text, "máximo") : null;
            if (criterio.Minimo > criterio.Maximo) throw new InvalidOperationException("El mínimo no puede superar al máximo.");
            aceptar(criterio); await Navigation.PopAsync();
        });
        Cuerpo.Add(Texto("El criterio se incorpora al formulario. Guarda la plantilla para conservar los cambios."));
    }
}

// Relaciona un lote activo con la plantilla que se utilizará para evaluarlo.
public sealed class PaginaNuevoControl : PaginaCalidad
{
    private readonly string? loteId;
    public PaginaNuevoControl(RepositorioCalidad repo, string usuario, string? lote = null) : base(repo, usuario, "Nuevo control") { loteId = lote; }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); await Seguro(async () =>
        {
            var datos = await Repo.LeerAsync(); Cuerpo.Clear();
            var lotes = datos.Lotes.Where(x => !x.Archivado).OrderBy(x => x.Numero).ToList();
            var plantillas = datos.Plantillas.Where(x => !x.Archivada).OrderBy(x => x.Nombre).ToList();
            if (lotes.Count == 0 || plantillas.Count == 0)
            {
                Cuerpo.Add(Texto("Necesitas al menos un lote activo y una plantilla activa para realizar un control."));
                Boton("Ir a lotes", () => Abrir(new PaginaLotes(Repo, Usuario)));
                Boton("Ir a plantillas", () => Abrir(new PaginaPlantillas(Repo, Usuario))); return;
            }
            Cuerpo.Add(Texto("Selecciona el lote y la plantilla de evaluación."));
            Cuerpo.Add(Texto("Lote *"));
            var lote = new Picker { Title = "Selecciona un lote", ItemsSource = lotes, TextColor = Colors.Black, BackgroundColor = Colors.White };
            if (loteId is not null) lote.SelectedItem = lotes.FirstOrDefault(x => x.Id == loteId);
            Cuerpo.Add(lote); Cuerpo.Add(Texto("Plantilla *"));
            var plantilla = new Picker { Title = "Selecciona una plantilla", ItemsSource = plantillas, TextColor = Colors.Black, BackgroundColor = Colors.White };
            Cuerpo.Add(plantilla);
            if (ConexionCalidad.PuedeRegistrar) Boton("Comenzar evaluación", async () =>
            {
                if (lote.SelectedItem is not LoteCalidad l || plantilla.SelectedItem is not PlantillaCalidad p)
                    throw new InvalidOperationException("Selecciona un lote y una plantilla.");
                // Sustituimos el selector para regresar directamente al listado tras guardar.
                var editor = new PaginaEvaluarControl(Repo, Usuario, RepositorioCalidad.CrearControl(l, p, Usuario));
                await Abrir(editor); Navigation.RemovePage(this);
            });
        });
    }
}

// Construye el formulario según los criterios guardados y permite guardar o finalizar la evaluación.
public sealed class PaginaEvaluarControl : PaginaCalidad
{
    protected override bool PuedeEditar => ConexionCalidad.PuedeRegistrar;
    private readonly ControlCalidad control;
    private readonly List<Func<RespuestaCalidad>> lecturas = [];
    private readonly Entry responsable;
    private readonly Editor observaciones;
    private readonly Editor? motivo;
    private readonly DatePicker fecha;
    private bool guardando;
    public PaginaEvaluarControl(RepositorioCalidad repo, string usuario, ControlCalidad valor) : base(repo, usuario,
        valor.Finalizado ? "Detalle del control" : "Evaluar control")
    {
        control = RepositorioCalidad.Copia(valor);
        Cuerpo.Add(Texto($"{control.PlantillaNombre} · versión {control.PlantillaVersion}", true));
        Cuerpo.Add(Texto(control.PlantillaDescripcion));
        Cuerpo.Add(Texto($"Control: {control.Id}\nLote: {control.LoteId}\nEstado: {control.Resultado}"));
        if (control.Finalizado) Boton("Generar reporte PDF", () => Abrir(new PaginaReporteControl(Repo, Usuario, control.Id)));
        if (control.RevisionDeId is not null) Cuerpo.Add(Texto($"Revisión del control: {control.RevisionDeId}"));
        responsable = Campo("Responsable *", control.Finalizado || !ConexionCalidad.PuedeRegistrar ? control.Responsable : Usuario);
        responsable.IsReadOnly = true; // La API utiliza la identidad de la sesión.
        fecha = Fecha("Fecha de la evaluación", control.Fecha);
        if (control.RevisionDeId is not null) motivo = Area("Motivo de la revisión *", control.MotivoRevision);
        foreach (var respuesta in control.Respuestas) AgregarRespuesta(respuesta);
        observaciones = Area("Observaciones generales", control.Observaciones);
        if (control.Finalizado)
        {
            responsable.IsReadOnly = true; fecha.IsEnabled = false; observaciones.IsReadOnly = true;
            if (motivo is not null) motivo.IsReadOnly = true;
            Cuerpo.Add(Texto($"Finalizado: {control.FinalizadoEn?.ToLocalTime():dd/MM/yyyy HH:mm}\nEste registro se conserva sin modificaciones."));

            if (ConexionCalidad.PuedeRegistrar) Boton("Crear revisión", async () =>
            {
                var datos = await Repo.LeerAsync();
                var existente = datos.Controles.FirstOrDefault(x => x.RevisionDeId == control.Id);
                if (existente is not null)
                {
                    await DisplayAlertAsync("Revisión existente", "Este control ya tiene una revisión. Se abrirá ese registro.", "Aceptar");
                    await Abrir(new PaginaEvaluarControl(Repo, Usuario, existente)); return;
                }
                await Abrir(new PaginaEvaluarControl(Repo, Usuario, RepositorioCalidad.CrearRevision(control, Usuario)));
            });
        }
        else
        {
            Cuerpo.Add(Texto("Cumple y números dentro de límites aprueban. Un incumplimiento rechaza. Texto y números sin límites solo se registran. Sin criterios evaluables, el resultado será «Registrado»."));
            if (ConexionCalidad.PuedeRegistrar) Boton("Guardar borrador", () => Guardar(false));
            if (ConexionCalidad.PuedeRegistrar) Boton("Finalizar control", () => Guardar(true));
        }
        if (control.Version > 0) Boton("Historial de cambios", () => Abrir(new PaginaHistorialCambios(Repo, Usuario, "Control", control.Id)));
    }
    private void AgregarRespuesta(RespuestaCalidad respuesta)
    {
        var criterio = respuesta.Criterio;
        Cuerpo.Add(Texto(criterio.Nombre + (criterio.Obligatorio ? " *" : " (opcional)"), true));
        if (criterio.Tipo == TipoCriterio.SiNo)
        {
            var opciones = new[] { "Sin responder", "Cumple", "No cumple" };
            var picker = new Picker { ItemsSource = opciones, SelectedIndex = Math.Max(0, Array.IndexOf(opciones, respuesta.Valor)),
                TextColor = Colors.Black, BackgroundColor = Colors.White, IsEnabled = !control.Finalizado };
            Cuerpo.Add(picker);
            lecturas.Add(() => new() { Criterio = RepositorioCalidad.Copia(criterio), Valor = picker.SelectedIndex <= 0 ? "" : opciones[picker.SelectedIndex] });
        }
        else
        {
            string valor = respuesta.Valor;
            if (criterio.Tipo == TipoCriterio.Numero)
            {
                Cuerpo.Add(Texto($"Unidad: {criterio.Unidad} · Mínimo: {Formato(criterio.Minimo)} · Máximo: {Formato(criterio.Maximo)}"));
                if (decimal.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var numero)) valor = Formato(numero);
            }
            var entrada = new Entry { Text = valor, TextColor = Colors.Black, BackgroundColor = Colors.White,
                Keyboard = criterio.Tipo == TipoCriterio.Numero ? Keyboard.Numeric : Keyboard.Default, IsReadOnly = control.Finalizado };
            Cuerpo.Add(entrada);
            lecturas.Add(() => new()
            {
                Criterio = RepositorioCalidad.Copia(criterio), Valor = criterio.Tipo == TipoCriterio.Numero && !string.IsNullOrWhiteSpace(entrada.Text)
                    ? Numero(entrada.Text, criterio.Nombre).ToString(CultureInfo.InvariantCulture) : entrada.Text?.Trim() ?? ""
            });
        }
    }
    private async Task Guardar(bool finalizar)
    {
        if (guardando) return;
        guardando = true;
        try
        {
            var guardar = RepositorioCalidad.Copia(control);
            guardar.Responsable = responsable.Text?.Trim() ?? ""; guardar.Fecha = fecha.Date ?? DateTime.Today;
            guardar.Observaciones = observaciones.Text ?? ""; guardar.MotivoRevision = motivo?.Text ?? "";
            guardar.Respuestas = lecturas.Select(x => x()).ToList();
            string resultado = RepositorioCalidad.Evaluar(guardar.Respuestas, finalizar);
            if (finalizar && !await DisplayAlertAsync("Finalizar control", $"Resultado: {resultado}. Se conservará sin modificaciones. ¿Continuar?", "Finalizar", "Cancelar")) return;
            await Repo.GuardarControlAsync(guardar, Usuario, finalizar);
            await DisplayAlertAsync("Control guardado", finalizar ? $"Resultado: {resultado}." : "Puedes continuar el borrador desde el historial.", "Aceptar");
            await Navigation.PopAsync();
        }
        finally { guardando = false; }
    }
}

// Consulta controles por lote, responsable, plantilla o resultado y permite continuar borradores.
public sealed class PaginaHistorialControles : PaginaCalidad
{
    private readonly string? loteId;
    private DatosCalidad datos = new();
    private readonly VerticalStackLayout lista = new() { Spacing = 14 };
    private readonly SearchBar buscar = new() { Placeholder = "Buscar lote, plantilla o responsable", TextColor = Colors.Black };
    private readonly Picker estado = new() { ItemsSource = new[] { "Todos", "Borrador", "Aprobado", "Rechazado", "Registrado" }, SelectedIndex = 0, TextColor = Colors.Black };
    public PaginaHistorialControles(RepositorioCalidad repo, string usuario, string? lote = null) : base(repo, usuario, "Historial de controles")
    {
        loteId = lote;
        if (ConexionCalidad.PuedeRegistrar) Boton("Nuevo control", () => Abrir(new PaginaNuevoControl(Repo, Usuario, loteId)));
        Cuerpo.Add(buscar); Cuerpo.Add(estado); Cuerpo.Add(lista);
        buscar.TextChanged += (_, _) => Mostrar(); estado.SelectedIndexChanged += (_, _) => Mostrar();
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); await Seguro(async () => { datos = await Repo.LeerAsync(); Mostrar(); });
    }
    private void Mostrar()
    {
        lista.Clear(); string filtro = buscar.Text?.Trim() ?? "";
        var controles = datos.Controles.Where(x => (loteId is null || x.LoteId == loteId) &&
            (estado.SelectedIndex <= 0 || x.Resultado == estado.SelectedItem?.ToString())).OrderByDescending(x => x.Fecha).ThenByDescending(x => x.FinalizadoEn);
        int cantidad = 0;
        foreach (var control in controles)
        {
            var lote = datos.Lotes.FirstOrDefault(x => x.Id == control.LoteId);
            if (!$"{lote} {control.PlantillaNombre} {control.Responsable} {control.Id}".Contains(filtro, StringComparison.OrdinalIgnoreCase)) continue;
            cantidad++;
            var posterior = datos.Controles.FirstOrDefault(x => x.RevisionDeId == control.Id);
            Tarjeta($"{control.Resultado} · {lote?.Numero}", $"{control.PlantillaNombre} · {control.Fecha:dd/MM/yyyy}\nResponsable: {control.Responsable}" +
                (control.RevisionDeId is not null ? "\nEs una revisión; conserva el vínculo con el original." : "") +
                (posterior is not null ? $"\nTiene revisión {(posterior.Finalizado ? "finalizada" : "en borrador")}: {posterior.Id}" : ""), fila =>
                Boton(control.Finalizado || !ConexionCalidad.PuedeRegistrar ? "Ver control" : "Continuar borrador", () => Abrir(new PaginaEvaluarControl(Repo, Usuario, control)), fila), lista);
        }
        if (cantidad == 0) lista.Add(Texto("No hay controles que coincidan con la búsqueda."));
    }
}

// Muestra quién realizó cada cambio, cuándo lo hizo y qué información se modificó.
public sealed class PaginaHistorialCambios : PaginaCalidad
{
    private readonly string entidad, id;
    public PaginaHistorialCambios(RepositorioCalidad repo, string usuario, string tipo, string identificador) : base(repo, usuario, "Historial de cambios")
    { entidad = tipo; id = identificador; }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); await Seguro(async () =>
        {
            var datos = await Repo.LeerAsync(); Cuerpo.Clear();
            Cuerpo.Add(Texto($"{entidad}: {id}"));
            Cuerpo.Add(Texto("Los cambios nuevos registran la cuenta autenticada por el servidor. Los registros importados conservan su procedencia local."));
            var cambios = datos.Cambios.Where(x => x.Entidad == entidad && x.EntidadId == id).OrderByDescending(x => x.Fecha).ToList();
            if (cambios.Count == 0) Cuerpo.Add(Texto("Todavía no hay cambios guardados."));
            foreach (var cambio in cambios)
                Tarjeta(cambio.Accion, $"{cambio.Fecha.ToLocalTime():dd/MM/yyyy HH:mm:ss} · Usuario: {cambio.Usuario}", fila =>
                    Boton("Ver qué cambió", () => Abrir(new PaginaDetalleCambio(Repo, Usuario, cambio)), fila));
        });
    }
}

// Compara los datos anteriores y posteriores de una modificación registrada.
public sealed class PaginaDetalleCambio : PaginaCalidad
{
    public PaginaDetalleCambio(RepositorioCalidad repo, string usuario, CambioCalidad cambio) : base(repo, usuario, "Detalle del cambio")
    {
        Cuerpo.Add(Texto($"{cambio.Accion}\n{cambio.Fecha.ToLocalTime():dd/MM/yyyy HH:mm:ss} · {cambio.Usuario}", true));
        // Se comparan campos y respuestas; no exponemos JSON en la interfaz de usuario.
        using var despues = JsonDocument.Parse(cambio.Despues);
        using var antes = string.IsNullOrEmpty(cambio.Antes) ? null : JsonDocument.Parse(cambio.Antes);
        var etiquetas = new Dictionary<string, string>
        {
            ["Numero"] = "Número de lote", ["Producto"] = "Producto o proceso", ["Fecha"] = "Fecha", ["Cantidad"] = "Cantidad",
            ["Unidad"] = "Unidad", ["Observaciones"] = "Observaciones", ["Archivado"] = "Archivado", ["Nombre"] = "Nombre",
            ["Descripcion"] = "Descripción", ["Archivada"] = "Archivada", ["Criterios"] = "Criterios", ["Responsable"] = "Responsable",
            ["Finalizado"] = "Finalizado", ["Resultado"] = "Resultado", ["MotivoRevision"] = "Motivo de revisión", ["Respuestas"] = "Respuestas"
        };
        foreach (var campo in despues.RootElement.EnumerateObject())
        {
            if (!etiquetas.TryGetValue(campo.Name, out var etiqueta)) continue;
            string anterior = antes is not null && antes.RootElement.TryGetProperty(campo.Name, out var previo) ? Legible(previo) : "Sin registro";
            string actual = Legible(campo.Value);
            if (anterior == actual) continue;
            Cuerpo.Add(Texto(etiqueta, true)); Cuerpo.Add(Texto($"Antes: {anterior}\nDespués: {actual}"));
        }
    }
    private static string Legible(JsonElement valor)
    {
        if (valor.ValueKind == JsonValueKind.Array)
            return string.Join("\n", valor.EnumerateArray().Select(x =>
            {
                var criterio = x.TryGetProperty("Criterio", out var c) ? c : x;
                string nombre = criterio.GetProperty("Nombre").GetString() ?? "";
                if (x.TryGetProperty("Valor", out var v)) return $"{nombre}: {v.GetString()}";
                return $"{nombre} · {criterio.GetProperty("Tipo").GetString()} · Obligatorio: {Legible(criterio.GetProperty("Obligatorio"))}" +
                    $" · Mínimo: {Legible(criterio.GetProperty("Minimo"))} · Máximo: {Legible(criterio.GetProperty("Maximo"))} · {criterio.GetProperty("Unidad").GetString()}";
            }));
        return valor.ValueKind switch
        {
            JsonValueKind.True => "Sí", JsonValueKind.False => "No", JsonValueKind.Null => "Sin límite",
            JsonValueKind.String => valor.GetString() ?? "", _ => valor.ToString()
        };
    }
}

// Resume los resultados vigentes sin contar dos veces un control que ya fue corregido.
public sealed class PaginaResumenCalidad : PaginaCalidad
{
    public PaginaResumenCalidad(RepositorioCalidad repo, string usuario) : base(repo, usuario, "Resumen de calidad") { }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); await Seguro(async () =>
        {
            var datos = await Repo.LeerAsync(); Cuerpo.Clear();
            Cuerpo.Add(Texto("Resumen central", true));
            Cuerpo.Add(Texto($"Lotes activos: {datos.Lotes.Count(x => !x.Archivado)}\nLotes archivados: {datos.Lotes.Count(x => x.Archivado)}\nPlantillas activas: {datos.Plantillas.Count(x => !x.Archivada)}\nBorradores: {datos.Controles.Count(x => !x.Finalizado)}"));
            // Una revisión en borrador no sustituye al resultado final del original.
            var sustituidos = datos.Controles.Where(x => x.Finalizado && x.RevisionDeId is not null).Select(x => x.RevisionDeId).ToHashSet();
            var vigentes = datos.Controles.Where(x => x.Finalizado && !sustituidos.Contains(x.Id)).ToList();
            foreach (string resultado in new[] { "Aprobado", "Rechazado", "Registrado" })
                Cuerpo.Add(Texto($"{resultado}: {vigentes.Count(x => x.Resultado == resultado)}", true));
            Cuerpo.Add(Texto("Se cuenta el último control finalizado de cada cadena de revisiones, incluyendo lotes archivados. Los originales siguen disponibles en el historial."));
            Boton("Consultar controles", () => Abrir(new PaginaHistorialControles(Repo, Usuario)));
        });
    }
}










