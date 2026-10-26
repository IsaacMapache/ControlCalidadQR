# Base central de control de calidad

La solución contiene la aplicación original ControlCalidadQR y el componente ServidorCalidad. Los datos viajan desde la aplicación a la API en .NET y después a SQL Server. La app no contiene credenciales de SQL Server. Ambos servicios deben estar encendidos para trabajar.

## Preparar SQL Server

Abre SQL Server Management Studio y verifica que puedes conectarte. En una terminal dentro de ServidorCalidad ejecuta lo siguiente, sustituyendo localhost por tu instancia:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__Calidad = "Server=localhost;Database=ControlCalidad;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
dotnet run -- --inicializar
dotnet run -- --crear-usuario operador
```

El alta solicita una contraseña de al menos 12 caracteres sin mostrarla. No hay cuentas ni claves predeterminadas. Repite el alta con nombres diferentes para otros operadores. La cuenta de Windows usada para inicializar debe tener permisos para crear la base y tablas. Repetir un nombre falla sin reemplazar su contraseña.

TrustServerCertificate=True solo se utiliza en la prueba local. En un servidor compartido utiliza certificado válido y TrustServerCertificate=False. Configura la conexión de producción mediante ConnectionStrings__Calidad, sin guardar contraseñas en el código.

## Conectar la app

1. Inicia la API con `dotnet run` dentro de ServidorCalidad.
2. Pulsa CONFIGURAR SERVIDOR en el inicio de sesión.
3. Para el emulador Android del mismo equipo usa `http://10.0.2.2:5080`.
4. Para Windows en el mismo equipo usa `http://localhost:5080`.
5. Inicia sesión con la cuenta creada en el servidor.

HTTP solo se permite en Debug hacia localhost o el host del emulador. Un celular físico necesita una dirección HTTPS accesible y un certificado confiable. No desactives la validación de certificados. Para varios celulares publica la API con HTTPS y configura la misma dirección en todos.

Visual Studio permite iniciar ambos proyectos desde las propiedades de la solución, seleccionando varios proyectos de inicio. ControlCalidadQR sigue siendo la app original; ServidorCalidad atiende las peticiones.

## Datos anteriores

No desinstales la app para migrar: podrías perder su almacenamiento privado. Pulsa EXPORTAR DATOS ANTERIORES y guarda calidad-anterior.json en el equipo del servidor. Conserva una copia.

Antes de registrar lotes en la base central:

```powershell
dotnet run -- --importar "C:\ruta\calidad-anterior.json"
```

La importación comprueba identificadores, relaciones, resultados y orden de revisiones. Solo se permite en una base sin lotes ni controles y con la plantilla inicial sin editar. Conserva los identificadores para la futura relación con los QR. El historial antiguo se marca como Registro local porque sus usuarios no estaban autenticados. El archivo original no se elimina. El comando requiere acceso al equipo y permisos SQL; no hay una ruta pública de importación.

## Tablas y reglas

- Lotes: clave estable, versión y datos del lote.
- Plantillas: clave, versión y definición configurable.
- Controles: clave, versión y relaciones con lote, plantilla y revisión original.
- Cambios: historial de valores anteriores y nuevos.
- Usuarios: nombres y contraseñas transformadas con PasswordHasher de ASP.NET Core Identity.
- Sesiones: huellas de tokens aleatorios con vencimiento de ocho horas.

Las definiciones variables y las instantáneas se guardan como JSON validado dentro de SQL Server. Las relaciones tienen claves foráneas SQL. Ejemplo de consulta:

```sql
SELECT Id, Version, JSON_VALUE(Datos, '$.numero') AS Numero,
       JSON_VALUE(Datos, '$.producto') AS Producto
FROM dbo.Lotes;
```

El servidor valida cada operación, comprueba la versión y guarda el cambio y su historial en una transacción. Una edición obsoleta se rechaza para que el operador vuelva a abrir el registro. Un control finalizado solo se corrige mediante una revisión. La identidad auditada procede de la sesión autenticada.

## Rutas de la API

| Método | Ruta | Uso |
| --- | --- | --- |
| POST | /api/sesion | Recibe usuario y clave; devuelve token, usuario y vencimiento |
| DELETE | /api/sesion | Revoca la sesión |
| GET | /api/datos | Consulta los datos centrales |
| POST | /api/lotes | Crea o actualiza un lote |
| POST | /api/plantillas | Crea o actualiza una plantilla |
| POST | /api/controles | Recibe control y finalizar |

Excepto el inicio, todas requieren Authorization: Bearer seguido del token. Los errores incluyen mensaje: 401 sesión inválida, 409 regla o versión incompatible, 429 demasiados intentos, 503 SQL Server no disponible.

## Alcance actual

Los roles Administrador, Inspector y Consulta controlan las acciones permitidas. La guía GUIA_USUARIOS.md en la raíz explica la actualización y la administración. Las cuentas comparten la misma información; todavía no hay separación por departamento o empresa. Las consultas descargan el conjunto completo y las escrituras usan un bloqueo SQL de corta duración; grandes volúmenes requerirán paginación y operaciones por entidad. No hay modo sin conexión ni sincronización automática. Ante un tiempo de espera, revisa el historial antes de repetir un guardado.

Para un servidor compartido configura copias de seguridad SQL, una cuenta con permisos mínimos y HTTPS. No publiques directamente SQL Server para los celulares.

## Comprobación de instalación

Inicia sesión en dos clientes, crea un lote en uno y consulta el mismo lote en el otro. Edita el mismo registro desde ambos y comprueba que la segunda edición obsoleta se rechaza. Finaliza un control, reinicia la API y comprueba que permanece guardado. Cierra sesión y verifica que el token revocado ya no permite consultar. Estas pruebas requieren una instancia SQL accesible; compilar no confirma por sí solo la conexión.

