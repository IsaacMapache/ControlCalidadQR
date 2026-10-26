using ControlCalidadQR.Calidad;
using System.Globalization;
// Comprobaciones reproducibles sin alterar la base SQL de trabajo.
public static class PruebasDominio
{
    public static async Task EjecutarAsync()
    {
        await Task.CompletedTask;

string ruta = Path.Combine(Path.GetTempPath(), "PruebasControlCalidad", "datos-prueba-" + Guid.NewGuid().ToString("N"), "calidad.json");
var repo = new RepositorioCalidad(ruta);
int aprobadas = 0;
void Verificar(bool condicion, string nombre)
{
    if (!condicion) throw new Exception("FALLÓ: " + nombre);
    Console.WriteLine("OK: " + nombre); aprobadas++;
}
async Task Rechaza(Func<Task> accion, string nombre)
{
    try { await accion(); }
    catch (InvalidOperationException) { Verificar(true, nombre); return; }
    throw new Exception("FALLÓ: debía impedir " + nombre);
}
var inicial = await repo.LeerAsync();
Verificar(inicial.Plantillas.Count == 1 && inicial.Lotes.Count == 0, "Inicio sin datos ficticios y plantilla editable");
var lote = new LoteCalidad { Numero = "LOT-001", Producto = "Pieza", Cantidad = 10 };
await repo.GuardarLoteAsync(lote, "Ana");
var datos = await repo.LeerAsync(); lote = datos.Lotes.Single();
string id = lote.Id;
var obsoleto = RepositorioCalidad.Copia(lote);
lote.Numero = "LOT-CORREGIDO"; lote.Cantidad = 12;
await repo.GuardarLoteAsync(lote, "Luis");
datos = await repo.LeerAsync(); lote = datos.Lotes.Single();
Verificar(lote.Id == id && lote.Version == 2, "Corregir lote conserva identidad y versiona");
Verificar(datos.Cambios.Count == 2 && datos.Cambios.Last().Usuario == "Luis" && datos.Cambios.Last().Antes.Contains("LOT-001"), "Historial conserva usuario y datos anteriores");
await Rechaza(() => repo.GuardarLoteAsync(obsoleto, "Ana"), "Edición obsoleta no sobrescribe cambios recientes");
await Rechaza(() => repo.GuardarLoteAsync(new() { Numero = "X", Producto = "P", Cantidad = -1 }, "Ana"), "Cantidad negativa bloqueada");
var plantilla = new PlantillaCalidad
{
    Nombre = "Dimensiones", Criterios = [
        new() { Nombre = "Acabado", Tipo = TipoCriterio.SiNo },
        new() { Nombre = "Longitud", Tipo = TipoCriterio.Numero, Minimo = 10, Maximo = 20, Unidad = "mm" },
        new() { Nombre = "Notas", Tipo = TipoCriterio.Texto, Obligatorio = false }]
};
await repo.GuardarPlantillaAsync(plantilla, "Ana");
plantilla = (await repo.LeerAsync()).Plantillas.Single(x => x.Nombre == "Dimensiones");
var control = RepositorioCalidad.CrearControl(lote, plantilla, "Ana");
await repo.GuardarControlAsync(control, "Ana", false);
control = (await repo.LeerAsync()).Controles.Single();
Verificar(!control.Finalizado && control.Resultado == "Borrador", "Borrador permite respuestas incompletas");
await Rechaza(() => repo.GuardarControlAsync(control, "Ana", true), "Finalización exige respuestas obligatorias");
plantilla.Criterios[1].Maximo = 15;
await repo.GuardarPlantillaAsync(plantilla, "Ana");
control.Respuestas[0].Valor = "Cumple"; control.Respuestas[1].Valor = "20";
await repo.GuardarControlAsync(control, "Ana", true);
control = (await repo.LeerAsync()).Controles.Single();
Verificar(control.Finalizado && control.Resultado == "Aprobado" && control.Respuestas[1].Criterio.Maximo == 20, "Límite inclusivo y plantilla histórica intacta tras editar plantilla");
await Rechaza(() => repo.GuardarControlAsync(control, "Ana", false), "Control finalizado inmutable");
var revision = RepositorioCalidad.CrearRevision(control, "Luis");
await Rechaza(() => repo.GuardarControlAsync(revision, "Luis", false), "Revisión exige motivo");
revision.MotivoRevision = "Corregir medición"; revision.Respuestas[1].Valor = "21";
await repo.GuardarControlAsync(revision, "Luis", true);
datos = await repo.LeerAsync();
Verificar(datos.Controles.Count == 2 && datos.Controles.Single(x => x.Id == control.Id).Resultado == "Aprobado" &&
    datos.Controles.Single(x => x.RevisionDeId == control.Id).Resultado == "Rechazado", "Revisión conserva original y relaciona resultado corregido");
var duplicada = RepositorioCalidad.CrearRevision(control, "Ana"); duplicada.MotivoRevision = "Otra";
await Rechaza(() => repo.GuardarControlAsync(duplicada, "Ana", false), "Sin revisiones paralelas del mismo original");
plantilla = (await repo.LeerAsync()).Plantillas.Single(x => x.Nombre == "Dimensiones");
var nuevo = RepositorioCalidad.CrearControl(lote, plantilla, "Ana");
nuevo.Respuestas[0].Valor = "No cumple"; nuevo.Respuestas[1].Valor = "12";
await repo.GuardarControlAsync(nuevo, "Ana", true);
Verificar((await repo.LeerAsync()).Controles.Single(x => x.Id == nuevo.Id).Resultado == "Rechazado", "No cumple rechaza aunque el número esté dentro de límites");
var alterado = RepositorioCalidad.CrearControl(lote, plantilla, "Ana"); alterado.Respuestas[0].Criterio.Obligatorio = false;
await Rechaza(() => repo.GuardarControlAsync(alterado, "Ana", false), "Criterios manipulados no se guardan");
lote.Archivado = true; await repo.GuardarLoteAsync(lote, "Ana");
await Rechaza(() => repo.GuardarControlAsync(RepositorioCalidad.CrearControl(obsoleto, plantilla, "Ana"), "Ana", false), "Lote archivado bloquea controles");
lote = (await repo.LeerAsync()).Lotes.Single(); lote.Archivado = false; await repo.GuardarLoteAsync(lote, "Ana");
Verificar((await repo.LeerAsync()).Lotes.Single().Archivado == false, "Lote puede reactivarse sin perder registros");
var texto = new RespuestaCalidad { Criterio = new() { Nombre = "Texto", Tipo = TipoCriterio.Texto }, Valor = "Inspección documentada" };
Verificar(RepositorioCalidad.Evaluar([texto], true) == "Registrado", "Texto no aprueba automáticamente");
await Rechaza(() => Task.Run(() => RepositorioCalidad.Evaluar([new() { Criterio = new() { Nombre = "Opcional", Obligatorio = false } }], true)), "Control final sin respuestas bloqueado");
foreach (string cultura in new[] { "es-MX", "es-ES" })
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultura);
    string valor = 12.5m.ToString();
    Verificar(RepositorioCalidad.LeerNumero(valor, out var n) && n == 12.5m, "Número decimal según cultura " + cultura);
}
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var malo = RepositorioCalidad.Copia(plantilla); malo.Criterios[1].Minimo = 30; malo.Criterios[1].Maximo = 10;
await Rechaza(() => repo.GuardarPlantillaAsync(malo, "Ana"), "Rango invertido bloqueado");
var trasReinicio = await new RepositorioCalidad(ruta).LeerAsync();
Verificar(trasReinicio.Controles.Count == 3 && trasReinicio.Lotes.Single().Id == id, "Persistencia tras crear otra instancia");
Verificar(File.Exists(ruta + ".bak") && (await File.ReadAllTextAsync(ruta + ".bak")).Contains(id), "Copia de seguridad de la escritura anterior");
var lotesNuevos = Enumerable.Range(1, 5).Select(i => new LoteCalidad { Numero = "MISMO", Producto = "P" + i, Cantidad = 1 }).ToList();
await Task.WhenAll(lotesNuevos.Select(x => repo.GuardarLoteAsync(x, "Ana")));
datos = await repo.LeerAsync();
Verificar(datos.Lotes.Count == 6 && datos.Lotes.Select(x => x.Id).Distinct().Count() == 6, "Escrituras simultáneas sin pérdida y números visibles repetidos con Id único");
await File.WriteAllTextAsync(ruta, "archivo dañado");
try { await repo.LeerAsync(); throw new Exception("No detectó corrupción"); }
catch (InvalidDataException) { Verificar(true, "Detecta archivo ilegible"); }
try { await repo.GuardarLoteAsync(new() { Numero = "Z", Producto = "P", Cantidad = 1 }, "Ana"); throw new Exception("Sobrescribió corrupción"); }
catch (InvalidDataException) { Verificar(await File.ReadAllTextAsync(ruta) == "archivo dañado", "No sobrescribe datos ilegibles"); }
Console.WriteLine($"TOTAL: {aprobadas} comprobaciones correctas.");
    }
}
