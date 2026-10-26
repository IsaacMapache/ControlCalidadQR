using ControlCalidadQR.Calidad;
using Microsoft.Data.SqlClient;

namespace ServidorCalidad;

public sealed partial class AccesoCalidad
{
    public async Task<List<UsuarioCalidad>> ListarAsync()
    {
        await using var conexion = await datos.AbrirAsync();
        using var comando = BaseCalidad.Comando(conexion, null, "SELECT Nombre,Rol,Activo,Version FROM dbo.Usuarios ORDER BY Nombre");
        using var lector = await comando.ExecuteReaderAsync();
        var usuarios = new List<UsuarioCalidad>();
        while (await lector.ReadAsync()) usuarios.Add(new(lector.GetString(0), lector.GetString(1), lector.GetBoolean(2), lector.GetInt32(3)));
        return usuarios;
    }

    private static void ValidarClave(string? clave)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length is < 12 or > 200)
            throw new InvalidOperationException("La contraseña debe tener de 12 a 200 caracteres.");
    }

    private static async Task BloquearUsuariosAsync(SqlConnection conexion, SqlTransaction transaccion, string? autor)
    {
        using var bloqueo = BaseCalidad.Comando(conexion, transaccion, """
            DECLARE @resultado int;
            EXEC @resultado=sys.sp_getapplock @Resource='UsuariosCalidad', @LockMode='Exclusive',
                @LockOwner='Transaction', @LockTimeout=15000;
            IF @resultado<0 THROW 51000, 'No se pudo bloquear la administración de usuarios.', 1;
            """);
        await bloqueo.ExecuteNonQueryAsync();
        // Se vuelve a comprobar dentro de la transacción por si se revocó el permiso mientras esperaba.
        if (autor is not null)
        {
            using var permiso = BaseCalidad.Comando(conexion, transaccion,
                "SELECT COUNT(*) FROM dbo.Usuarios WHERE Nombre=@autor AND Activo=1 AND Rol='Administrador'", ("@autor", autor));
            if ((int)(await permiso.ExecuteScalarAsync())! != 1)
                throw new InvalidOperationException("Tu cuenta ya no tiene permiso para administrar usuarios.");
        }
    }

    private static async Task RegistrarUsuarioAsync(SqlConnection conexion, SqlTransaction transaccion,
        string? autor, string usuario, string accion, string detalle)
    {
        // Nunca se incluyen contraseñas, hashes ni tokens en el historial.
        using var comando = BaseCalidad.Comando(conexion, transaccion,
            "INSERT dbo.CambiosUsuarios(Autor,Usuario,Accion,Detalle) VALUES(@autor,@usuario,@accion,@detalle)",
            ("@autor", autor ?? "Consola del servidor"), ("@usuario", usuario), ("@accion", accion), ("@detalle", detalle));
        await comando.ExecuteNonQueryAsync();
    }

    public async Task CrearUsuarioAsync(string usuario, string clave, string rol = RolesCalidad.Inspector, string? autor = null)
    {
        usuario = usuario?.Trim() ?? "";
        if (usuario.Length is < 3 or > 100 || usuario.Any(char.IsControl))
            throw new InvalidOperationException("Usa un nombre de usuario de 3 a 100 caracteres.");
        ValidarClave(clave);
        if (!RolesCalidad.Valido(rol)) throw new InvalidOperationException("Selecciona un rol válido.");
        await using var conexion = await datos.AbrirAsync();
        using var transaccion = conexion.BeginTransaction();
        await BloquearUsuariosAsync(conexion, transaccion, autor);
        using var existe = BaseCalidad.Comando(conexion, transaccion,
            "SELECT COUNT(*) FROM dbo.Usuarios WHERE Nombre=@usuario", ("@usuario", usuario));
        if ((int)(await existe.ExecuteScalarAsync())! > 0) throw new InvalidOperationException("Ese nombre de usuario ya existe.");
        using var comando = BaseCalidad.Comando(conexion, transaccion,
            "INSERT dbo.Usuarios(Nombre,Clave,Rol,Activo,Version) VALUES(@usuario,@clave,@rol,1,1)",
            ("@usuario", usuario), ("@clave", Claves.HashPassword(usuario, clave)), ("@rol", rol));
        await comando.ExecuteNonQueryAsync();
        await RegistrarUsuarioAsync(conexion, transaccion, autor, usuario, "Crear usuario", $"Rol: {rol}");
        await transaccion.CommitAsync();
    }

    public async Task CambiarUsuarioAsync(CambioUsuario cambio, string? autor)
    {
        if (!RolesCalidad.Valido(cambio.Rol)) throw new InvalidOperationException("Selecciona un rol válido.");
        if (!string.IsNullOrEmpty(cambio.Clave)) ValidarClave(cambio.Clave);
        await using var conexion = await datos.AbrirAsync();
        using var transaccion = conexion.BeginTransaction();
        await BloquearUsuariosAsync(conexion, transaccion, autor);
        UsuarioCalidad actual;
        using (var buscar = BaseCalidad.Comando(conexion, transaccion,
            "SELECT Nombre,Rol,Activo,Version FROM dbo.Usuarios WHERE Nombre=@usuario", ("@usuario", cambio.Nombre)))
        using (var lector = await buscar.ExecuteReaderAsync())
        {
            if (!await lector.ReadAsync()) throw new InvalidOperationException("El usuario no existe.");
            actual = new(lector.GetString(0), lector.GetString(1), lector.GetBoolean(2), lector.GetInt32(3));
        }
        if (actual.Version != cambio.Version) throw new InvalidOperationException("El usuario cambió. Vuelve a abrirlo antes de guardar.");
        using var contar = BaseCalidad.Comando(conexion, transaccion, "SELECT COUNT(*) FROM dbo.Usuarios WHERE Activo=1 AND Rol='Administrador'");
        RolesCalidad.ValidarCambio(actual, cambio.Rol, cambio.Activo, (int)(await contar.ExecuteScalarAsync())!);
        var nuevaClave = string.IsNullOrEmpty(cambio.Clave) ? null : Claves.HashPassword(actual.Nombre, cambio.Clave);
        using var guardar = BaseCalidad.Comando(conexion, transaccion, """
            UPDATE dbo.Usuarios SET Rol=@rol,Activo=@activo,Version=Version+1,
                Clave=COALESCE(@clave,Clave) WHERE Nombre=@usuario;
            DELETE dbo.Sesiones WHERE Usuario=@usuario;
            """, ("@rol", cambio.Rol), ("@activo", cambio.Activo), ("@clave", nuevaClave), ("@usuario", actual.Nombre));
        await guardar.ExecuteNonQueryAsync();
        await RegistrarUsuarioAsync(conexion, transaccion, autor, actual.Nombre, "Actualizar usuario",
            $"Rol: {actual.Rol} → {cambio.Rol}; activo: {actual.Activo} → {cambio.Activo}; contraseña restablecida: {nuevaClave is not null}");
        await transaccion.CommitAsync();
    }

    // Recuperación local: solo quien tiene acceso al servidor y a SQL puede ejecutarla.
    public async Task HacerAdministradorAsync(string nombre)
    {
        var usuario = (await ListarAsync()).SingleOrDefault(x => string.Equals(x.Nombre, nombre, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No existe ese usuario. Créalo antes con --crear-usuario.");
        await CambiarUsuarioAsync(new(usuario.Nombre, RolesCalidad.Administrador, true, usuario.Version, null), null);
    }
}

