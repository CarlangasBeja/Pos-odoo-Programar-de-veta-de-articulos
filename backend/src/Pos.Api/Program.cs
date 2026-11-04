using Pos.Api;

var app = PosApiHost.Build(args);
app.Run();

/// <summary>Punto de entrada; expuesto como parcial para las pruebas de la API.</summary>
public partial class Program;
