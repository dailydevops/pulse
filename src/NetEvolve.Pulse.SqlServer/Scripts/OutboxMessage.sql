-- ============================================================================
-- OutboxMessage Table Schema
-- ============================================================================
-- Purpose: Stores events for reliable delivery using the outbox pattern.
-- Compatible with: NetEvolve.Pulse.SqlServer (ADO.NET)
--                  NetEvolve.Pulse.EntityFramework (EF Core)
--
-- Usage: Replace [pulse] with your desired schema name if different.
--        Execute this script to create the outbox infrastructure.
-- ============================================================================

-- Create schema if it doesn't exist
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE [name] = N'pulse')
BEGIN
    EXEC('CREATE SCHEMA [pulse]');
END
GO

-- Create OutboxMessage table
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[pulse].[OutboxMessage]') AND [type] = N'U')
BEGIN
    CREATE TABLE [pulse].[OutboxMessage]
    (
        [Id] UNIQUEIDENTIFIER NOT NULL,
        [EventType] NVARCHAR(500) NOT NULL,
        [Payload] NVARCHAR(MAX) NOT NULL,
        [CorrelationId] NVARCHAR(100) NULL,
        [CreatedAt] DATETIMEOFFSET NOT NULL,
        [UpdatedAt] DATETIMEOFFSET NOT NULL,
        [ProcessedAt] DATETIMEOFFSET NULL,
        [RetryCount] INT NOT NULL CONSTRAINT [DF_OutboxMessage_RetryCount] DEFAULT (0),
        [Error] NVARCHAR(MAX) NULL,
        [Status] INT NOT NULL CONSTRAINT [DF_OutboxMessage_Status] DEFAULT (0),
        CONSTRAINT [PK_OutboxMessage] PRIMARY KEY CLUSTERED ([Id])
    );

    -- Index for efficient polling of pending messages
    CREATE NONCLUSTERED INDEX [IX_OutboxMessage_Status_CreatedAt]
        ON [pulse].[OutboxMessage] ([Status], [CreatedAt])
        INCLUDE ([EventType], [Payload], [CorrelationId], [RetryCount])
        WHERE [Status] IN (0, 3); -- Pending and Failed

    -- Index for cleanup of completed messages
    CREATE NONCLUSTERED INDEX [IX_OutboxMessage_Status_ProcessedAt]
        ON [pulse].[OutboxMessage] ([Status], [ProcessedAt])
        WHERE [Status] = 2; -- Completed
END
GO

-- ============================================================================
-- Stored Procedures
-- ============================================================================

-- usp_GetPendingOutboxMessages: Retrieves and locks pending messages for processing
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[pulse].[usp_GetPendingOutboxMessages]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [pulse].[usp_GetPendingOutboxMessages];
END
GO

CREATE PROCEDURE [pulse].[usp_GetPendingOutboxMessages]
    @batchSize INT
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
            [RetryCount],
            [Error],
            [Status]
        FROM [pulse].[OutboxMessage] WITH (ROWLOCK, READPAST)
        WHERE [Status] = 0 -- Pending
        ORDER BY [CreatedAt]
    )
    UPDATE CTE
    SET
        [Status] = 1, -- Processing
        [UpdatedAt] = SYSDATETIMEOFFSET()
    OUTPUT
        INSERTED.[Id],
        INSERTED.[EventType],
        INSERTED.[Payload],
        INSERTED.[CorrelationId],
        INSERTED.[CreatedAt],
        INSERTED.[UpdatedAt],
        INSERTED.[ProcessedAt],
        INSERTED.[RetryCount],
        INSERTED.[Error],
        INSERTED.[Status];
END
GO

-- usp_GetFailedOutboxMessagesForRetry: Retrieves failed messages eligible for retry
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[pulse].[usp_GetFailedOutboxMessagesForRetry]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [pulse].[usp_GetFailedOutboxMessagesForRetry];
END
GO

CREATE PROCEDURE [pulse].[usp_GetFailedOutboxMessagesForRetry]
    @maxRetryCount INT,
    @batchSize INT
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
            [RetryCount],
            [Error],
            [Status]
        FROM [pulse].[OutboxMessage] WITH (ROWLOCK, READPAST)
        WHERE [Status] = 3 -- Failed
          AND [RetryCount] < @maxRetryCount
        ORDER BY [UpdatedAt]
    )
    UPDATE CTE
    SET
        [Status] = 1, -- Processing
        [UpdatedAt] = SYSDATETIMEOFFSET()
    OUTPUT
        INSERTED.[Id],
        INSERTED.[EventType],
        INSERTED.[Payload],
        INSERTED.[CorrelationId],
        INSERTED.[CreatedAt],
        INSERTED.[UpdatedAt],
        INSERTED.[ProcessedAt],
        INSERTED.[RetryCount],
        INSERTED.[Error],
        INSERTED.[Status];
END
GO

-- usp_MarkOutboxMessageCompleted: Marks a message as successfully processed
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[pulse].[usp_MarkOutboxMessageCompleted]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [pulse].[usp_MarkOutboxMessageCompleted];
END
GO

CREATE PROCEDURE [pulse].[usp_MarkOutboxMessageCompleted]
    @messageId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [pulse].[OutboxMessage]
    SET
        [Status] = 2, -- Completed
        [ProcessedAt] = SYSDATETIMEOFFSET(),
        [UpdatedAt] = SYSDATETIMEOFFSET()
    WHERE [Id] = @messageId;
END
GO

-- usp_MarkOutboxMessageFailed: Marks a message as failed with error details
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[pulse].[usp_MarkOutboxMessageFailed]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [pulse].[usp_MarkOutboxMessageFailed];
END
GO

CREATE PROCEDURE [pulse].[usp_MarkOutboxMessageFailed]
    @messageId UNIQUEIDENTIFIER,
    @error NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [pulse].[OutboxMessage]
    SET
        [Status] = 3, -- Failed
        [RetryCount] = [RetryCount] + 1,
        [Error] = @error,
        [UpdatedAt] = SYSDATETIMEOFFSET()
    WHERE [Id] = @messageId;
END
GO

-- usp_MarkOutboxMessageDeadLetter: Moves a message to dead letter status
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[pulse].[usp_MarkOutboxMessageDeadLetter]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [pulse].[usp_MarkOutboxMessageDeadLetter];
END
GO

CREATE PROCEDURE [pulse].[usp_MarkOutboxMessageDeadLetter]
    @messageId UNIQUEIDENTIFIER,
    @error NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [pulse].[OutboxMessage]
    SET
        [Status] = 4, -- DeadLetter
        [Error] = @error,
        [UpdatedAt] = SYSDATETIMEOFFSET()
    WHERE [Id] = @messageId;
END
GO

-- usp_DeleteCompletedOutboxMessages: Removes old completed messages
IF EXISTS (SELECT 1 FROM sys.objects WHERE [object_id] = OBJECT_ID(N'[pulse].[usp_DeleteCompletedOutboxMessages]') AND [type] = N'P')
BEGIN
    DROP PROCEDURE [pulse].[usp_DeleteCompletedOutboxMessages];
END
GO

CREATE PROCEDURE [pulse].[usp_DeleteCompletedOutboxMessages]
    @olderThanUtc DATETIMEOFFSET
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM [pulse].[OutboxMessage]
    WHERE [Status] = 2 -- Completed
      AND [ProcessedAt] < @olderThanUtc;

    SELECT @@ROWCOUNT AS [DeletedCount];
END
GO
