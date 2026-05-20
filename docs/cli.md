# CLI Guide

The command-line host lives in `src/NgcSharp.App`.

Run help:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- --help
```

Most commands can also be run through a built DLL after building:

```powershell
dotnet src/NgcSharp.App/bin/Debug/net10.0/NgcSharp.App.dll --help
```

## Disc Inspection

Show disc metadata:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- disc-info "path\to\game.iso"
```

List FST entries:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- disc-files "path\to\game.rvz"
```

Read bytes from a disc image:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- disc-read "path\to\game.gcm" 0x420 0x40
```

Disassemble from a disc address:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- disc-disasm "path\to\game.rvz" 0x80003100 64
```

## DOL Inspection And Execution

Show DOL sections:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- dol-info fixtures/devkitpro/xfb-smoke/xfb-smoke.dol
```

Run a bounded DOL:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-dol fixtures/devkitpro/xfb-smoke/xfb-smoke.dol --max-instructions 1000000 --no-registers
```

Dump a simple framebuffer:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-dol fixtures/devkitpro/xfb-smoke/xfb-smoke.dol --max-instructions 1000000 --dump-frame artifacts/xfb-smoke/frame.png --frame-address 0x80010000 --frame-width 320 --frame-height 240 --frame-format rgb565 --no-registers
```

## Disc Execution

Run a bounded disc probe:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "path\to\game.rvz" --max-instructions 3000000 --fast-forward-idle --run-summary artifacts/run-summary.json --no-registers --quiet
```

`--run-summary <json-path>` writes a compact JSON ledger with the executed instruction count, final PC, stop reason, selected registers, GX FIFO byte count, and fast-forward counters. It is the preferred way to compare bounded probes without scraping console output.

`--di-command-latency-cycles <n>` overrides the default scheduled DVD command latency for diagnostic runs. Without an override, DVD reads use transfer-size-aware latency; use the override with `--run-summary` or the compatibility matrix to sweep whether a retail title is sensitive to DI completion timing.

Enable a formatted memory card in Slot A:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "path\to\game.rvz" --max-instructions 12000000 --memory-card-a --fast-forward-idle --no-registers --quiet
```

Hold or window controller input:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "path\to\game.rvz" --controller-button a --max-instructions 5000000
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "path\to\game.rvz" --controller-button-window a 22000000 23000000 --max-instructions 35000000
```

## Tracing

Instruction trace:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "path\to\game.rvz" --max-instructions 1000000 --trace-file artifacts/trace.csv --trace-tail 100 --quiet
```

SI and EXI MMIO traces:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "path\to\game.rvz" --max-instructions 12000000 --trace-si artifacts/si.csv --trace-exi artifacts/exi.csv --quiet --no-registers
```

Stop and watch examples:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "path\to\game.rvz" --stop-on-pc 0x80100000 --trace-tail 32
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "path\to\game.rvz" --watch-write-range 0x80300000 0x1000 --watch-limit 20
```

## GX Diagnostics

Dump a GX frame:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "path\to\game.rvz" --max-instructions 50000000 --fast-forward-idle --dump-gx-frame artifacts/gx/frame.png --gx-frame-source largest-display-copy --gx-frame-max-draws 900 --gx-frame-max-raster-pixels 12000000 --no-registers --quiet
```

Dump draw/copy/coverage/TEV diagnostics:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "path\to\game.rvz" --max-instructions 50000000 --fast-forward-idle --dump-gx-draws artifacts/gx/draws.txt --dump-gx-copies artifacts/gx/copies.csv --dump-gx-coverage artifacts/gx/coverage.csv --dump-gx-tev-samples artifacts/gx/tev.csv --no-registers --quiet
```

Sweep draw windows:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "path\to\game.rvz" --max-instructions 50000000 --dump-gx-frame-sweep artifacts/gx/sweep 0 600 8 --gx-frame-source last-nonblack-display-copy --no-registers --quiet
```

## Image Comparison

Compare two PNGs:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- compare-images artifacts/baseline.png artifacts/candidate.png --diff artifacts/diff.png
```

## Retail Workflow Scripts

Run the default short compatibility checks for Sonic and Pikmin:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-retail-benchmarks.ps1
```

Run the curated compatibility matrix:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-compat-matrix.ps1 -Suites quick -NoBuild -SkipMissing
powershell -ExecutionPolicy Bypass -File scripts/run-compat-matrix.ps1 -Targets sonic-20m,pikmin-5m -NoBuild -SkipMissing
```

The default suite keeps long Sonic probes lightweight enough to finish under the watchdog. Use `-DeepGx` when you explicitly want heavyweight GX copy CSVs for every target:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-retail-benchmarks.ps1 -Targets sonic-20m -DeepGx
```

Run only the Sonic checks:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-sonic-check.ps1
```

Run the optional Mario Kart Double Dash debug-image probes:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-retail-benchmarks.ps1 -Targets mariokart-debug-5m,mariokart-debug-20m
```

Both scripts write timestamped directories under `artifacts/compat-runs` with:

- `run.json`: machine-readable compatibility ledger for that target.
- `emulator-summary.json`: per-run emulator stop reason, final PC, GX FIFO byte count, fast-forward counters, and selected GX frame source metadata when a frame is dumped.
- `summary.csv` / `summary.json`: suite-level rollup.
- `auto.png`: auto-selected GX frame.
- `exi.summary.json`: EXI/card milestone counts.
- `gx-copies.summary.json`: display/texture copy counts, nonblack frame milestones, and per-XFB destination lifecycles showing black overwrites after nonblack copies.

Summarize existing traces without rerunning a game:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-exi-trace.ps1 -TracePath artifacts/run/exi.csv
powershell -ExecutionPolicy Bypass -File scripts/summarize-gx-copies.ps1 -CopyCsvPath artifacts/run/gx-copies.csv
```

For presentation bugs, inspect `displayDestinations` and `displayTimeline` in the GX copy summary. They call out cases where a useful display copy exists briefly and is then overwritten by black at the same framebuffer address.
