# Diagramas de ControlCalidadQR

Abre Indice_diagramas.html en un navegador. Funciona sin conexión. Cada diagrama tiene su SVG para ampliarlo o insertarlo en Word y su fuente DOT editable. El botón Imprimir permite guardar el conjunto como PDF.

Para regenerar un SVG con Graphviz:

```powershell
dot -Tsvg 05-evaluacion.dot -o 05-evaluacion.svg
```

- **Flujo general del proyecto**: Mapa de navegación y relación entre módulos. Cada operación central utiliza una sesión autenticada. Los archivos PDF y QR se generan en el dispositivo.
- **Decisiones de inicio de sesión**: Los campos se validan en la app. La API limita los intentos y comprueba la cuenta; el token vence a las ocho horas y cada petición vuelve a comprobar la sesión.
- **Decisiones de una escritura central**: Flujo común de lotes, plantillas y controles. Las validaciones y las versiones se comprueban en el servidor; una falla no confirma la transacción.
- **Lotes y plantillas configurables**: Todos los roles pueden consultar. Los lotes conservan su Id al editarse o archivarse. Las plantillas se copian dentro del control para conservar sus criterios históricos.
- **Decisiones de evaluación de calidad**: El cálculo se ejecuta en el servidor. Un número con límites inclusivos o una respuesta Cumple/No cumple es evaluable; texto y números sin límites se registran sin decidir aprobación.
- **Historial revisiones y resumen**: Un control finalizado no se sobrescribe. El historial conserva todas las versiones; el resumen deja de contar un resultado anterior cuando hay una revisión sucesora finalizada.
- **Generación y lectura de códigos QR**: El QR contiene ccqr:lote:1: y el Id permanente. No guarda las respuestas del control ni contraseñas. Consultarlo requiere acceso al mismo servidor.
- **Decisiones de generación de reportes PDF**: El PDF conserva las respuestas y criterios del control y muestra los datos del lote consultados al emitirlo. Abrir el PDF permite imprimir desde un lector instalado.
- **Decisiones de administración de usuarios**: La API protege estas operaciones aunque se intente omitir la interfaz. Un cambio de rol, estado o contraseña revoca las sesiones de la cuenta modificada.
- **Instalación migración y operación**: Estos pasos se ejecutan por el responsable del servidor. La importación de archivos antiguos es opcional y no sustituye un respaldo SQL.
- **Flujo del Administrador**: Puede registrar y corregir lotes y controles, administrar plantillas y gestionar usuarios. También puede consultar QR, historial, resumen y generar reportes. El servidor valida cada operación.
- **Flujo del Inspector**: Puede registrar lotes, realizar controles y crear revisiones. Consulta las plantillas existentes, pero no puede modificarlas ni administrar usuarios. Puede generar QR y PDF.
- **Flujo del rol Consulta**: Acceso de lectura: puede ver lotes y plantillas, consultar controles, historial y resumen, leer o generar QR y exportar reportes de controles finalizados. No guarda cambios ni administra cuentas.
