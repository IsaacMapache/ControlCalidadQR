using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace ControlCalidadQR.Calidad;

// Genera el mismo documento en Windows y Android, sin depender de una impresora o de una página web.
public static class ReporteControlPdf
{
    private static readonly object Exclusivo = new();
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-MX");

    public static byte[] Crear(DatosCalidad datos, string controlId, DateTimeOffset emision)
    {
        var control = datos.Controles.SingleOrDefault(x => x.Id == controlId)
            ?? throw new InvalidOperationException("El control no existe en el servidor.");
        if (!control.Finalizado) throw new InvalidOperationException("Finaliza el control antes de generar su reporte.");
        var lote = datos.Lotes.SingleOrDefault(x => x.Id == control.LoteId)
            ?? throw new InvalidOperationException("No se encontró el lote de este control.");
        // PDFsharp mantiene una caché global de fuentes; se configura una sola vez.
        lock (Exclusivo)
        {
            GlobalFontSettings.FontResolver ??= new FuentesReporte();
            using var documento = new PdfDocument();
            documento.Info.Title = "Reporte de control de calidad";
            documento.Info.Author = "Control de calidad";
            documento.Info.Subject = $"Control {control.Id}";
            using (var escritor = new EscritorReporte(documento, control.Id, emision))
            {
                escritor.Seccion("Resultado del control");
                escritor.Texto(control.Resultado, true, 16);
                escritor.Campo("Control", control.Id);
                escritor.Campo("Versión del registro", control.Version.ToString(Cultura));
                escritor.Campo("Responsable registrado", control.Responsable);
                escritor.Campo("Fecha de evaluación", control.Fecha.ToString("dd/MM/yyyy", Cultura));
                escritor.Campo("Finalizado", control.FinalizadoEn?.ToString("dd/MM/yyyy HH:mm zzz", Cultura) ?? "No disponible");

                var revision = datos.Controles.FirstOrDefault(x => x.RevisionDeId == control.Id);
                if (revision is not null)
                    escritor.Campo(revision.Finalizado ? "Reporte histórico: sustituido por el control" : "Existe una revisión en borrador",
                        revision.Id);
                if (control.RevisionDeId is not null)
                {
                    escritor.Campo("Revisión del control", control.RevisionDeId);
                    escritor.Campo("Motivo de revisión", control.MotivoRevision);
                }

                escritor.Seccion("Lote");
                escritor.Campo("Número", lote.Numero);
                escritor.Campo("Producto o proceso", lote.Producto);
                escritor.Campo("Identificador", lote.Id);
                escritor.Campo("Fecha del lote", lote.Fecha.ToString("dd/MM/yyyy", Cultura));
                escritor.Campo("Cantidad", $"{lote.Cantidad.ToString(Cultura)} {lote.Unidad}");
                escritor.Campo("Estado", lote.Archivado ? "Archivado" : "Activo");
                if (!string.IsNullOrWhiteSpace(lote.Observaciones)) escritor.Campo("Observaciones del lote", lote.Observaciones);
                escritor.Texto("Los datos del lote son los consultados al emitir este documento. Los criterios y respuestas se conservan como fueron registrados en el control.", false, 9);

                escritor.Seccion("Plantilla utilizada");
                escritor.Campo("Nombre y versión", $"{control.PlantillaNombre} - versión {control.PlantillaVersion}");
                if (!string.IsNullOrWhiteSpace(control.PlantillaDescripcion)) escritor.Texto(control.PlantillaDescripcion);

                escritor.Seccion($"Evaluación - {control.Respuestas.Count} criterios");
                for (int i = 0; i < control.Respuestas.Count; i++)
                {
                    var respuesta = control.Respuestas[i];
                    var criterio = respuesta.Criterio;
                    escritor.Espacio(8);
                    escritor.Reservar(65);
                    escritor.Texto($"{i + 1}. {criterio.Nombre}", true);
                    var tipo = criterio.Tipo switch { TipoCriterio.SiNo => "Cumple / No cumple", TipoCriterio.Numero => "Número", _ => "Texto" };
                    escritor.Texto($"{tipo} - {(criterio.Obligatorio ? "Obligatorio" : "Opcional")}", false, 9);
                    if (criterio.Tipo == TipoCriterio.Numero)
                    {
                        escritor.Campo("Límites inclusivos",
                            $"Mínimo: {criterio.Minimo?.ToString(Cultura) ?? "sin límite"}; máximo: {criterio.Maximo?.ToString(Cultura) ?? "sin límite"}; unidad: {criterio.Unidad}");
                    }
                    var valor = respuesta.Valor;
                    if (criterio.Tipo == TipoCriterio.Numero && decimal.TryParse(valor, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var numero))
                        valor = numero.ToString(Cultura);
                    escritor.Campo("Respuesta", string.IsNullOrWhiteSpace(valor) ? "Sin respuesta" : valor);
                }
                escritor.Seccion("Observaciones del control");
                escritor.Texto(string.IsNullOrWhiteSpace(control.Observaciones) ? "Sin observaciones." : control.Observaciones);
                escritor.Reservar(250);
                escritor.Seccion("Consulta del lote");
                escritor.Qr(lote.Id);
                escritor.Texto("Escanea este QR desde la app para consultar el lote y su historial actualizado. Se requiere iniciar sesión en el mismo servidor.", false, 9);
                escritor.Texto("Este reporte reproduce los registros guardados; no constituye una firma digital.", false, 9);
            }
            using var salida = new MemoryStream();
            documento.Save(salida, false);
            return salida.ToArray();
        }
    }

