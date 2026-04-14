-- ============================================================================
-- 002_CreateIdempotencyKeyTable.sql
-- ============================================================================
-- Purpose: Creates the IdempotencyKey table for the SQLite idempotency store.
-- Compatible with: NetEvolve.Pulse.SQLite (ADO.NET)
--
-- Configuration:
--   This script uses SQLite CLI .parameter to emulate SQLCMD-style variables.
--   Set values before running (the defaults below are used when not provided):
--     @table_name   (default: IdempotencyKey)
--     @journal_mode (default: wal)
--
-- Usage:
--   sqlite3 idempotency.db -cmd ".parameter set @table_name IdempotencyKey" \
--                          -cmd ".parameter set @journal_mode wal" \
--                          ".read 002_CreateIdempotencyKeyTable.sql"
-- ============================================================================

.bail on
.mode list
.headers off
.separator ''

.output idempotency.schema.tmp.sql
SELECT 'PRAGMA journal_mode=' || lower(coalesce(@journal_mode, 'wal')) || ';';
SELECT 'CREATE TABLE IF NOT EXISTS '
       || printf('"%w"', coalesce(@table_name, 'IdempotencyKey'))
       || ' ('
       || '"IdempotencyKey" TEXT NOT NULL,'
       || '"CreatedAt"      TEXT NOT NULL,'
       || 'CONSTRAINT '
       || printf('"PK_%w"', coalesce(@table_name, 'IdempotencyKey'))
       || ' PRIMARY KEY ("IdempotencyKey")'
       || ');';
SELECT 'CREATE INDEX IF NOT EXISTS '
       || printf('"IX_%w_CreatedAt"', coalesce(@table_name, 'IdempotencyKey'))
       || ' ON '
       || printf('"%w"', coalesce(@table_name, 'IdempotencyKey'))
       || ' ("CreatedAt");';
.output stdout

.read idempotency.schema.tmp.sql
