using System.Text;
using AutoLeads.Data;
using AutoLeads.Models;
using AutoLeads.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── Connection string from environment (Docker injects DATABASE_URL) ──────────
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5433;Database=autoleads;Username=autoleads;Password=autoleads_pass";

// ── JWT signing key (required — never hardcoded) ──────────────────────────────
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET");
if (string.IsNullOrWhiteSpace(jwtSecret))
{
    throw new InvalidOperationException(
        "JWT_SECRET no está configurada. Es obligatoria (usá por ejemplo: openssl rand -hex 32).");
}

// ── CORS allowed origins (comma-separated env var) ────────────────────────────
// Development falls back to the local dev origins. Production requires an
// explicit value and fails fast: a silent localhost fallback would let a
// deployment start "healthy" while blocking the real frontend at CORS.
var allowedOriginsEnv = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS");
string[] allowedOrigins;

if (!string.IsNullOrWhiteSpace(allowedOriginsEnv))
{
    allowedOrigins = allowedOriginsEnv
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
else if (builder.Environment.IsDevelopment())
{
    allowedOrigins = new[] { "http://localhost:5173", "http://localhost:3000", "http://127.0.0.1:5173" };
}
else
{
    throw new InvalidOperationException(
        "ALLOWED_ORIGINS no está configurada. Es obligatoria fuera de Development " +
        "(orígenes separados por coma, ej: https://autoleads-crm.pages.dev).");
}

// ── Dependency Injection ──────────────────────────────────────────────────────
builder.Services.AddSingleton<IConsultaRepository>(_ => new ConsultaRepository(connectionString));
builder.Services.AddSingleton<IMasterDataRepository>(_ => new MasterDataRepository(connectionString));
builder.Services.AddSingleton<IUsuarioRepository>(_ => new UsuarioRepository(connectionString));
builder.Services.AddSingleton<ExcelService>();
builder.Services.AddSingleton(new JwtService(jwtSecret));

// ── CORS Configuration ────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .SetIsOriginAllowedToAllowWildcardSubdomains()
            .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
            .WithHeaders("Content-Type", "Authorization", "Accept")
            .WithExposedHeaders("Content-Disposition")
            .AllowCredentials();
    });
});

// ── Authentication: JWT Bearer ────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "No autorizado. Token ausente o inválido." });
            }
        };
    });

// ── Authorization: rol "admin" ────────────────────────────────────────────────
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("admin"));
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
app.UseAuthentication();
app.UseAuthorization();

// ── Startup configuration log (never prints the secret) ───────────────────────
app.Logger.LogInformation(
    "Configuración de arranque — JWT_SECRET: configurada; ALLOWED_ORIGINS: {AllowedOrigins}",
    string.Join(", ", allowedOrigins));

// ── Health check (public, used by Docker/nginx) ───────────────────────────────
app.MapGet("/health", () =>
    Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));

// ── POST /api/auth/login (public) ─────────────────────────────────────────────
app.MapPost("/api/auth/login", async (IUsuarioRepository usuarios, JwtService jwt, LoginRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "Email y contraseña son requeridos." });

    var usuario = await usuarios.ObtenerPorEmailAsync(req.Email);

    if (usuario is null || !BCrypt.Net.BCrypt.Verify(req.Password, usuario.PasswordHash))
        return Results.Unauthorized();

    var token = jwt.GenerarToken(usuario);
    return Results.Ok(new { token, nombre = usuario.Nombre, rol = usuario.Rol });
});

// ── All /api/* endpoints require a valid JWT ──────────────────────────────────
var api = app.MapGroup("/api").RequireAuthorization();

// ── GET /api/catalogos — Dynamic Dropdown Options from DB ─────────────────────
api.MapGet("/catalogos", async (IMasterDataRepository masterRepo) =>
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
api.MapGet("/modelos", async (IMasterDataRepository masterRepo, bool? soloActivos) =>
{
    var list = await masterRepo.ListarModelosAsync(soloActivos ?? false);
    return Results.Ok(list);
});

api.MapPost("/modelos", async (IMasterDataRepository masterRepo, CreateMasterDataItemRequest req) =>
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
}).RequireAuthorization("Admin");

api.MapPut("/modelos/{id:int}", async (IMasterDataRepository masterRepo, int id, UpdateMasterDataItemRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Nombre))
        return Results.BadRequest(new { error = "El nombre del modelo es requerido." });

    var updated = await masterRepo.ActualizarModeloAsync(id, req.Nombre, req.Activo);
    if (!updated) return Results.NotFound(new { error = "Modelo no encontrado." });
    return Results.Ok(new { id, nombre = req.Nombre, activo = req.Activo });
}).RequireAuthorization("Admin");

api.MapDelete("/modelos/{id:int}", async (IMasterDataRepository masterRepo, int id) =>
{
    var updated = await masterRepo.AlternarEstadoModeloAsync(id, activo: false);
    if (!updated) return Results.NotFound(new { error = "Modelo no encontrado." });
    return Results.Ok(new { message = "Modelo desactivado (borrado lógico) correctamente." });
}).RequireAuthorization("Admin");

// ── Master Data: Vendedores CRUD ─────────────────────────────────────────────
api.MapGet("/vendedores", async (IMasterDataRepository masterRepo, bool? soloActivos) =>
{
    var list = await masterRepo.ListarVendedoresAsync(soloActivos ?? false);
    return Results.Ok(list);
});

