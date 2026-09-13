using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AutoLeads.Data;
using AutoLeads.Models;
using AutoLeads.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// ── Nombre de la cookie HttpOnly que transporta el JWT ────────────────────────
const string AuthCookieName = "autoleads_token";

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

// HS256 con menos de 32 bytes (256 bits) es forzable por fuerza bruta. Exigimos
// el mínimo antes de arrancar para que una config débil no firme tokens forjables.
if (Encoding.UTF8.GetByteCount(jwtSecret) < 32)
{
    throw new InvalidOperationException(
        "JWT_SECRET es demasiado corta: HS256 requiere al menos 32 bytes (256 bits). " +
        "Generá una con: openssl rand -hex 32.");
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

    // Un valor como "," no es whitespace, pero tras el split queda vacío. Sin
    // este chequeo la API arrancaría "sana" bloqueando todo origen por CORS.
    if (allowedOrigins.Length == 0)
    {
        throw new InvalidOperationException(
            "ALLOWED_ORIGINS está seteada pero no contiene ningún origen válido. " +
            "Separá los orígenes con coma, ej: https://autoleads-crm.pages.dev");
    }
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
builder.Services.AddSingleton<IMetricasRepository>(_ => new MetricasRepository(connectionString));
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
            // El JWT viaja en una cookie HttpOnly; solo caemos al header
            // Authorization si no hay cookie (compatibilidad con clientes API).
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token))
                {
                    var cookieToken = context.Request.Cookies[AuthCookieName];
                    if (!string.IsNullOrEmpty(cookieToken))
                        context.Token = cookieToken;
                }
                return Task.CompletedTask;
            },

            // Revocación: un token sigue siendo criptográficamente válido por 8h,
            // así que validamos contra la BD que la cuenta siga activa y con el
            // mismo rol. Así desactivar o degradar a un usuario invalida su token.
            OnTokenValidated = async context =>
            {
                var repo = context.HttpContext.RequestServices.GetRequiredService<IUsuarioRepository>();
                var principal = context.Principal;

                var sub = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

                if (!int.TryParse(sub, out var id))
                {
                    context.Fail("Token inválido.");
                    return;
                }

                try
                {
                    var usuario = await repo.ObtenerPorIdAsync(id);
                    if (usuario is null || !usuario.Activo)
                    {
                        context.Fail("La cuenta ya no está activa.");
                        return;
                    }

                    var rolClaim = principal?.FindFirst(ClaimTypes.Role)?.Value;
                    if (!string.Equals(rolClaim, usuario.Rol, StringComparison.Ordinal))
                    {
                        context.Fail("El rol del usuario cambió. Volvé a iniciar sesión.");
                    }
                }
                catch
                {
                    context.Fail("No se pudo validar la cuenta.");
                }
            },

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

// ── Migración idempotente + bootstrap del primer admin ────────────────────────
// init.sql solo corre al crear el volumen por primera vez; este paso cubre un
// volumen ya inicializado (agrega `usuarios` y su índice) y garantiza que
// siempre exista un admin activo sin sembrar contraseñas publicadas.
try
{
    await DatabaseInitializer.EnsureSchemaAsync(connectionString);
    await AdminBootstrapper.EnsureSeedAdminAsync(
        app.Services.GetRequiredService<IUsuarioRepository>(), app.Logger);
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Falló la inicialización de la base de datos. La API no puede arrancar.");
    throw;
}

// ── Startup configuration log (never prints the secret) ───────────────────────
app.Logger.LogInformation(
    "Configuración de arranque — JWT_SECRET: configurada; ALLOWED_ORIGINS: {AllowedOrigins}",
    string.Join(", ", allowedOrigins));

// ── Health check (public, used by Docker/nginx) ───────────────────────────────
app.MapGet("/health", () =>
    Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));

// Opciones de la cookie de sesión. SameSite=None+Secure en producción (cross-site
// Pages->API); Lax+no-Secure en dev (mismo sitio vía proxy de Vite).
static CookieOptions AuthCookieOptions(bool isDevelopment) => new()
{
    HttpOnly = true,
    Secure   = !isDevelopment,
    SameSite = isDevelopment ? SameSiteMode.Lax : SameSiteMode.None,
    Path     = "/",
    MaxAge   = TimeSpan.FromHours(8),
};

// Un error de unicidad (email/nombre duplicado) es culpa del request (400);
// cualquier otra excepción de BD es 500 y su detalle se queda en los logs.
static bool EsViolacionUnica(Exception ex) =>
    ex is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;

