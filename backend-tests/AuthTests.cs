using System.IdentityModel.Tokens.Jwt;
using AutoLeads.Models;
using AutoLeads.Services;
using Xunit;

namespace AutoLeads.Tests;

/// <summary>
/// Unit tests for authentication: JWT generation and BCrypt password hashing.
/// These run without any DB.
/// </summary>
public class AuthTests
{
    [Fact]
    public void JwtService_GenerarToken_ContieneClaimsYExpiraEn8Horas()
    {
        var service = new JwtService("clave-de-prueba-con-suficiente-longitud-32");
        var usuario = new Usuario { Id = 7, Email = "a@b.com", Nombre = "Admin", Rol = "admin" };

        var token = service.GenerarToken(usuario);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Equal("7", jwt.Subject);
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "a@b.com");
        Assert.Contains(jwt.Claims, c =>
            c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" && c.Value == "admin");

        // Expiración ~8 horas (con margen).
        Assert.True(jwt.ValidTo > DateTime.UtcNow.AddHours(7));
        Assert.True(jwt.ValidTo < DateTime.UtcNow.AddHours(9));
    }

    [Fact]
    public void Bcrypt_HashYVerify_Funcionan()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("clave-segura");

        Assert.True(BCrypt.Net.BCrypt.Verify("clave-segura", hash));
        Assert.False(BCrypt.Net.BCrypt.Verify("clave-incorrecta", hash));
    }

    [Fact]
    public void Seeds_ContraseñasPorDefecto_CoincidenConLosHashesDeInitSql()
    {
        // Debe mantenerse sincronizado con backend/sql/init.sql.
        const string hashAdmin  = "$2a$11$JLWSSx5c24QLqO2zd/B7muzEzgD23JaNIbgMMoeTzeZF3qI25clc.";
        const string hashAsesor = "$2a$11$OSnjK4mbOTizQrGc5VssAu6VcUpZ0/L85VKez6tF1Pwy7wlsHZMNW";

        Assert.True(BCrypt.Net.BCrypt.Verify("admin123",  hashAdmin));
        Assert.True(BCrypt.Net.BCrypt.Verify("asesor123", hashAsesor));
    }
}
