namespace ControlCalidadQR.Calidad;

// El Id interno permanece igual aunque se corrijan los datos visibles del lote.
public sealed class LoteCalidad
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Version { get; set; }
    public string Numero { get; set; } = "";
    public string Producto { get; set; } = "";
    public DateTime Fecha { get; set; } = DateTime.Today;
    public decimal Cantidad { get; set; }
    public string Unidad { get; set; } = "unidades";
    public string Observaciones { get; set; } = "";
    public bool Archivado { get; set; }
    public override string ToString() => $"{Numero} · {Producto}";
}

public enum TipoCriterio { SiNo, Numero, Texto }
public sealed class CriterioCalidad
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Nombre { get; set; } = "";
    public TipoCriterio Tipo { get; set; }
    public bool Obligatorio { get; set; } = true;
    public string Unidad { get; set; } = "";
    public decimal? Minimo { get; set; }
    public decimal? Maximo { get; set; }
}
public sealed class PlantillaCalidad
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Version { get; set; }
    public string Nombre { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public bool Archivada { get; set; }
    public List<CriterioCalidad> Criterios { get; set; } = [];
    public override string ToString() => $"{Nombre} · v{Version}";
}
public sealed class RespuestaCalidad
{
    // Copia del criterio: editar la plantilla no altera controles anteriores.
    public CriterioCalidad Criterio { get; set; } = new();
    public string Valor { get; set; } = "";
}
public sealed class ControlCalidad
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Version { get; set; }
    public string LoteId { get; set; } = "";
    public string PlantillaId { get; set; } = "";
    public string PlantillaNombre { get; set; } = "";
    public string PlantillaDescripcion { get; set; } = "";
    public int PlantillaVersion { get; set; }
    public string Responsable { get; set; } = "";
    public DateTime Fecha { get; set; } = DateTime.Today;
    public string Observaciones { get; set; } = "";
    public bool Finalizado { get; set; }
    public string Resultado { get; set; } = "Borrador";
    public DateTimeOffset? FinalizadoEn { get; set; }
    public string? RevisionDeId { get; set; }
    public string MotivoRevision { get; set; } = "";
    public List<RespuestaCalidad> Respuestas { get; set; } = [];
}
public sealed class CambioCalidad
{
    public DateTimeOffset Fecha { get; set; } = DateTimeOffset.Now;
    public string Usuario { get; set; } = "";
    public string Entidad { get; set; } = "";
    public string EntidadId { get; set; } = "";
    public string Accion { get; set; } = "";
    public string Antes { get; set; } = "";
    public string Despues { get; set; } = "";
}
public sealed class DatosCalidad
{
    public int Esquema { get; set; } = 1;
    public List<LoteCalidad> Lotes { get; set; } = [];
    public List<PlantillaCalidad> Plantillas { get; set; } = [];
    public List<ControlCalidad> Controles { get; set; } = [];
    public List<CambioCalidad> Cambios { get; set; } = [];
}
