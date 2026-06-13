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

Write exact disc bytes to a binary file:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- disc-read-bin "path\to\game.rvz" 0x3304783C 0x590C0 artifacts/source-disc.bin
```

Disassemble from a disc address:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- disc-disasm "path\to\game.rvz" 0x80003100 64
```

Decode a Sega PRS-style compressed blob:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- prs-decode artifacts/buffer-80fffe60.bin artifacts/decoded.bin --slice 0xB8670 0x100 artifacts/bridge-packet.bin
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

`--run-summary <json-path>` writes a compact JSON ledger with the executed instruction count, final PC, stop reason, selected scalar registers, full GPR values, paired-single FPR lanes, GX FIFO byte count, and fast-forward counters. It is the preferred way to compare bounded probes without scraping console output.

`--dump-disasm <addr> <count>` disassembles live emulated RAM after a bounded run or stop condition. This is useful for DVD-loaded overlays that are not visible through static `disc-disasm`.

`--dump-memory-bin <addr> <len> <path>` writes a raw byte-for-byte snapshot of live emulated main RAM at the end of a bounded run or stop condition. `--dump-memory-bin-at <instruction> <addr> <len> <path>` writes the same kind of binary snapshot immediately before the specified instruction executes, which is useful when a temporary decode/copy buffer is reused later in the frame. Use these with `disc-read-bin` when verifying that a DI DMA buffer matches its source disc bytes.

`--trace-sonic-bitstream-decoder <csv-path> <addr> <len>` records Sonic PRS-style decompressor fast-forwards whose decompressed output overlaps the requested range. Each row includes source/destination pointers, decompressed length, source bytes, the overlapped output bytes, and final decoder state.

Summarize a replayed Sonic decoded resource:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/summarize-sonic-decoded-resource.ps1 -DecodedPath artifacts/decoded-80fffe60.bin -BaseAddress 0x8125FE60
```

Pass `-ScanAllPackets` to search the whole decoded resource for packet-like records. The default stays focused on the provided addresses because broad packet discovery is more expensive in PowerShell. Add `-PacketOnly` when you only need packet fields/families and want to skip stream and pointer expansion.

Compare two binary files:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/compare-binary-files.ps1 artifacts/source-disc.bin artifacts/source-ram.bin
```

`--di-command-latency-cycles <n>` overrides the default scheduled DVD command latency for diagnostic runs. Without an override, DVD reads use transfer-size-aware latency; use the override with `--run-summary` or the compatibility matrix to sweep whether a retail title is sensitive to DI completion timing.

`--profile-after <n>` scopes PC, branch-site, PC/LR, and indirect branch-site profile collection to a later instruction window. This is useful when an early hot loop otherwise dominates the profile for a longer compatibility probe. The legacy `--profile-indirect-call-site <addr> <n>` flag now records both linked indirect calls and plain indirect branches such as `bcctr`, which is useful for jump-table dispatch loops.

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

Sonic path lookup differential trace:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz" --max-instructions 20000000 --memory-card-a --controller-button a --fast-forward-idle --fast-forward-write-watch --trace-sonic-path-lookup artifacts/sonic-path-lookup.csv --run-summary artifacts/sonic-path-lookup-summary.json --no-registers
```

`--trace-sonic-path-lookup <csv-path>` observes Sonic Adventure 2 Battle's live `0x800EECFC` resource pathname lookup without skipping it. The CSV compares the C# table-walk model against the PPC routine's real return value, records volatile register/CR side effects, captures full GPR and caller-stack snapshots, reports interpreter-loop and elapsed timebase counts, estimates lookup cycles from table-walk metrics, counts interrupt entries during the call, and marks whether the current guard would consider the call fast-forward eligible. Use this before enabling any routine-level skip so scheduler timing stays explainable.

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
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "path\to\game.rvz" --max-instructions 50000000 --fast-forward-idle --dump-gx-draws artifacts/gx/draws.txt --dump-gx-copies artifacts/gx/copies.csv --dump-gx-copy-events artifacts/gx/copy-events.csv --dump-gx-coverage artifacts/gx/coverage.csv --dump-gx-triangle-coverage artifacts/gx/triangle-coverage.csv --dump-gx-tev-samples artifacts/gx/tev.csv --no-registers --quiet
```

Use `--dump-gx-copy-events` when you only need the BP `0x52` EFB-copy timeline. It decodes copy commands and draw indices without rasterizing the FIFO, so it is the faster first pass for presentation bugs.

Use `--gx-frame-skip-copy-memory-writes` for current-EFB lifecycle probes and selected display-copy probes where copy/clear timing matters more than texture/XFB memory side effects. It still applies EFB clears and records copy markers. Display-copy frame sources can now snapshot the selected XFB directly from the EFB copy parameters, so exact copy-index diagnostics do not need to round-trip the selected display copy through emulated RAM.

