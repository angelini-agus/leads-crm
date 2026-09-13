using System.Security.Cryptography;
using AutoLeads.Data;
using AutoLeads.Models;

namespace AutoLeads.Services;

/// <summary>
/// Crea el primer usuario admin si no existe ninguno activo. Evita sembrar
/// contraseñas publicadas en el repositorio: si no se inyecta
/// SEED_ADMIN_PASSWORD, se genera una temporal aleatoria que se loguea una
/// única vez para que el operador la cambie al ingresar.
/// </summary>
public static class AdminBootstrapper
{
    private const string Charset = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%&*";

    public static string GenerarPasswordAleatoria(int length = 24)
    {
        if (length < 8) throw new ArgumentOutOfRangeException(nameof(length));

        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Charset[RandomNumberGenerator.GetInt32(Charset.Length)];
        }
        return new string(chars);
    }

    public static async Task EnsureSeedAdminAsync(IUsuarioRepository repo, ILogger logger)
    {
        if (await repo.ContarAdminsActivosAsync() > 0) return;

        var email = (Environment.GetEnvironmentVariable("SEED_ADMIN_EMAIL") ?? "admin@autoleads.com")
            .Trim().ToLowerInvariant();

        var password = Environment.GetEnvironmentVariable("SEED_ADMIN_PASSWORD");
        var generada = string.IsNullOrWhiteSpace(password);
        if (generada) password = GenerarPasswordAleatoria();

        var admin = new Usuario
        {
            Nombre       = "Administrador",
            Email        = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Rol          = "admin",
            Activo       = true
        };

        await repo.CrearAsync(admin);

        if (generada)
        {
            logger.LogWarning(
                "No había administradores activos. Se creó {Email} con una contraseña temporal: {Password}. " +
                "Cambiala después de ingresar (o seteá SEED_ADMIN_PASSWORD antes del primer arranque).",
                email, password);
        }
        else
        {
            logger.LogInformation("No había administradores activos. Se creó {Email} con SEED_ADMIN_PASSWORD.", email);
        }
    }
}
