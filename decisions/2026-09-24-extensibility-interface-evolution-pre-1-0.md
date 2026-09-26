---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.Extensibility/**/*.cs"
  - "**/*.*"

created: 2026-09-24

lastModified: 2026-09-26

state: accepted

instructions: |
  Applies to all public interfaces in NetEvolve.Pulse.Extensibility and governs commit messages and PR descriptions, not only source files.
  While the major version is 0, add new members directly to these interfaces without default interface methods and update all first-party providers in the same PR.
  While the major version is 0, no commit of any type may use `!` (e.g. `feat!:`, `refactor!:`, `fix!:`) or a `BREAKING CHANGE:` footer for any interface change (addition, signature change, removal), because GitVersion would bump to 1.0.0. Use `feat:` for additions and `refactor:` or `fix:` for changes and removals.
  This is an explicit 0.x exception to the breaking change marker rule of the Conventional Commits decision.
  Every PR that adds, changes or removes interface members MUST list them under an "Impact" section of the PR description; revisit this decision before the 1.0.0 release.
---

# Decision: Extensibility Interface Evolution Before 1.0

A decision on how to evolve public extensibility interfaces in `NetEvolve.Pulse.Extensibility` while the package version is `0.x`: change the interfaces directly, never mark these changes as breaking in commit messages, and document them for external implementers in the pull request.

## Context

`NetEvolve.Pulse.Extensibility` exposes public interfaces that provider packages implement, for example:

- `IOutboxManagement` and `IOutboxRepository`
- `ICommandDeadLetterManagement` and `ICommandDeadLetterStore`
- `IAuditManagement` and `IAuditStore`
- `IIdempotencyKeyRepository` and `IIdempotencyStore`

This decision covers all public interfaces in `NetEvolve.Pulse.Extensibility`, not only the examples above.

All first-party implementations (Entity Framework, SQL Server, PostgreSQL, SQLite, MySQL, MongoDB, Cosmos DB, and others) live in this repository. Third parties MAY implement these interfaces as well.

New features regularly require new members on these interfaces (for example, additional query or management operations), and existing members occasionally need a different signature. Adding, changing or removing a member of a public interface is a source- and binary-breaking change for every external implementer.

The project versions packages with GitVersion from Conventional Commits. `GitVersion.yml` configures `major-version-bump-message` to match `<type>!:` and `BREAKING CHANGE:` footers for every commit type (`build`, `chore`, `ci`, `docs`, `feat`, `fix`, `perf`, `refactor`, `revert`, `style`, `test`). Marking any interface change as breaking therefore bumps the version from `0.x` straight to `1.0.0`, which the project does not intend yet.

Semantic Versioning 2.0.0 states that major version zero is for initial development and that anything MAY change at any time. The public API is not considered stable before `1.0.0`.

## Decision

While the major version is `0`, the project applies the following rules to public extensibility interfaces:

- MUST add new members directly to the public interface.
- MUST NOT use default interface methods (DIMs) to provide fallback implementations for new members.
- MUST implement added or changed members in all first-party providers in the same pull request.
- MUST use a plain `feat:` (or `feat(scope):`) commit type for interface additions, and `refactor:` or `fix:` for signature changes and removals.
- MUST NOT use `!` on any commit type (`feat!:`, `refactor!:`, `fix!:`, ...) or a `BREAKING CHANGE:` footer for any interface change, because GitVersion would bump the version to `1.0.0`.
- MUST list every added, changed or removed interface member under an "Impact" section in the pull request description. The pull request template provides this section. Release Drafter lists pull request titles with links in the release notes, so external implementers reach the "Impact" section through the linked pull request.

This decision amends the [Conventional Commits](./2025-07-10-conventional-commits.md) decision: its rule that breaking changes MUST be marked with `!` or a `BREAKING CHANGE:` footer does not apply to changes of public extensibility interfaces while the major version is `0`. The Conventional Commits decision references this exception.

The project MUST revisit this decision before releasing `1.0.0`. From `1.0.0` on, interface changes MUST either be marked as breaking (`!` or `BREAKING CHANGE:` footer) or be introduced in a non-breaking way, for example with default interface methods or new, separate interfaces.

## Consequences

**Positive Consequences:**

1. **Stable 0.x versioning**: The version stays in the `0.x` range until the project deliberately decides to release `1.0.0`
2. **Clean interfaces**: Interfaces contain only abstract members; no placeholder implementations throw `NotSupportedException` at runtime
3. **Compile-time enforcement**: Every implementer, first-party or external, gets a compiler error for missing members instead of silent fallback behavior
4. **Low overhead**: Contributors do not need to design compatibility shims for an API that is not yet stable

**Potential Challenges:**

1. **Breaking minor releases**: External implementers MAY face compile or runtime errors (`TypeLoadException`, `MissingMethodException`) after a minor version update
2. **Commit history ambiguity**: The commit type alone does not reveal that a change is breaking for implementers
3. **Discipline required**: Contributors MUST remember to document interface changes in the pull request

**Risk Mitigation:**

- Document interface changes under "Impact" in every affected pull request, supported by the pull request template
- Keep all first-party providers in this repository and update them in the same pull request
- Reevaluate the policy as part of the `1.0.0` release preparation

## Alternatives Considered

### 1. Default Interface Methods

**Description**: Add new members with a default implementation, typically throwing `NotSupportedException`.

**Pros**: Binary and source compatible for external implementers

**Cons**: Hides missing implementations until runtime, clutters the interfaces, and adds complexity for an API that is not yet stable

**Rejection Reason**: The compatibility benefit does not justify the runtime risk and complexity during `0.x` development

### 2. Mark Interface Changes as Breaking Changes

**Description**: Commit interface changes with `<type>!:` or a `BREAKING CHANGE:` footer.

**Pros**: Accurately signals the breaking nature in the commit history

**Cons**: GitVersion bumps the version to `1.0.0` immediately, declaring a stable API prematurely

**Rejection Reason**: The project is not ready for a `1.0.0` release

### 3. Map Breaking Changes to a Minor Bump During 0.x

**Description**: Change the repository-owned `major-version-bump-message` in `GitVersion.yml` during `0.x` so that `!` and `BREAKING CHANGE:` no longer bump the major version, and match them in `minor-version-bump-message` instead.

**Pros**: Commit messages stay truthful and the version stays in `0.x`

**Cons**: Deviates from the accepted GitVersion configuration, has to be reverted exactly at the `1.0.0` release, and a forgotten revert silently turns real breaking changes into minor bumps after `1.0.0`. GitVersion offers no dedicated setting for this, so the behavior relies on hand-maintained regular expressions

**Rejection Reason**: A versioning configuration with a hidden expiry date carries more risk than a documented commit convention; it MAY be reconsidered if the convention proves error-prone

### 4. New Companion Interfaces

**Description**: Introduce new interfaces (for example `IOutboxManagement2`) for additional members and keep existing interfaces unchanged.

**Pros**: Fully non-breaking for external implementers

**Cons**: Fragments the API, increases the number of registrations and type checks, and creates long-term maintenance burden

**Rejection Reason**: Speculative complexity that is not warranted before `1.0.0`

## Related Decisions

- [Conventional Commits](./2025-07-10-conventional-commits.md) - Defines the commit types; this decision amends its breaking change marker rule for public extensibility interface changes during `0.x`
- [GitVersion for Automated Semantic Versioning](./2025-07-10-gitversion-automated-semantic-versioning.md) - GitVersion derives the major version bump from breaking change indicators, which is the main reason for this decision