Coverage CSVs include projected/clipped vertex counts plus near-clip triangle counters (`clip_input_triangles`, `near_clip_output_triangles`, and `near_clip_culled_triangles`). Use these fields to separate draws that are genuinely outside the frustum from draws that rasterize but fail depth, alpha, or color writes.

Use `--dump-gx-triangle-coverage <csv-path>` with the same GX draw window when a draw-level row is still too coarse. It emits one row per primitive triangle with source vertex indices, projected/clipped input counts, screen bounds, view-space `w` bounds, active near-plane `w`, stage0 texture-coordinate bounds, raster/color/depth/alpha counters, TEV centroid samples, texture mip sample summaries for mipfiltered textures, and a coarse reason such as `rendered`, `near-clip-culled`, or `degenerate-or-offscreen`.

Summarize TEV sample rows and overlay sampled texels on the dumped texture image:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-gx-tev-samples.ps1 -TevSampleCsvPath artifacts/gx/tev.csv
powershell -ExecutionPolicy Bypass -File scripts/build-gx-tev-sample-texture-map.ps1 -TevSampleCsvPath artifacts/gx/tev.csv -TextureIndexCsvPath artifacts/gx/textures/index.csv -DrawIndex 12188
```

Summarize rendered/dark triangle coverage:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-gx-triangle-coverage.ps1 -TriangleCoverageCsvPath artifacts/gx/triangle-coverage.csv
```

Roll rendered triangles up by material/texture binding:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-gx-materials.ps1 -TriangleCoverageSummaryCsvPath artifacts/gx/triangle-coverage.summary.csv
```

Dump active texture bindings as PNGs:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "path\to\game.rvz" --max-instructions 50000000 --fast-forward-idle --dump-gx-textures artifacts/gx/textures --gx-draw-skip-draws 12180 --gx-draw-max-draws 20 --no-registers --quiet
```

`--dump-gx-textures` writes `index.csv` plus RGB/alpha PNGs for each active texture map in the draw window. Mipfiltered textures now emit one row and image pair per mip level, so material reports can be checked against the exact decoded mip chain.

Join Sonic's worst GX material with the closest Dolphin-aligned visual crop:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-sonic-material-visual-report.ps1 -MaterialSummaryCsvPath artifacts/compat-runs/<run>/sonic-gx-window-12140-provenance/gx-materials.summary.csv -AlignmentDirectory artifacts/sonic-visual-alignment/<run> -Region bridge
```

Build a Sonic bridge material timeline and texture contact sheet:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-sonic-bridge-material-timeline.ps1 -MaterialSummaryCsvPath artifacts/compat-runs/<run>/sonic-gx-window-12140-provenance/gx-materials.summary.csv -TextureIndexCsvPath artifacts/sonic-texture-dumps/<run>/textures/index.csv -PacketTimelineCsvPath artifacts/compat-runs/<run>/sonic-gx-window-12140-provenance/sonic-packet-timeline.csv -TextureBindTraceCsvPath artifacts/compat-runs/<bind-run>/sonic-texture-binds.csv
```

The timeline joins material rows, decoded texture PNGs, Sonic packet/object ranges, and Sonic texture-bind context where available. The contact sheet makes the dark `0x0072C600` bridge binding easy to compare with neighboring high-coverage textures such as the brighter city material.

Decode a captured Sonic CMPR texture binary and join it with bind/TEV/bitstream provenance:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-sonic-decoded-texture-report.ps1 -TextureBinPath artifacts/compat-runs/<run>/dest-8072c600-after-copies.bin -BitstreamCsvPath artifacts/compat-runs/<run>/source-bitstream.csv -TextureBindCsvPath artifacts/compat-runs/<bind-run>/sonic-texture-binds.csv -TevSummaryCsvPath artifacts/compat-runs/<tev-run>/gx-tev-samples.summary.csv
```

Scan a decoded Sonic PRS/resource blob for `GVRT` texture headers:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-sonic-gvrt-textures.ps1 -DecodedPath artifacts/compat-runs/<run>/decoded-8125fe60.bin -BaseAddress 0x8125FE60 -FocusPayloadAddress 0x8137DFA0
```

