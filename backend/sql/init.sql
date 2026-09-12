-- AutoLeads CRM — Database Schema & Initial Seed
-- This script runs once when PostgreSQL container is first initialized

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

-- Master Data Tables (with Soft Delete / Borrado Lógico)
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

-- Index for common filter columns
CREATE INDEX IF NOT EXISTS idx_consultas_canal          ON consultas(canal);
CREATE INDEX IF NOT EXISTS idx_consultas_asesor         ON consultas(asesor_asignado);
CREATE INDEX IF NOT EXISTS idx_consultas_fecha          ON consultas(fecha DESC);

-- Master Data Initial Seeds
INSERT INTO modelos (nombre) VALUES
    ('JOLION H.SUPREME'), ('JOLION PRO'), ('H6'), ('H6 Pro Hev'),
    ('C31 BOX'), ('ORA 03'), ('ORA Funky Cat'),
    ('TANK 300'), ('TANK 500'), ('Dargo X')
ON CONFLICT (nombre) DO NOTHING;

INSERT INTO vendedores (nombre) VALUES
    ('Diego'), ('Marcos'), ('Laura'), ('Carlos'), ('Ana'), ('Martín')
ON CONFLICT (nombre) DO NOTHING;

-- Seed data (Realistic Argentine leads sample)
INSERT INTO consultas (fecha, canal, modelo, nombre_cliente, telefono, ciudad, asesor_asignado, observaciones)
VALUES
  (NOW() - INTERVAL '2 hours',   'Instagram',    'JOLION H.SUPREME', 'Fabián Davalle',   '341-5061333',  'Rosario',       'Diego',  'Consulta por financiación 100% en cuotas fijas'),
  (NOW() - INTERVAL '5 hours',   'WhatsApp',     'C31 BOX',          'Cristian López',   '341-6588166',  'Rosario',       'Marcos', 'Interesado en entregar utilitario usado en parte de pago'),
  (NOW() - INTERVAL '12 hours',  'Mercado Libre','ORA 03',           'Rebeca Fernández', '341-7052360',  'Córdoba',       'Diego',  'Solicita prueba de manejo para el fin de semana'),
  (NOW() - INTERVAL '1 day',     'WhatsApp',     'H6 Pro Hev',       'Lisandro García',  '3462-636380',  'Venado Tuerto', 'Diego',  'Solicita cotización formal con factura A'),
  (NOW() - INTERVAL '2 days',    'Facebook',     'TANK 300',         'María Sánchez',    '351-4561234',  'Córdoba',       'Laura',  'Pregunta por disponibilidad de entrega inmediata en color negro'),
  (NOW() - INTERVAL '3 days',    'Web',          'H6',               'Roberto Giménez',  '011-45678901', 'Buenos Aires',  'Carlos', 'Solicitud de contacto por formulario web oficial'),
  (NOW() - INTERVAL '4 days',    'Llamado',      'TANK 500',         'Valentina Torres', '342-4987654',  'Santa Fe',      'Ana',    'Interesada en plan de pago 60 cuotas ajustables'),
  (NOW() - INTERVAL '5 days',    'Referido',     'JOLION PRO',       'Héctor Morales',   '3462-111222',  'Venado Tuerto', 'Martín', 'Cliente referido por Diego Ríos (comprador previo)'),
  (NOW() - INTERVAL '6 days',    'Presencial',   'Dargo X',          'Gonzalo Benítez',  '3492-543210',  'Rafaela',       'Laura',  'Visitó el showroom, interesado en test drive'),
  (NOW() - INTERVAL '7 days',    'Instagram',    'ORA Funky Cat',    'Camila Rodríguez', '341-8765432',  'San Lorenzo',   'Ana',    'Consulta por colores disponibles y garantía oficial'),
  (NOW() - INTERVAL '8 days',    'Instagram',    'JOLION PRO',       'Sofía Martínez',   '341-6112233',  'Rosario',       'Diego',  'Consulta por colores y stock disponible'),
  (NOW() - INTERVAL '9 days',    'WhatsApp',     'H6',               'Nicolás Ferreyra', '341-5556677',  'Rosario',       'Marcos', 'Pidió cotización de patentamiento incluido'),
  (NOW() - INTERVAL '10 days',   'Facebook',     'TANK 300',         'Lucía Romero',     '342-4445566',  'Santa Fe',      'Laura',  'Compara con Toyota SW4, pide test drive'),
  (NOW() - INTERVAL '11 days',   'Mercado Libre','ORA 03',           'Diego Sosa',       '011-66778899', 'Buenos Aires',  'Carlos', 'Pregunta por entrega inmediata'),
  (NOW() - INTERVAL '12 days',   'Web',          'JOLION H.SUPREME', 'Mariela Acosta',   '3462-333444',  'Venado Tuerto', 'Martín', 'Completó el formulario web oficial'),
  (NOW() - INTERVAL '14 days',   'Llamado',      'C31 BOX',          'Federico Bianchi', '341-2223334',  'Rosario',       'Ana',    'Llamó para consultar plan de financiación'),
  (NOW() - INTERVAL '15 days',   'Presencial',   'Dargo X',          'Carolina Núñez',   '3492-777888',  'Rafaela',       'Diego',  'Visitó el showroom y dejó sus datos'),
  (NOW() - INTERVAL '17 days',   'Referido',     'TANK 500',         'Martín Paredes',   '342-9990001',  'Santa Fe',      'Laura',  'Referido por un cliente que ya compró'),
  (NOW() - INTERVAL '18 days',   'Instagram',    'ORA Funky Cat',    'Julieta Vega',     '341-8889990',  'San Lorenzo',   'Marcos', 'Consulta por garantía oficial y service'),
  (NOW() - INTERVAL '20 days',   'WhatsApp',     'H6 Pro Hev',       'Alejandro Ruiz',   '3462-555666',  'Venado Tuerto', 'Carlos', 'Interesado en plan de ahorro 84 cuotas'),
  (NOW() - INTERVAL '22 days',   'Facebook',     'H6',               'Bárbara Ledesma',  '011-33344455', 'Buenos Aires',  'Ana',    'Pregunta por diferencias entre versiones'),
  (NOW() - INTERVAL '24 days',   'Web',          'JOLION PRO',       'Tomás Ibarra',     '341-7778889',  'Rosario',       'Diego',  'Solicita información por email'),
  (NOW() - INTERVAL '26 days',   'Mercado Libre','TANK 300',         'Rocío Molina',     '351-6667778',  'Córdoba',       'Laura',  'Consulta disponibilidad y tiempos de entrega'),
  (NOW() - INTERVAL '28 days',   'Llamado',      'ORA 03',           'Sebastián Cabrera','342-1231234',  'Santa Fe',      'Martín', 'Llamó por promoción de fin de mes'),
  (NOW() - INTERVAL '30 days',   'Instagram',    'JOLION H.SUPREME', 'Florencia Paz',    '341-9090909',  'Rosario',       'Marcos', 'Interesada en SUV compacta automática');
