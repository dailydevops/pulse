-- ============================================================================
-- CommandDeadLetter Table Schema
-- ============================================================================
-- Purpose: Stores commands that failed processing for later inspection, replay, or dismissal.
-- Compatible with: NetEvolve.Pulse.SqlServer (ADO.NET)
--
-- Configuration:
--   Adjust SchemaName and TableName below before executing.
--   This script requires SQLCMD mode:
--     - sqlcmd utility:    sqlcmd -i CommandDeadLetter.sql
--     - SSMS:              Query > SQLCMD Mode (Ctrl+Shift+Q)
--     - Azure Data Studio: Enable SQLCMD in the query toolbar
-- ============================================================================

-- ============================================================================
-- Configuration
-- ============================================================================
:setvar SchemaName "pulse"
:setvar TableName "CommandDeadLetter"

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
        [CommandType] NVARCHAR(500) NOT NULL,
        [Payload] NVARCHAR(MAX) NOT NULL,
        [ExceptionType] NVARCHAR(500) NULL,
        [ExceptionMessage] NVARCHAR(MAX) NULL,
        [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
        [AttemptCount] INT NOT NULL CONSTRAINT [DF_$(TableName)_AttemptCount] DEFAULT (1),
        [Status] SMALLINT NOT NULL CONSTRAINT [DF_$(TableName)_Status] DEFAULT (0),
        CONSTRAINT [PK_$(TableName)] PRIMARY KEY CLUSTERED ([Id])
    );

    -- Index for efficient filtering of pending entries (Status = New)
    CREATE NONCLUSTERED INDEX [IX_$(TableName)_Status]
        ON [$(SchemaName)].[$(TableName)] ([Status]);

    -- Index for ordering entries by occurrence time (oldest first)
    CREATE NONCLUSTERED INDEX [IX_$(TableName)_OccurredAt]
        ON [$(SchemaName)].[$(TableName)] ([OccurredAt]);
END
GO
