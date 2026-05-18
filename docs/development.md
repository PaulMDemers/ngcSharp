# Development Guide

This guide covers the local workflow for ngcSharp.

## Prerequisites

- .NET SDK capable of `net10.0`.
- PowerShell for repository scripts.
- Optional: devkitPro/devkitPPC to rebuild GameCube homebrew fixtures.
- Optional: Dolphin for reference screenshots.

## Build

```powershell
dotnet restore
dotnet build
```

## Test

Run the full test suite:

```powershell
dotnet test --no-restore
```

Run focused tests while iterating:

```powershell
dotnet test --no-restore --filter GameCubeBusTests
dotnet test --no-restore --filter PowerPcInterpreterTests
dotnet test --no-restore --filter GxFifoSoftwareRendererTests
```

## Local Artifacts

Use these ignored folders for generated output:

- `artifacts/` for PNGs, CSVs, contact sheets, and comparison runs.
- `traces/` for instruction/MMIO traces.

Do not commit generated benchmark outputs unless a future maintainer explicitly promotes a small fixture into source control.

## Homebrew Fixtures

The repository includes devkitPro fixture source and checked-in DOL/ELF outputs for small GX/XFB tests.

Rebuild fixtures:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-gx-fixtures.ps1
```

Run a GX fixture sweep:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-gx-demo-sweep.ps1
```

See [build-devkitpro-fixtures.md](build-devkitpro-fixtures.md) and [gx-fixtures.md](gx-fixtures.md).

## Retail Benchmarks

Retail testing requires legally obtained user-provided game images. Keep them outside git. The default local benchmark scripts look for:

- `Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz`
- `Pikmin (USA).rvz`

Generate Dolphin reference frames:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/generate-dolphin-reference.ps1 -Seconds 16
```

Compare emulator output to the latest reference run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-retail-reference-compare.ps1 -NoBuild
```

Run bounded compatibility checks with watchdogs and machine-readable summaries:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-retail-benchmarks.ps1 -Targets sonic-5m,pikmin-5m -NoBuild
powershell -ExecutionPolicy Bypass -File scripts/run-sonic-check.ps1 -NoBuild
```

These write timestamped runs under `artifacts/compat-runs` with per-target `run.json`, auto-selected GX frames, selected frame-source metadata, EXI summaries, GX copy summaries, and suite-level `summary.csv` / `summary.json`.
The GX copy summaries include `displayDestinations` and `displayTimeline` sections, which are the quickest way to spot XFB addresses that receive a nonblack frame and are later overwritten by black.
The default 20M retail targets use lighter GX settings and skip copy CSVs so the routine suite remains bounded; add `-DeepGx` when chasing copy/presentation details.
Each `emulator-summary.json` also includes a `timings` section that splits wall-clock time into emulation, measured diagnostics, GX frame/copy/coverage/TEV/texture dumps, memory dumps, and profile output. The suite-level `summary.csv` / `summary.json` surface the main timing columns as well; use those before starting an optimization pass so the next hotspot is visible in the artifact instead of inferred from console timing.

Regenerate summaries from existing trace artifacts:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-exi-trace.ps1 -TracePath artifacts/compat-runs/<run>/<target>/exi.csv
powershell -ExecutionPolicy Bypass -File scripts/summarize-gx-copies.ps1 -CopyCsvPath artifacts/compat-runs/<run>/<target>/gx-copies.csv
```

See [retail-benchmarks.md](retail-benchmarks.md).

## Debugging Style

Prefer small, bounded probes:

- Set `--max-instructions`.
- Add one trace or dump at a time.
- Use `--quiet` and `--no-registers` for long runs.
- Capture GX CSVs before rendering expensive PNGs.
- Compare against Dolphin only when both captures represent the same boot window or scene.

Good first diagnostics:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "game.rvz" --max-instructions 3000000 --fast-forward-idle --trace-si artifacts/si.csv --no-registers --quiet

dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "game.rvz" --max-instructions 12000000 --fast-forward-idle --dump-gx-copies artifacts/gx-copies.csv --dump-gx-coverage artifacts/gx-coverage.csv --no-registers --quiet
```

## Coding Guidelines

- Preserve existing style: nullable enabled, implicit usings enabled, focused classes, xUnit tests.
- Add tests for CPU instructions, MMIO behavior, parsing, and CLI option changes.
- Keep fast-forwards exact and conservative. Document the observed code pattern and why it is safe.
- Do not hide missing hardware behavior behind broad game-specific hacks.
- Keep public docs free of copyrighted game data, extracted assets, or proprietary SDK material.