Build a `GVRT` neighbor contact sheet and join decoded payload hashes to runtime GX texture/material rows:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-sonic-gvrt-texture-contact-sheet.ps1 -DecodedPath artifacts/compat-runs/<run>/decoded-8125fe60.bin -GvrtCsvPath artifacts/compat-runs/<run>/gvrt-summary/sonic-gvrt-textures.csv -TextureIndexCsvPath artifacts/sonic-texture-dumps/<run>/textures/index.csv -MaterialSummaryCsvPath artifacts/compat-runs/<run>/sonic-gx-window-12140-provenance/gx-materials.summary.csv -FocusPayloadAddress 0x8137DFA0
```

Build the focused Sonic bridge object/material identity row:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-sonic-bridge-object-material-identity.ps1 -PacketTimelineCsvPath artifacts/compat-runs/<run>/sonic-gx-window-12140-provenance/sonic-packet-timeline.csv -MaterialSummaryCsvPath artifacts/compat-runs/<run>/sonic-gx-window-12140-provenance/gx-materials.summary.csv -GvrtContactCsvPath artifacts/compat-runs/<resource-run>/gvrt-contact-sheet/sonic-gvrt-contact-sheet.csv -TextureBindCsvPath artifacts/compat-runs/<bind-run>/sonic-texture-binds.csv
```

This writes one CSV/JSON row joining packet `0x813184D0`, texture `0x0072C600`, and GVRT payload `0x8137DFA0`. Use it as the compact checkpoint before moving from texture/material debugging back to scene phase or Dolphin reference comparison.

Sweep existing runtime texture dumps for the decoded Sonic `GVRT` resource hashes:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/find-sonic-gvrt-runtime-matches.ps1 -GvrtContactCsvPath artifacts/compat-runs/<resource-run>/gvrt-contact-sheet/sonic-gvrt-contact-sheet.csv -TextureIndexRoot artifacts
```

The sweep scans `textures/index.csv` files, joins mip0 source hashes back to the decoded `GVRT` rows, and writes summary/details CSVs. Use it to check whether neighboring decoded resources are used in any existing capture before spending time on another texture bind trace.

Build the cross-run Sonic bridge phase report:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-sonic-bridge-phase-report.ps1 -CompatRoot artifacts/compat-runs -GvrtContactCsvPath artifacts/compat-runs/<resource-run>/gvrt-contact-sheet/sonic-gvrt-contact-sheet.csv
```

The report scans compatibility `run.json` ledgers and joins the focus packet, focus material, texture-index hash matches, copy activity, coverage totals, and timing columns. Use `sonic-gx-window-12140-materials` when you need a fresh run that contains packet provenance, material summaries, and `textures/index.csv` in one target directory.

Join the bridge phase report with Dolphin visual-alignment rows:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-sonic-dolphin-phase-report.ps1 -PhaseReportCsvPath artifacts/compat-runs/<phase-run>/sonic-bridge-phase-report.csv -AlignmentDirectory artifacts/sonic-visual-alignment/<alignment-run> -Target sonic-gx-window-12140-materials
```

This writes a one-row verdict plus per-region rows for bridge, skyline, and lower-track crops. It is useful for separating a broad scene/camera phase mismatch from a local material/rendering mismatch after the texture and packet identity have been pinned down.

Build an approximate material mask/overlay for a focused GX material:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-sonic-material-mask-report.ps1 -TriangleCoverageCsvPath artifacts/compat-runs/<run>/<target>/gx-triangle-coverage.csv -GxCopiesCsvPath artifacts/compat-runs/<run>/<target>/gx-copies.csv -CandidateImagePath artifacts/compat-runs/<run>/<target>/auto.png -DrawIndex 12188 -TextureAddress 0x0072C600
```

The report reconstructs a mask from rendered triangle vertices when `gx-vertices.csv` is present, or from triangle bounds otherwise. Use `sonic-gx-window-12180-efb-material` for a same-coordinate EFB capture around draw `12188`; it emits the EFB frame, coverage, triangle coverage, texture dumps, exact vertices, and material summaries.

Classify a dumped frame's display-copy/current-EFB phase from existing artifacts:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-gx-frame-lifecycle-report.ps1 -RunDirectory artifacts/compat-runs/<run>/<target>
```

The report prefers `emulator-summary.json` lifecycle metadata when available, and otherwise infers the phase from `gx-copies.csv` plus the bounded coverage window.

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

Use `-Configuration Release` for long retail probes when you need the fastest local executable. The suite records the selected configuration in each `run.json`, `summary.csv`, and `summary.json`:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-retail-benchmarks.ps1 -Targets sonic-gx-window-12180-efb-lifecycle -Configuration Release -TimeoutSeconds 300
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

Run Sonic's lightweight full copy-event timeline:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-retail-benchmarks.ps1 -Targets sonic-copy-events-50m -NoBuild -TimeoutSeconds 900
```

