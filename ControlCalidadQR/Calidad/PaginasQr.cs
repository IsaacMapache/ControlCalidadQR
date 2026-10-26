using System.Net;
using System.Text;
using CommunityToolkit.Maui.Storage;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace ControlCalidadQR.Calidad;

// Consulta siempre el repositorio autenticado; el QR solo selecciona el lote.
public sealed class PaginaConsultarLote : PaginaCalidad
{
    private readonly string id;
    public PaginaConsultarLote(RepositorioCalidad repo, string usuario, string id)
        : base(repo, usuario, "Consultar lote") { this.id = id; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Cuerpo.Clear();
        await Seguro(async () =>
        {
            var datos = await Repo.LeerAsync();
            var lote = datos.Lotes.SingleOrDefault(x => x.Id == id)
                ?? throw new InvalidOperationException("Este lote no existe en el servidor configurado.");
            Cuerpo.Add(Texto(lote.ToString(), true));
            Cuerpo.Add(Texto($"Fecha: {lote.Fecha:dd/MM/yyyy} · Cantidad: {lote.Cantidad} {lote.Unidad}"));
            Cuerpo.Add(Texto($"Estado: {(lote.Archivado ? "Archivado" : "Activo")} · Identificador: {lote.Id}"));
            Cuerpo.Add(Texto(lote.Observaciones));
            Boton("Ver QR y etiqueta", () => Abrir(new PaginaQrLote(Repo, Usuario, id)));
            Boton("Historial de controles", () => Abrir(new PaginaHistorialControles(Repo, Usuario, id)));
            if (!lote.Archivado && ConexionCalidad.PuedeRegistrar) Boton("Nuevo control de este lote", () => Abrir(new PaginaNuevoControl(Repo, Usuario, id)));
            if (ConexionCalidad.PuedeRegistrar) Boton("Editar lote", () => Abrir(new PaginaEditarLote(Repo, Usuario, RepositorioCalidad.Copia(lote))));
            Boton("Historial de cambios", () => Abrir(new PaginaHistorialCambios(Repo, Usuario, "Lote", id)));
        });
    }
}

public sealed class PaginaQrLote : PaginaCalidad
{
    private readonly string id;
    public PaginaQrLote(RepositorioCalidad repo, string usuario, string id) : base(repo, usuario, "QR del lote")
    { this.id = id; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Cuerpo.Clear();
        await Seguro(async () =>
        {
            // Se recupera la última información guardada, no cambios aún sin guardar del formulario.
            var lote = (await Repo.LeerAsync()).Lotes.SingleOrDefault(x => x.Id == id)
                ?? throw new InvalidOperationException("El lote ya no está disponible en este servidor.");
            var png = CodigoLote.GenerarPng(id);
            Cuerpo.Add(Texto(lote.ToString(), true));
            Cuerpo.Add(new Image { Source = ImageSource.FromStream(() => new MemoryStream(png)),
                HeightRequest = 280, WidthRequest = 280, Aspect = Aspect.AspectFit,
                BackgroundColor = Colors.White, HorizontalOptions = LayoutOptions.Center });
            Cuerpo.Add(Texto($"Identificador: {id}"));
            Cuerpo.Add(Texto("Este QR abre el lote dentro de la app. Conserva el mismo código aunque cambien los datos o se agreguen controles."));
            Boton("Guardar QR (PNG)", () => Guardar($"QR-lote-{id}.png", png));
            Boton("Compartir QR", async () =>
            {
                var archivo = Path.Combine(FileSystem.CacheDirectory, $"QR-lote-{id}.png");
                await File.WriteAllBytesAsync(archivo, png);
                await Share.Default.RequestAsync(new ShareFileRequest("QR del lote", new ShareFile(archivo, "image/png")));
            });
            Boton("Guardar etiqueta para imprimir", () => Guardar($"Etiqueta-lote-{id}.html", CrearEtiqueta(lote, png)));
            Boton("Copiar código del lote", async () =>
            {
                await Clipboard.Default.SetTextAsync(CodigoLote.Crear(id));
                await DisplayAlertAsync("Código copiado", "Puedes pegarlo en «Abrir lote por QR».", "Aceptar");
            });
            Cuerpo.Add(Texto("Para imprimir, guarda la etiqueta HTML, ábrela en el navegador y selecciona Imprimir. El QR requiere iniciar sesión en la app y usar el servidor donde se registró el lote."));
        });
    }

