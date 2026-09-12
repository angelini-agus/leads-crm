using AutoLeads.Data;
using AutoLeads.Models;
using Dapper;
using Npgsql;
using Xunit;

namespace AutoLeads.Tests;

/// <summary>
/// Integration tests for ConsultaRepository.
/// REQUIRES: PostgreSQL running on localhost:5432
/// Run: docker compose up db -d  (from autoleads-crm directory)
/// Cada test limpia las filas que inserta (telefono "000-0000") en DisposeAsync.
/// </summary>
public class ConsultaRepositoryTests : IAsyncLifetime
{
    private static readonly string ConnStr =
        Environment.GetEnvironmentVariable("TEST_DATABASE_URL")
        ?? "Host=localhost;Port=5433;Database=autoleads;Username=autoleads;Password=autoleads_pass";

    private readonly ConsultaRepository _repo = new(ConnStr);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Todas las filas creadas por estos tests usan telefono "000-0000".
        await using var conn = new NpgsqlConnection(ConnStr);
        await conn.ExecuteAsync("DELETE FROM consultas WHERE telefono = '000-0000';");
    }

    private static Consulta BuildConsulta(string suffix = "") => new()
    {
        Canal          = $"Canal_Test{suffix}",
        Modelo         = $"Modelo_Test{suffix}",
        NombreCliente  = $"Cliente Test{suffix}",
        Telefono       = "000-0000",
        Ciudad         = "Ciudad Test",
        AsesorAsignado = $"Asesor_Test{suffix}",
        Observaciones  = "Observación de test"
    };

    [Fact]
    public async Task CrearAsync_ShouldReturnPositiveId()
    {
        // RED: Fails until ConsultaRepository.CrearAsync is implemented and DB is running
        var id = await _repo.CrearAsync(BuildConsulta("_Insert"));
        Assert.True(id > 0, $"Expected positive id, got {id}");
    }

    [Fact]
    public async Task ListarAsync_SinFiltros_DebeRetornarRegistros()
    {
        // Ensure at least 1 record exists
        await _repo.CrearAsync(BuildConsulta("_SinFiltro"));

        var resultados = await _repo.ListarAsync(new ConsultaFiltros());

        Assert.NotEmpty(resultados);
    }

    [Fact]
    public async Task ListarAsync_FiltrandoPorCanal_SoloDevuelveEseCanal()
    {
        var uniqueCanal = $"Canal_Unico_{Guid.NewGuid():N}";
        var consulta    = BuildConsulta();
        consulta.Canal  = uniqueCanal;

        await _repo.CrearAsync(consulta);

        var filtros    = new ConsultaFiltros { Canal = uniqueCanal };
        var resultados = (await _repo.ListarAsync(filtros)).ToList();

        Assert.NotEmpty(resultados);
        Assert.All(resultados, r => Assert.Equal(uniqueCanal, r.Canal));
    }

    [Fact]
    public async Task ListarAsync_FiltrandoPorAsesor_SoloDevuelveEseAsesor()
    {
        var uniqueAsesor        = $"Asesor_Unico_{Guid.NewGuid():N}";
        var consulta            = BuildConsulta();
        consulta.AsesorAsignado = uniqueAsesor;

        await _repo.CrearAsync(consulta);

        var filtros    = new ConsultaFiltros { AsesorAsignado = uniqueAsesor };
        var resultados = (await _repo.ListarAsync(filtros)).ToList();

        Assert.NotEmpty(resultados);
        Assert.All(resultados, r => Assert.Equal(uniqueAsesor, r.AsesorAsignado));
    }

    [Fact]
    public async Task ListarAsync_FiltrosCombinados_AplicaAmbosCondiciones()
    {
        var uniqueCanal         = $"Canal_Combo_{Guid.NewGuid():N}";
        var uniqueAsesor        = $"Asesor_Combo_{Guid.NewGuid():N}";
        var consulta            = BuildConsulta();
        consulta.Canal          = uniqueCanal;
        consulta.AsesorAsignado = uniqueAsesor;

        await _repo.CrearAsync(consulta);

        var filtros = new ConsultaFiltros
        {
            Canal          = uniqueCanal,
            AsesorAsignado = uniqueAsesor
        };
        var resultados = (await _repo.ListarAsync(filtros)).ToList();

        Assert.NotEmpty(resultados);
        Assert.All(resultados, r =>
        {
            Assert.Equal(uniqueCanal,  r.Canal);
            Assert.Equal(uniqueAsesor, r.AsesorAsignado);
        });
    }

    [Fact]
    public async Task ListarAsync_FiltrandoPorFechaDesde_SoloDevuelvePosteriores()
    {
        // Insert a fresh record (will have NOW() as fecha)
        await _repo.CrearAsync(BuildConsulta("_FechaHoy"));

        var hoy     = DateOnly.FromDateTime(DateTime.UtcNow);
        var filtros = new ConsultaFiltros { FechaDesde = hoy };

        var resultados = (await _repo.ListarAsync(filtros)).ToList();

        Assert.NotEmpty(resultados);
        Assert.All(resultados, r =>
            Assert.True(
                DateOnly.FromDateTime(r.Fecha.UtcDateTime) >= hoy,
                $"Fecha {r.Fecha} is before FechaDesde {hoy}"
            ));
    }
}
