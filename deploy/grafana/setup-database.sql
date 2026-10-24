-- Grafana's read-only login on the server's SQL Server: db_datareader on Weather (the Weather collector's minute
-- summaries; created by deploy/weather/setup-database.sh, which must have run first).
-- Run by setup-database.sh as sa, which sets the login's password first (:setvar Password, generated on the host).
-- Safe to re-run: it changes the password (setup-database.sh writes it to Grafana's settings).
SET NOCOUNT ON;
IF DB_ID(N'Weather') IS NULL
    THROW 50000, 'The Weather database does not exist yet: run deploy/weather/setup-database.sh first.', 1;
GO
IF SUSER_ID(N'grafana') IS NULL
    CREATE LOGIN [grafana] WITH PASSWORD = N'$(Password)', DEFAULT_DATABASE = [Weather],
        CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
ELSE
    ALTER LOGIN [grafana] WITH PASSWORD = N'$(Password)';
GO
USE [Weather];
IF USER_ID(N'grafana') IS NULL
    CREATE USER [grafana] FOR LOGIN [grafana];
ELSE
    ALTER USER [grafana] WITH LOGIN = [grafana];
ALTER ROLE [db_datareader] ADD MEMBER [grafana];
GO
PRINT 'Login grafana is set, and can read the database Weather.';
