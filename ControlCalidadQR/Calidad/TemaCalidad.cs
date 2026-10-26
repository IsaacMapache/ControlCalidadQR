namespace ControlCalidadQR.Calidad;

// Los formularios C# usan la misma paleta que las páginas XAML.
// Para cambiarla, modifica Resources/Styles/Colors.xaml.
internal static class TemaCalidad
{
    public static Color Color(string clave)
    {
        if (Application.Current?.Resources.TryGetValue(clave, out var recurso) == true && recurso is Color color)
            return color;
        throw new InvalidOperationException($"No se encontró el color de interfaz: {clave}.");
    }
}
