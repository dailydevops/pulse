-- ============================================================================
-- 001_CreateOutboxTable.sql
-- ============================================================================
-- Purpose: Creates the OutboxMessage table for the SQLite outbox pattern.
-- Compatible with: NetEvolve.Pulse.SQLite (ADO.NET)
--
-- Configuration:
--   This script uses SQLite CLI .parameter to emulate SQLCMD-style variables.
--   Set values before running (the defaults below are used when not provided):
--     @table_name   (default: OutboxMessage)
--     @journal_mode (default: wal)
--
-- Usage:
--   sqlite3 outbox.db -cmd ".parameter set @table_name OutboxMessage" \
--                     -cmd ".parameter set @journal_mode wal" \
--                     ".read 001_CreateOutboxTable.sql"
-- ============================================================================

.bail on
.mode list
.headers off
.separator ''

.output outbox.schema.tmp.sql
SELECT 'PRAGMA journal_mode=' || lower(coalesce(@journal_mode, 'wal')) || ';';
SELECT 'CREATE TABLE IF NOT EXISTS '
       || printf('"%w"', coalesce(@table_name, 'OutboxMessage'))
       || ' ('
       || '"Id"            TEXT        NOT NULL,'
       || '"EventType"     TEXT        NOT NULL,'
       || '"Payload"       TEXT        NOT NULL,'
       || '"CorrelationId" TEXT        NULL,'
       || '"CausationId"   TEXT        NULL,'
       || '"CreatedAt"     TEXT        NOT NULL,'
       || '"UpdatedAt"     TEXT        NOT NULL,'
       || '"ProcessedAt"   TEXT        NULL,'
       || '"NextRetryAt"   TEXT        NULL,'
       || '"RetryCount"    INTEGER     NOT NULL DEFAULT 0,'
       || '"Error"         TEXT        NULL,'
       || '"Status"        INTEGER     NOT NULL DEFAULT 0,'
       || 'CONSTRAINT '
       || printf('"PK_%w"', coalesce(@table_name, 'OutboxMessage'))
       || ' PRIMARY KEY ("Id")'
       || ');';
SELECT 'CREATE INDEX IF NOT EXISTS '
       || printf('"IX_%w_Status_CreatedAt"', coalesce(@table_name, 'OutboxMessage'))
       || ' ON '
       || printf('"%w"', coalesce(@table_name, 'OutboxMessage'))
       || ' ("Status", "CreatedAt") WHERE "Status" IN (0, 3);';
SELECT 'CREATE INDEX IF NOT EXISTS '
       || printf('"IX_%w_Status_ProcessedAt"', coalesce(@table_name, 'OutboxMessage'))
       || ' ON '
       || printf('"%w"', coalesce(@table_name, 'OutboxMessage'))
       || ' ("Status", "ProcessedAt") WHERE "Status" = 2;';
.output stdout

.read outbox.schema.tmp.sql
