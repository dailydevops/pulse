-- ============================================================================
-- 004_CreateAuditEntryTable.sql
-- ============================================================================
-- Purpose: Creates the AuditEntry table for the SQLite audit trail store.
-- Compatible with: NetEvolve.Pulse.SQLite (ADO.NET)
--
-- Configuration:
--   This script uses SQLite CLI .parameter to emulate SQLCMD-style variables.
--   Set values before running (the defaults below are used when not provided):
--     @table_name   (default: AuditEntry)
--     @journal_mode (default: wal)
--
-- Usage:
--   sqlite3 audit.db -cmd ".parameter set @table_name AuditEntry" \
--                     -cmd ".parameter set @journal_mode wal" \
--                     ".read 004_CreateAuditEntryTable.sql"
-- ============================================================================

.bail on
.mode list
.headers off
.separator ''

.output audit.schema.tmp.sql
SELECT 'PRAGMA journal_mode=' || lower(coalesce(@journal_mode, 'wal')) || ';';
SELECT 'CREATE TABLE IF NOT EXISTS '
       || printf('"%w"', coalesce(@table_name, 'AuditEntry'))
       || ' ('
       || '"Id"               TEXT        NOT NULL,'
       || '"CommandType"      TEXT        NOT NULL,'
       || '"UserId"           TEXT        NULL,'
       || '"CorrelationId"    TEXT        NULL,'
       || '"OccurredAt"       TEXT        NOT NULL,'
       || '"DurationMs"       REAL        NOT NULL,'
       || '"Result"           INTEGER     NOT NULL,'
       || '"Payload"          TEXT        NULL,'
       || '"ExceptionMessage" TEXT        NULL,'
       || 'CONSTRAINT '
       || printf('"PK_%w"', coalesce(@table_name, 'AuditEntry'))
       || ' PRIMARY KEY ("Id")'
       || ');';
SELECT 'CREATE INDEX IF NOT EXISTS '
       || printf('"IX_%w_OccurredAt"', coalesce(@table_name, 'AuditEntry'))
       || ' ON '
       || printf('"%w"', coalesce(@table_name, 'AuditEntry'))
       || ' ("OccurredAt");';
SELECT 'CREATE INDEX IF NOT EXISTS '
       || printf('"IX_%w_CommandType"', coalesce(@table_name, 'AuditEntry'))
       || ' ON '
       || printf('"%w"', coalesce(@table_name, 'AuditEntry'))
       || ' ("CommandType");';
.output stdout

.read audit.schema.tmp.sql
