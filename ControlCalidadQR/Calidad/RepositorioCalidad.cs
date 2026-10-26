using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControlCalidadQR.Calidad;

// Reglas compartidas por la aplicación y el servidor; el almacenamiento se puede sustituir.
public class RepositorioCalidad(string ruta)
{
    private readonly Func<Task<DatosCalidad>>? leer;
    private readonly Func<DatosCalidad, Task>? guardar;
    // El servidor proporciona almacenamiento dentro de su transacción SQL.
    public RepositorioCalidad(Func<Task<DatosCalidad>> leer, Func<DatosCalidad, Task> guardar) : this("")
    {
        this.leer = leer;
        this.guardar = guardar;
    }
    private static readonly SemaphoreSlim Exclusivo = new(1, 1);
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }
    };
    public static T Copia<T>(T valor) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(valor, Json), Json)!;
    public static bool LeerNumero(string? texto, out decimal numero) => decimal.TryParse(texto,
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite |
        NumberStyles.AllowTrailingWhite, CultureInfo.CurrentCulture, out numero);
    public static string TipoTexto(TipoCriterio tipo) => tipo switch
    {
        TipoCriterio.SiNo => "Cumple / No cumple", TipoCriterio.Numero => "Número", _ => "Texto"
    };
    public virtual async Task<DatosCalidad> LeerAsync()
    {
        await Exclusivo.WaitAsync();
        try { return await LeerInternoAsync(); }
        finally { Exclusivo.Release(); }
    }
    private async Task<DatosCalidad> LeerInternoAsync()
    {
        if (leer is not null) return await leer();
        if (!File.Exists(ruta))
            return new DatosCalidad
            {
                Plantillas = [new PlantillaCalidad
                {
                    Id = "plantilla-general", Version = 1, Nombre = "Inspección general",
                    Descripcion = "Ejemplo editable. Adapta los criterios antes de utilizarlo.",
                    Criterios = [new() { Id = "general-cumple", Nombre = "El producto cumple los requisitos definidos", Tipo = TipoCriterio.SiNo },
                        new() { Id = "general-detalle", Nombre = "Detalle de la inspección", Tipo = TipoCriterio.Texto, Obligatorio = false }]
                }]
            };
        try
        {
            var datos = JsonSerializer.Deserialize<DatosCalidad>(await File.ReadAllTextAsync(ruta), Json)
                ?? throw new JsonException("Archivo vacío.");
            if (datos.Esquema != 1) throw new InvalidDataException("La versión del archivo de calidad no es compatible.");
            return datos;
        }
        catch (JsonException ex)
        {
            // Un archivo ilegible nunca se sustituye silenciosamente por datos vacíos.
            throw new InvalidDataException("No se pudo leer el archivo de calidad. Conserva el archivo y revisa su copia .bak antes de continuar.", ex);
        }
    }
    private async Task GuardarInternoAsync(DatosCalidad datos)
    {
        if (guardar is not null) { await guardar(datos); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ruta))!);
        string temporal = ruta + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporal, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, datos, Json);
                await stream.FlushAsync();
                stream.Flush(true);
            }
            // Reemplazo atómico y copia de la versión anterior en el mismo directorio.
            if (File.Exists(ruta)) File.Replace(temporal, ruta, ruta + ".bak", true);
            else File.Move(temporal, ruta);
        }
        finally { if (File.Exists(temporal)) File.Delete(temporal); }
    }
    private async Task CambiarAsync(Action<DatosCalidad> accion)
    {
        await Exclusivo.WaitAsync();
        try
        {
            var datos = await LeerInternoAsync();
            accion(datos);
            await GuardarInternoAsync(datos);
        }
        finally { Exclusivo.Release(); }
    }
    private static void Exigir(bool condicion, string mensaje)
    {
        if (!condicion) throw new InvalidOperationException(mensaje);
    }
    private static void Registrar(DatosCalidad datos, string usuario, string entidad, string id,
        string accion, object? antes, object despues)
    {
        Exigir(!string.IsNullOrWhiteSpace(usuario), "Indica el usuario responsable del cambio.");
        datos.Cambios.Add(new CambioCalidad
        {
            Usuario = usuario.Trim(), Entidad = entidad, EntidadId = id, Accion = accion,
            Antes = antes is null ? "" : JsonSerializer.Serialize(antes, Json),
            Despues = JsonSerializer.Serialize(despues, Json)
        });
    }
    public virtual Task GuardarLoteAsync(LoteCalidad lote, string usuario) => CambiarAsync(datos =>
    {
        var nuevo = Copia(lote);
        nuevo.Numero = nuevo.Numero.Trim(); nuevo.Producto = nuevo.Producto.Trim(); nuevo.Unidad = nuevo.Unidad.Trim();
        Exigir(nuevo.Numero.Length > 0 && nuevo.Producto.Length > 0 && nuevo.Unidad.Length > 0,
            "Completa número de lote, producto o proceso y unidad.");
        Exigir(nuevo.Cantidad > 0, "La cantidad debe ser mayor que cero.");
        var anterior = datos.Lotes.SingleOrDefault(x => x.Id == nuevo.Id);
        Exigir(nuevo.Version == (anterior?.Version ?? 0), "El lote cambió. Vuelve a abrirlo antes de guardar.");
        nuevo.Version++;
        Registrar(datos, usuario, "Lote", nuevo.Id, anterior is null ? "Crear lote" : "Actualizar lote", anterior, nuevo);
        if (anterior is not null) datos.Lotes.Remove(anterior);
        datos.Lotes.Add(nuevo);
    });
    public virtual Task GuardarPlantillaAsync(PlantillaCalidad plantilla, string usuario) => CambiarAsync(datos =>
    {
        var nueva = Copia(plantilla); nueva.Nombre = nueva.Nombre.Trim();
        Exigir(nueva.Nombre.Length > 0, "Escribe el nombre de la plantilla.");
        Exigir(nueva.Criterios.Count > 0, "Agrega al menos un criterio.");
        Exigir(nueva.Criterios.Select(x => x.Id).Distinct().Count() == nueva.Criterios.Count, "Los criterios deben ser diferentes.");
        foreach (var criterio in nueva.Criterios)
        {
            criterio.Nombre = criterio.Nombre.Trim();
            Exigir(criterio.Nombre.Length > 0, "Todos los criterios necesitan un nombre.");
            Exigir(Enum.IsDefined(criterio.Tipo), "Selecciona un tipo de respuesta válido.");
            Exigir(criterio.Minimo is null || criterio.Maximo is null || criterio.Minimo <= criterio.Maximo,
                $"El mínimo no puede superar al máximo: {criterio.Nombre}.");
            if (criterio.Tipo != TipoCriterio.Numero) { criterio.Minimo = null; criterio.Maximo = null; criterio.Unidad = ""; }
        }
        var anterior = datos.Plantillas.SingleOrDefault(x => x.Id == nueva.Id);
        Exigir(nueva.Version == (anterior?.Version ?? 0), "La plantilla cambió. Vuelve a abrirla.");
        nueva.Version++;
        Registrar(datos, usuario, "Plantilla", nueva.Id, anterior is null ? "Crear plantilla" : "Actualizar plantilla", anterior, nueva);
        if (anterior is not null) datos.Plantillas.Remove(anterior);
        datos.Plantillas.Add(nueva);
    });
    public static ControlCalidad CrearControl(LoteCalidad lote, PlantillaCalidad plantilla, string usuario)
    {
        Exigir(!lote.Archivado && !plantilla.Archivada, "Elige un lote y una plantilla activos.");
        return new ControlCalidad
        {
            LoteId = lote.Id, PlantillaId = plantilla.Id, PlantillaNombre = plantilla.Nombre,
            PlantillaDescripcion = plantilla.Descripcion, PlantillaVersion = plantilla.Version, Responsable = usuario,
            Respuestas = plantilla.Criterios.Select(x => new RespuestaCalidad { Criterio = Copia(x) }).ToList()
        };
    }
    public static ControlCalidad CrearRevision(ControlCalidad original, string usuario)
    {
        Exigir(original.Finalizado, "Solo se revisan controles finalizados.");
        var revision = Copia(original);
        revision.Id = Guid.NewGuid().ToString("N"); revision.Version = 0; revision.RevisionDeId = original.Id;
        revision.MotivoRevision = ""; revision.Responsable = usuario; revision.Fecha = DateTime.Today;
        revision.Finalizado = false; revision.FinalizadoEn = null; revision.Resultado = "Borrador";
        return revision;
    }
    public virtual Task GuardarControlAsync(ControlCalidad control, string usuario, bool finalizar) => CambiarAsync(datos =>
    {
        var nuevo = Copia(control);
        var anterior = datos.Controles.SingleOrDefault(x => x.Id == nuevo.Id);
        Exigir(anterior?.Finalizado != true, "El control finalizado no puede modificarse. Crea una revisión.");
        Exigir(nuevo.Version == (anterior?.Version ?? 0), "El control cambió. Vuelve a abrirlo.");
        var lote = datos.Lotes.SingleOrDefault(x => x.Id == nuevo.LoteId);
        Exigir(lote is not null && !lote.Archivado, "El lote no existe o está archivado. Reactívalo para guardar controles.");
        Exigir(!string.IsNullOrWhiteSpace(nuevo.Responsable), "Indica el responsable del control.");
        Exigir(nuevo.Respuestas.Count > 0, "El control necesita criterios de evaluación.");
        if (nuevo.RevisionDeId is not null)
        {
            var original = datos.Controles.SingleOrDefault(x => x.Id == nuevo.RevisionDeId);
            Exigir(original?.Finalizado == true && original.LoteId == nuevo.LoteId, "El control original de esta revisión no es válido.");
            Exigir(!datos.Controles.Any(x => x.RevisionDeId == nuevo.RevisionDeId && x.Id != nuevo.Id),
                "Ya existe una revisión de este control. Abre la revisión existente.");
            Exigir(!string.IsNullOrWhiteSpace(nuevo.MotivoRevision), "Explica el motivo de la revisión.");
        }
        // La instantánea se valida también al guardar, no solo en la interfaz.
        ControlCalidad referencia;
        if (anterior is not null) referencia = anterior;
        else if (nuevo.RevisionDeId is not null) referencia = datos.Controles.Single(x => x.Id == nuevo.RevisionDeId);
        else
        {
            var plantilla = datos.Plantillas.SingleOrDefault(x => x.Id == nuevo.PlantillaId);
            Exigir(plantilla is not null && !plantilla.Archivada, "La plantilla no existe o está archivada.");
            Exigir(plantilla!.Version == nuevo.PlantillaVersion, "La plantilla cambió. Inicia un nuevo control con su versión actual.");
            referencia = CrearControl(lote!, plantilla, usuario);
        }
        Exigir(nuevo.LoteId == referencia.LoteId && nuevo.PlantillaId == referencia.PlantillaId &&
            nuevo.PlantillaVersion == referencia.PlantillaVersion &&
            (anterior is null || nuevo.RevisionDeId == anterior.RevisionDeId) &&
            JsonSerializer.Serialize(nuevo.Respuestas.Select(x => x.Criterio), Json) ==
            JsonSerializer.Serialize(referencia.Respuestas.Select(x => x.Criterio), Json),
            "No se pueden cambiar el lote, la plantilla ni los criterios de un control existente.");
        nuevo.PlantillaNombre = referencia.PlantillaNombre; nuevo.PlantillaDescripcion = referencia.PlantillaDescripcion;
        nuevo.Resultado = Evaluar(nuevo.Respuestas, finalizar);
        nuevo.Finalizado = finalizar; nuevo.FinalizadoEn = finalizar ? DateTimeOffset.Now : null; nuevo.Version++;
        Registrar(datos, usuario, "Control", nuevo.Id, finalizar ? "Finalizar control" : "Guardar borrador", anterior, nuevo);
        if (anterior is not null) datos.Controles.Remove(anterior);
        datos.Controles.Add(nuevo);
    });
    public static string Evaluar(IEnumerable<RespuestaCalidad> respuestas, bool finalizar)
    {
        bool rechazo = false; int evaluables = 0, contestadas = 0;
        foreach (var respuesta in respuestas)
        {
            var criterio = respuesta.Criterio; string valor = respuesta.Valor.Trim();
            if (valor.Length == 0)
            {
                Exigir(!finalizar || !criterio.Obligatorio, $"Completa el criterio obligatorio: {criterio.Nombre}.");
                continue;
            }
            contestadas++;
            switch (criterio.Tipo)
            {
                case TipoCriterio.SiNo:
                    Exigir(valor is "Cumple" or "No cumple", $"Selecciona Cumple o No cumple en {criterio.Nombre}.");
                    evaluables++; rechazo |= valor == "No cumple"; break;
                case TipoCriterio.Numero:
                    // La interfaz convierte el formato regional al formato interno invariable.
                    Exigir(decimal.TryParse(valor, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var numero), $"Escribe un número válido en {criterio.Nombre}.");
                    if (criterio.Minimo is not null || criterio.Maximo is not null)
                    {
                        evaluables++;
                        rechazo |= (criterio.Minimo is not null && numero < criterio.Minimo) ||
                            (criterio.Maximo is not null && numero > criterio.Maximo);
                    }
                    break;
                case TipoCriterio.Texto: break;
                default: throw new InvalidOperationException("Tipo de criterio no válido.");
            }
        }
        Exigir(!finalizar || contestadas > 0, "Contesta al menos un criterio antes de finalizar.");
        return !finalizar ? "Borrador" : rechazo ? "Rechazado" : evaluables > 0 ? "Aprobado" : "Registrado";
    }
}

