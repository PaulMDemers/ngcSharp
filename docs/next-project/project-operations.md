# Project Operations

## Keep The Working Loop Tight

A good emulator workflow has nested loops:

- Unit test loop: seconds.
- Focused fixture loop: seconds to a minute.
- Focused retail/local benchmark: one to three minutes.
- Broad quick matrix: minutes.
- Deep graphics or compatibility run: only after the smaller loops explain why.

Do not make the deepest run your default iteration cycle.

## Commit In Coherent Slices

Good commit shapes:

- One CPU instruction family plus tests.
- One device behavior plus a fixture/probe.
- One diagnostic feature plus docs.
- One exact fast-forward plus tests and benchmark baseline.
- One renderer path plus image/hash validation.

Avoid mixing generated artifact churn, formatting sweeps, and emulator behavior changes.

## Treat Generated Files Carefully

Artifact folders should be ignored. Generated maps, traces, screenshots, local media, and reference emulator profiles should not be staged unless the project deliberately promotes a tiny artifact.

Before staging:

- Check `git status`.
- Stage explicit files.
- Review cached diff.
- Leave unrelated dirty files alone.

This matters because emulator workflows produce lots of local noise.

## Make Scripts Boring

Scripts should be repeatable and predictable:

- Accept target filters.
- Support `-NoBuild` for fast iteration.
- Support `-SkipMissing` for local-only assets.
- Use watchdogs.
- Write timestamped artifact directories.
- Emit summary CSV/JSON.
- Print concise status.

Prefer one maintained harness over many forgotten one-off commands.

## Keep Documentation Close To Work

Update docs when:

- A new fixture is added.
- A new compatibility target is promoted.
- A new diagnostic is added.
- A benchmark baseline changes.
- A shortcut/fast-forward is introduced.
- A current blocker is understood.

Docs do not need to be polished essays. They need to preserve operational knowledge.

## Name Things By Intent

Good names help future maintainers:

- `resource-hot-pc-probe`
- `display-copy-summary`
- `timeBaseReadInstructions`
- `last-nonblack-display-copy`
- `stop-on-hot-pc`

Avoid names that only make sense during one debugging session.

## Public Repo Hygiene

Before public release:

- Add license.
- Add contribution guide.
- Add legal/assets policy.
- Document requirements.
- Document current limitations honestly.
- Confirm ignored paths cover local media and generated artifacts.
- Avoid implying commercial compatibility that does not exist.

Emulator users will test the hardest software first. A clear status section prevents confusion.

## Know When To Write A Tool

Write or promote a tool when:

- You manually compare the same fields twice.
- You rerun the same command with slight changes.
- You need to explain a result to yourself.
- A future regression would be hard to diagnose from console text.

Good tools convert exploration into muscle memory.
