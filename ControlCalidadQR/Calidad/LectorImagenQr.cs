namespace ControlCalidadQR.Calidad;

// Convierte las imágenes al formato que admite el lector de cada plataforma.
public static class LectorImagenQr
{
    public static async Task<string[]> LeerAsync(Stream imagen)
    {
#if WINDOWS
        // Windows no admite todos los PNG monocromos como SoftwareBitmap.
        // Se solicita BGRA explícitamente y se limita el tamaño antes de leer los píxeles.
        using var memoria = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var salida = memoria.AsStreamForWrite())
        {
            await imagen.CopyToAsync(salida);
            await salida.FlushAsync();
            memoria.Seek(0);
            var decodificador = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(memoria);
            var escala = Math.Min(1d, 2400d / Math.Max(decodificador.PixelWidth, decodificador.PixelHeight));
            var ancho = Math.Max(1u, (uint)(decodificador.PixelWidth * escala));
            var alto = Math.Max(1u, (uint)(decodificador.PixelHeight * escala));
            var transformacion = new Windows.Graphics.Imaging.BitmapTransform { ScaledWidth = ancho, ScaledHeight = alto };
            var pixeles = await decodificador.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, transformacion,
                Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
                Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
            var fuente = new ZXing.RGBLuminanceSource(pixeles.DetachPixelData(), (int)ancho, (int)alto, ZXing.RGBLuminanceSource.BitmapFormat.BGRA32);
            var lector = new ZXing.BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new ZXing.Common.DecodingOptions { TryHarder = true, PossibleFormats = [ZXing.BarcodeFormat.QR_CODE] }
            };
            return (lector.DecodeMultiple(fuente) ?? []).Select(x => x.Text).ToArray();
        }
#else
        var resultados = await ZXing.Net.Maui.BarcodeReader.DecodeAsync(imagen,
            new ZXing.Net.Maui.BarcodeReaderOptions
            {
                Formats = ZXing.Net.Maui.BarcodeFormat.QrCode, AutoRotate = true, TryHarder = true, Multiple = true
            });
        return (resultados ?? []).Select(x => x.Value).ToArray();
#endif
    }
}