Run only the Sonic checks:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-sonic-check.ps1
```

Run the optional Mario Kart Double Dash debug-image probes:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-retail-benchmarks.ps1 -Targets mariokart-debug-5m,mariokart-debug-20m
```

Run focused Sonic GX draw-window probes:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-retail-benchmarks.ps1 -Targets sonic-gx-window-280,sonic-gx-window-680 -NoBuild -TimeoutSeconds 900
```

Run the Sonic city/bridge visual anchor against the latest Dolphin reference samples:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-sonic-visual-anchor.ps1 -NoBuild
```

Use `-CandidatePath <png>` to recompare an existing ngcSharp frame without rerunning the emulator. The harness captures or reuses the draw-8,302 through draw-12,180 city/bridge frame, compares it against Sonic Dolphin samples, emits full-frame and focused crop summaries, and writes a contact sheet under `artifacts/sonic-visual-anchor/<timestamp>`.

Pass emulator diagnostics through `-ExtraRunArgs` for A/B captures:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-sonic-visual-anchor.ps1 -NoBuild -ExtraRunArgs --disable-sonic-paired-transform-fast-forward
```

Search shifted Sonic visual crops against Dolphin samples:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/search-sonic-visual-alignment.ps1 -CandidatePath artifacts/sonic-visual-anchor/<run>/candidate.png -SampleDirectory artifacts/dolphin-reference/<run>/sonic-adventure-2-battle/samples
```

The shifted-crop harness writes `shift-summary.csv`, `best-shifts.csv`, and best sample/candidate crop PNGs under `artifacts/sonic-visual-alignment/<timestamp>`. It filters nearly black reference/candidate crops by default so fade frames do not win the score; use `-MinSampleNonBlackPercent 0 -MinCandidateNonBlackPercent 0` only when intentionally checking black-frame timing.

The generated Sonic window targets currently cover draw skips `280`, `400`, `480`, `520`, `560`, `600`, `640`, `680`, `1000`, `1500`, `4380`, and `8260`. They disable PNG capture and write bounded GX copy/coverage diagnostics so `summary.csv` can show nonblack display-copy counts, coverage bounds, raster-budget state, and color-write totals for each window. Heavy variants such as `sonic-gx-window-560-heavy` also add bounded draw logs and TEV samples for the suspicious window.

Use `sonic-gx-interval-8302` when a copy needs warmed EFB history from the prior copy interval; it renders a longer draw span starting at draw 8,302 and writes copy diagnostics without coverage rows. Use `sonic-gx-interval-8302-frame` for the matching PNG capture from the largest display copy in that interval. Use `sonic-gx-window-12180-display-copy` as the named draw-12,180 presented-frame capture; it uses the same warmed interval, selects exact copy `1657`/XFB `0x004A2460`, and skips GX copy memory writes while directly snapshotting the selected display copy. Use `sonic-gx-window-16058-warmed-display-copy` to render from draw 8,302 through the later bridge/city display copy, force copy `1660`/XFB `0x00538460`, and preserve the pre-copy EFB history needed by sky/background diagnostics. Use `sonic-gx-window-12140-heavy` to inspect the draw/projection/TEV state immediately before the full city/bridge display copy at draw 12,180. Use `sonic-gx-window-12140-provenance` for the standard bridge investigation bundle: bounded GX state/vertex diagnostics, Sonic packet-to-FIFO provenance across FIFO `0x2D1E00..0x2D5DFF`, scene-state tracing, matrix-writer tracing, packet-selection tracing, auto summaries, and a rebuilt `sonic-packet-timeline.csv`. Use `sonic-gx-window-12140-materials` for the same provenance bundle plus a runtime texture dump under `textures/index.csv`. Use `sonic-gx-window-12180-efb-material` when a material draw occurs after the display copy and must be compared in EFB coordinates rather than against the prior XFB/display frame. Use `sonic-gx-window-12180-efb-lifecycle` for a lighter current-EFB phase probe that skips copy memory writes while preserving copy-clear lifecycle metadata. The `*-no-held-input` variants keep Slot A inserted but do not hold A, which helps separate renderer mismatches from input-driven scene timing differences.

