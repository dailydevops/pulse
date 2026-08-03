-- ============================================================================
-- 003_CreateCommandDeadLetterTable.sql
-- ============================================================================
-- Purpose: Creates the CommandDeadLetter table for the SQLite command dead
--          letter store.
-- Compatible with: NetEvolve.Pulse.SQLite (ADO.NET)
--
-- Configuration:
--   This script uses SQLite CLI .parameter to emulate SQLCMD-style variables.
--   Set values before running (the defaults below are used when not provided):
--     @table_name   (default: CommandDeadLetter)
--     @journal_mode (default: wal)
--
-- Usage:
--   sqlite3 deadletter.db -cmd ".parameter set @table_name CommandDeadLetter" \
--                         -cmd ".parameter set @journal_mode wal" \
--                         ".read 003_CreateCommandDeadLetterTable.sql"
-- ============================================================================

.bail on
.mode list
.headers off
.separator ''

.output deadletter.schema.tmp.sql
SELECT 'PRAGMA journal_mode=' || lower(coalesce(@journal_mode, 'wal')) || ';';
SELECT 'CREATE TABLE IF NOT EXISTS '
       || printf('"%w"', coalesce(@table_name, 'CommandDeadLetter'))
       || ' ('
       || '"Id"               TEXT        NOT NULL,'
       || '"CommandType"      TEXT        NOT NULL,'
       || '"Payload"          TEXT        NOT NULL,'
       || '"ExceptionType"    TEXT        NULL,'
       || '"ExceptionMessage" TEXT        NULL,'
       || '"OccurredAt"       TEXT        NOT NULL,'
       || '"AttemptCount"     INTEGER     NOT NULL DEFAULT 1,'
       || '"Status"           INTEGER     NOT NULL DEFAULT 0,'
       || 'CONSTRAINT '
       || printf('"PK_%w"', coalesce(@table_name, 'CommandDeadLetter'))
       || ' PRIMARY KEY ("Id")'
       || ');';
SELECT 'CREATE INDEX IF NOT EXISTS '
       || printf('"IX_%w_Status"', coalesce(@table_name, 'CommandDeadLetter'))
       || ' ON '
       || printf('"%w"', coalesce(@table_name, 'CommandDeadLetter'))
       || ' ("Status");';
SELECT 'CREATE INDEX IF NOT EXISTS '
       || printf('"IX_%w_OccurredAt"', coalesce(@table_name, 'CommandDeadLetter'))
       || ' ON '
       || printf('"%w"', coalesce(@table_name, 'CommandDeadLetter'))
       || ' ("OccurredAt");';
.output stdout

.read deadletter.schema.tmp.sql
