-- ============================================================================
-- AuditEntry Table Schema
-- ============================================================================
-- Purpose: Stores audit trail records for processed requests (commands and,
--          optionally, queries), for later inspection and statistics reporting.
-- Compatible with: NetEvolve.Pulse.SqlServer (ADO.NET)
--
-- Configuration:
--   Adjust SchemaName and TableName below before executing.
--   This script requires SQLCMD mode:
--     - sqlcmd utility:    sqlcmd -i AuditEntry.sql
--     - SSMS:              Query > SQLCMD Mode (Ctrl+Shift+Q)
--     - Azure Data Studio: Enable SQLCMD in the query toolbar
-- ============================================================================

-- ============================================================================
-- Configuration
-- ============================================================================
:setvar SchemaName "pulse"
:setvar TableName "AuditEntry"

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
        [UserId] NVARCHAR(256) NULL,
        [CorrelationId] NVARCHAR(100) NULL,
        [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
        [DurationMs] FLOAT NOT NULL,
        [Result] SMALLINT NOT NULL,
        [Payload] NVARCHAR(MAX) NULL,
        [ExceptionMessage] NVARCHAR(MAX) NULL,
        CONSTRAINT [PK_$(TableName)] PRIMARY KEY CLUSTERED ([Id])
    );

    -- Index for ordering/range-filtering entries by occurrence time (most recent first)
    CREATE NONCLUSTERED INDEX [IX_$(TableName)_OccurredAt]
        ON [$(SchemaName)].[$(TableName)] ([OccurredAt]);

    -- Index for efficient filtering by request type
    CREATE NONCLUSTERED INDEX [IX_$(TableName)_CommandType]
        ON [$(SchemaName)].[$(TableName)] ([CommandType]);
END
GO
