await PruebasDominio.EjecutarAsync();
await PruebasPermisos.EjecutarAsync();
await PruebasReportes.EjecutarAsync();
Console.WriteLine("Todas las comprobaciones terminaron correctamente.");
