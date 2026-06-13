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

## Compatibility Matrix

The compatibility matrix is the preferred entry point when a change could affect multiple subsystems. It runs checked-in target definitions from `compat/targets.json` while keeping binaries and generated output under ignored `artifacts/` paths.

List curated targets:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-compat-matrix.ps1 -List
```

Run the quick smoke matrix:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-compat-matrix.ps1 -Suites quick -NoBuild -SkipMissing
```

Refresh the local binary inventory:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/update-compat-inventory.ps1
```

See [compat-matrix.md](compat-matrix.md).

## Retail Benchmarks

Retail testing requires legally obtained user-provided game images. Keep them outside git. The default local benchmark scripts look for:

- `Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz`
- `Pikmin (USA).rvz`

Generate Dolphin reference frames:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/generate-dolphin-reference.ps1 -Seconds 16
```

For a longer Sonic-only reference sweep:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/generate-dolphin-reference.ps1 -Games sonic -Seconds 60 -SampleFrames 1,300,600,900,1200,1500,1800
```

Compare a single candidate frame against every PNG sample in a reference directory:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/compare-image-against-samples.ps1 -CandidatePath artifacts/run/gx-frame.png -SampleDirectory artifacts/dolphin-reference/<run>/sonic-adventure-2-battle/samples -NoBuild
```

Search shifted Sonic city/bridge crops against a reference directory:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/search-sonic-visual-alignment.ps1 -CandidatePath artifacts/sonic-visual-anchor/<run>/candidate.png -SampleDirectory artifacts/dolphin-reference/<run>/sonic-adventure-2-battle/samples
```

Fit the affine transform represented by a Sonic transform source map:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/fit-sonic-transform-affine.ps1 -SourceMapCsvPath artifacts/compat-runs/<run>/sonic-transform-source-map.csv
```

Decode Sonic matrix-writer FPR pairs as the transform lanes consumed by the vertex transform path:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-sonic-transform-lanes.ps1 -TraceCsvPath artifacts/compat-runs/<run>/sonic-matrix-writer.csv
```

Decode Sonic root-matrix producer inputs, outputs, and row metrics:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-sonic-root-matrix.ps1 -TraceCsvPath artifacts/compat-runs/<run>/sonic-root-matrix.csv
```

Capture and summarize a focused Sonic locked-cache matrix producer window:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-sonic-matrix-producer-probe.ps1 -NoBuild -RunName sonic-matrix-producer-bridge -MaxInstructions 26783000 -TraceAfter 26770000 -RangeBaseAddress 0xE0000000 -RangeLength 0xC0
```

Compare emulator output to the latest reference run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-retail-reference-compare.ps1 -NoBuild
```

Run bounded compatibility checks with watchdogs and machine-readable summaries:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-retail-benchmarks.ps1 -Targets sonic-5m,pikmin-5m -NoBuild
powershell -ExecutionPolicy Bypass -File scripts/run-retail-benchmarks.ps1 -Targets sonic-5m,pikmin-5m,mariokart-debug-5m -Configuration Release -PerfOnly
powershell -ExecutionPolicy Bypass -File scripts/run-retail-benchmarks.ps1 -Targets sonic-profile-5m,pikmin-profile-5m,mariokart-debug-profile-5m -Configuration Release -PerfOnly
powershell -ExecutionPolicy Bypass -File scripts/run-sonic-check.ps1 -NoBuild
```

These write timestamped runs under `artifacts/compat-runs` with per-target `run.json`, auto-selected GX frames, selected frame-source metadata, EXI summaries, GX copy summaries, and suite-level `summary.csv` / `summary.json`.
Use `-PerfOnly` for speed probes: it keeps quiet/no-register run summaries but disables routine GX/EXI diagnostics and writes a compact `performance.csv` with process MIPS, emulation MIPS, diagnostic share, and stdout/stderr byte counts.
Use the `*-profile-5m` targets when the MIPS baseline is low; they add `--profile-pc 30`, promote the filtered top PCs into `performance.csv` as `pcProfileHead`, and automatically write `profile-clusters/profile-clusters.csv` unless `-SkipProfileClusterReport` is supplied.
Cluster an existing profile run without rerunning the emulator:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-profile-cluster-report.ps1 -RunDirectory artifacts/compat-runs/<run>
```

The report writes `profile-clusters/profile-clusters.csv` and groups adjacent hot PCs so small loops show up as one ranked block.
Use `-Configuration Release` on `run-retail-benchmarks.ps1` for long retail probes; the selected configuration is recorded in the per-target and suite summaries. GX frame dumps also promote lifecycle fields such as selected source, phase, last display-copy index, draw delta since the last display copy, and whether the EFB was cleared after that copy into `summary.csv`.
The GX copy summaries include `displayDestinations` and `displayTimeline` sections, which are the quickest way to spot XFB addresses that receive a nonblack frame and are later overwritten by black.
The default 20M retail targets use lighter GX settings and skip copy CSVs so the routine suite remains bounded; add `-DeepGx` when chasing copy/presentation details.
Each `emulator-summary.json` also includes a `timings` section that splits wall-clock time into emulation, measured diagnostics, GX frame/copy/coverage/TEV/texture dumps, memory dumps, and profile output. GX frame dumps also include nested renderer timings for FIFO expansion, replay, vertex decode, rasterization, raster sub-phases, EFB copies, and PNG writing. The suite-level `summary.csv` / `summary.json` surface the main timing columns as well; use those before starting an optimization pass so the next hotspot is visible in the artifact instead of inferred from console timing.
PC profile summaries include both the raw top PCs and filtered views. `pcProfileWithoutExternalInterruptLeaves` hides SDK interrupt helper leaves; `pcProfileWithoutFastForwardLeaves` also hides recognized exact fast-forward leaf stubs such as `blr`, `return 0`, varargs register-save, timebase-read, and interrupt helper entries. Use the filtered view for long retail probes where helper-entry samples can otherwise obscure the next real subsystem boundary.

GX software-renderer environment toggles are available for focused A/B probes:

- `NGCSHARP_GX_DISABLE_FAST_TEV=1` disables the single-stage TEV fast path.
- `NGCSHARP_GX_DISABLE_CLIP_VOLUME=1` bypasses clip-volume rasterization entirely.
- `NGCSHARP_GX_DISABLE_CLIP_PLANES=Left,Top` disables selected clip planes by name for one run.
- `NGCSHARP_GX_FORCE_TEXTURE_LOD=<lod>` or `NGCSHARP_GX_BASE_TEXTURE_LOD=1` forces texture LOD selection.
- `NGCSHARP_GX_AFFINE_TEXCOORDS=1` disables perspective-correct texture interpolation.
- `NGCSHARP_GX_TEXTURE_PHASE_PROBE=0xADDR:sOffset:tOffset` offsets texture coordinates for a specific texture address.

Keep these as diagnostic A/B tools only. Normal compatibility results should be captured with all renderer toggles unset.

Compare two emulator summaries after an optimization or compatibility change:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/compare-run-summaries.ps1 artifacts/compat-matrix/<old-run>/<target-dir> artifacts/compat-matrix/<new-run>/<target-dir>
```

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
