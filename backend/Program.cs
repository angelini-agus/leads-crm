using System.Security.Cryptography;
using AutoLeads.Data;
using AutoLeads.Models;
using AutoLeads.Services;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// ── Connection string from environment (Docker injects DATABASE_URL) ──────────
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5433;Database=autoleads;Username=autoleads;Password=autoleads_pass";

// ── API Key for server-to-server auth (required on every /api/* request) ──────
var apiKey = Environment.GetEnvironmentVariable("API_KEY");
var apiKeyConfigured = !string.IsNullOrWhiteSpace(apiKey);

// ── CORS allowed origins (comma-separated env var, dev fallback) ──────────────
var allowedOriginsEnv = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS");
var allowedOrigins = string.IsNullOrWhiteSpace(allowedOriginsEnv)
    ? new[] { "http://localhost:5173", "http://localhost:3000", "http://127.0.0.1:5173" }
    : allowedOriginsEnv
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

// ── Dependency Injection ──────────────────────────────────────────────────────
builder.Services.AddSingleton<IConsultaRepository>(_ => new ConsultaRepository(connectionString));
builder.Services.AddSingleton<IMasterDataRepository>(_ => new MasterDataRepository(connectionString));
builder.Services.AddSingleton<ExcelService>();

// ── CORS Configuration ────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .SetIsOriginAllowedToAllowWildcardSubdomains()
            .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
            .WithHeaders("Content-Type", "Authorization", "Accept", "X-Api-Key")
            .WithExposedHeaders("Content-Disposition")
            .AllowCredentials();
    });
});

var app = builder.Build();

// ── Global Exception Handling ─────────────────────────────────────────────────
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
        var ex = exceptionHandlerPathFeature?.Error;

        var response = new
        {
            error = "Ocurrió un error interno en el servidor.",
            details = app.Environment.IsDevelopment() ? ex?.Message : null
        };

        await context.Response.WriteAsJsonAsync(response);
    });
});

app.UseCors();

// ── Startup configuration log (never prints the key value) ────────────────────
app.Logger.LogInformation(
    "Configuración de arranque — API_KEY: {ApiKeyState}; ALLOWED_ORIGINS: {AllowedOrigins}",
    apiKeyConfigured ? "configurada" : "AUSENTE",
    string.Join(", ", allowedOrigins));

if (!apiKeyConfigured)
{
    app.Logger.LogWarning(
        "API_KEY no configurada. Autenticación deshabilitada en Development; en Production se rechazan las requests a /api/* con 503.");
}

// ── API Key middleware — /health is public, every /api/* request needs the key ─
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    if (!apiKeyConfigured)
    {
        if (app.Environment.IsProduction())
        {
            app.Logger.LogError(
                "API_KEY ausente en Production: rechazando {Method} {Path}.",
                context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "Servicio no configurado: falta API_KEY." });
            return;
        }

        await next();
        return;
    }

    var providedKey = context.Request.Headers["X-Api-Key"].ToString();
    if (string.IsNullOrEmpty(providedKey) || !ApiKeyMatches(providedKey, apiKey!))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "API Key inválida o ausente." });
        return;
    }

    await next();
});

// ── Health check (public, used by Docker/nginx) ───────────────────────────────
app.MapGet("/health", () =>
    Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));

// ── GET /api/catalogos — Dynamic Dropdown Options from DB ─────────────────────
app.MapGet("/api/catalogos", async (IMasterDataRepository masterRepo) =>
{
    var modelosActivos = await masterRepo.ListarModelosAsync(soloActivos: true);
    var vendedoresActivos = await masterRepo.ListarVendedoresAsync(soloActivos: true);

    return Results.Ok(new
    {
        canales = new[]
        {
            "WhatsApp", "Instagram", "Facebook", "Mercado Libre",
            "Web", "Llamado", "Presencial", "Referido"
        },
        modelos = modelosActivos.Select(m => m.Nombre),
        asesores = vendedoresActivos.Select(v => v.Nombre),
        ciudades = new[]
        {
            "Rosario", "Córdoba", "Buenos Aires", "Santa Fe",
            "Venado Tuerto", "Rafaela", "San Lorenzo", "Paraná"
        }
    });
});

// ── Master Data: Modelos CRUD ────────────────────────────────────────────────
app.MapGet("/api/modelos", async (IMasterDataRepository masterRepo, bool? soloActivos) =>
{
    var list = await masterRepo.ListarModelosAsync(soloActivos ?? false);
    return Results.Ok(list);
});

app.MapPost("/api/modelos", async (IMasterDataRepository masterRepo, CreateMasterDataItemRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Nombre))
        return Results.BadRequest(new { error = "El nombre del modelo es requerido." });

    try
    {
        var id = await masterRepo.CrearModeloAsync(req.Nombre);
        return Results.Created($"/api/modelos/{id}", new { id, nombre = req.Nombre, activo = true });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = "No se pudo crear el modelo. Es posible que ya exista.", detail = ex.Message });
    }
});

app.MapPut("/api/modelos/{id:int}", async (IMasterDataRepository masterRepo, int id, UpdateMasterDataItemRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Nombre))
        return Results.BadRequest(new { error = "El nombre del modelo es requerido." });

    var updated = await masterRepo.ActualizarModeloAsync(id, req.Nombre, req.Activo);
    if (!updated) return Results.NotFound(new { error = "Modelo no encontrado." });
    return Results.Ok(new { id, nombre = req.Nombre, activo = req.Activo });
});

