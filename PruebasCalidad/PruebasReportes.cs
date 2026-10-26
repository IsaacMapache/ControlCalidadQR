using ControlCalidadQR.Calidad;
using PdfSharp.Pdf.IO;
// Comprobaciones reproducibles sin alterar la base SQL de trabajo.
public static class PruebasReportes
{
    public static async Task EjecutarAsync()
    {
        await Task.CompletedTask;
var carpeta = Path.Combine(Path.GetTempPath(), "PruebasControlCalidad", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(carpeta);
var datos = new DatosCalidad();
var lote = new LoteCalidad { Numero = "LT-2026-014", Producto = "Inspección de piezas metálicas", Cantidad = 250, Unidad = "piezas", Version = 2 };
datos.Lotes.Add(lote);
var control = new ControlCalidad
{
    LoteId = lote.Id, PlantillaNombre = "Inspección de recepción", PlantillaVersion = 3, Version = 1,
    Responsable = "operador", Finalizado = true, FinalizadoEn = DateTimeOffset.Now, Resultado = "Aprobado",
    Observaciones = "Se revisó una muestra de 25 piezas. No se observaron daños ni desviaciones.",
    Respuestas = [
        new() { Criterio = new() { Nombre = "Integridad del empaque", Tipo = TipoCriterio.SiNo }, Valor = "Cumple" },
        new() { Criterio = new() { Nombre = "Diámetro exterior", Tipo = TipoCriterio.Numero, Minimo = 19.5m, Maximo = 20.5m, Unidad = "mm" }, Valor = "20.1" },
        new() { Criterio = new() { Nombre = "Comentarios de la muestra", Tipo = TipoCriterio.Texto, Obligatorio = false }, Valor = "Superficie uniforme, sin corrosión." }
    ]
};
datos.Controles.Add(control);
var corto = ReporteControlPdf.Crear(datos, control.Id, DateTimeOffset.Now);
File.WriteAllBytes(Path.Combine(carpeta, "reporte-corto.pdf"), corto);
using (var pdf = PdfReader.Open(new MemoryStream(corto), PdfDocumentOpenMode.Import))
    Console.WriteLine($"Reporte corto: {pdf.PageCount} páginas");
var originalId = control.Id;
var revision = new ControlCalidad { RevisionDeId = originalId, LoteId = lote.Id, Finalizado = true, Resultado = "Rechazado" };
datos.Controles.Add(revision);
control.Observaciones += " " + string.Join(" ", Enumerable.Repeat("Descripción extensa con acentos: evaluación, inspección y trazabilidad.", 65));
for (int i = 0; i < 35; i++)
    control.Respuestas.Add(new RespuestaCalidad { Criterio = new CriterioCalidad { Nombre = $"Criterio adicional {i+1} para comprobar la continuidad entre páginas", Tipo = TipoCriterio.Texto }, Valor = i == 0 ? new string('A', 250) : "Se verificaron los requisitos establecidos en el procedimiento de inspección." });
var largo = ReporteControlPdf.Crear(datos, control.Id, DateTimeOffset.Now);
File.WriteAllBytes(Path.Combine(carpeta, "reporte-largo.pdf"), largo);
using (var pdf = PdfReader.Open(new MemoryStream(largo), PdfDocumentOpenMode.Import))
{
    if (pdf.PageCount < 3) throw new Exception("No se probó paginación.");
    Console.WriteLine($"Reporte largo: {pdf.PageCount} páginas");
}
control.Finalizado = false;
try { ReporteControlPdf.Crear(datos, control.Id, DateTimeOffset.Now); throw new Exception("Aceptó borrador"); }
catch (InvalidOperationException) { Console.WriteLine("OK: rechaza borrador"); }
control.Finalizado = true; datos.Lotes.Clear();
try { ReporteControlPdf.Crear(datos, control.Id, DateTimeOffset.Now); throw new Exception("Aceptó lote ausente"); }
catch (InvalidOperationException) { Console.WriteLine("OK: rechaza lote ausente"); }
Console.WriteLine("OK: reportes generados para revisión visual.");
    }
}
