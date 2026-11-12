-- Utilities' database, login and database user, on the server's SQL Server.
-- Run by setup-database.sh as sa, which sets the login's password first (:setvar Password, generated on the host).
-- Safe to re-run: it changes the login's password (setup-database.sh writes the new connection string) and re-maps
-- the user to the login, e.g. after a database is imported.
SET NOCOUNT ON;
IF DB_ID(N'Utilities') IS NULL
BEGIN
    -- The app creates its tables on first start; the readings come in through its spreadsheet import.
    CREATE DATABASE [Utilities];
    PRINT 'Created the database Utilities (empty).';
END
GO
IF SUSER_ID(N'utilities') IS NULL
    CREATE LOGIN [utilities] WITH PASSWORD = N'$(Password)', DEFAULT_DATABASE = [Utilities],
        CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
ELSE
    ALTER LOGIN [utilities] WITH PASSWORD = N'$(Password)';
GO
USE [Utilities];
IF USER_ID(N'utilities') IS NULL
    CREATE USER [utilities] FOR LOGIN [utilities];
ELSE
    ALTER USER [utilities] WITH LOGIN = [utilities];
ALTER ROLE [db_owner] ADD MEMBER [utilities];
GO
PRINT 'Login utilities is set, and owns the database Utilities.';
