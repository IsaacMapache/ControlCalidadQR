# Usuarios y permisos

## Actualizar una instalación existente

Detén la API con Ctrl+C en su terminal y cierra la app. Abre una terminal en ServidorCalidad y ejecuta:

    dotnet run -- --inicializar
    dotnet run -- --administrador operador
    dotnet run

Sustituye operador si tu cuenta tiene otro nombre. La actualización agrega Rol, Activo y Version a Usuarios y crea CambiosUsuarios. Conserva contraseñas, lotes, controles e historial. Las cuentas existentes empiezan como Inspector; el segundo comando concede explícitamente la administración a la cuenta elegida y revoca sus sesiones previas.

Vuelve a compilar e iniciar la aplicación. Entra con la misma contraseña. El menú mostrará el rol y el apartado Usuarios y permisos.

## Alcance por rol

| Acción | Administrador | Inspector | Consulta |
| --- | --- | --- | --- |
| Consultar lotes, plantillas, controles e historial | Sí | Sí | Sí |
| Leer, guardar y compartir QR | Sí | Sí | Sí |
| Crear o editar lotes y archivar/reactivar | Sí | Sí | No |
| Registrar, finalizar o revisar controles | Sí | Sí | No |
| Crear o modificar plantillas y criterios | Sí | No | No |
| Crear usuarios, asignar roles y desactivar cuentas | Sí | No | No |
| Restablecer contraseñas | Sí | No | No |

Un control finalizado continúa siendo inmutable; un inspector o administrador puede crear una revisión.

## Administración desde la aplicación

En Usuarios y permisos se pueden crear cuentas, elegir su rol, desactivarlas o restablecer su contraseña. Al editar, deja la contraseña vacía para conservarla. Una contraseña nueva debe tener de 12 a 200 caracteres.

Los cambios cierran las sesiones de la cuenta afectada, incluida la propia si el administrador se modifica. Una cuenta desactivada no puede iniciar sesión y conserva todos los registros que creó. No se puede quitar ni desactivar al último administrador activo. Dos administradores que editan la misma cuenta no sobrescriben cambios silenciosamente: se comprueba su versión.

La API verifica los permisos además de ocultar botones y mostrar formularios de consulta. Los tokens no contienen roles elegidos por el cliente: cada petición recupera el rol y estado actuales de SQL Server.

Los cambios administrativos quedan en CambiosUsuarios, con fecha, autor y acción; no contiene contraseñas ni tokens.

## Administración local y recuperación

Desde la terminal del servidor también se puede crear una cuenta:

    dotnet run -- --crear-usuario nuevoInspector --rol Inspector

El comando pide la contraseña sin mostrarla. Los roles válidos son Administrador, Inspector y Consulta. Si se omite el rol, se usa Inspector.

Si se pierde acceso administrativo, quien tenga acceso al equipo y permisos SQL puede rehabilitar una cuenta existente con --administrador. No es una ruta de la API y no se ofrece a los usuarios de consulta.

## Comprobación después de actualizar

1. Inicia sesión como administrador y crea una cuenta Inspector y otra Consulta.
2. Comprueba que el inspector puede crear controles pero no modificar plantillas ni administrar usuarios.
3. Comprueba que Consulta puede leer lotes y QR pero no guardar cambios.
4. Desactiva una cuenta con sesión abierta; su siguiente consulta debe pedir que inicie sesión otra vez y no debe poder entrar.
5. Cambia un rol, vuelve a iniciar sesión con esa cuenta y comprueba el nuevo menú.
6. Intenta desactivar el último administrador: debe rechazarse.

Las pruebas automáticas comprueban las políticas y las reglas de protección. La migración, la revocación y el flujo completo requieren probarse contra la instancia SQL de la instalación.