For Sonic transform A/B checks, pass extra emulator arguments through the benchmark harness:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-retail-benchmarks.ps1 -Targets sonic-gx-interval-8302-frame -ExtraRunArgs --disable-sonic-paired-transform-fast-forward -NoBuild -TimeoutSeconds 1200
```

`--disable-sonic-paired-transform-fast-forward` keeps the rest of the fast-forward system enabled but forces Sonic's paired-transform 2D/4D helpers through the interpreter. Use it when a GX capture looks geometrically plausible but the camera/object transform differs from Dolphin.

`--disable-sonic-gx-fast-forward` disables Sonic-specific GX FIFO emit/state fast paths. `--disable-sonic-geometry-fast-forward` disables the paired/vector/generated geometry helper family. `--disable-sonic-resource-fast-forward` disables the Sonic resource lookup/mode/state/fixup fast paths while leaving GX and geometry shortcuts available. Sonic resource lookup, mode-query, and state-poll helpers are default-off because phase probes showed they can alter Sonic's resource/material phase; pass `--enable-sonic-resource-lookup-fast-forward`, `--enable-sonic-resource-mode-query-fast-forward`, or `--enable-sonic-resource-state-poll-fast-forward` only for profiling experiments. The Sonic resource-fixup helper is default-on again after the `0xCA` previous-resource side-effect case was changed to fall back to the interpreter; use `--disable-sonic-resource-fixup-fast-forward` for A/B compatibility checks. The older `--enable-sonic-resource-fixup-fast-forward` switch is accepted for compatibility with saved scripts.

Heavy GX windows also write `gx-transforms.csv`. You can request that directly with:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz" --max-instructions 50000000 --fast-forward-idle --fast-forward-write-watch --memory-card-a --controller-button a --dump-gx-transforms artifacts/gx-transforms.csv --gx-draw-skip-draws 12140 --gx-draw-max-draws 80 --no-registers --quiet
```

The transform CSV records each draw's active viewport/projection state plus model, view, and projected screen bounds. It is the quickest way to inspect whether a scene mismatch is already present in emitted vertices or introduced by the diagnostic projection path.

Use `--dump-gx-state-timeline <csv-path>` with the same draw window when you need the state changes leading into those draws. It writes draw rows plus relevant CP, XF, BP, and copy events with active viewport/projection, position matrix rows, scissor, pixel mode, and EFB copy state.

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz" --max-instructions 50000000 --fast-forward-idle --fast-forward-write-watch --memory-card-a --controller-button a --dump-gx-state-timeline artifacts/gx-state-timeline.csv --gx-draw-skip-draws 12170 --gx-draw-max-draws 70 --no-registers --quiet
```

Use `--dump-gx-vertices <csv-path>` with the same `--gx-draw-skip-draws` / `--gx-draw-max-draws` window when aggregate transform bounds are not enough. It writes one CSV row per decoded vertex with raw FIFO bytes, model/view/screen coordinates, clip state, color, and TEX0 values.

Summarize a GX vertex CSV, optionally around a specific FIFO offset:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-gx-vertices.ps1 `
  -VertexCsvPath artifacts/compat-runs/<run>/sonic-bridge-vertices/gx-vertices.csv `
  -FocusFifoOffset 0x2D1E42
```

Use a bounded FIFO write trace when you need to attribute a suspicious GX byte range back to CPU code without writing the full FIFO stream:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-disc "Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz" --max-instructions 50000000 --fast-forward-idle --fast-forward-write-watch --memory-card-a --controller-button a --disable-sonic-gx-fast-forward --trace-gx-fifo-window artifacts/gx-fifo-window.csv 0x2CEF00 0x9000 --no-registers --quiet
```

Use `--trace-sonic-matrix-stack <csv-path>` to snapshot Sonic's locked-cache matrix-stack operations around the object renderer. It records matrix stack base/previous/current pointers, key registers, object/packet memory windows, `r4/r5/r6` context windows, and the base/previous/current 0x30-byte matrix slots. Pair it with `--trace-pc-after` and `--watch-limit` for focused bridge probes.

Use `--trace-sonic-matrix-writer <csv-path>` when the stack snapshot proves a bad matrix but you need the producer context. It records the matrix-store PCs, LR/CTR/CR, key GPRs/FPRs, inferred paired-single store address, current locked-cache matrix bytes, source matrix bytes, and nearby stack/object pointer windows.

Use `--trace-sonic-root-matrix <csv-path>` to capture the camera/root matrix producer around Sonic's `0x800ED368..0x800ED430` affine multiply, the adjacent scalar store path, and the small rotation-matrix builder feeding it. It records both input matrices, output/root matrix windows, FPR pairs, and key GPR context.

Use `--trace-sonic-scene-state <csv-path>` to capture a compact phase snapshot at Sonic's draw-packet renderer entry. It records the active packet/object/streams, vertex base, matrix stack pointers, selected Sonic resource/mode globals, key GPRs, and compact packet/object/state/small-data/matrix memory windows.

Use `--trace-sonic-packet-selection <csv-path>` to capture the wrapper and dispatch PCs immediately around Sonic's draw-packet renderer. It infers the active packet from live registers, records packet bounds/object vectors, and keeps compact register/stack/object windows so packet ordering can be compared before `0x8011D414`.

Summarize a Sonic scene-state trace into object vectors, mode/resource fields, and stable window hashes:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-sonic-scene-state.ps1 `
  -TraceCsvPath artifacts/compat-runs/<run>/sonic-scene-state.csv