app.MapDelete("/api/modelos/{id:int}", async (IMasterDataRepository masterRepo, int id) =>
{
    var updated = await masterRepo.AlternarEstadoModeloAsync(id, activo: false);
    if (!updated) return Results.NotFound(new { error = "Modelo no encontrado." });
    return Results.Ok(new { message = "Modelo desactivado (borrado lógico) correctamente." });
});

// ── Master Data: Vendedores CRUD ─────────────────────────────────────────────
app.MapGet("/api/vendedores", async (IMasterDataRepository masterRepo, bool? soloActivos) =>
{
    var list = await masterRepo.ListarVendedoresAsync(soloActivos ?? false);
    return Results.Ok(list);
});

app.MapPost("/api/vendedores", async (IMasterDataRepository masterRepo, CreateMasterDataItemRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Nombre))
        return Results.BadRequest(new { error = "El nombre del vendedor es requerido." });

    try
    {
        var id = await masterRepo.CrearVendedorAsync(req.Nombre);
        return Results.Created($"/api/vendedores/{id}", new { id, nombre = req.Nombre, activo = true });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = "No se pudo crear el vendedor. Es posible que ya exista.", detail = ex.Message });
    }
});

app.MapPut("/api/vendedores/{id:int}", async (IMasterDataRepository masterRepo, int id, UpdateMasterDataItemRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Nombre))
        return Results.BadRequest(new { error = "El nombre del vendedor es requerido." });

    var updated = await masterRepo.ActualizarVendedorAsync(id, req.Nombre, req.Activo);
    if (!updated) return Results.NotFound(new { error = "Vendedor no encontrado." });
    return Results.Ok(new { id, nombre = req.Nombre, activo = req.Activo });
});

app.MapDelete("/api/vendedores/{id:int}", async (IMasterDataRepository masterRepo, int id) =>
{
    var updated = await masterRepo.AlternarEstadoVendedorAsync(id, activo: false);
    if (!updated) return Results.NotFound(new { error = "Vendedor no encontrado." });
    return Results.Ok(new { message = "Vendedor desactivado (borrado lógico) correctamente." });
});

// ── GET /api/consultas — List with optional filters ───────────────────────────
app.MapGet("/api/consultas", async (
    IConsultaRepository repo,
    string?             canal,
    string?             asesorAsignado,
    string?             fechaDesde,
    string?             fechaHasta) =>
{
    try
    {
        var filtros = new ConsultaFiltros
        {
            Canal          = canal,
            AsesorAsignado = asesorAsignado,
            FechaDesde     = DateOnly.TryParse(fechaDesde, out var fd) ? fd : null,
            FechaHasta     = DateOnly.TryParse(fechaHasta, out var fh) ? fh : null
        };

        var data = await repo.ListarAsync(filtros);
        return Results.Ok(data);
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            title: "Error al consultar la base de datos.",
            statusCode: StatusCodes.Status500InternalServerError
        );
    }
});

// ── POST /api/consultas — Create a new lead ───────────────────────────────────
app.MapPost("/api/consultas", async (IConsultaRepository repo, CreateConsultaRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Canal)   ||
        string.IsNullOrWhiteSpace(req.Modelo)  ||
        string.IsNullOrWhiteSpace(req.Telefono)||
        string.IsNullOrWhiteSpace(req.AsesorAsignado))
    {
        return Results.BadRequest(new { error = "Canal, Modelo, Telefono y AsesorAsignado son requeridos." });
    }

    // Phone format validation (must contain between 7 and 15 digits)
    var digitsOnly = new string(req.Telefono.Where(char.IsDigit).ToArray());
    if (digitsOnly.Length < 7 || digitsOnly.Length > 15)
    {
        return Results.BadRequest(new { error = "El número de teléfono debe contener entre 7 y 15 dígitos válidos." });
    }

    try
    {
        var consulta = new Consulta
        {
            Fecha          = DateTimeOffset.UtcNow,
            Canal          = req.Canal.Trim(),
            Modelo         = req.Modelo.Trim(),
            NombreCliente  = req.NombreCliente?.Trim() ?? string.Empty,
            Telefono       = req.Telefono.Trim(),
            Ciudad         = req.Ciudad?.Trim(),
            AsesorAsignado = req.AsesorAsignado.Trim(),
            Observaciones  = req.Observaciones?.Trim()
        };

        var id = await repo.CrearAsync(consulta);
        return Results.Created($"/api/consultas/{id}", new { id });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            title: "Error al guardar la consulta en la base de datos.",
            statusCode: StatusCodes.Status500InternalServerError
        );
    }
});

// ── GET /api/consultas/export — Download Excel with current filters ────────────
app.MapGet("/api/consultas/export", async (
    IConsultaRepository repo,
    ExcelService         excel,
    string?              canal,
    string?              asesorAsignado,
    string?              fechaDesde,
    string?              fechaHasta) =>
{
    try
    {
        var filtros = new ConsultaFiltros
        {
            Canal          = canal,
            AsesorAsignado = asesorAsignado,
            FechaDesde     = DateOnly.TryParse(fechaDesde, out var fd) ? fd : null,
            FechaHasta     = DateOnly.TryParse(fechaHasta, out var fh) ? fh : null
        };

        var data     = await repo.ListarAsync(filtros);
        var bytes    = excel.GenerarExcel(data);
        var filename = $"consultas_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

        return Results.File(
            bytes,
            contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileDownloadName: filename
        );
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            title: "Error al generar el archivo Excel.",
            statusCode: StatusCodes.Status500InternalServerError
        );
    }
});

app.Run();

// Constant-time comparison to avoid leaking the key length/content via timing.
static bool ApiKeyMatches(string provided, string expected)
{
    var providedBytes = System.Text.Encoding.UTF8.GetBytes(provided);
    var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
    return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
}

public partial class Program { }