api.MapPost("/vendedores", async (IMasterDataRepository masterRepo, CreateMasterDataItemRequest req) =>
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
}).RequireAuthorization("Admin");

api.MapPut("/vendedores/{id:int}", async (IMasterDataRepository masterRepo, int id, UpdateMasterDataItemRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Nombre))
        return Results.BadRequest(new { error = "El nombre del vendedor es requerido." });

    var updated = await masterRepo.ActualizarVendedorAsync(id, req.Nombre, req.Activo);
    if (!updated) return Results.NotFound(new { error = "Vendedor no encontrado." });
    return Results.Ok(new { id, nombre = req.Nombre, activo = req.Activo });
}).RequireAuthorization("Admin");

api.MapDelete("/vendedores/{id:int}", async (IMasterDataRepository masterRepo, int id) =>
{
    var updated = await masterRepo.AlternarEstadoVendedorAsync(id, activo: false);
    if (!updated) return Results.NotFound(new { error = "Vendedor no encontrado." });
    return Results.Ok(new { message = "Vendedor desactivado (borrado lógico) correctamente." });
}).RequireAuthorization("Admin");

// ── Usuarios CRUD (admin only) ────────────────────────────────────────────────
api.MapGet("/usuarios", async (IUsuarioRepository repo) =>
{
    var list = await repo.ListarAsync();
    return Results.Ok(list);
}).RequireAuthorization("Admin");

api.MapPost("/usuarios", async (IUsuarioRepository repo, CreateUsuarioRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Nombre) ||
        string.IsNullOrWhiteSpace(req.Email)  ||
        string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.BadRequest(new { error = "Nombre, email y contraseña son requeridos." });
    }

    if (req.Rol is not ("admin" or "asesor"))
        return Results.BadRequest(new { error = "El rol debe ser 'admin' o 'asesor'." });

    try
    {
        var usuario = new Usuario
        {
            Nombre       = req.Nombre.Trim(),
            Email        = req.Email.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Rol          = req.Rol,
            Activo       = true
        };

        var id = await repo.CrearAsync(usuario);
        return Results.Created($"/api/usuarios/{id}",
            new { id, nombre = usuario.Nombre, email = usuario.Email, rol = usuario.Rol, activo = true });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = "No se pudo crear el usuario. Es posible que el email ya exista.", detail = ex.Message });
    }
}).RequireAuthorization("Admin");

api.MapPut("/usuarios/{id:int}", async (IUsuarioRepository repo, int id, UpdateUsuarioRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Nombre) || string.IsNullOrWhiteSpace(req.Email))
        return Results.BadRequest(new { error = "Nombre y email son requeridos." });

    if (req.Rol is not ("admin" or "asesor"))
        return Results.BadRequest(new { error = "El rol debe ser 'admin' o 'asesor'." });

    try
    {
        var updated = await repo.ActualizarAsync(id, req.Nombre.Trim(), req.Email.Trim(), req.Rol, req.Activo);
        if (!updated) return Results.NotFound(new { error = "Usuario no encontrado." });
        return Results.Ok(new { id, nombre = req.Nombre.Trim(), email = req.Email.Trim(), rol = req.Rol, activo = req.Activo });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = "No se pudo actualizar el usuario. Es posible que el email ya exista.", detail = ex.Message });
    }
}).RequireAuthorization("Admin");

api.MapPut("/usuarios/{id:int}/password", async (IUsuarioRepository repo, int id, UpdatePasswordRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "La contraseña es requerida." });

    var hash    = BCrypt.Net.BCrypt.HashPassword(req.Password);
    var updated = await repo.ActualizarPasswordAsync(id, hash);

    if (!updated) return Results.NotFound(new { error = "Usuario no encontrado." });
    return Results.Ok(new { message = "Contraseña actualizada correctamente." });
}).RequireAuthorization("Admin");

api.MapDelete("/usuarios/{id:int}", async (IUsuarioRepository repo, int id) =>
{
    var updated = await repo.AlternarEstadoAsync(id, activo: false);
    if (!updated) return Results.NotFound(new { error = "Usuario no encontrado." });
    return Results.Ok(new { message = "Usuario desactivado (borrado lógico) correctamente." });
}).RequireAuthorization("Admin");

// ── GET /api/consultas — List with optional filters ───────────────────────────
api.MapGet("/consultas", async (
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
api.MapPost("/consultas", async (IConsultaRepository repo, CreateConsultaRequest req) =>
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
api.MapGet("/consultas/export", async (
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

// ── GET /api/metricas — aggregate counts (admin only) ─────────────────────────
api.MapGet("/metricas", async (IConsultaRepository repo) =>
{
    try
    {
        var data = (await repo.ListarAsync(new ConsultaFiltros())).ToList();
        var total = data.Count;
        var ultimos30 = data.Count(c => c.Fecha >= DateTimeOffset.UtcNow.AddDays(-30));
        var porCanal = data.GroupBy(c => c.Canal).ToDictionary(g => g.Key, g => g.Count());
        var porAsesor = data.GroupBy(c => c.AsesorAsignado).ToDictionary(g => g.Key, g => g.Count());

        return Results.Ok(new
        {
            totalConsultas = total,
            consultasUltimos30Dias = ultimos30,
            porCanal,
            porAsesor
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            title: "Error al generar las métricas.",
            statusCode: StatusCodes.Status500InternalServerError
        );
    }
}).RequireAuthorization("Admin");

app.Run();

public partial class Program { }