```

Summarize a Sonic packet-selection trace into ordered wrapper/renderer rows:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-sonic-packet-selection.ps1 `
  -TraceCsvPath artifacts/compat-runs/<run>/sonic-packet-selection.csv
```

Summarize a Sonic matrix-writer trace into packet rows, source translations, and packed FPR columns:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-sonic-matrix-writer.ps1 `
  -TraceCsvPath artifacts/compat-runs/<run>/sonic-matrix-writer.csv
```

Summarize the same matrix-writer trace as Sonic transform lanes with basis length/dot diagnostics:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-sonic-transform-lanes.ps1 `
  -TraceCsvPath artifacts/compat-runs/<run>/sonic-matrix-writer.csv
```

Summarize Sonic root-matrix trace rows into left/right/output/root matrix rows and orthogonality metrics:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-sonic-root-matrix.ps1 `
  -TraceCsvPath artifacts/compat-runs/<run>/sonic-root-matrix.csv
```

Summarize a Sonic matrix-stack trace into decoded object and matrix fields:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-sonic-matrix-stack.ps1 `
  -TraceCsvPath artifacts/compat-runs/<run>/sonic-matrix-stack.csv
```

Build an anchored object-to-draw visual map by joining matrix-stack summaries with GX vertex bounds:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-sonic-object-visual-map.ps1 `
  -MatrixSummaryCsvPath artifacts/compat-runs/<matrix-run>/sonic-matrix-stack.summary.csv `
  -VertexSummaryCsvPath artifacts/compat-runs/<vertex-run>/gx-vertices.summary.csv `
  -Anchor 0x813184D0=0x2D1E3F,0x81318FC8=0x2D375C
```

Anchors use `<packet>=<fifo-offset>` because the GX vertex stream does not carry Sonic packet pointers directly. Pass `-AnchorCsvPath artifacts/compat-runs/<run>/sonic-vertex-provenance.summary.csv` to consume anchors generated by `summarize-sonic-vertex-provenance.ps1` instead of typing them manually. The script emits object transform/matrix rows plus mapped draw ranges, vertex counts, clip counts, and aggregate view/screen bounds.

Build a packet timeline by joining scene-state rows, matrix-writer rows, and optional GX vertex bounds:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-sonic-packet-timeline.ps1 `
  -SceneSummaryCsvPath artifacts/compat-runs/<scene-run>/sonic-scene-state.summary.csv `
  -MatrixWriterSummaryCsvPath artifacts/compat-runs/<scene-run>/sonic-matrix-writer.summary.csv `
  -VertexSummaryCsvPath artifacts/compat-runs/<vertex-run>/gx-vertices.summary.csv `
  -Anchor 0x813184D0=0x2D1E3F,0x81318FC8=0x2D375C
```

This keeps Sonic packet order, object vectors, matrix translation, mode/resource hashes, packet-to-matrix instruction delta, and anchored draw/clip/view/screen bounds in one CSV. `-AnchorCsvPath` is supported here too. Omit `-VertexSummaryCsvPath` and anchor arguments when you only need CPU-side packet phase.

Use `--trace-sonic-draw-packets <csv-path>` to snapshot Sonic draw packet objects at the `0x8011D414` packet renderer entry. It honors `--trace-pc-after` and `--watch-limit`, and records the packet pointer, its two command-stream pointers, the active global vertex base, register context, and compact packet/stream/vertex memory windows. Stream0 capture is widened from its header length and capped at 0x800 bytes, while stream1 capture is capped at 0x200 bytes.

Use `--trace-sonic-gx-emitters <csv-path>` to snapshot the Sonic GX FIFO emitter helper PCs around `0x801200C8..0x80120150` and `0x8011D8E8..0x8011D914`. It honors `--trace-pc-after` and `--watch-limit`, and records the current GX FIFO byte offset, key GPRs, source vertex record words, and FPR values/bits before the store executes.

Use `--trace-sonic-texture-binds <csv-path>` to snapshot Sonic's fast-forwarded `GXLoadTexObj`-style texture binds. It records the GX FIFO byte offset, LR caller, texture object and sampler object pointers, generated BP words, decoded source address/size/format, and compact object memory windows. This is the fastest way to connect a suspicious material row, such as the dark bridge `0x0072C600` binding, back to the game-side texture object that produced it.