// Parseo estricto de fechas de query. Un valor no vacío pero inválido es un
// error del cliente (400), nunca se traduce silenciosamente a "sin filtro".
static bool TryParseFecha(string? raw, out DateOnly? value, out string? error)
{
    value = null;
    error = null;

    if (string.IsNullOrWhiteSpace(raw)) return true;

    if (!DateOnly.TryParse(raw, out var parsed))
    {
        error = $"Fecha inválida: '{raw}'. Formato esperado: YYYY-MM-DD.";
        return false;
    }

    value = parsed;
    return true;
}

static string? ValidarRango(DateOnly? desde, DateOnly? hasta) =>
    desde.HasValue && hasta.HasValue && desde > hasta
        ? "El rango de fechas es inválido: 'desde' es posterior a 'hasta'."
        : null;

// ── POST /api/auth/login (public) ─────────────────────────────────────────────
app.MapPost("/api/auth/login", async (IUsuarioRepository usuarios, JwtService jwt, LoginRequest req, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "Email y contraseña son requeridos." });

    var usuario = await usuarios.ObtenerPorEmailAsync(req.Email);

    if (usuario is null || !BCrypt.Net.BCrypt.Verify(req.Password, usuario.PasswordHash))
        return Results.Unauthorized();

    var token = jwt.GenerarToken(usuario);

    // El JWT viaja en cookie HttpOnly (no en el body ni en localStorage): no es
    // legible por JS, así que un XSS no puede exfiltrarlo. En producción va
    // SameSite=None+Secure por ser cross-site (Pages -> API); en dev Lax.
    http.Response.Cookies.Append(AuthCookieName, token, AuthCookieOptions(app.Environment.IsDevelopment()));

    return Results.Ok(new { nombre = usuario.Nombre, rol = usuario.Rol });
});

// ── POST /api/auth/logout (public: borra la cookie aunque el token expiró) ─────
app.MapPost("/api/auth/logout", (HttpContext http) =>
{
    http.Response.Cookies.Delete(AuthCookieName, new CookieOptions
    {
        Path     = "/",
        Secure   = !app.Environment.IsDevelopment(),
        SameSite = app.Environment.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.None,
    });
    return Results.Ok(new { message = "Sesión cerrada." });
});

// ── All /api/* endpoints require a valid JWT ──────────────────────────────────
var api = app.MapGroup("/api").RequireAuthorization();

// ── GET /api/auth/me — identidad actual (restaura sesión al recargar) ─────────
api.MapGet("/auth/me", (ClaimsPrincipal user) =>
{
    var nombre = user.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
    var rol    = user.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
    return Results.Ok(new { nombre, rol });
});

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
    catch (Exception ex) when (EsViolacionUnica(ex))
    {
        return Results.BadRequest(new { error = "No se pudo crear el modelo. Es posible que ya exista." });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error al crear modelo.");
        return Results.Problem(title: "No se pudo crear el modelo.", statusCode: StatusCodes.Status500InternalServerError);
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
    catch (Exception ex) when (EsViolacionUnica(ex))
    {
        return Results.BadRequest(new { error = "No se pudo crear el vendedor. Es posible que ya exista." });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error al crear vendedor.");
        return Results.Problem(title: "No se pudo crear el vendedor.", statusCode: StatusCodes.Status500InternalServerError);
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
            Email        = req.Email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Rol          = req.Rol,
            Activo       = true
        };

        var id = await repo.CrearAsync(usuario);
        return Results.Created($"/api/usuarios/{id}",
            new { id, nombre = usuario.Nombre, email = usuario.Email, rol = usuario.Rol, activo = true });
    }
    catch (Exception ex) when (EsViolacionUnica(ex))
    {
        return Results.BadRequest(new { error = "No se pudo crear el usuario. Es posible que el email ya exista." });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error al crear usuario.");
        return Results.Problem(title: "No se pudo crear el usuario.", statusCode: StatusCodes.Status500InternalServerError);
    }
}).RequireAuthorization("Admin");

