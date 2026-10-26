using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlCalidadQR.Calidad;
using Microsoft.Data.SqlClient;

namespace ServidorCalidad;

// Cada entidad tiene su clave y relaciones SQL; los criterios variables se conservan como JSON.
public sealed class BaseCalidad(IConfiguration configuracion)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    private string Conexion => configuracion.GetConnectionString("Calidad")
        ?? throw new InvalidOperationException("Configura ConnectionStrings__Calidad en el servidor.");

    public async Task<SqlConnection> AbrirAsync()
    {
        var conexion = new SqlConnection(Conexion);
        try { await conexion.OpenAsync(); return conexion; }
        catch { await conexion.DisposeAsync(); throw; }
    }

    public static SqlCommand Comando(SqlConnection conexion, SqlTransaction? transaccion, string sql,
        params (string Nombre, object? Valor)[] valores)
    {
        var comando = new SqlCommand(sql, conexion, transaccion);
        foreach (var (nombre, valor) in valores) comando.Parameters.AddWithValue(nombre, valor ?? DBNull.Value);
        return comando;
    }

    // La creación es explícita: el servicio normal no necesita permisos para crear bases.
    public async Task InicializarAsync()
    {
        var cadena = new SqlConnectionStringBuilder(Conexion);
        var nombre = cadena.InitialCatalog;
        if (string.IsNullOrWhiteSpace(nombre)) throw new InvalidOperationException("Indica Database en la conexión.");
        cadena.InitialCatalog = "master";
        await using (var maestra = new SqlConnection(cadena.ConnectionString))
        {
            await maestra.OpenAsync();
            using var crear = Comando(maestra, null,
                "IF DB_ID(@nombre) IS NULL BEGIN DECLARE @sql nvarchar(max)=N'CREATE DATABASE '+QUOTENAME(@nombre); EXEC(@sql); END;", ("@nombre", nombre));
            await crear.ExecuteNonQueryAsync();
        }
        await using var conexion = await AbrirAsync();
        using var transaccionEsquema = conexion.BeginTransaction();
        using var esquema = Comando(conexion, transaccionEsquema, """
            IF OBJECT_ID('dbo.Lotes') IS NULL BEGIN
              CREATE TABLE dbo.Lotes(Id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY, Version int NOT NULL,
                Datos nvarchar(max) NOT NULL CHECK(ISJSON(Datos)=1));
              CREATE TABLE dbo.Plantillas(Id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY, Version int NOT NULL,
                Datos nvarchar(max) NOT NULL CHECK(ISJSON(Datos)=1));
              CREATE TABLE dbo.Controles(Id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY, Version int NOT NULL,
                LoteId nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL REFERENCES dbo.Lotes(Id),
                PlantillaId nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL REFERENCES dbo.Plantillas(Id),
                RevisionDeId nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL REFERENCES dbo.Controles(Id),
                Datos nvarchar(max) NOT NULL CHECK(ISJSON(Datos)=1));
              CREATE UNIQUE INDEX IX_RevisionUnica ON dbo.Controles(RevisionDeId) WHERE RevisionDeId IS NOT NULL;
              CREATE INDEX IX_ControlesLote ON dbo.Controles(LoteId);
              CREATE TABLE dbo.Cambios(Id bigint IDENTITY PRIMARY KEY,
                Datos nvarchar(max) NOT NULL CHECK(ISJSON(Datos)=1));
            END;
            IF OBJECT_ID('dbo.Usuarios') IS NULL BEGIN
              CREATE TABLE dbo.Usuarios(Nombre nvarchar(100) NOT NULL PRIMARY KEY,
                Clave nvarchar(1000) NOT NULL);
              CREATE TABLE dbo.Sesiones(Huella char(64) NOT NULL PRIMARY KEY,
                Usuario nvarchar(100) NOT NULL REFERENCES dbo.Usuarios(Nombre),
                Vence datetimeoffset NOT NULL);
            END;
            """);
        await esquema.ExecuteNonQueryAsync();
        // La migración conserva cuentas, contraseñas y datos existentes.
        using var roles = Comando(conexion, transaccionEsquema, """
            IF COL_LENGTH('dbo.Usuarios','Rol') IS NULL
              ALTER TABLE dbo.Usuarios ADD Rol nvarchar(20) NOT NULL CONSTRAINT DF_Usuarios_Rol DEFAULT 'Inspector'
                CONSTRAINT CK_Usuarios_Rol CHECK (Rol IN ('Administrador','Inspector','Consulta'));
            IF COL_LENGTH('dbo.Usuarios','Activo') IS NULL
              ALTER TABLE dbo.Usuarios ADD Activo bit NOT NULL CONSTRAINT DF_Usuarios_Activo DEFAULT 1;
            IF COL_LENGTH('dbo.Usuarios','Version') IS NULL
              ALTER TABLE dbo.Usuarios ADD Version int NOT NULL CONSTRAINT DF_Usuarios_Version DEFAULT 1;
            IF OBJECT_ID('dbo.CambiosUsuarios') IS NULL
              CREATE TABLE dbo.CambiosUsuarios(Id bigint IDENTITY PRIMARY KEY,
                Fecha datetimeoffset NOT NULL DEFAULT SYSDATETIMEOFFSET(), Autor nvarchar(100) NOT NULL,
                Usuario nvarchar(100) NOT NULL, Accion nvarchar(100) NOT NULL, Detalle nvarchar(500) NOT NULL);
            """);
        await roles.ExecuteNonQueryAsync();
        await transaccionEsquema.CommitAsync();
        await CambiarAsync(async repo =>
        {
            var datos = await repo.LeerAsync();
            if (datos.Plantillas.Count == 0)
            {
                await repo.GuardarPlantillaAsync(new PlantillaCalidad
                {
                    Id = "plantilla-general", Nombre = "Inspección general",
                    Descripcion = "Ejemplo editable. Adapta los criterios antes de utilizarlo.",
                    Criterios = [new() { Id = "general-cumple", Nombre = "El producto cumple los requisitos definidos", Tipo = TipoCriterio.SiNo },
                        new() { Id = "general-detalle", Nombre = "Detalle de la inspección", Tipo = TipoCriterio.Texto, Obligatorio = false }]
                }, "Configuración inicial");
            }
        });
    }

    private static async Task<DatosCalidad> LeerAsync(SqlConnection conexion, SqlTransaction transaccion)
    {
        var datos = new DatosCalidad();
        // Los nombres de tabla son constantes internas, nunca entradas del usuario.
        async Task<List<T>> LeerTabla<T>(string tabla)
        {
            using var comando = Comando(conexion, transaccion, "SELECT Datos FROM dbo." + tabla + (tabla == "Cambios" ? " ORDER BY Id" : ""));
            using var lector = await comando.ExecuteReaderAsync();
            var filas = new List<T>();
            while (await lector.ReadAsync()) filas.Add(JsonSerializer.Deserialize<T>(lector.GetString(0), Json)!);
            return filas;
        }
        datos.Lotes = await LeerTabla<LoteCalidad>("Lotes");
        datos.Plantillas = await LeerTabla<PlantillaCalidad>("Plantillas");
        datos.Controles = await LeerTabla<ControlCalidad>("Controles");
        datos.Cambios = await LeerTabla<CambioCalidad>("Cambios");
        return datos;
    }

    private static async Task BloquearAsync(SqlConnection conexion, SqlTransaction transaccion, bool escritura)
    {
        // El bloqueo pertenece a la transacción y también coordina varias instancias de la API.
        using var comando = Comando(conexion, transaccion, """
            DECLARE @resultado int;
            EXEC @resultado=sys.sp_getapplock @Resource='Calidad', @LockMode=@modo,
                @LockOwner='Transaction', @LockTimeout=15000;
            IF @resultado < 0 THROW 51000, 'No se pudo obtener acceso a los datos. Inténtalo de nuevo.', 1;
            """, ("@modo", escritura ? "Exclusive" : "Shared"));
        await comando.ExecuteNonQueryAsync();
    }

    public async Task<DatosCalidad> LeerAsync()
    {
        await using var conexion = await AbrirAsync();
        using var transaccion = conexion.BeginTransaction();
        await BloquearAsync(conexion, transaccion, false);
        var datos = await LeerAsync(conexion, transaccion);
        await transaccion.CommitAsync();
        return datos;
    }

    public async Task CambiarAsync(Func<RepositorioCalidad, Task> accion)
    {
        await using var conexion = await AbrirAsync();
        using var transaccion = conexion.BeginTransaction();
        await BloquearAsync(conexion, transaccion, true);
        var datos = await LeerAsync(conexion, transaccion);
        var antes = RepositorioCalidad.Copia(datos);
        var repo = new RepositorioCalidad(() => Task.FromResult(datos), _ => Task.CompletedTask);
        await accion(repo);
        // Guardar y registrar el historial forman una sola operación atómica.
        foreach (var lote in datos.Lotes.Where(x => !antes.Lotes.Any(a => a.Id == x.Id && JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(x, Json))))
            await GuardarEntidadAsync(conexion, transaccion, "Lotes", lote.Id, lote.Version, lote);
        foreach (var plantilla in datos.Plantillas.Where(x => !antes.Plantillas.Any(a => a.Id == x.Id && JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(x, Json))))
            await GuardarEntidadAsync(conexion, transaccion, "Plantillas", plantilla.Id, plantilla.Version, plantilla);
        foreach (var control in datos.Controles.Where(x => !antes.Controles.Any(a => a.Id == x.Id && JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(x, Json))))
        {
            using var comando = Comando(conexion, transaccion, """
                UPDATE dbo.Controles SET Version=@version, Datos=@datos WHERE Id=@id;
                IF @@ROWCOUNT=0 INSERT dbo.Controles(Id,Version,LoteId,PlantillaId,RevisionDeId,Datos)
                  VALUES(@id,@version,@lote,@plantilla,@revision,@datos);
                """, ("@id", control.Id), ("@version", control.Version), ("@lote", control.LoteId),
                ("@plantilla", control.PlantillaId), ("@revision", control.RevisionDeId),
                ("@datos", JsonSerializer.Serialize(control, Json)));
            await comando.ExecuteNonQueryAsync();
        }
        foreach (var cambio in datos.Cambios.Skip(antes.Cambios.Count))
        {
            using var comando = Comando(conexion, transaccion, "INSERT dbo.Cambios(Datos) VALUES(@datos)",
                ("@datos", JsonSerializer.Serialize(cambio, Json)));
            await comando.ExecuteNonQueryAsync();
        }
        await transaccion.CommitAsync();
    }

    private static async Task GuardarEntidadAsync(SqlConnection conexion, SqlTransaction transaccion,
        string tabla, string id, int version, object entidad)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) throw new InvalidOperationException("Identificador no válido.");
        using var comando = Comando(conexion, transaccion,
            $"UPDATE dbo.{tabla} SET Version=@version, Datos=@datos WHERE Id=@id; " +
            $"IF @@ROWCOUNT=0 INSERT dbo.{tabla}(Id,Version,Datos) VALUES(@id,@version,@datos);",
            ("@id", id), ("@version", version), ("@datos", JsonSerializer.Serialize(entidad, Json)));
        await comando.ExecuteNonQueryAsync();
    }
}



