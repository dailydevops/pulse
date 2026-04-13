-- ============================================================================
-- OutboxMessage Table Schema
-- ============================================================================
-- Purpose: Stores events for reliable delivery using the outbox pattern.
-- Compatible with: NetEvolve.Pulse.SqlServer (ADO.NET)
--                  NetEvolve.Pulse.EntityFramework (EF Core)
--
-- Configuration:
--   Adjust SchemaName and TableName below before executing.
--   This script requires SQLCMD mode:
--     - sqlcmd utility:    sqlcmd -i OutboxMessage.sql
--     - SSMS:              Query > SQLCMD Mode (Ctrl+Shift+Q)
--     - Azure Data Studio: Enable SQLCMD in the query toolbar
-- ============================================================================

-- ============================================================================
-- Configuration
-- ============================================================================
:setvar SchemaName "pulse"
:setvar TableName "OutboxMessage"

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
        [Id] UNIQUEIDENTIFIER NOT NULL,
        [EventType] NVARCHAR(500) NOT NULL,
        [Payload] NVARCHAR(MAX) NOT NULL,
        [CorrelationId] NVARCHAR(100) NULL,
        [CreatedAt] DATETIMEOFFSET NOT NULL,
        [UpdatedAt] DATETIMEOFFSET NOT NULL,
        [ProcessedAt] DATETIMEOFFSET NULL,
        [NextRetryAt] DATETIMEOFFSET NULL,
        [RetryCount] INT NOT NULL CONSTRAINT [DF_$(TableName)_RetryCount] DEFAULT (0),
        [Error] NVARCHAR(MAX) NULL,
        [Status] INT NOT NULL CONSTRAINT [DF_$(TableName)_Status] DEFAULT (0),
        CONSTRAINT [PK_$(TableName)] PRIMARY KEY CLUSTERED ([Id])
    );

    -- Index for efficient polling of pending messages
    CREATE NONCLUSTERED INDEX [IX_$(TableName)_Status_CreatedAt]
        ON [$(SchemaName)].[$(TableName)] ([Status], [CreatedAt])
        INCLUDE ([EventType], [Payload], [CorrelationId], [RetryCount])
        WHERE [Status] IN (0, 3); -- Pending and Failed

    -- Index for cleanup of completed messages
    CREATE NONCLUSTERED INDEX [IX_$(TableName)_Status_ProcessedAt]
        ON [$(SchemaName)].[$(TableName)] ([Status], [ProcessedAt])
        WHERE [Status] = 2; -- Completed
END
GO

-- ============================================================================
-- Stored Procedures
-- ============================================================================

-- usp_GetPendingOutboxMessages: Retrieves and locks pending messages for processing
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[$(SchemaName)].[usp_GetPendingOutboxMessages]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [$(SchemaName)].[usp_GetPendingOutboxMessages];
END
GO

CREATE PROCEDURE [$(SchemaName)].[usp_GetPendingOutboxMessages]
    @batchSize INT,
    @nowUtc DATETIMEOFFSET
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH CTE AS (
        SELECT TOP (@batchSize)
            [Id],
            [EventType],
            [Payload],
            [CorrelationId],
            [CreatedAt],
            [UpdatedAt],
            [ProcessedAt],
            [NextRetryAt],
            [RetryCount],
            [Error],
            [Status]
        FROM [$(SchemaName)].[$(TableName)] WITH (ROWLOCK, READPAST)
        WHERE [Status] = 0 -- Pending
          AND ([NextRetryAt] IS NULL OR [NextRetryAt] <= @nowUtc)
        ORDER BY [CreatedAt]
    )
    UPDATE CTE
    SET
        [Status] = 1, -- Processing
        [UpdatedAt] = @nowUtc
    OUTPUT
        INSERTED.[Id],
        INSERTED.[EventType],
        INSERTED.[Payload],
        INSERTED.[CorrelationId],
        INSERTED.[CreatedAt],
        INSERTED.[UpdatedAt],
        INSERTED.[ProcessedAt],
        INSERTED.[NextRetryAt],
        INSERTED.[RetryCount],
        INSERTED.[Error],
        INSERTED.[Status];
END
GO

-- usp_GetFailedOutboxMessagesForRetry
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[$(SchemaName)].[usp_GetFailedOutboxMessagesForRetry]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [$(SchemaName)].[usp_GetFailedOutboxMessagesForRetry];
END
GO