Use `--trace-sonic-vertex-provenance <csv-path> <fifo-start> <fifo-len>` for a focused bridge-style FIFO provenance pass. It automatically forces the real Sonic GX helper path, watches emitter PCs within the requested FIFO byte window, infers the active draw packet/stream record, decodes the stream triplet, and records the source transformed vertex record that produced each FIFO vertex.

Summarize Sonic vertex provenance rows into packet anchors and source-record bounds:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-sonic-vertex-provenance.ps1 `
  -TraceCsvPath artifacts/compat-runs/<run>/sonic-vertex-provenance.csv
```

Join Sonic vertex provenance with transformed-table write and transform-input traces:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-sonic-vertex-lineage.ps1 `
  -ProvenanceCsvPath artifacts/compat-runs/<run>/sonic-vertex-provenance.csv `
  -WriteTraceCsvPath artifacts/compat-runs/<run>/sonic-output-writes.csv `
  -TransformCsvPath artifacts/compat-runs/<run>/sonic-transform-inputs.csv
```

The lineage CSV maps each suspicious FIFO vertex to its source record, latest producing write, and overlapping Sonic transform loop row. For bridge work, capture all three traces in the same run so instruction indices and reused vertex-table contents stay comparable.

Summarize a Sonic transform lineage into source/output coordinates:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-sonic-transform-source-map.ps1 `
  -LineageCsvPath artifacts/compat-runs/<run>/sonic-vertex-lineage.csv
```

The source map decodes the captured transform input bytes as 16-byte Sonic source records and maps them to transformed output records. This is useful after widening `--trace-sonic-transform-inputs` enough to cover the whole transform batch.

Fit an affine transform from a Sonic transform source map:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/fit-sonic-transform-affine.ps1 `
  -SourceMapCsvPath artifacts/compat-runs/<run>/sonic-transform-source-map.csv
```

The fit writes `sonic-transform-affine-fit.csv/json` next to the source map. It is useful for checking whether captured input/output vertices are explained by one stable CPU-side matrix and whether the fitted rows look orthonormal or sheared.

When a source map contains multiple packets, fit each packet separately:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/fit-sonic-transform-affine-groups.ps1 `
  -SourceMapCsvPath artifacts/compat-runs/<run>/sonic-transform-source-map.csv
```

The grouped fit writes `sonic-transform-affine-fit-groups.csv/json` and avoids blending unrelated object matrices into one misleading residual.

Use `--trace-sonic-transform-inputs <csv-path>` to snapshot Sonic paired-single transform loop entries at `0x8011DAA8`, `0x8011DB94`, and the indexed 4D variant. It honors `--trace-pc-after` and `--watch-limit`, and records input/output cursors, GQRs, FPR pairs, and compact input/output memory windows. Add `--trace-sonic-transform-output-range <addr> <len>` to emit only transform rows whose destination span overlaps a target output table range.

Use `--trace-sonic-input-writes <csv-path> <addr> <len>` to capture stores and bulk writes that overlap a focused Sonic packed-input range. The CSV records the writer PC/opcode, selected registers, watched trace address/length, the watched range bytes after the write, and a short post-write window at the write address. It honors `--trace-pc-after` and `--watch-limit`.

Summarize a Sonic input/object write trace by writer/source batch:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-sonic-input-writes.ps1 `
  -TraceCsvPath artifacts/compat-runs/<run>/sonic-input-writes.csv
```

Join focused writer traces back to repeated Sonic scene packet events. The report writes direct writer rows, target value snapshots decoded from `range_bytes`, target changes, and the last direct write/change before each focus event:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-sonic-scene-writer-event-report.ps1 `
  -SceneStateCsvPath artifacts/compat-runs/<run>/sonic-scene-state.csv `
  -TraceCsvPath "artifacts/compat-runs/<run>/state-range/sonic-input-writes.csv,artifacts/compat-runs/<run>/small-data-range/sonic-input-writes.csv"
```

For the current Sonic bridge/city investigation, run the two focused changed-global ranges and build the join report in one step:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-sonic-scene-writer-probe.ps1 -Configuration Release
```

Pass `-ExtraRunArgs --disable-sonic-resource-fast-forward` to rerun the same focused writer/scene-state probe with Sonic resource lookup/mode/state/fixup fast paths disabled. Add `-SkipWriterReport` for longer A/B probes when you only need the scene-state and small-data phase reports.

Compare two small-data phase event CSVs:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-sonic-small-data-phase-comparison.ps1 `
  -BaselinePhaseCsvPath artifacts/compat-runs/<baseline>/small-data-range/sonic-small-data-phase/small-data-phase-events.csv `
  -CandidatePhaseCsvPath artifacts/compat-runs/<candidate>/small-data-range/sonic-small-data-phase/small-data-phase-events.csv
