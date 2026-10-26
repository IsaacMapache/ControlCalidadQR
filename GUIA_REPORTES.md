# Reportes de control de calidad

## Generar un PDF

1. En el menú principal, abre **Controles y reportes**.
2. Selecciona un control finalizado.
3. Pulsa **Generar reporte PDF**.
4. Elige **Guardar PDF**, **Compartir PDF** o **Abrir PDF / imprimir**.

La impresión se realiza desde el lector PDF del dispositivo. Los borradores deben finalizarse antes de generar un reporte. Si falla la conexión, la pantalla permite reintentar.

El documento contiene el resultado, las respuestas y los criterios originales, responsable, fechas, versión y QR del lote. Los datos del lote se consultan al generar el archivo; las evaluaciones anteriores conservan su propia instantánea de criterios. Las revisiones se identifican sin borrar el historial.

## Ejecutar los cambios

Abre ControlCalidadQR.slnx en Visual Studio y ejecuta el proyecto ControlCalidadQR para Windows o Android. Mantén ServidorCalidad y SQL Server en ejecución. Esta actualización de interfaz y reportes no requiere borrar la base de datos ni desinstalar la aplicación.

En el mismo equipo, la API local usa http://localhost:5080. En el emulador Android, usa http://10.0.2.2:5080. Estas direcciones HTTP se usan para desarrollo local; en un dispositivo físico configura un servidor HTTPS accesible.

## Verificar las reglas

Desde la carpeta de la solución:

```powershell
dotnet run --project PruebasCalidad/PruebasCalidad.csproj
```

Las pruebas usan archivos temporales y no alteran la base SQL de trabajo. La cámara, el selector de archivos, el uso compartido y la impresión requieren comprobarse en el dispositivo de destino.

El informe del código está en Documentacion/Informe_tecnico_ControlCalidadQR.docx.
