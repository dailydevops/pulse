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

-- usp_ReserveIdempotencyKey: Atomically inserts an idempotency key or refreshes an expired one.
-- Parameters:
--   @idempotencyKey  Required. The idempotency key to reserve.
--   @createdAt       Required. The creation timestamp stored for a new or refreshed key.
--   @validFrom       Optional. Keys created before this cutoff count as expired and are refreshed;
--                    NULL means keys never expire and an existing key is never modified.
-- Returns: one row with the BIT column [Reserved], 1 when the key was inserted or refreshed,
--          0 when a key that has not expired already exists.
-- Errors:  50000 when @idempotencyKey or @createdAt is NULL; other errors are rethrown unchanged.
-- HOLDLOCK makes the MERGE serializable for the key range, so concurrent reservations cannot both win.
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[$(SchemaName)].[usp_ReserveIdempotencyKey]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [$(SchemaName)].[usp_ReserveIdempotencyKey];
END
GO

CREATE PROCEDURE [$(SchemaName)].[usp_ReserveIdempotencyKey]
    @idempotencyKey NVARCHAR(500),
    @createdAt      DATETIMEOFFSET,
    @validFrom      DATETIMEOFFSET = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @idempotencyKey IS NULL OR @createdAt IS NULL
    BEGIN
        THROW 50000, N'usp_ReserveIdempotencyKey: @idempotencyKey and @createdAt must not be NULL.', 1;
    END

    DECLARE @reserved BIT;

    BEGIN TRY
        MERGE INTO [$(SchemaName)].[$(TableName)] WITH (HOLDLOCK) AS target
        USING (SELECT @idempotencyKey AS [IdempotencyKey], @createdAt AS [CreatedAt]) AS source
        ON (target.[IdempotencyKey] = source.[IdempotencyKey])
        WHEN MATCHED AND @validFrom IS NOT NULL AND target.[CreatedAt] < @validFrom THEN
            UPDATE SET [CreatedAt] = source.[CreatedAt]
        WHEN NOT MATCHED THEN
            INSERT ([IdempotencyKey], [CreatedAt])
            VALUES (source.[IdempotencyKey], source.[CreatedAt]);

        SET @reserved = CASE WHEN @@ROWCOUNT > 0 THEN 1 ELSE 0 END;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH

    SELECT @reserved AS [Reserved];
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
