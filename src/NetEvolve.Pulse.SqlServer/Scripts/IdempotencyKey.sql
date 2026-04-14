-- ============================================================================
-- IdempotencyKey Table Schema
-- ============================================================================
-- Purpose: Stores idempotency keys for at-most-once command processing.
-- Compatible with: NetEvolve.Pulse.SqlServer (ADO.NET)
--
-- Configuration:
--   Adjust SchemaName and TableName below before executing.
--   This script requires SQLCMD mode:
--     - sqlcmd utility:    sqlcmd -i IdempotencyKey.sql
--     - SSMS:              Query > SQLCMD Mode (Ctrl+Shift+Q)
--     - Azure Data Studio: Enable SQLCMD in the query toolbar
-- ============================================================================

-- ============================================================================
-- Configuration
-- ============================================================================
:setvar SchemaName "pulse"
:setvar TableName "IdempotencyKey"

-- Create schema if it doesn't exist
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE [name] = N'$(SchemaName)')
BEGIN
    EXEC('CREATE SCHEMA [$(SchemaName)]');
END
GO

-- Create table if it doesn't exist
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[$(SchemaName)].[$(TableName)]') AND [type] = N'U')
BEGIN
    CREATE TABLE [$(SchemaName)].[$(TableName)]
    (
        [IdempotencyKey] NVARCHAR(500) NOT NULL,
        [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
        CONSTRAINT [PK_$(TableName)] PRIMARY KEY CLUSTERED ([IdempotencyKey])
    );

    -- Index for TTL-based queries (efficient filtering by CreatedAt)
    CREATE NONCLUSTERED INDEX [IX_$(TableName)_CreatedAt]
        ON [$(SchemaName)].[$(TableName)] ([CreatedAt]);
END
GO

-- ============================================================================
-- Stored Procedures
-- ============================================================================

-- usp_ExistsIdempotencyKey: Checks if an idempotency key exists and is still valid
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[$(SchemaName)].[usp_ExistsIdempotencyKey]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [$(SchemaName)].[usp_ExistsIdempotencyKey];
END
GO

CREATE PROCEDURE [$(SchemaName)].[usp_ExistsIdempotencyKey]
    @idempotencyKey NVARCHAR(500),
    @validFrom      DATETIMEOFFSET = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @validFrom IS NULL
    BEGIN
        -- No TTL filtering: check if key exists regardless of age
        SELECT CASE WHEN EXISTS (
            SELECT 1
            FROM [$(SchemaName)].[$(TableName)]
            WHERE [IdempotencyKey] = @idempotencyKey
        ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS [Exists];
    END
    ELSE
    BEGIN
        -- TTL filtering: only return true if key exists and is not expired
        SELECT CASE WHEN EXISTS (
            SELECT 1
            FROM [$(SchemaName)].[$(TableName)]
            WHERE [IdempotencyKey] = @idempotencyKey
              AND [CreatedAt] >= @validFrom
        ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS [Exists];
    END
END
GO

-- usp_InsertIdempotencyKey: Inserts an idempotency key (idempotent operation)
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[$(SchemaName)].[usp_InsertIdempotencyKey]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [$(SchemaName)].[usp_InsertIdempotencyKey];
END
GO

CREATE PROCEDURE [$(SchemaName)].[usp_InsertIdempotencyKey]
    @idempotencyKey NVARCHAR(500),
    @createdAt      DATETIMEOFFSET
AS
BEGIN
    SET NOCOUNT ON;

    -- Use MERGE to handle duplicate key gracefully (idempotent operation)
    MERGE INTO [$(SchemaName)].[$(TableName)] AS target
    USING (SELECT @idempotencyKey AS [IdempotencyKey], @createdAt AS [CreatedAt]) AS source
    ON (target.[IdempotencyKey] = source.[IdempotencyKey])
    WHEN NOT MATCHED THEN
        INSERT ([IdempotencyKey], [CreatedAt])
        VALUES (source.[IdempotencyKey], source.[CreatedAt]);
END
GO

-- usp_DeleteExpiredIdempotencyKeys: Removes expired idempotency keys (cleanup maintenance)
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[$(SchemaName)].[usp_DeleteExpiredIdempotencyKeys]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [$(SchemaName)].[usp_DeleteExpiredIdempotencyKeys];
END
GO

CREATE PROCEDURE [$(SchemaName)].[usp_DeleteExpiredIdempotencyKeys]
    @validFrom DATETIMEOFFSET
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM [$(SchemaName)].[$(TableName)]
    WHERE [CreatedAt] < @validFrom;

    -- Return the number of deleted rows
    SELECT @@ROWCOUNT AS [DeletedCount];
END
GO
