-- Pay & Pensions' database, login and database user, on the server's SQL Server.
-- Run by setup-database.sh as sa, which sets the login's password first (:setvar Password, generated on the host).
-- Safe to re-run: it changes the login's password (setup-database.sh writes the new connection string) and re-maps
-- the user to the login, e.g. after a database is imported.
SET NOCOUNT ON;
IF DB_ID(N'PayAndPensions') IS NULL
BEGIN
    -- The app creates its tables on first start; the payslips come in through its spreadsheet import.
    CREATE DATABASE [PayAndPensions];
    PRINT 'Created the database PayAndPensions (empty).';
END
GO
IF SUSER_ID(N'payandpensions') IS NULL
    CREATE LOGIN [payandpensions] WITH PASSWORD = N'$(Password)', DEFAULT_DATABASE = [PayAndPensions],
        CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
ELSE
    ALTER LOGIN [payandpensions] WITH PASSWORD = N'$(Password)';
GO
USE [PayAndPensions];
IF USER_ID(N'payandpensions') IS NULL
    CREATE USER [payandpensions] FOR LOGIN [payandpensions];
ELSE
    ALTER USER [payandpensions] WITH LOGIN = [payandpensions];
ALTER ROLE [db_owner] ADD MEMBER [payandpensions];
GO
PRINT 'Login payandpensions is set, and owns the database PayAndPensions.';
