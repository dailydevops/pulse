namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using System;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility.Outbox;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Pins the contract of <see cref="SqlIdentifier.Validate(string?, string)"/>: any
/// identifier that does not match <c>[A-Za-z_][A-Za-z0-9_]*</c> is rejected. This
/// guards the SQL providers in NetEvolve.Pulse against injection through
/// configuration-supplied <c>Schema</c> and <c>TableName</c> options that are
/// interpolated raw into bracketed/quoted SQL syntax.
/// </summary>
[TestGroup("Outbox")]
public sealed class SqlIdentifierTests
{
    [Test]
    [Arguments("pulse")]
    [Arguments("OutboxMessage")]
    [Arguments("My_Table_123")]
    [Arguments("_underscored")]
    [Arguments("a")]
    public async Task IsValid_AcceptsSafeIdentifiers(string identifier) =>
        _ = await Assert.That(SqlIdentifier.IsValid(identifier)).IsTrue();

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("1leading_digit")]
    [Arguments("with space")]
    [Arguments("with-dash")]
    [Arguments("with.dot")]
    [Arguments("with;semicolon")]
    [Arguments("with]bracket")]
    [Arguments("with\"quote")]
    [Arguments("with`backtick")]
    [Arguments("with'apostrophe")]
    [Arguments("with--comment")]
    [Arguments("with/*comment")]
    [Arguments("with\nnewline")]
    [Arguments("with\rcr")]
    [Arguments("with\0null")]
    public async Task IsValid_RejectsUnsafeIdentifiers(string identifier) =>
        _ = await Assert.That(SqlIdentifier.IsValid(identifier)).IsFalse();

    [Test]
    public async Task IsValid_NullReturnsFalse() => _ = await Assert.That(SqlIdentifier.IsValid(null)).IsFalse();

    [Test]
    public Task Validate_AcceptsDefaultPulseSchema()
    {
        // Sanity: the library default constants must pass validation.
        SqlIdentifier.Validate("pulse", "Schema");
        SqlIdentifier.Validate("OutboxMessage", "TableName");
        SqlIdentifier.Validate("IdempotencyKey", "TableName");

        return Task.CompletedTask;
    }

    [Test]
    public async Task Validate_RejectsBracketBreakoutInSqlServerSchema() =>
        _ = await Assert.That(() => SqlIdentifier.Validate("pulse].[evil] -- ", "Schema")).Throws<ArgumentException>();

    [Test]
    public async Task Validate_RejectsDoubleQuoteBreakoutInPostgresSchema() =>
        _ = await Assert
            .That(() => SqlIdentifier.Validate("pulse\".\"evil\" -- ", "Schema"))
            .Throws<ArgumentException>();

    [Test]
    public async Task Validate_RejectsBacktickBreakoutInMySqlTable() =>
        _ = await Assert
            .That(() => SqlIdentifier.Validate("evil`; DROP TABLE x; --", "TableName"))
            .Throws<ArgumentException>();

    [Test]
    public async Task Validate_RejectsClassicDropTableSemicolon() =>
        _ = await Assert
            .That(() => SqlIdentifier.Validate("OutboxMessage;DROP TABLE Users", "TableName"))
            .Throws<ArgumentException>();

    [Test]
    public async Task Validate_RejectsNull() =>
        _ = await Assert.That(() => SqlIdentifier.Validate(null, "Schema")).Throws<ArgumentException>();

    [Test]
    public async Task Validate_RejectsEmpty() =>
        _ = await Assert.That(() => SqlIdentifier.Validate(string.Empty, "Schema")).Throws<ArgumentException>();

    [Test]
    public async Task Validate_RejectsOverLengthIdentifier()
    {
        var tooLong = new string('a', SqlIdentifier.MaxLength + 1);
        _ = await Assert.That(() => SqlIdentifier.Validate(tooLong, "Schema")).Throws<ArgumentException>();
    }

    [Test]
    public Task Validate_AcceptsExactlyMaxLengthIdentifier()
    {
        var atLimit = new string('a', SqlIdentifier.MaxLength);
        SqlIdentifier.Validate(atLimit, "Schema");
        return Task.CompletedTask;
    }

    [Test]
    public void Validate_ExceptionParamNameMatchesCaller() =>
        Assert.Throws<ArgumentException>("Schema", () => SqlIdentifier.Validate("bad name", "Schema"));

    [Test]
    public async Task Validate_ExceptionMessageDoesNotEchoRejectedIdentifier()
    {
        // Defense-in-depth: ensure we do not surface attacker-controlled payloads in logs.
        const string Payload = "pulse].[secret_admin_table] -- ";

        var exception = await Assert.That(() => SqlIdentifier.Validate(Payload, "Schema")).Throws<ArgumentException>();

        _ = await Assert.That(exception!.Message).DoesNotContain(Payload);
    }
}
