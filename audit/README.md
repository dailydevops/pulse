# Pulse Audit

Multi-agent quality + practical-usability audit.

## Loop schema
- **Phase 1 (looped)** — Discovery scouts (1 quality + 1 usability) in worktrees form ASSUMPTIONS only. Output → `assumptions/round-NN-quality.md` and `assumptions/round-NN-usability.md`. Each round must read prior rounds and avoid duplicating earlier IDs; new assumptions in new round get a fresh prefix (`Q01b`, `U01b`, …) or new numbers continuing the series.
- **Phase 2** — Verifiers in worktrees independently confirm/refute each assumption with file:line evidence, then write FAILING tests for every confirmed one. Output → `verification/round-NN-<id>.md` + tests under `audit/tests/` (later promoted into the real test projects per provider).
- **Phase 3** — Builders in worktrees, 1 PR per confirmed assumption, English (US), conventional commits.

## Conventions
- All agents work in `git worktree`-isolated copies.
- All PRs against `main`, English (US), conventional commits.
- No code change without a confirming verifier and a failing test first.
