using AutoLeads.Data;
using Dapper;
using Npgsql;
using Xunit;

namespace AutoLeads.Tests;

/// <summary>
/// Integration tests for MetricasRepository.
/// REQUIRES: PostgreSQL running. Usa una fecha futura fija para no mezclarse
/// con el seed y limpia sus filas (telefono "999-9999") en DisposeAsync.
/// </summary>
public class MetricasRepositoryTests : IAsyncLifetime
{
    private static readonly string ConnStr =
        Environment.GetEnvironmentVariable("TEST_DATABASE_URL")
        ?? "Host=localhost;Port=5433;Database=autoleads;Username=autoleads;Password=autoleads_pass";

    private readonly MetricasRepository _repo = new(ConnStr);

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnStr);
        await conn.ExecuteAsync("""
            INSERT INTO consultas (fecha, canal, modelo, nombre_cliente, telefono, ciudad, asesor_asignado, observaciones)
            VALUES
              ('2099-01-15 12:00:00+00', 'WhatsApp',  'MOD_TEST_A', 'Cliente Test', '999-9999', 'Rosario', 'AsesorX', null),
              ('2099-01-15 13:00:00+00', 'WhatsApp',  'MOD_TEST_A', 'Cliente Test', '999-9999', 'Rosario', 'AsesorX', null),
              ('2099-01-15 14:00:00+00', 'Instagram', 'MOD_TEST_B', 'Cliente Test', '999-9999', 'Rosario', 'AsesorY', null);
            """);
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnStr);
        await conn.ExecuteAsync("DELETE FROM consultas WHERE telefono = '999-9999';");
    }

    [Fact]
    public async Task ObtenerMetricasAsync_AgregaTotalesYCanalOrdenado()
    {
        var r = await _repo.ObtenerMetricasAsync(new DateOnly(2099, 1, 15), new DateOnly(2099, 1, 15));

        Assert.Equal(3, r.TotalConsultas);
        Assert.Equal("WhatsApp", r.PorCanal[0].Canal);
        Assert.Equal(2, r.PorCanal[0].Cantidad);
        Assert.Equal("Instagram", r.PorCanal[1].Canal);
        Assert.Equal(1, r.PorCanal[1].Cantidad);
    }

    [Fact]
    public async Task ObtenerMetricasAsync_AgregaPorAsesorModeloYDia()
    {
        var r = await _repo.ObtenerMetricasAsync(new DateOnly(2099, 1, 15), new DateOnly(2099, 1, 15));

        Assert.Equal(2, r.PorAsesor.First(a => a.Asesor == "AsesorX").Cantidad);
        Assert.Equal("MOD_TEST_A", r.PorModelo[0].Modelo);
        Assert.Equal(2, r.PorModelo[0].Cantidad);

        Assert.Single(r.PorDia);
        Assert.Equal(3, r.PorDia[0].Cantidad);
    }
}
