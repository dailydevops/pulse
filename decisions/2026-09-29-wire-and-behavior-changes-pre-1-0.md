---
authors:
  - Martin Stühmer

applyTo:
  - "**/*.*"

created: 2026-09-29

lastModified: 2026-09-29

state: accepted

instructions: |
  While the major version is 0, commits that change the wire format or observable runtime behavior of a first-party package (for example HTTP payload casing, framing or headers) MUST NOT use `!` or a `BREAKING CHANGE:` footer, because GitVersion would bump to 1.0.0. Use `fix:`, `feat:` or `refactor:` instead.
  Every such change MUST be listed under the "Impact" section of the PR description and in a breaking-change note in the affected package README.
  This extends the 0.x exception of 2026-09-24-extensibility-interface-evolution-pre-1-0.md to wire and behavior changes; revisit it before the 1.0.0 release.
---

# Decision: Wire and Behavior Changes Before 1.0

A decision to extend the `0.x` exception for breaking change markers from public extensibility interfaces to wire format and observable behavior changes of first-party packages, and to document these changes in the pull request and the package README instead.

## Context

The [Conventional Commits](./2025-07-10-conventional-commits.md) decision requires breaking changes to be marked with `!` or a `BREAKING CHANGE:` footer. GitVersion bumps the major version for these markers, so any marked commit moves the project from `0.x` to `1.0.0`.

[Extensibility Interface Evolution Before 1.0](./2026-09-24-extensibility-interface-evolution-pre-1-0.md) exempts changes of public interfaces in `NetEvolve.Pulse.Extensibility` from this rule. It does not cover changes that break clients without touching a C# signature, for example the JSON casing, framing or headers of an HTTP endpoint. [Stream Query HTTP JSON Contract](./2026-09-29-stream-query-http-json-contract.md) is such a change: NDJSON and pre-.NET 10 SSE items switch from PascalCase to the HTTP naming policy. Without an exception, the only compliant commit would bump the version to `1.0.0`.

Semantic Versioning 2.0.0 states that anything MAY change at any time while the major version is zero. The repository history contains no marked breaking commits, so the practice already follows this exception informally.

## Decision

While the major version is `0`:

- Commits that change the wire format or observable runtime behavior of a first-party package MUST NOT use `!` or a `BREAKING CHANGE:` footer.
- These commits use `fix:`, `feat:` or `refactor:` like any other change.
- The pull request MUST list every such change under its "Impact" section.
- The affected package README MUST contain a breaking-change note that names the old and the new behavior.

The project MUST revisit this decision together with the extensibility interface exception before releasing `1.0.0`.

## Consequences

* The version stays in `0.x` until the project deliberately releases `1.0.0`.
* Clients learn about wire changes from the README note and the "Impact" section of the linked pull request in the release notes, not from the commit type.
* The commit history alone does not reveal breaking wire changes.

## Alternatives Considered

* **Mark wire changes as breaking**: rejected, because GitVersion would release `1.0.0` prematurely.
* **Map breaking markers to a minor bump in `GitVersion.yml` during `0.x`**: rejected for the same reasons as in the extensibility interface decision; the configuration would have a hidden expiry date.

## Related Decisions

* [Conventional Commits](./2025-07-10-conventional-commits.md) - This decision adds a second `0.x` exception to its breaking change marker rule.
* [Extensibility Interface Evolution Before 1.0](./2026-09-24-extensibility-interface-evolution-pre-1-0.md) - The existing exception for public interfaces, extended here to wire and behavior changes.
* [Stream Query HTTP JSON Contract](./2026-09-29-stream-query-http-json-contract.md) - The first change documented under this decision.