CREATE PROCEDURE [$(SchemaName)].[usp_GetFailedOutboxMessagesForRetry]
    @maxRetryCount INT,
    @batchSize INT,
    @nowUtc DATETIMEOFFSET
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH CTE AS (
        SELECT TOP (@batchSize)
            [Id],
            [EventType],
            [Payload],
            [CorrelationId],
            [CreatedAt],
            [UpdatedAt],
            [ProcessedAt],
            [NextRetryAt],
            [RetryCount],
            [Error],
            [Status]
        FROM [$(SchemaName)].[$(TableName)] WITH (ROWLOCK, READPAST)
        WHERE [Status] = 3 -- Failed
          AND [RetryCount] < @maxRetryCount
          AND ([NextRetryAt] IS NULL OR [NextRetryAt] <= @nowUtc)
        ORDER BY [UpdatedAt]
    )
    UPDATE CTE
    SET
        [Status] = 1, -- Processing
        [UpdatedAt] = @nowUtc
    OUTPUT
        INSERTED.[Id],
        INSERTED.[EventType],
        INSERTED.[Payload],
        INSERTED.[CorrelationId],
        INSERTED.[CreatedAt],
        INSERTED.[UpdatedAt],
        INSERTED.[ProcessedAt],
        INSERTED.[NextRetryAt],
        INSERTED.[RetryCount],
        INSERTED.[Error],
        INSERTED.[Status];
END
GO

-- usp_MarkOutboxMessageCompleted
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[$(SchemaName)].[usp_MarkOutboxMessageCompleted]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [$(SchemaName)].[usp_MarkOutboxMessageCompleted];
END
GO

CREATE PROCEDURE [$(SchemaName)].[usp_MarkOutboxMessageCompleted]
    @messageId      UNIQUEIDENTIFIER,
    @processedAtUtc DATETIMEOFFSET,
    @updatedAtUtc   DATETIMEOFFSET
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [$(SchemaName)].[$(TableName)]
    SET
        [Status] = 2, -- Completed
        [ProcessedAt] = @processedAtUtc,
        [UpdatedAt] = @updatedAtUtc
    WHERE [Id] = @messageId
      AND [Status] = 1; -- Processing
END
GO

-- usp_MarkOutboxMessageFailed: Marks a message as failed with error details
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[$(SchemaName)].[usp_MarkOutboxMessageFailed]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [$(SchemaName)].[usp_MarkOutboxMessageFailed];
END
GO

CREATE PROCEDURE [$(SchemaName)].[usp_MarkOutboxMessageFailed]
    @messageId   UNIQUEIDENTIFIER,
    @error       NVARCHAR(MAX),
    @nowUtc      DATETIMEOFFSET,
    @nextRetryAt DATETIMEOFFSET = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [$(SchemaName)].[$(TableName)]
    SET
        [Status] = 3, -- Failed
        [RetryCount] = [RetryCount] + 1,
        [Error] = @error,
        [NextRetryAt] = @nextRetryAt,
        [UpdatedAt] = @nowUtc
    WHERE [Id] = @messageId
      AND [Status] = 1; -- Processing
END
GO

-- usp_MarkOutboxMessageDeadLetter: Moves a message to dead letter status
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[$(SchemaName)].[usp_MarkOutboxMessageDeadLetter]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [$(SchemaName)].[usp_MarkOutboxMessageDeadLetter];
END
GO

CREATE PROCEDURE [$(SchemaName)].[usp_MarkOutboxMessageDeadLetter]
    @messageId UNIQUEIDENTIFIER,
    @error     NVARCHAR(MAX),
    @nowUtc    DATETIMEOFFSET
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [$(SchemaName)].[$(TableName)]
    SET
        [Status] = 4, -- DeadLetter
        [Error] = @error,
        [UpdatedAt] = @nowUtc
    WHERE [Id] = @messageId
      AND [Status] = 1; -- Processing
END
GO

-- usp_DeleteCompletedOutboxMessages: Removes old completed messages
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[$(SchemaName)].[usp_DeleteCompletedOutboxMessages]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [$(SchemaName)].[usp_DeleteCompletedOutboxMessages];
END
GO

CREATE PROCEDURE [$(SchemaName)].[usp_DeleteCompletedOutboxMessages]
    @olderThanUtc DATETIMEOFFSET
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM [$(SchemaName)].[$(TableName)]
    WHERE [Status] = 2 -- Completed
      AND [ProcessedAt] < @olderThanUtc;

    SELECT @@ROWCOUNT AS [DeletedCount];
END
GO

-- ============================================================================
-- Management Stored Procedures
-- ============================================================================

-- usp_GetDeadLetterOutboxMessages: Returns a paginated list of dead-letter messages
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[$(SchemaName)].[usp_GetDeadLetterOutboxMessages]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [$(SchemaName)].[usp_GetDeadLetterOutboxMessages];
END
GO

