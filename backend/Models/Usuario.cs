namespace AutoLeads.Models;

/// <summary>
/// Entidad de usuario del sistema. Mapea 1:1 a la tabla `usuarios`.
/// El borrado es lógico (`activo`), mismo patrón que modelos/vendedores.
/// </summary>
public class Usuario
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
}

/// <summary>
/// Body del POST /api/auth/login.
/// </summary>
public record LoginRequest(string Email, string Password);

/// <summary>
/// Usuario sin el hash de contraseña (lo que expone la API al listar).
/// </summary>
public class UsuarioDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
}

/// <summary>
/// Body del POST /api/usuarios.
/// </summary>
public record CreateUsuarioRequest(string Nombre, string Email, string Password, string Rol);

/// <summary>
/// Body del PUT /api/usuarios/{id}.
/// </summary>
public record UpdateUsuarioRequest(string Nombre, string Email, string Rol, bool Activo);

/// <summary>
/// Body del PUT /api/usuarios/{id}/password.
/// </summary>
public record UpdatePasswordRequest(string Password);