    private async Task Guardar(string nombre, byte[] contenido)
    {
        using var stream = new MemoryStream(contenido);
        var resultado = await FileSaver.Default.SaveAsync(nombre, stream, CancellationToken.None);
        if (resultado.IsSuccessful) await DisplayAlertAsync("Archivo guardado", resultado.FilePath, "Aceptar");
        else if (resultado.Exception is not OperationCanceledException)
            throw new InvalidOperationException("No se pudo guardar el archivo.", resultado.Exception);
    }

    private static byte[] CrearEtiqueta(LoteCalidad lote, byte[] png)
    {
        // Los textos introducidos por el usuario se codifican antes de incluirlos en HTML.
        var numero = WebUtility.HtmlEncode(lote.Numero);
        var producto = WebUtility.HtmlEncode(lote.Producto);
        var html = $$"""
            <!doctype html>
            <html lang="es"><head><meta charset="utf-8"><title>Etiqueta del lote</title>
            <style>
            body { font-family: Arial, sans-serif; color: #111; background: white; }
            main { width: 85mm; padding: 8mm; border: 1px solid #555; text-align: center; overflow-wrap: anywhere; }
            img { width: 65mm; height: 65mm; image-rendering: pixelated; }
            h1 { font-size: 20px; } p { margin: 8px 0; } small { font-size: 10px; }
            @page { margin: 12mm; } @media print { main { break-inside: avoid; } }
            </style></head><body><main>
            <h1>Control de calidad</h1><p><strong>Lote: {{numero}}</strong></p><p>{{producto}}</p>
            <img alt="Código QR del lote" src="data:image/png;base64,{{Convert.ToBase64String(png)}}">
            <p><small>{{lote.Id}}</small></p>
            <p>Escanea desde la app para consultar el lote y sus controles.</p>
            <small>Etiqueta generada: {{DateTime.Now:dd/MM/yyyy HH:mm}}. Los datos actualizados se consultan en la app.</small>
            </main></body></html>
            """;
        return Encoding.UTF8.GetBytes(html);
    }
}

// En Windows se puede elegir una imagen o pegar el código; en móviles también se usa la cámara.
public sealed class PaginaLeerQr : PaginaCalidad
{
    private readonly Entry entrada;
    private bool consultando;
    public PaginaLeerQr(RepositorioCalidad repo, string usuario) : base(repo, usuario, "Abrir lote por QR")
    {
        Cuerpo.Add(Texto("Consulta un lote y sus controles en el servidor donde iniciaste sesión."));
#if ANDROID || IOS
        Boton("Escanear con cámara", async () =>
        {
            if (!BarcodeScanning.IsSupported) throw new InvalidOperationException("No se encontró una cámara disponible. Puedes leer el QR desde una imagen.");
            var permiso = await Permissions.RequestAsync<Permissions.Camera>();
            if (permiso != PermissionStatus.Granted)
            {
                await DisplayAlertAsync("Permiso de cámara", "Puedes permitir la cámara en los ajustes de la aplicación o elegir una imagen del QR.", "Aceptar");
                return;
            }
            await Abrir(new PaginaCamaraQr(Repo, Usuario));
        });
#endif
        Boton("Leer QR desde una imagen", async () =>
        {
            var archivo = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Selecciona la imagen del QR", FileTypes = FilePickerFileType.Images });
            if (archivo is null) return;
            using var stream = await archivo.OpenReadAsync();
            // Acotar la entrada evita cargar archivos de tamaño excesivo.
            using var copia = new MemoryStream();
            var buffer = new byte[81920];
            int leidos;
            while ((leidos = await stream.ReadAsync(buffer)) > 0)
            {
                if (copia.Length + leidos > 20 * 1024 * 1024)
                    throw new InvalidOperationException("Selecciona una imagen de menos de 20 MB.");
                await copia.WriteAsync(buffer.AsMemory(0, leidos));
            }
            copia.Position = 0;
            var resultados = await LectorImagenQr.LeerAsync(copia);
            var codigos = new List<string>();
            foreach (var resultado in resultados ?? [])
            {
                try { codigos.Add(CodigoLote.Leer(resultado)); }
                catch (InvalidOperationException) { }
            }
            var ids = codigos.Distinct().ToList();
            if (ids.Count == 0) throw new InvalidOperationException("No se encontró un QR de lote válido. Usa una imagen nítida que incluya todo el margen blanco.");
            if (ids.Count > 1) throw new InvalidOperationException("La imagen contiene varios lotes. Selecciona una imagen con un solo QR.");
            await Consultar(ids[0]);
        });
        entrada = Campo("Código o identificador del lote");
        entrada.Placeholder = "Pega el código o utiliza un lector USB";
        Boton("Consultar lote", () => Consultar(entrada.Text));
        entrada.Completed += async (_, _) => await Seguro(() => Consultar(entrada.Text));
    }

