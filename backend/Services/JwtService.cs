using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using AutoLeads.Models;

namespace AutoLeads.Services;

/// <summary>
/// Genera JWT firmados (HS256) para usuarios autenticados.
/// Claims: sub (id), email, rol. Expiración: 8 horas (una jornada laboral).
/// La clave de firma viene de la variable de entorno JWT_SECRET.
/// </summary>
public class JwtService(string secret)
{
    private static readonly TimeSpan Expiracion = TimeSpan.FromHours(8);

    private SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(secret));

    public string GenerarToken(Usuario usuario)
    {
        var credentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, usuario.Email),
            new Claim(ClaimTypes.Name, usuario.Nombre),
            new Claim(ClaimTypes.Role, usuario.Rol),
        };

        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.Add(Expiracion),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