```

Use `--trace-locked-cache-writes <csv-path> <addr> <len>` when a transform source lives under `0xE0000000`. This traces locked-cache stores with the same PC/opcode/register context and captures the watched range through the bus, so it works for Sonic matrix sources such as `0xE0000090`.

Summarize a locked-cache write trace without hand-reading every paired-single store:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-locked-cache-writes.ps1 `
  -TraceCsvPath artifacts/compat-runs/<run>/locked-cache-writes.csv
```

The summary groups stores by writer PC/disassembly, shows the touched locked-cache address range, keeps common source/destination registers, and decodes the final watched bytes as big-endian words plus single-precision floats.

Build a Sonic matrix producer timeline from a locked-cache range trace:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-sonic-matrix-producer-timeline.ps1 `
  -LockedCacheCsvPath artifacts/compat-runs/<run>/locked-cache-writes.csv `
  -RangeBaseAddress 0xE0000030
```

Capture the range as `0xE0000030 0x90` to cover the parent/current/transform matrix slots. The timeline decodes each 0x30-byte slot into rows and translations, then highlights terminal copy/translation/transpose events.

Run a focused Sonic matrix-producer probe and generate the locked-cache, timeline, orthogonality, matrix-writer, transform-lane, root-matrix, and packet-selection summaries in one directory:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-sonic-matrix-producer-probe.ps1 `
  -NoBuild `
  -RunName sonic-matrix-producer-bridge `
  -MaxInstructions 26783000 `
  -TraceAfter 26770000 `
  -RangeBaseAddress 0xE0000000 `
  -RangeLength 0xC0
```

Use `--disable-sonic-bit-unpack-fast-forward` to force Sonic's bit-unpack/decode loop to execute normally while leaving other fast-forward helpers available. This is useful for A/B checks of packed model input streams.

Summarize Sonic emitter stream records and match them back to a draw packet capture:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\summarize-sonic-emitter-stream.ps1 `
  -EmitterCsvPath artifacts\compat-runs\20260526-sonic-gx-emitter-source-fifo-offset\sonic-gx-emitters.csv `
  -DrawPacketCsvPath artifacts\compat-runs\20260526-sonic-draw-packets-wide-19m\sonic-draw-packets.csv `
  -RecordCsvPath artifacts\compat-runs\20260526-sonic-gx-emitter-source-fifo-offset\sonic-emitter-stream-records.csv
```

Summarize a Sonic draw-packet trace without manually decoding the hex windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\summarize-sonic-draw-packets.ps1 `
  -TraceCsvPath artifacts\compat-runs\20260526-sonic-draw-packets-30m\sonic-draw-packets.csv `
  -JsonPath artifacts\compat-runs\20260526-sonic-draw-packets-30m\sonic-draw-packets.summary.json `
  -RecordCsvPath artifacts\compat-runs\20260526-sonic-draw-packets-30m\sonic-stream0-records.csv
```

Summarize a transform CSV without reopening the full file:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-gx-transforms.ps1 -Path artifacts/compat-runs/<run>/sonic-gx-window-12140-heavy/gx-transforms.csv
```

Both scripts write timestamped directories under `artifacts/compat-runs` with:

- `run.json`: machine-readable compatibility ledger for that target.
- `emulator-summary.json`: per-run emulator stop reason, final PC, GX FIFO byte count, fast-forward counters, and selected GX frame source metadata when a frame is dumped.
- `summary.csv` / `summary.json`: suite-level rollup.
- `auto.png`: auto-selected GX frame.
- `exi.summary.json`: EXI/card milestone counts.
- `gx-copies.summary.json`: display/texture copy counts, nonblack frame milestones, and per-XFB destination lifecycles showing black overwrites after nonblack copies.
- `gx-copy-events.csv`: optional lightweight BP `0x52` copy-event timeline with FIFO offsets, draw indices, copy kind, destination, dimensions, and clear flags.

Summarize existing traces without rerunning a game:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/summarize-exi-trace.ps1 -TracePath artifacts/run/exi.csv
powershell -ExecutionPolicy Bypass -File scripts/summarize-gx-copies.ps1 -CopyCsvPath artifacts/run/gx-copies.csv
powershell -ExecutionPolicy Bypass -File scripts/build-profile-cluster-report.ps1 -RunDirectory artifacts/compat-runs/<run>
```

For presentation bugs, inspect `displayDestinations` and `displayTimeline` in the GX copy summary. They call out cases where a useful display copy exists briefly and is then overwritten by black at the same framebuffer address.
For CPU performance probes, inspect `profile-clusters/profile-clusters.csv`; it groups adjacent hot PCs from `emulator-summary.json` into ranked blocks.
