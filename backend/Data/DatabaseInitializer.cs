using Npgsql;

namespace AutoLeads.Data;

/// <summary>
/// Migración idempotente del schema. Se ejecuta al arrancar la API para que
/// un volumen de PostgreSQL ya inicializado (donde init.sql no vuelve a correr)
/// reciba igual las tablas e índices nuevos. Todo el DDL es IF NOT EXISTS, así
/// que es seguro correrlo en cada boot.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task EnsureSchemaAsync(string connectionString)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS consultas (
                id               SERIAL PRIMARY KEY,
                fecha            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                canal            VARCHAR(50)  NOT NULL,
                modelo           VARCHAR(100) NOT NULL,
                nombre_cliente   VARCHAR(200) NOT NULL,
                telefono         VARCHAR(30)  NOT NULL,
                ciudad           VARCHAR(100),
                asesor_asignado  VARCHAR(100) NOT NULL,
                observaciones    TEXT
            );

            CREATE TABLE IF NOT EXISTS modelos (
                id     SERIAL PRIMARY KEY,
                nombre VARCHAR(100) UNIQUE NOT NULL,
                activo BOOLEAN NOT NULL DEFAULT TRUE
            );

            CREATE TABLE IF NOT EXISTS vendedores (
                id     SERIAL PRIMARY KEY,
                nombre VARCHAR(100) UNIQUE NOT NULL,
                activo BOOLEAN NOT NULL DEFAULT TRUE
            );

            CREATE TABLE IF NOT EXISTS usuarios (
                id            SERIAL PRIMARY KEY,
                nombre        VARCHAR(200) NOT NULL,
                email         VARCHAR(200) NOT NULL,
                password_hash VARCHAR(255) NOT NULL,
                rol           VARCHAR(20)  NOT NULL CHECK (rol IN ('admin','asesor')),
                activo        BOOLEAN NOT NULL DEFAULT TRUE
            );

            -- Unicidad case-insensitive del email (el login matchea lower(email)).
            CREATE UNIQUE INDEX IF NOT EXISTS ux_usuarios_email_lower ON usuarios (lower(email));

            CREATE INDEX IF NOT EXISTS idx_consultas_canal  ON consultas(canal);
            CREATE INDEX IF NOT EXISTS idx_consultas_asesor ON consultas(asesor_asignado);
            CREATE INDEX IF NOT EXISTS idx_consultas_fecha  ON consultas(fecha DESC);
            """;

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
