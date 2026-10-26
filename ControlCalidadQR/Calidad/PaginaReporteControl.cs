using CommunityToolkit.Maui.Storage;

namespace ControlCalidadQR.Calidad;

// Se recuperan los datos mediante la sesión actual antes de generar cualquier archivo.
public sealed class PaginaReporteControl : PaginaCalidad
{
    private readonly string controlId;
    private byte[]? pdf;
    private bool preparando;
    private string archivo = "";
    public PaginaReporteControl(RepositorioCalidad repo, string usuario, string controlId)
        : base(repo, usuario, "Reporte PDF") { this.controlId = controlId; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Volver del visor conserva el documento ya generado.
        if (pdf != null || preparando) return;
        await PrepararAsync();
    }

    private async Task PrepararAsync()
    {
        preparando = true;
        Cuerpo.Clear();
        Cuerpo.Add(Texto("Preparando reporte..."));
        await Seguro(async () =>
        {
            // Descargar una sola vez conserva una vista coherente del lote, control y revisiones.
            var datos = await Repo.LeerAsync();
            var control = datos.Controles.SingleOrDefault(x => x.Id == controlId)
                ?? throw new InvalidOperationException("El control no está disponible.");
            var generado = await Task.Run(() => ReporteControlPdf.Crear(datos, controlId, DateTimeOffset.Now));
            archivo = Path.Combine(FileSystem.CacheDirectory, $"Reporte-control-{controlId}-{Guid.NewGuid():N}.pdf");
            await File.WriteAllBytesAsync(archivo, generado);
            pdf = generado;
            Cuerpo.Clear();
            Cuerpo.Add(Texto("Reporte listo", true));
            Cuerpo.Add(Texto($"{control.PlantillaNombre} · {control.Resultado}"));
            Cuerpo.Add(Texto($"Control: {controlId}"));
            Boton("Abrir PDF / imprimir", async () =>
            {
                if (!await Launcher.Default.OpenAsync(new OpenFileRequest("Reporte de control", new ReadOnlyFile(archivo, "application/pdf"))))
                    await DisplayAlertAsync("Abrir reporte", "Guarda el PDF y ábrelo con un lector de documentos. Desde ese lector puedes imprimirlo.", "Aceptar");
            });

            Boton("Guardar PDF", async () =>
            {
                using var memoria = new MemoryStream(pdf);
                var resultado = await FileSaver.Default.SaveAsync($"Reporte-control-{controlId}.pdf", memoria, CancellationToken.None);
                if (resultado.IsSuccessful) await DisplayAlertAsync("Reporte guardado", resultado.FilePath, "Aceptar");
                else if (resultado.Exception is not OperationCanceledException)
                    throw new InvalidOperationException("No se pudo guardar el reporte.", resultado.Exception);
            });

            Boton("Compartir PDF", () => Share.Default.RequestAsync(new ShareFileRequest("Reporte de control de calidad", new ShareFile(archivo, "application/pdf"))));
            Cuerpo.Add(Texto("Incluye el resultado, los criterios y respuestas, el responsable, las fechas y el QR. Para imprimir, abre el archivo y utiliza la opción del lector PDF."));
        });
        preparando = false;
        if (pdf == null)
        {
            Cuerpo.Clear();
            Cuerpo.Add(Texto("No se pudo preparar el reporte. Comprueba la conexión y vuelve a intentarlo."));
            Boton("Reintentar", PrepararAsync);
        }
    }
}