    private async Task Consultar(string? texto)
    {
        if (consultando) return;
        consultando = true;
        try
        {
            var id = CodigoLote.Leer(texto);
            var datos = await Repo.LeerAsync();
            if (!datos.Lotes.Any(x => x.Id == id))
                throw new InvalidOperationException("El lote no existe en este servidor. Revisa que hayas configurado el servidor correcto.");
            await Abrir(new PaginaConsultarLote(Repo, Usuario, id));
        }
        finally { consultando = false; }
    }
}

#if ANDROID || IOS
public sealed class PaginaCamaraQr : ContentPage
{
    private readonly CameraBarcodeReaderView camara;
    private readonly RepositorioCalidad repo;
    private readonly string usuario;
    private bool visible;
    private int procesando;
    public PaginaCamaraQr(RepositorioCalidad repo, string usuario)
    {
        this.repo = repo; this.usuario = usuario; Title = "Escanear QR";
        camara = new CameraBarcodeReaderView
        {
            Options = new BarcodeReaderOptions { Formats = BarcodeFormat.QrCode, AutoRotate = true, Multiple = false },
            IsDetecting = false, CameraLocation = CameraLocation.Rear
        };
        camara.BarcodesDetected += Detectado;
        var instrucciones = new Label { Text = "Centra el QR completo dentro de la cámara.", TextColor = Colors.Black, Margin = 16 };
        var rejilla = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        rejilla.Add(instrucciones, 0, 0); rejilla.Add(camara, 0, 1); Content = rejilla;
    }
    protected override void OnAppearing()
    {
        base.OnAppearing(); visible = true; camara.IsDetecting = true;
    }
    protected override void OnDisappearing()
    {
        visible = false; camara.IsDetecting = false; camara.IsTorchOn = false;
        base.OnDisappearing();
    }
    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        if (!Navigation.NavigationStack.Contains(this))
        {
            camara.BarcodesDetected -= Detectado;
            camara.Handler?.DisconnectHandler();
        }
    }
    private void Detectado(object? sender, BarcodeDetectionEventArgs e)
    {
        var contenido = e.Results.FirstOrDefault()?.Value;
        if (contenido is null || Interlocked.CompareExchange(ref procesando, 1, 0) != 0) return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (!visible) return;
                camara.IsDetecting = false;
                var id = CodigoLote.Leer(contenido);
                if (!(await repo.LeerAsync()).Lotes.Any(x => x.Id == id))
                    throw new InvalidOperationException("El lote no existe en el servidor configurado.");
                if (!visible) return;
                await Navigation.PushAsync(new PaginaConsultarLote(repo, usuario, id));
                // Se libera la cámara al terminar y se regresa después al selector de lectura.
                Navigation.RemovePage(this);
                camara.BarcodesDetected -= Detectado;
                camara.Handler?.DisconnectHandler();
            }
            catch (Exception ex)
            {
                if (visible) await DisplayAlertAsync("No se pudo leer el lote", ex.Message, "Aceptar");
            }
            finally
            {
                Interlocked.Exchange(ref procesando, 0);
                if (visible) camara.IsDetecting = true;
            }
        });
    }
}
#endif





