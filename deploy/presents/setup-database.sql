-- Presents' database, login and database user, on the server's SQL Server.
-- Run by setup-database.sh as sa, which sets the login's password first (:setvar Password, generated on the host).
-- Safe to re-run: it changes the login's password (setup-database.sh writes the new connection string) and re-maps
-- the user to the login, e.g. after a database is imported.
SET NOCOUNT ON;
IF DB_ID(N'Presents') IS NULL
BEGIN
    -- The app creates its tables on first start; the presents come in through its spreadsheet import.
    CREATE DATABASE [Presents];
    PRINT 'Created the database Presents (empty).';
END
GO
IF SUSER_ID(N'presents') IS NULL
    CREATE LOGIN [presents] WITH PASSWORD = N'$(Password)', DEFAULT_DATABASE = [Presents],
        CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
ELSE
    ALTER LOGIN [presents] WITH PASSWORD = N'$(Password)';
GO
USE [Presents];
IF USER_ID(N'presents') IS NULL
    CREATE USER [presents] FOR LOGIN [presents];
ELSE
    ALTER USER [presents] WITH LOGIN = [presents];
ALTER ROLE [db_owner] ADD MEMBER [presents];
GO
PRINT 'Login presents is set, and owns the database Presents.';
