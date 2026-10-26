using QRCoder;

namespace ControlCalidadQR.Calidad;

// El contenido no incluye nombres, resultados, contraseñas ni la dirección del servidor.
public static class CodigoLote
{
    private const string Prefijo = "ccqr:lote:1:";

    public static string Crear(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new InvalidOperationException("El identificador de este lote no permite generar un QR.");
        return Prefijo + id;
    }

    // No se abren direcciones externas: solo se admite nuestro formato o un identificador pegado.
    public static string Leer(string? contenido)
    {
        var texto = contenido?.Trim() ?? "";
        var id = texto.StartsWith(Prefijo, StringComparison.Ordinal) ? texto[Prefijo.Length..] : texto;
        if (!Guid.TryParseExact(id, "N", out _))
            throw new InvalidOperationException("Este código no corresponde a un lote de Control de Calidad.");
        return id;
    }

    public static byte[] GenerarPng(string id)
    {
        using var datos = QRCodeGenerator.GenerateQrCode(Crear(id), QRCodeGenerator.ECCLevel.Q);
        using var imagen = new PngByteQRCode(datos);
        // Se mantiene el margen blanco que necesita el lector para reconocer el símbolo.
        return imagen.GetGraphic(16);
    }
}
