using System.Text.Json;
using System.Text.Json.Serialization;
using ControlCalidadQR.Calidad;

namespace ServidorCalidad;

public static class ImportacionCalidad
{
    // Solo se ejecuta desde la consola del servidor y sobre una base sin trabajo registrado.
    public static async Task ImportarAsync(BaseCalidad baseDatos, string archivo)
    {
        var opciones = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
        var origen = JsonSerializer.Deserialize<DatosCalidad>(await File.ReadAllTextAsync(archivo), opciones)
            ?? throw new InvalidOperationException("El archivo está vacío.");
        void Exigir(bool condicion, string mensaje) { if (!condicion) throw new InvalidOperationException(mensaje); }
        Exigir(origen.Esquema == 1, "Versión de archivo no compatible.");
        void RevisarIds(IEnumerable<string> valores)
        {
            var ids = valores.ToList();
            Exigir(ids.All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 64) && ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == ids.Count,
                "Hay identificadores inválidos o duplicados.");
        }
        RevisarIds(origen.Lotes.Select(x => x.Id));
        RevisarIds(origen.Plantillas.Select(x => x.Id));
        RevisarIds(origen.Controles.Select(x => x.Id));
        // Se comprueban las reglas de lotes y plantillas sin modificar el archivo original.
        var validacion = new DatosCalidad();
        var repo = new RepositorioCalidad(() => Task.FromResult(validacion), _ => Task.CompletedTask);
        foreach (var lote in origen.Lotes)
        {
            Exigir(lote.Version > 0, "Versión de lote inválida.");
            var copia = RepositorioCalidad.Copia(lote); copia.Version = 0;
            await repo.GuardarLoteAsync(copia, "Validación de importación");
        }
        foreach (var plantilla in origen.Plantillas)
        {
            Exigir(plantilla.Version > 0, "Versión de plantilla inválida.");
            var copia = RepositorioCalidad.Copia(plantilla); copia.Version = 0;
            await repo.GuardarPlantillaAsync(copia, "Validación de importación");
        }
        foreach (var control in origen.Controles)
        {
            Exigir(control.Version > 0 && origen.Lotes.Any(x => x.Id == control.LoteId) &&
                origen.Plantillas.Any(x => x.Id == control.PlantillaId), "Un control no tiene lote o plantilla válidos.");
            Exigir(control.Respuestas.Count > 0 && !string.IsNullOrWhiteSpace(control.Responsable), "Control incompleto.");
            Exigir(RepositorioCalidad.Evaluar(control.Respuestas, control.Finalizado) == control.Resultado, "Resultado de control inconsistente.");
            if (control.RevisionDeId is not null)
            {
                Exigir(control.RevisionDeId != control.Id && origen.Controles.Any(x => x.Id == control.RevisionDeId && x.Finalizado && x.LoteId == control.LoteId),
                    "Revisión sin original válido.");
                Exigir(origen.Controles.Count(x => x.RevisionDeId == control.RevisionDeId) == 1, "Hay revisiones duplicadas.");
            }
        }
        // Ordenar las revisiones permite respetar la clave foránea al insertarlas.
        var pendientes = origen.Controles.ToList();
        var ordenados = new List<ControlCalidad>();
        while (pendientes.Count > 0)
        {
            var siguientes = pendientes.Where(x => x.RevisionDeId is null || ordenados.Any(a => a.Id == x.RevisionDeId)).ToList();
            Exigir(siguientes.Count > 0, "Las revisiones contienen un ciclo.");
            foreach (var control in siguientes) { ordenados.Add(control); pendientes.Remove(control); }
        }
        await baseDatos.CambiarAsync(async destino =>
        {
            var datos = await destino.LeerAsync();
            Exigir(datos.Lotes.Count == 0 && datos.Controles.Count == 0 &&
                datos.Plantillas.All(x => x.Id == "plantilla-general" && x.Version == 1), "Importa antes de empezar a registrar información central.");
            datos.Lotes.AddRange(origen.Lotes);
            // Si no está en el archivo, se conserva la plantilla inicial del servidor.
            foreach (var plantilla in origen.Plantillas)
            {
                datos.Plantillas.RemoveAll(x => x.Id == plantilla.Id);
                datos.Plantillas.Add(plantilla);
            }
            datos.Controles.AddRange(ordenados);
            foreach (var cambio in origen.Cambios)
            {
                cambio.Usuario = "[Registro local] " + cambio.Usuario;
                datos.Cambios.Add(cambio);
            }
            datos.Cambios.Add(new CambioCalidad { Usuario = "Administrador del servidor", Entidad = "Importación",
                EntidadId = Guid.NewGuid().ToString("N"), Accion = "Importar archivo local",
                Despues = JsonSerializer.Serialize(new { lotes = origen.Lotes.Count, controles = origen.Controles.Count }) });
        });
    }
}
