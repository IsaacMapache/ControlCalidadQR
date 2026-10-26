# Diseño de la aplicación

La interfaz usa una escala de morados con fondo lavanda claro y tarjetas blancas.

| Uso | Color | Recurso |
| --- | --- | --- |
| Cabecera | #38205A | CabeceraCalidad |
| Botones principales | #7040AD | Primary |
| Fondos secundarios | #EEE5F8 | Secondary |
| Fondo general | #F7F4FC | FondoCalidad |
| Bordes | #DED3EC | BordeCalidad |
| Texto secundario | #665673 | TextoSecundarioCalidad |

Los colores compartidos se encuentran en ControlCalidadQR/Resources/Styles/Colors.xaml. Las páginas XAML usan esos recursos y los formularios C# acceden a ellos mediante Calidad/TemaCalidad.cs. Los colores de la ventana nativa Android se ajustan en Platforms/Android/Resources/values/colors.xml. Después de modificarlos, recompila la aplicación.

El acceso permite mostrar u ocultar la contraseña y pasar del usuario a la contraseña con la tecla de avance. Al regresar a la pantalla se limpian los campos y vuelve a ocultarse la contraseña. Los mensajes de lotes vacíos distinguen filtros sin resultados, registros archivados y permisos de consulta.

## Diagramas

Abre Documentacion/Diagramas/Indice_diagramas.html. Funciona sin conexión e incluye diez flujos con sus decisiones. Puedes abrir un SVG ampliado, insertarlo en Word o imprimir desde el navegador. Los archivos DOT permiten editar cada diagrama con Graphviz.

Los diagramas describen el código actual y las comprobaciones de la API; las verificaciones visuales del dispositivo no sustituyen las autorizaciones del servidor.
