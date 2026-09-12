namespace AutoLeads.Models;

/// <summary>
/// Resultado agregado del endpoint de métricas. Se arma en SQL (GROUP BY),
/// no trayendo todas las filas a memoria.
/// </summary>
public class MetricasDto
{
    public int TotalConsultas { get; set; }
    public List<ConteoCanal> PorCanal { get; set; } = new();
    public List<ConteoAsesor> PorAsesor { get; set; } = new();
    public List<ConteoModelo> PorModelo { get; set; } = new();
    public List<ConteoDia> PorDia { get; set; } = new();
}

public class ConteoCanal
{
    public string Canal { get; set; } = string.Empty;
    public int Cantidad { get; set; }
}

public class ConteoAsesor
{
    public string Asesor { get; set; } = string.Empty;
    public int Cantidad { get; set; }
}

public class ConteoModelo
{
    public string Modelo { get; set; } = string.Empty;
    public int Cantidad { get; set; }
}

public class ConteoDia
{
    public DateTime Fecha { get; set; }
    public int Cantidad { get; set; }
}
