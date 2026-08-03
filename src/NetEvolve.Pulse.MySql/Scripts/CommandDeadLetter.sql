-- ============================================================================
-- CommandDeadLetter Table Schema (MySQL)
-- ============================================================================
--
-- Purpose:   Stores commands that failed processing so operators can inspect,
--            replay, or dismiss them later.
-- Provider:  NetEvolve.Pulse.MySql (ADO.NET)
--            NetEvolve.Pulse.EntityFramework with MySql.EntityFrameworkCore
--
-- Prerequisites:
--   MySQL 8.0 or later
--
-- Column types:
--   Id               BINARY(16)    -- Guid.ToByteArray(), primary key
--   CommandType      VARCHAR(500)  -- assembly-qualified type name of the failed command
--   Payload          LONGTEXT      -- JSON serialized command payload
--   ExceptionType    VARCHAR(500)  -- assembly-qualified type name of the exception (nullable)
--   ExceptionMessage LONGTEXT      -- message of the exception (nullable)
--   OccurredAt       BIGINT        -- UTC ticks (use dto.UtcTicks / new DateTimeOffset(ticks, TimeSpan.Zero))
--   AttemptCount     INT           -- number of processing attempts, default 1
--   Status           TINYINT       -- CommandDeadLetterStatus enum value, default 0 (New)
--
-- Usage:
--   Run this script in the target MySQL database once before deploying the application:
--     mysql -u <user> -p <database> < CommandDeadLetter.sql
--
--   If you need a custom table name, replace all occurrences of `CommandDeadLetter`
--   and update CommandDeadLetterOptions.TableName in your application configuration accordingly.
--
-- Note on schema:
--   MySQL does not use schema namespaces in the same way as SQL Server or PostgreSQL.
--   Tables are created in whichever database is active when this script runs.
--   Pass the desired database in the connection string (Database=<dbname>).
-- ============================================================================

CREATE TABLE IF NOT EXISTS `CommandDeadLetter` (
    `Id`               BINARY(16)   NOT NULL,
    `CommandType`      VARCHAR(500) NOT NULL,
    `Payload`          LONGTEXT     NOT NULL,
    `ExceptionType`    VARCHAR(500)     NULL,
    `ExceptionMessage` LONGTEXT         NULL,
    `OccurredAt`       BIGINT       NOT NULL,
    `AttemptCount`     INT          NOT NULL DEFAULT 1,
    `Status`           TINYINT      NOT NULL DEFAULT 0,
    CONSTRAINT `PK_CommandDeadLetter` PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Index to efficiently filter entries by status (for GetPendingAsync filtering)
CREATE INDEX `IX_CommandDeadLetter_Status`
    ON `CommandDeadLetter` (`Status`);

-- Index to efficiently order entries by occurrence time
CREATE INDEX `IX_CommandDeadLetter_OccurredAt`
    ON `CommandDeadLetter` (`OccurredAt`);
