-- The Weather collector's summaries database, login and database user, on the server's SQL Server.
-- Run by setup-database.sh as sa, which sets the login's password first (:setvar Password, generated on the host).
-- Safe to re-run: it changes the login's password (setup-database.sh writes the new connection string).
-- The database holds derived data only (minute summaries for Grafana): it needs no backup, and dropping it makes the
-- collector rebuild it from the archive.
SET NOCOUNT ON;
IF DB_ID(N'Weather') IS NULL
BEGIN
    -- The collector creates its tables on its next start, and fills them from the archive.
    CREATE DATABASE [Weather];
    ALTER DATABASE [Weather] SET RECOVERY SIMPLE; -- rebuildable, so no log backups
    PRINT 'Created the database Weather (empty).';
END
GO
IF SUSER_ID(N'weathercollector') IS NULL
    CREATE LOGIN [weathercollector] WITH PASSWORD = N'$(Password)', DEFAULT_DATABASE = [Weather],
        CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
ELSE
    ALTER LOGIN [weathercollector] WITH PASSWORD = N'$(Password)';
GO
USE [Weather];
IF USER_ID(N'weathercollector') IS NULL
    CREATE USER [weathercollector] FOR LOGIN [weathercollector];
ELSE
    ALTER USER [weathercollector] WITH LOGIN = [weathercollector];
ALTER ROLE [db_owner] ADD MEMBER [weathercollector];
GO
PRINT 'Login weathercollector is set, and owns the database Weather.';
