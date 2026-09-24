---
authors:
  - Martin Stühmer

applyTo:
  - "src/**/*.cs"

created: 2026-09-24

lastModified: 2026-09-24

state: proposed

instructions: |
  While the major version is 0, add new members directly to public extensibility interfaces (e.g. IOutboxManagement, IOutboxRepository, ICommandDeadLetterManagement, ICommandDeadLetterStore, IAuditManagement, IAuditStore, IIdempotencyStore) without default interface methods, and commit such additions with plain `feat:` (never `feat!:` or a `BREAKING CHANGE:` footer, which would bump GitVersion to 1.0.0).
  Every PR that adds interface members MUST list them under an "Impact" section for external implementers; revisit this decision before the 1.0.0 release.
---

# Decision: Extensibility Interface Evolution Before 1.0

A decision on how to evolve public extensibility interfaces in `NetEvolve.Pulse.Extensibility` while the package version is `0.x`: add new members directly to the interfaces, use plain `feat:` commits, and document the additions for external implementers in the pull request.

## Context

`NetEvolve.Pulse.Extensibility` exposes public interfaces that provider packages implement, for example:

- `IOutboxManagement` and `IOutboxRepository`
- `ICommandDeadLetterManagement` and `ICommandDeadLetterStore`
- `IAuditManagement` and `IAuditStore`
- `IIdempotencyKeyRepository` and `IIdempotencyStore`

All first-party implementations (Entity Framework, SQL Server, PostgreSQL, SQLite, MySQL, MongoDB, Cosmos DB, and others) live in this repository. Third parties MAY implement these interfaces as well.

New features regularly require new members on these interfaces (for example, additional query or management operations). Adding a member to a public interface is a source- and binary-breaking change for every external implementer.

The project versions packages with GitVersion from Conventional Commits. `GitVersion.yml` configures `major-version-bump-message` to match `feat!:` and `BREAKING CHANGE:` footers. Marking an interface addition as breaking therefore bumps the version from `0.x` straight to `1.0.0`, which the project does not intend yet.

Semantic Versioning 2.0.0 states that major version zero is for initial development and that anything MAY change at any time. The public API is not considered stable before `1.0.0`.

## Decision

While the major version is `0`, the project applies the following rules to public extensibility interfaces:

- MUST add new members directly to the public interface.
- MUST NOT use default interface methods (DIMs) to provide fallback implementations for new members.
- MUST implement the new members in all first-party providers in the same pull request.
- MUST use a plain `feat:` (or `feat(scope):`) commit type for interface additions.
- MUST NOT use `feat!:` or a `BREAKING CHANGE:` footer for interface additions, because GitVersion would bump the version to `1.0.0`.
- MUST list every added or changed interface member under an "Impact" section in the pull request description, so external implementers can find the required changes in the release notes.

The project MUST revisit this decision before releasing `1.0.0`. From `1.0.0` on, interface additions MUST either be marked as breaking (`feat!:` or `BREAKING CHANGE:` footer) or be introduced in a non-breaking way, for example with default interface methods or new, separate interfaces.

## Consequences

**Positive Consequences:**

1. **Stable 0.x versioning**: The version stays in the `0.x` range until the project deliberately decides to release `1.0.0`
2. **Clean interfaces**: Interfaces contain only abstract members; no placeholder implementations throw `NotSupportedException` at runtime
3. **Compile-time enforcement**: Every implementer, first-party or external, gets a compiler error for missing members instead of silent fallback behavior
4. **Low overhead**: Contributors do not need to design compatibility shims for an API that is not yet stable

**Potential Challenges:**

1. **Breaking minor releases**: External implementers MAY face compile or runtime errors (`TypeLoadException`, `MissingMethodException`) after a minor version update
2. **Commit history ambiguity**: The commit type alone does not reveal that an addition is breaking for implementers
3. **Discipline required**: Contributors MUST remember to document interface additions in the pull request

**Risk Mitigation:**

- Document interface additions under "Impact" in every affected pull request
- Keep all first-party providers in this repository and update them in the same pull request
- Reevaluate the policy as part of the `1.0.0` release preparation

## Alternatives Considered

### 1. Default Interface Methods

**Description**: Add new members with a default implementation, typically throwing `NotSupportedException`.

**Pros**: Binary and source compatible for external implementers

**Cons**: Hides missing implementations until runtime, clutters the interfaces, and adds complexity for an API that is not yet stable

**Rejection Reason**: The compatibility benefit does not justify the runtime risk and complexity during `0.x` development

### 2. Mark Interface Additions as Breaking Changes

**Description**: Commit interface additions with `feat!:` or a `BREAKING CHANGE:` footer.

**Pros**: Accurately signals the breaking nature in the commit history

**Cons**: GitVersion bumps the version to `1.0.0` immediately, declaring a stable API prematurely

**Rejection Reason**: The project is not ready for a `1.0.0` release

### 3. New Companion Interfaces

**Description**: Introduce new interfaces (for example `IOutboxManagement2`) for additional members and keep existing interfaces unchanged.

**Pros**: Fully non-breaking for external implementers

**Cons**: Fragments the API, increases the number of registrations and type checks, and creates long-term maintenance burden

**Rejection Reason**: Speculative complexity that is not warranted before `1.0.0`

## Related Decisions

- [Conventional Commits](./2025-07-10-conventional-commits.md) - Defines the commit types; this decision restricts the use of breaking change indicators for interface additions during `0.x`
- [GitVersion for Automated Semantic Versioning](./2025-07-10-gitversion-automated-semantic-versioning.md) - GitVersion derives the major version bump from breaking change indicators, which is the main reason for this decision