api.MapPut("/usuarios/{id:int}", async (IUsuarioRepository repo, int id, UpdateUsuarioRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Nombre) || string.IsNullOrWhiteSpace(req.Email))
        return Results.BadRequest(new { error = "Nombre y email son requeridos." });

    if (req.Rol is not ("admin" or "asesor"))
        return Results.BadRequest(new { error = "El rol debe ser 'admin' o 'asesor'." });

    // No permitir que la edición deje al sistema sin administradores activos
    // (degradar a asesor o desactivar al último admin).
    var actual = await repo.ObtenerPorIdAsync(id);
    if (actual is null) return Results.NotFound(new { error = "Usuario no encontrado." });

    var dejaDeSerAdminActivo = actual.Rol == "admin" && actual.Activo
        && (req.Rol != "admin" || !req.Activo);
    if (dejaDeSerAdminActivo && await repo.ContarAdminsActivosAsync() <= 1)
        return Results.BadRequest(new { error = "No se puede dejar al sistema sin ningún administrador activo." });

    try
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var updated = await repo.ActualizarAsync(id, req.Nombre.Trim(), email, req.Rol, req.Activo);
        if (!updated) return Results.NotFound(new { error = "Usuario no encontrado." });
        return Results.Ok(new { id, nombre = req.Nombre.Trim(), email, rol = req.Rol, activo = req.Activo });
    }
    catch (Exception ex) when (EsViolacionUnica(ex))
    {
        return Results.BadRequest(new { error = "No se pudo actualizar el usuario. Es posible que el email ya exista." });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error al actualizar usuario.");
        return Results.Problem(title: "No se pudo actualizar el usuario.", statusCode: StatusCodes.Status500InternalServerError);
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
    var actual = await repo.ObtenerPorIdAsync(id);
    if (actual is null) return Results.NotFound(new { error = "Usuario no encontrado." });

    // Un clic accidental no puede dejar el sistema sin forma de administrar
    // usuarios. Rechazamos desactivar al último admin activo.
    if (actual.Rol == "admin" && actual.Activo && await repo.ContarAdminsActivosAsync() <= 1)
        return Results.BadRequest(new { error = "No se puede desactivar al último administrador activo." });

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
    if (!TryParseFecha(fechaDesde, out var fd, out var errFecha)) return Results.BadRequest(new { error = errFecha });
    if (!TryParseFecha(fechaHasta, out var fh, out errFecha)) return Results.BadRequest(new { error = errFecha });
    if (ValidarRango(fd, fh) is { } rangoError) return Results.BadRequest(new { error = rangoError });

    try
    {
        var filtros = new ConsultaFiltros
        {
            Canal          = canal,
            AsesorAsignado = asesorAsignado,
            FechaDesde     = fd,
            FechaHasta     = fh
        };

        var data = await repo.ListarAsync(filtros);
        return Results.Ok(data);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error al consultar consultas.");
        return Results.Problem(
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
        app.Logger.LogError(ex, "Error al guardar consulta.");
        return Results.Problem(
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
    if (!TryParseFecha(fechaDesde, out var fd, out var errFecha)) return Results.BadRequest(new { error = errFecha });
    if (!TryParseFecha(fechaHasta, out var fh, out errFecha)) return Results.BadRequest(new { error = errFecha });
    if (ValidarRango(fd, fh) is { } rangoError) return Results.BadRequest(new { error = rangoError });

    try
    {
        var filtros = new ConsultaFiltros
        {
            Canal          = canal,
            AsesorAsignado = asesorAsignado,
            FechaDesde     = fd,
            FechaHasta     = fh
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
        app.Logger.LogError(ex, "Error al generar Excel.");
        return Results.Problem(
            title: "Error al generar el archivo Excel.",
            statusCode: StatusCodes.Status500InternalServerError
        );
    }
});

// ── GET /api/consultas/metricas — dashboard aggregates (admin only) ──────────
api.MapGet("/consultas/metricas", async (
    IMetricasRepository repo,
    string?             fechaDesde,
    string?             fechaHasta) =>
{
    if (!TryParseFecha(fechaDesde, out var desde, out var errFecha)) return Results.BadRequest(new { error = errFecha });
    if (!TryParseFecha(fechaHasta, out var hasta, out errFecha)) return Results.BadRequest(new { error = errFecha });
    if (ValidarRango(desde, hasta) is { } rangoError) return Results.BadRequest(new { error = rangoError });

    try
    {
        var data = await repo.ObtenerMetricasAsync(desde, hasta);
        return Results.Ok(data);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error al generar métricas.");
        return Results.Problem(
            title: "Error al generar las métricas.",
            statusCode: StatusCodes.Status500InternalServerError
        );
    }
}).RequireAuthorization("Admin");

app.Run();

public partial class Program { }