    private sealed class FuentesReporte : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) =>
            new(bold ? "Semibold" : "Regular");
        public byte[] GetFont(string faceName)
        {
            using var fuente = typeof(ReporteControlPdf).Assembly.GetManifestResourceStream($"Fuentes.{faceName}.ttf")
                ?? throw new InvalidOperationException("No se encontró la fuente del reporte.");
            using var salida = new MemoryStream();
            fuente.CopyTo(salida);
            return salida.ToArray();
        }
    }

    // El texto se divide por ancho real y continúa en otra página sin recortes.
    private sealed class EscritorReporte : IDisposable
    {
        private readonly PdfDocument documento;
        private readonly string controlId;
        private readonly DateTimeOffset emision;
        private XGraphics lienzo = null!;
        private PdfPage pagina = null!;
        private double y;
        private const double Margen = 42;
        private double Ancho => pagina.Width.Point - 2 * Margen;
        private static readonly XColor Azul = XColor.FromArgb(18, 52, 71);
        private static XFont Fuente(double tamano, bool negrita = false) => new("Reporte", tamano,
            negrita ? XFontStyleEx.Bold : XFontStyleEx.Regular);

        public EscritorReporte(PdfDocument documento, string controlId, DateTimeOffset emision)
        { this.documento = documento; this.controlId = controlId; this.emision = emision; NuevaPagina(); }

        private void NuevaPagina()
        {
            lienzo?.Dispose();
            pagina = documento.AddPage();
            pagina.Size = PdfSharp.PageSize.A4;
            lienzo = XGraphics.FromPdfPage(pagina);
            lienzo.DrawString("CONTROL DE CALIDAD", Fuente(17, true), new XSolidBrush(Azul),
                new XRect(Margen, 28, Ancho, 25), XStringFormats.TopLeft);
            lienzo.DrawString("Reporte de evaluación finalizada", Fuente(10), XBrushes.DimGray,
                new XRect(Margen, 54, Ancho, 18), XStringFormats.TopLeft);
            lienzo.DrawLine(new XPen(Azul, 1), Margen, 78, pagina.Width.Point - Margen, 78);
            y = 94;
        }
        public void Reservar(double alto) { if (y + alto > pagina.Height.Point - 58) NuevaPagina(); }
        public void Espacio(double alto) { Reservar(alto); y += alto; }
        public void Seccion(string titulo)
        {
            Reservar(62); y += 10;
            lienzo.DrawRectangle(new XSolidBrush(XColor.FromArgb(235, 242, 247)), Margen, y, Ancho, 25);
            lienzo.DrawString(titulo, Fuente(11, true), new XSolidBrush(Azul), new XRect(Margen + 8, y + 4, Ancho - 16, 20), XStringFormats.TopLeft);
            y += 33;
        }
        public void Campo(string nombre, string valor)
        { Texto(nombre + ": " + valor); }
        public void Texto(string valor, bool negrita = false, double tamano = 10)
        {
            var fuente = Fuente(tamano, negrita);
            foreach (var linea in Lineas(valor ?? "", fuente))
            {
                Reservar(tamano * 1.5);
                lienzo.DrawString(linea, fuente, XBrushes.Black, new XRect(Margen, y, Ancho, tamano * 1.5), XStringFormats.TopLeft);
                y += tamano * 1.5;
            }
            y += 4;
        }
        private IEnumerable<string> Lineas(string texto, XFont fuente)
        {
            foreach (var parrafo in texto.Replace("\r", "").Split('\n'))
            {
                var linea = "";
                foreach (var palabra in Regex.Split(parrafo, @"\s+").Where(x => x.Length > 0))
                {
                    var intento = linea.Length == 0 ? palabra : linea + " " + palabra;
                    if (lienzo.MeasureString(intento, fuente).Width <= Ancho) { linea = intento; continue; }
                    if (linea.Length > 0) { yield return linea; linea = ""; }
                    // También se dividen identificadores o palabras muy largas.
                    var elementos = StringInfo.GetTextElementEnumerator(palabra);
                    while (elementos.MoveNext())
                    {
                        var caracter = elementos.GetTextElement();
                        if (linea.Length > 0 && lienzo.MeasureString(linea + caracter, fuente).Width > Ancho)
                        { yield return linea; linea = ""; }
                        linea += caracter;
                    }
                }
                yield return linea;
            }
        }
        public void Qr(string id)
        {
            Reservar(140);
            using var codigo = QRCoder.QRCodeGenerator.GenerateQrCode(CodigoLote.Crear(id), QRCoder.QRCodeGenerator.ECCLevel.Q);
            double modulo = 124d / codigo.ModuleMatrix.Count;
            lienzo.DrawRectangle(XBrushes.White, Margen, y, 124, 124);
            // Módulos vectoriales: el QR mantiene su nitidez al imprimir o ampliar el PDF.
            for (int fila = 0; fila < codigo.ModuleMatrix.Count; fila++)
                for (int columna = 0; columna < codigo.ModuleMatrix.Count; columna++)
                    if (codigo.ModuleMatrix[fila][columna])
                        lienzo.DrawRectangle(XBrushes.Black, Margen + columna * modulo, y + fila * modulo, modulo, modulo);
            y += 136;
        }
        public void Dispose()
        {
            lienzo.Dispose();
            for (int i = 0; i < documento.PageCount; i++)
            {
                var hoja = documento.Pages[i];
                using var pie = XGraphics.FromPdfPage(hoja);
                var altura = hoja.Height.Point;
                pie.DrawLine(XPens.LightGray, Margen, altura - 49, hoja.Width.Point - Margen, altura - 49);
                pie.DrawString($"Control: {controlId}", Fuente(8), XBrushes.DimGray,
                    new XRect(Margen, altura - 43, hoja.Width.Point - 2 * Margen, 12), XStringFormats.TopLeft);
                pie.DrawString($"Emitido: {emision:dd/MM/yyyy HH:mm zzz}", Fuente(8), XBrushes.DimGray,
                    new XRect(Margen, altura - 30, hoja.Width.Point - 2 * Margen, 12), XStringFormats.TopLeft);
                pie.DrawString($"Página {i + 1} de {documento.PageCount}", Fuente(8), XBrushes.DimGray,
                    new XRect(Margen, altura - 30, hoja.Width.Point - 2 * Margen, 12), XStringFormats.TopRight);
            }
        }
    }
}


