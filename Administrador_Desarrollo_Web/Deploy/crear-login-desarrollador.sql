/* ============================================================================
   Login restringido para el build de desarrollador
   ----------------------------------------------------------------------------
   Ejecutar CONECTADO A LA BASE DE LA APLICACIÓN (no a master), con una cuenta
   administradora. Crea un usuario contenido: no necesita login de servidor y
   solo existe dentro de esta base.

   Por qué no reutilizar la cuenta del administrador: la contraseña viaja
   incrustada en el .exe que se reparte al equipo y es recuperable. Este usuario
   acota el daño — puede leer y escribir datos, pero no alterar el esquema.
   ============================================================================ */

-- 1) Cambia esta contraseña antes de ejecutar. Mínimo 16 caracteres.
DECLARE @password nvarchar(128) = N'CAMBIA-ESTA-CONTRASENA-2026!';

DECLARE @sql nvarchar(max) = N'CREATE USER [app_dev] WITH PASSWORD = ' + QUOTENAME(@password, '''') + N';';

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'app_dev')
    EXEC sp_executesql @sql;
ELSE
    PRINT 'El usuario app_dev ya existe; se omite la creación (usa ALTER USER para cambiar la contraseña).';
GO

-- 2) Permisos de datos, nada de esquema.
--    Deliberadamente NO se otorga db_ddladmin ni db_owner: la app de desarrollador
--    no ejecuta migraciones (ver Program.cs, que en DEV_BUILD solo verifica conexión).
ALTER ROLE db_datareader ADD MEMBER [app_dev];
ALTER ROLE db_datawriter ADD MEMBER [app_dev];
GO

-- 3) Cerrar lo más sensible que db_datareader abriría de par en par.
--    DeploymentTargets guarda credenciales FTP de los servidores de despliegue: fuera por completo.
DENY SELECT, INSERT, UPDATE, DELETE ON [dbo].[DeploymentTargets] TO [app_dev];

--    AppSettings sí se puede LEER, pero no modificar. La aplicación necesita leer de ahí datos que
--    no son secretos (URL de organización y proyecto de Azure DevOps, servidores de correo) para
--    que el desarrollador pueda comentar tickets. Los valores marcados como secretos —PAT de la
--    instalación, API key de Freshdesk, contraseña de correo— se guardan cifrados con DPAPI en el
--    ámbito del USUARIO DE WINDOWS que los capturó, así que otra persona solo obtiene texto
--    cifrado que no puede descifrar. Lo que sí hay que impedir es que los cambie.
DENY INSERT, UPDATE, DELETE ON [dbo].[AppSettings] TO [app_dev];
GO

-- 4) Comprobación.
SELECT
    p.name                                   AS usuario,
    p.type_desc                              AS tipo,
    STRING_AGG(r.name, ', ')                 AS roles
FROM sys.database_principals p
LEFT JOIN sys.database_role_members m ON m.member_principal_id = p.principal_id
LEFT JOIN sys.database_principals   r ON r.principal_id        = m.role_principal_id
WHERE p.name = 'app_dev'
GROUP BY p.name, p.type_desc;
GO

/* ----------------------------------------------------------------------------
   SOBRE EL PAT DE AZURE DEVOPS:

   El token de cada desarrollador NO se guarda aquí. Es personal y vive cifrado con
   DPAPI en su propia computadora (%APPDATA%\AdministradorDesarrolloWeb\
   devops-personal.json), porque los comentarios que la aplicación publica en los
   work items quedan firmados en DevOps con el dueño del token: con uno compartido,
   el SLA no probaría quién atendió. Cada quien lo captura desde «Mis SLA → Mi PAT».
   ---------------------------------------------------------------------------- */

/* ----------------------------------------------------------------------------
   RIESGO QUE NO SE ELIMINA, y conviene tener presente:

   La aplicación es un cliente pesado que habla directo con la base y verifica la
   contraseña del lado del cliente, por lo que app_dev NECESITA leer Users,
   incluida la columna PasswordHash. Son hashes bcrypt (no reversibles), pero un
   desarrollador con el ejecutable puede extraerlos e intentar romperlos sin
   conexión. Mitigaciones reales, si algún día importa:
     · exigir contraseñas largas a los usuarios de la app;
     · mover la autenticación a un servicio/API que la base no exponga;
     · usar Microsoft Entra ID por usuario en vez de un login SQL compartido.
   ---------------------------------------------------------------------------- */

-- Cadena a incrustar (sustituye la contraseña real):
--   server = TU-SERVIDOR.database.windows.net; uid = app_dev; pwd = ...; database = TU_BASE
--
-- Luego:
--   .\build-dev-app.ps1 -ConnectionString "server = ...; uid = app_dev; pwd = ...; database = ..."