CREATE PROCEDURE [$(SchemaName)].[usp_GetDeadLetterOutboxMessages]
    @pageSize INT,
    @page     INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        [Id],
        [EventType],
        [Payload],
        [CorrelationId],
        [CreatedAt],
        [UpdatedAt],
        [ProcessedAt],
        [RetryCount],
        [Error],
        [Status]
    FROM [$(SchemaName)].[$(TableName)]
    WHERE [Status] = 4 -- DeadLetter
    ORDER BY [UpdatedAt] DESC
    OFFSET (@page * @pageSize) ROWS
    FETCH NEXT @pageSize ROWS ONLY;
END
GO

-- usp_GetDeadLetterOutboxMessage: Returns a single dead-letter message by Id
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[$(SchemaName)].[usp_GetDeadLetterOutboxMessage]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [$(SchemaName)].[usp_GetDeadLetterOutboxMessage];
END
GO

CREATE PROCEDURE [$(SchemaName)].[usp_GetDeadLetterOutboxMessage]
    @messageId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        [Id],
        [EventType],
        [Payload],
        [CorrelationId],
        [CreatedAt],
        [UpdatedAt],
        [ProcessedAt],
        [RetryCount],
        [Error],
        [Status]
    FROM [$(SchemaName)].[$(TableName)]
    WHERE [Id] = @messageId
      AND [Status] = 4; -- DeadLetter
END
GO

-- usp_GetDeadLetterOutboxMessageCount: Returns the count of dead-letter messages
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[$(SchemaName)].[usp_GetDeadLetterOutboxMessageCount]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [$(SchemaName)].[usp_GetDeadLetterOutboxMessageCount];
END
GO

CREATE PROCEDURE [$(SchemaName)].[usp_GetDeadLetterOutboxMessageCount]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COUNT_BIG(1) AS [DeadLetterCount]
    FROM [$(SchemaName)].[$(TableName)]
    WHERE [Status] = 4; -- DeadLetter
END
GO

-- usp_ReplayOutboxMessage: Resets a dead-letter message to Pending for reprocessing
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[$(SchemaName)].[usp_ReplayOutboxMessage]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [$(SchemaName)].[usp_ReplayOutboxMessage];
END
GO

CREATE PROCEDURE [$(SchemaName)].[usp_ReplayOutboxMessage]
    @messageId UNIQUEIDENTIFIER,
    @nowUtc    DATETIMEOFFSET
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [$(SchemaName)].[$(TableName)]
    SET
        [Status]     = 0, -- Pending
        [RetryCount] = 0,
        [Error]      = NULL,
        [NextRetryAt] = NULL,
        [UpdatedAt]  = @nowUtc
    WHERE [Id] = @messageId
      AND [Status] = 4; -- DeadLetter

    SELECT @@ROWCOUNT AS [UpdatedCount];
END
GO

-- usp_ReplayAllDeadLetterOutboxMessages: Resets all dead-letter messages to Pending
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[$(SchemaName)].[usp_ReplayAllDeadLetterOutboxMessages]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [$(SchemaName)].[usp_ReplayAllDeadLetterOutboxMessages];
END
GO

CREATE PROCEDURE [$(SchemaName)].[usp_ReplayAllDeadLetterOutboxMessages]
    @nowUtc DATETIMEOFFSET
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [$(SchemaName)].[$(TableName)]
    SET
        [Status]     = 0, -- Pending
        [RetryCount] = 0,
        [Error]      = NULL,
        [NextRetryAt] = NULL,
        [UpdatedAt]  = @nowUtc
    WHERE [Status] = 4; -- DeadLetter

    SELECT @@ROWCOUNT AS [UpdatedCount];
END
GO

-- usp_GetOutboxStatistics: Returns message counts grouped by status
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[$(SchemaName)].[usp_GetOutboxStatistics]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [$(SchemaName)].[usp_GetOutboxStatistics];
END
GO

CREATE PROCEDURE [$(SchemaName)].[usp_GetOutboxStatistics]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        COALESCE(SUM(CASE WHEN [Status] = 0 THEN CAST(1 AS BIGINT) ELSE 0 END), CAST(0 AS BIGINT)) AS [Pending],
        COALESCE(SUM(CASE WHEN [Status] = 1 THEN CAST(1 AS BIGINT) ELSE 0 END), CAST(0 AS BIGINT)) AS [Processing],
        COALESCE(SUM(CASE WHEN [Status] = 2 THEN CAST(1 AS BIGINT) ELSE 0 END), CAST(0 AS BIGINT)) AS [Completed],
        COALESCE(SUM(CASE WHEN [Status] = 3 THEN CAST(1 AS BIGINT) ELSE 0 END), CAST(0 AS BIGINT)) AS [Failed],
        COALESCE(SUM(CASE WHEN [Status] = 4 THEN CAST(1 AS BIGINT) ELSE 0 END), CAST(0 AS BIGINT)) AS [DeadLetter]
    FROM [$(SchemaName)].[$(TableName)];
END
GO
