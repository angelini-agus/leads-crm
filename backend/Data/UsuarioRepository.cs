using Dapper;
using Npgsql;
using AutoLeads.Models;

namespace AutoLeads.Data;

public interface IUsuarioRepository
{
    Task<Usuario?> ObtenerPorEmailAsync(string email);
    Task<IEnumerable<UsuarioDto>> ListarAsync();
    Task<int> CrearAsync(Usuario usuario);
    Task<bool> ActualizarAsync(int id, string nombre, string email, string rol, bool activo);
    Task<bool> ActualizarPasswordAsync(int id, string passwordHash);
    Task<bool> AlternarEstadoAsync(int id, bool activo);
}

/// <summary>
/// Dapper-based PostgreSQL repository for usuarios.
/// Borrado lógico via `activo` (mismo patrón que modelos/vendedores).
/// Nunca expone el password_hash en las consultas de listado.
/// </summary>
public class UsuarioRepository(string connectionString) : IUsuarioRepository
{
    private NpgsqlConnection CreateConnection() => new(connectionString);

    public async Task<Usuario?> ObtenerPorEmailAsync(string email)
    {
        const string sql = """
            SELECT id,
                   nombre,
                   email,
                   password_hash AS PasswordHash,
                   rol,
                   activo
            FROM usuarios
            WHERE lower(email) = lower(@Email)
              AND activo = TRUE
            LIMIT 1;
            """;

        await using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Usuario>(sql, new { Email = email.Trim() });
    }

    public async Task<IEnumerable<UsuarioDto>> ListarAsync()
    {
        const string sql = """
            SELECT id, nombre, email, rol, activo
            FROM usuarios
            ORDER BY id;
            """;

        await using var conn = CreateConnection();
        return await conn.QueryAsync<UsuarioDto>(sql);
    }

    public async Task<int> CrearAsync(Usuario usuario)
    {
        const string sql = """
            INSERT INTO usuarios (nombre, email, password_hash, rol)
            VALUES (@Nombre, @Email, @PasswordHash, @Rol)
            RETURNING id;
            """;

        await using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<int>(sql, usuario);
    }

    public async Task<bool> ActualizarAsync(int id, string nombre, string email, string rol, bool activo)
    {
        const string sql = """
            UPDATE usuarios
            SET nombre = @Nombre, email = @Email, rol = @Rol, activo = @Activo
            WHERE id = @Id;
            """;

        await using var conn = CreateConnection();
        var affected = await conn.ExecuteAsync(sql, new { Id = id, Nombre = nombre, Email = email, Rol = rol, Activo = activo });
        return affected > 0;
    }

    public async Task<bool> ActualizarPasswordAsync(int id, string passwordHash)
    {
        const string sql = """
            UPDATE usuarios
            SET password_hash = @PasswordHash
            WHERE id = @Id;
            """;

        await using var conn = CreateConnection();
        var affected = await conn.ExecuteAsync(sql, new { Id = id, PasswordHash = passwordHash });
        return affected > 0;
    }

    public async Task<bool> AlternarEstadoAsync(int id, bool activo)
    {
        const string sql = """
            UPDATE usuarios
            SET activo = @Activo
            WHERE id = @Id;
            """;

        await using var conn = CreateConnection();
        var affected = await conn.ExecuteAsync(sql, new { Id = id, Activo = activo });
        return affected > 0;
    }
}
