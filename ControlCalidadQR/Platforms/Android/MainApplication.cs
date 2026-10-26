using Android.App;
using Android.Runtime;

namespace ControlCalidadQR
{
#if DEBUG
    // Permite HTTP únicamente hacia el equipo local durante las pruebas con el emulador.
    [Application(NetworkSecurityConfig = "@xml/red_desarrollo")]
#else
    [Application]
#endif
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}

