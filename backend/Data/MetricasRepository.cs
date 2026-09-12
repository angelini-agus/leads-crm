using Dapper;
using Npgsql;
using AutoLeads.Models;

namespace AutoLeads.Data;

public interface IMetricasRepository
{
    Task<MetricasDto> ObtenerMetricasAsync(DateOnly? fechaDesde, DateOnly? fechaHasta);
}

/// <summary>
/// Agregaciones para el dashboard de métricas. Todo se agrupa en SQL (GROUP BY);
/// no se traen las filas completas a memoria.
/// </summary>
public class MetricasRepository(string connectionString) : IMetricasRepository
{
    private NpgsqlConnection CreateConnection() => new(connectionString);

    public async Task<MetricasDto> ObtenerMetricasAsync(DateOnly? fechaDesde, DateOnly? fechaHasta)
    {
        await using var conn = CreateConnection();

        var desde = fechaDesde?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var hasta = fechaHasta?.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var condiciones = new List<string>();
        if (fechaDesde.HasValue) condiciones.Add("fecha >= @Desde");
        if (fechaHasta.HasValue) condiciones.Add("fecha <= @Hasta");
        var where = condiciones.Count > 0 ? " WHERE " + string.Join(" AND ", condiciones) : "";

        // Params frescos por query (solo las claves que el SQL usa).
        DynamicParameters Params()
        {
            var p = new DynamicParameters();
            if (fechaDesde.HasValue) p.Add("Desde", desde);
            if (fechaHasta.HasValue) p.Add("Hasta", hasta);
            return p;
        }

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM consultas{where}", Params());

        var porCanal = (await conn.QueryAsync<ConteoCanal>(
            $"SELECT canal, COUNT(*) AS cantidad FROM consultas{where} GROUP BY canal ORDER BY cantidad DESC",
            Params())).ToList();

        var porAsesor = (await conn.QueryAsync<ConteoAsesor>(
            $"SELECT asesor_asignado AS asesor, COUNT(*) AS cantidad FROM consultas{where} GROUP BY asesor_asignado ORDER BY cantidad DESC",
            Params())).ToList();

        var porModelo = (await conn.QueryAsync<ConteoModelo>(
            $"SELECT modelo, COUNT(*) AS cantidad FROM consultas{where} GROUP BY modelo ORDER BY cantidad DESC LIMIT 5",
            Params())).ToList();

        // Tendencia por día: rango filtrado, o últimos 30 días por defecto.
        var porDiaWhere = where;
        DynamicParameters porDiaParams = Params();
        if (condiciones.Count == 0)
        {
            porDiaWhere = " WHERE fecha >= NOW() - INTERVAL '30 days'";
            porDiaParams = new DynamicParameters();
        }

        var porDia = (await conn.QueryAsync<ConteoDia>(
            $"SELECT fecha::date AS fecha, COUNT(*) AS cantidad FROM consultas{porDiaWhere} GROUP BY fecha::date ORDER BY fecha::date",
            porDiaParams)).ToList();

        return new MetricasDto
        {
            TotalConsultas = total,
            PorCanal = porCanal,
            PorAsesor = porAsesor,
            PorModelo = porModelo,
            PorDia = porDia
        };
    }
}
