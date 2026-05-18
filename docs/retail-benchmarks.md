# Retail Benchmark Notes

The local retail benchmark images are user-provided RVZ files in the workspace root:

- `Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz`
- `Pikmin (USA).rvz`
- `Mario Kart - Double Dash!! (USA) (Debug).rvz`

Use short bounded diagnostics first, then raise the instruction budget only when the previous artifact explains the next blocker.

## Current Commands

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj --no-restore -- diagnose-disc "Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz" --max-instructions 3000000 --snapshot-interval 250000 --out artifacts/retail-smoke-display-scale/sonic --name sonic-display-scale
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj --no-restore -- diagnose-disc "Pikmin (USA).rvz" --max-instructions 3000000 --snapshot-interval 250000 --out artifacts/retail-smoke-display-scale/pikmin --name pikmin-display-scale
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj --no-restore -- diagnose-disc "Pikmin (USA).rvz" --max-instructions 12000000 --snapshot-interval 1000000 --out artifacts/retail-smoke-strcmpff2/pikmin-12m --name pikmin-strcmpff2-12m
```

## Dolphin Reference Frames

The local Dolphin build can be used as a visual oracle for short retail boot windows:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/generate-dolphin-reference.ps1 -Seconds 16
```

The script creates a timestamped run under `artifacts/dolphin-reference`, using a disposable Dolphin user profile with PNG frame dumping enabled. It keeps sampled PNGs plus a contact sheet for each benchmark game and removes the full per-frame dump unless `-KeepFullDump` is supplied.

The first generated reference run is `artifacts/dolphin-reference/20260517-125741`:

- Sonic Adventure 2 Battle (`GSNE8P`): 462 Dolphin frames; samples show the Nintendo license, Sega splash, and Sonic Team splash.
- Pikmin (`GPIE01`): 384 Dolphin frames; samples show the Nintendo splash and title screen.

Compare the current emulator output against the latest Dolphin reference run with:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-retail-reference-compare.ps1 -NoBuild
```

The comparison harness writes `our.png`, per-sample `diff.png`, and a summary CSV under `artifacts/retail-reference-compare/<timestamp>`. Use `-RecompareRunRoot <run-dir>` to regenerate diff reports from an existing emulator run without spending time rerunning the games.

The first comparison run is `artifacts/retail-reference-compare/20260517-131419`:

- Sonic at 50.5M instructions still presents the in-game "No Memory Card in Slot A" prompt, while Dolphin's same boot window reaches the Sega/Sonic Team splash sequence. This is a boot-state mismatch first, not yet a same-scene rendering diff.
- Pikmin at 12M instructions produces a full-frame gray diagnostic/display-copy image, while Dolphin reaches the Nintendo splash/title sequence. The next Pikmin target is therefore presentation/copy source selection and boot progression rather than TEV detail.

## Sonic Visible Frame Comparison

Use this pair to compare the draw-time texture snapshot path against final-RAM texture sampling on the 50.5M Sonic window:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj --no-restore -- run-disc "Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz" --max-instructions 50500000 --fast-forward-idle --dump-gx-frame artifacts/sonic-last-nonblack-display-copy-no-auto/frame.png --gx-frame-source last-nonblack-display-copy --gx-frame-max-draws 900 --gx-frame-max-raster-pixels 12000000 --gx-disable-auto-texture-snapshots --no-registers
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj --no-restore -- run-disc "Sonic Adventure 2 - Battle (USA) (En,Ja,Fr,De,Es).rvz" --max-instructions 50500000 --fast-forward-idle --dump-gx-frame artifacts/sonic-last-nonblack-display-copy-auto/frame.png --gx-frame-source last-nonblack-display-copy --gx-frame-max-draws 900 --gx-frame-max-raster-pixels 12000000 --no-registers
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj --no-restore -- compare-images artifacts/sonic-last-nonblack-display-copy-no-auto/frame.png artifacts/sonic-last-nonblack-display-copy-auto/frame.png --diff artifacts/sonic-last-nonblack-display-copy-auto/diff-vs-no-auto-cli.png
```

Expected current comparison result:

- `43,997` changed pixels (`14.322%`)
- baseline nonblack: `53,388` pixels, bounds `130,177-639,316`
- auto-snapshot nonblack: `51,571` pixels, bounds `130,177-639,302`
- diff artifact: `artifacts/sonic-last-nonblack-display-copy-auto/diff-vs-no-auto-cli.png`

## 2026-05-15 Baseline

After the display-copy scale/filter pass:

- Sonic at 3M instructions reaches VI/SI/AI/DI setup and thread scheduling, but no GX FIFO writes are captured yet. The tail is in startup math/audio work around `0x801114xx`/`0x801116xx`/`0x801183xx`.
- Pikmin at 3M instructions writes VI framebuffer registers and GX setup state, but has no draw command in the captured FIFO yet.
- Pikmin at 12M instructions reaches real GX quads. Before cache-loop fast-forward it captured 191,838 FIFO bytes and 736 quads, ending inside a `dcbi; addi +0x20; bdnz` cache maintenance loop.

After cache maintenance fast-forward:

- Pikmin at the same 12M instruction budget captures 242,543 FIFO bytes and 901 quads.
- The run reports `Fast-forwarded 1050585 cache maintenance instruction(s)`.
- The diagnostic GX frame is still mostly startup/clear geometry, but the benchmark now moves deeper per run.

After small leaf, memmove, and texture-sample helper fast-forwards:

- Pikmin at the same 12M instruction budget captures 284,924 FIFO bytes and 952 decoded draws.
- The latest run reports `Fast-forwarded 5752 small leaf helper instruction(s)`, `Fast-forwarded 12243330 memory copy instruction(s)`, and `Fast-forwarded 6831150 texture sample helper instruction(s)`.
- The current tail is around `PC=0x801F24D0`, and the hottest remaining profile cluster is the larger caller-side scan around `0x80027D3C..0x80027E68`, which repeatedly calls the texture-sample helper.

After null-terminated string copy and string compare fast-forwards:

- Pikmin at the same 12M instruction budget now lands later in allocator/accounting code around `PC=0x80047000`.
- The latest run reports `Fast-forwarded 9514 string copy instruction(s)` and `Fast-forwarded 5188668 string compare instruction(s)` in addition to the previous fast-forward buckets.
- This run captures 270,821 FIFO bytes and 557 decoded draws. The lower draw count versus the texture-sample-only run appears to be a phase change at the same interpreter budget rather than a FIFO regression: the run reaches different CPU code after skipping the repeated string-compare work.

After the small word-equality leaf fast-forward:

- A faster `run-disc` probe at 15M instructions, without PNG rendering, reaches object/list traversal around `PC=0x801B1328`.
- The run reports `Fast-forwarded 382332 small leaf helper instruction(s)`, up from 18,062 at the same 15M probe before the word-equality helper.
- The same 15M probe captures 354,037 FIFO bytes and 657 decoded draws. The tail repeatedly calls virtual methods while walking a linked list at `0x801B12EC..0x801B138C`, so it is not a safe broad fast-forward target.

After Sonic startup math/decode fast-forwards:

- The observed trig table at `0x80118300` writes `sin, cos` float pairs beginning at `0x802D7758`; the helper fills the remaining table and skips 11,796,482 startup instructions in the 10M/20M probes.
- Sonic's bit decode cluster around `0x800E1FA0..0x800E2178` is now partially collapsed by exact bit-scan and bit-unpack helpers. The 20M probe reports `Fast-forwarded 34339536 bit unpack instruction(s)`.
- The 20M Sonic probe now gets past the earlier decoder wall and a one-tick wait at `0x80117E0C`, then parks in a flag wait at `0x80022EF0..0x80022EF8`:
  `lwz r0, -31256(r13); cmpi r0,0; ble -8`.
- That flag wait is likely waiting for a subsystem event rather than CPU work. The next step is to identify the owner of `r13 - 31256` before forcing it; likely candidates are async DVD/resource load, audio init, or a scheduler flag.

After callback wait and dot-product fast-forwards:

- The `0x80022EF0` wait is tied to the OS callback slot at `r13 - 29932`, which points at `0x80022F5C`; that callback decrements the active flag at `r13 - 31256`. The exact wait helper now completes that pending callback count only when the registered slot matches the observed Sonic callback.
- The fixed dot-product loop at `0x80128DB8..0x80128F5C` now collapses 64-output blocks. The 20M Sonic probe reports `Fast-forwarded 1713855 dot product instruction(s)`.
- Sonic now reaches GX FIFO writes in the 20M probe. The current tail writes command byte `0x61` and a BP word through `0xCC008000` around `0x80104CEC..0x80104CF8`.

After the first Sonic GX capture and SI controller correction:

- A 20M Sonic GX diagnostic now renders a readable "No Memory Card in Slot A" prompt from the real FIFO stream. The capture in `artifacts/sonic-gx-20m/gx-frame.png` contains 937 decoded triangle-strip draws, 1874 rendered triangles, and 649,340 FIFO bytes.
- The prompt uses textured UI quads with RGB5A3 texture data and TEV modulate/pass-color stages, so the current renderer is no longer only clearing or drawing placeholder geometry.
- SI immediate transfers now use the communication-buffer command byte for controller `0x40/0x41` reads, return status/origin packets instead of stale type data, and acknowledge per-channel read-status data after the game consumes it. This fixes a serial-interrupt storm exposed by the more accurate controller response.
- Delayed input probes (`--controller-button-window a 20000000 24000000` and `--controller-button-window a 22000000 23000000`) both reach the same later CPU paths: at 24M the task-slot scan around `0x80125DEC..0x80125E18`, and at 30M the bitset dispatch around `0x800F3570..0x8010B37C`. The graphics FIFO is still dominated by repeated memory-card prompt draws in these windows, so "past the prompt" is not yet proven visually.
- GX frame dumping now has bounded render windows via `--gx-frame-skip-draws`, `--gx-frame-max-draws`, and `--gx-frame-max-raster-pixels`. A 24M delayed-input slice (`skip=900`, `max=25`, raster budget `500000`) completes and writes `artifacts/sonic-input-window-24m-raster-budget/gx-frame.png`; that particular slice is black, but the capture no longer hangs on large FIFO streams.
- GX frame sweeps can now be generated in one emulator pass with `--dump-gx-frame-sweep <dir> <start-skip> <step> <count>`. A 21M delayed-input smoke sweep wrote `artifacts/sonic-gx-window-sweep-smoke/gx-frame-sweep.csv` and two slices (`skip=0`, `skip=600`); the `skip=600` slice shows a few visible orange text fragments, so the next visible post-prompt signal is likely near that range. A heavier 24M sweep wrote four slices in `artifacts/sonic-gx-window-sweep-24m/` before manual stop, and its summary is flushed incrementally.
- Focused GX diagnostics show the prompt renderer is behaving in this region: a 21M slice at `skip=580`, `max=60`, raster budget `8000000` renders the full "No Memory Card in Slot A" panel cleanly in `artifacts/sonic-gx-window-skip580-focused/gx-frame.png`. Draw dumps at 21M/24M around skips `580..960` are still prompt-style pass-color bands plus RGB5A3 textured strips, not a new scene.
- Targeted SI tracing is now available with `--trace-si <csv-path>`. A 24M late-A run wrote `artifacts/sonic-si-late-a/si.csv`; during the late input window Sonic repeatedly writes `SIPOLL` and reads `SISR`, but does not read the channel input buffers, so it never samples the held A bit. A short held-A-from-boot trace wrote `artifacts/sonic-si-held-a-50k/si.csv` and confirms the current packet encoding does produce `0x01008080` when the game reads `SICxINBUFH`.

After the SI read-status correction and CTR loop fast-forwards:

- `SIPOLL` now marks enabled channels with the SDK-observed per-channel `0x20` read-status bit, and reading `SICxINBUFH/L` clears that bit. The late-A trace in `artifacts/sonic-si-late-a-rdst-set/si.csv` confirms Sonic reads all four channel input buffers during the prompt window and sees `0x01008080` while the A window is active.
- The old repeated-prompt FIFO pattern is gone after the SI fix. A 30M full-range GX render in `artifacts/sonic-gx-30m-post-si/gx-frame.png` is black, which likely means the prompt has been cleared and the next visible scene has not yet drawn within that budget.
- Added exact fast-forwards for `bdnz .` CTR delay loops and the Sonic decompressor's unrolled 8-byte CTR copy loop. The byte-copy helper was tightened to preserve forward overlap semantics after an initial snapshot-copy version exposed an invalid pointer path.
- A 35M Sonic probe with `--controller-button-window a 22000000 23000000` now completes cleanly and lands in math/update code around `0x800220F0..0x80022350`, with registers showing active data pointers in main RAM rather than the prior SI/task-scan loops.

After Sonic copy-loop fast-forwards and EFB-clear diagnostics:

- Added exact fast-forwards for Sonic's one-byte CTR remainder copy loop and the 32-byte unrolled word-copy loop at `0x8010C128..0x8010C16C`. The 50M Sonic probe now completes cleanly and tails in a bounded scan/list loop around `0x80116BBC..0x80116BE4`.
- A 50M GX sweep after the SI fix wrote black final-EFB images even though draw diagnostics found 2,076 decoded draws, including nonblack prompt/title-style textured strips. This showed the renderer was probably looking at the final cleared EFB after EFB-to-texture copies.
- Added diagnostic `--gx-frame-ignore-efb-copy-clear`, which leaves EFB contents intact after BP copy command `0x52`. With that option, the 50M Sonic capture at `artifacts/sonic-gx-50m-ignore-efb-clear/gx-frame.png` shows visible Sonic title/menu UI. The next graphics target is therefore presentation/copy-output correctness, not basic FIFO decode.
- Added `--gx-frame-source <efb|last-display-copy>` so GX dumps can decode the most recent display-copy target from emulated RAM. The 50M Sonic `last-display-copy` capture at `artifacts/sonic-gx-50m-last-display-copy/gx-frame.png` is black, so the visible title/menu content currently appears in pre-clear EFB or texture-copy traffic rather than the final display-copy target.
- Added `--dump-gx-copies <csv-path>` to replay FIFO rendering and log every BP `0x52` EFB copy with draw index, kind, destination, dimensions, clear flags, and source coverage. The first version alpha-gated coverage and hid Sonic's RGB-visible/alpha-zero output; the corrected RGB coverage in `artifacts/sonic-gx-copies-50m-rgb/gx-copies.csv` shows 24 nonblack display copies. The first is copy 5 after draw 4, with 46,741/307,200 RGB-nonblack source pixels and zero alpha-visible pixels, then the copy clear returns EFB to black.
- Added `--dump-gx-coverage <csv-path>` for per-draw EFB RGB coverage deltas and raster-budget accounting. The 50M Sonic coverage in `artifacts/sonic-gx-coverage-50m-rgb/gx-coverage.csv` shows RGB content starts at draw 2, alpha stays zero, and the diagnostic raster budget reaches zero at draw 97. A focused `last-display-copy` dump capped at four draws wrote `artifacts/sonic-gx-first-nonblack-display-copy-50m/gx-frame.png`, confirming the early display-copy target contains the dim Sonic title/menu layer before later black display copies overwrite the latest XFB.
- Coverage diagnostics now honor the existing GX draw window (`--gx-frame-skip-draws` / `--gx-frame-max-draws`), and draw diagnostics print vertex alpha without breaking the old color/texcoord text. An 80M global raster-budget run in `artifacts/sonic-gx-coverage-50m-rgb-80m-raster/gx-coverage.csv` reaches draw 689 before exhausting its budget, proving the previous draw-97 cutoff was diagnostic budget pressure rather than the FIFO going idle.
- Windowed coverage at `skip=680,max=160` in `artifacts/sonic-gx-coverage-50m-window-skip680/gx-coverage.csv` shows the same three-draw dim UI cycle continues past the global budget cliff. A later `skip=1500,max=160` window in `artifacts/sonic-gx-coverage-50m-window-skip1500/gx-coverage.csv` switches to a smaller lower-screen band around draws 1534-1660. The focused display-copy capture at `artifacts/sonic-gx-display-copy-50m-skip1500/gx-frame.png` shows that lower green/blue strip.
- Added `--dump-gx-tev-samples <csv-path>` to sample representative draw triangles through the current TEV evaluator and log raster color, TEV output, alpha-test result, color/alpha update enables, copy count, and per-stage inputs. The command is observational and uses the same draw window as frame dumping, so it can be paired with `--gx-frame-skip-draws` / `--gx-frame-max-draws` without mutating EFB output.
- Early Sonic samples in `artifacts/sonic-gx-tev-samples-50m-early/gx-tev-samples.csv` show the first visible UI layer uses very low vertex/raster alpha. Draw 2 passes through `180/108/40/4`, while the next two textured modulate draws evaluate to `53/0/5/0` and `123/0/64/0` at the sampled centroid and fail alpha test. Alpha update is disabled throughout, matching the earlier alpha-zero display-copy coverage.
- The late lower-band samples in `artifacts/sonic-gx-tev-samples-50m-skip1500/gx-tev-samples.csv` contain 160 sampled draws: 129 `Modulate` draws and 31 alternate pass-color stage-0 cases, which were initially labeled `Unknown` before the classifier learned the `(zero, raster, one, zero)` raster pass-through form. All pass alpha test, all enable color writes, and all disable alpha writes. The nonblack modulate rows repeatedly combine grey vertex fade values with a dark `0/32/8/255` texture sample, producing outputs that ramp up to `0/32/8/255`. This points toward either correct fade/texture-copy content or a texture-address/source-format issue, not a broad TEV color-combiner failure.
- TEV sample diagnostics now choose representative first/middle/last triangles for larger draws and sample four interior barycentric points per selected triangle. The updated early Sonic run in `artifacts/sonic-gx-tev-samples-50m-early-multi/gx-tev-samples.csv` writes 32 rows across the first four draws. It shows the earlier centroid-only transparent reads were incomplete: the same textured quads also hit opaque texture samples such as `115/99/222/255` and `255/255/255/255`, pass alpha, and emit visible TEV output.
- The updated late lower-band run in `artifacts/sonic-gx-tev-samples-50m-skip1500-multi/gx-tev-samples.csv` writes 1,280 rows across 160 draws. It still stays consistently dark across the expanded sample pattern, with texture samples concentrated at `0/32/8/255`, `0/0/16/255`, `0/65/0/255`, and `0/65/8/255`. The 248 rows labeled `Unknown` in that CSV are the same alternate pass-color form and will be labeled `PassColor` on the next capture. The dark green/blue strip is therefore unlikely to be a one-point sampling artifact; the next check should compare the texture-copy source and display-copy path feeding those texels.
- Copy diagnostics now include representative source/readback samples and a `texture_readback_mismatches` count for EFB texture copies. The 50M Sonic run in `artifacts/sonic-gx-copy-readback-50m/gx-copies.csv` logs 3,247 copies: 2,164 texture copies and 1,083 display copies. Every texture-copy readback sample reports zero mismatches, and no texture copy targets the late strip's sampled address `0x00696600`.
- TEV sample summaries now include texture address, format, size, wrap, UV, and texel coordinates. The late strip run in `artifacts/sonic-gx-tev-samples-50m-skip1500-texinfo/gx-tev-samples.csv` shows all textured samples come from a CMPR game asset at `0x00696600`, size `256x32`, not from EFB texture-copy traffic. This narrows the late strip to either correct asset content, CMPR sampling/filter details, or higher-level presentation/scene timing, not EFB copy encoding.
- Fixed CMPR/DXT1 selector handling for the `color0 <= color1` case: selector 3 now decodes as transparent black instead of opaque average color. A post-fix Sonic TEV sample in `artifacts/sonic-gx-tev-samples-50m-skip1500-cmpr-fix/gx-tev-samples.csv` is byte-for-byte equivalent in sampled TEV/alpha results for this window, so the late strip is not caused by that CMPR transparent-selector bug.

- Texture diagnostics now decode mode0 filter and LOD state, and the software sampler honors basic bilinear filtering when the texture's mag filter is linear. A corrected focused Sonic rerun in `artifacts/sonic-gx-tev-samples-50m-skip1500-filter-lod-corrected/gx-tev-samples.csv` shows the late strip texture uses `wrap=repeat:repeat/filter=linear:linear/lod=bias0:min0:max0`, so this asset is not using mip filtering in the sampled window. Linear sampling still changes 512 of 1,280 sampled rows versus the old nearest-only diagnostic, with common samples shifting from `0/0/16/255` to `0/0/14/217` or from `0/32/8/255` to `0/50/8/255`. The strip is still dark blue-green, but the next target is no longer mipmapping for this case; it is broader presentation timing or scene selection.

- Added smarter display-copy frame sources: `last-nonblack-display-copy` and `largest-display-copy`. The renderer snapshots selected display-copy pixels at copy time, so later black copies to the same XFB address no longer overwrite the diagnostic selection. At 50M Sonic, `artifacts/sonic-gx-50m-last-nonblack-display-copy/gx-frame.png` recovers the late nonblack XFB with 52,981 nonblack pixels, and `artifacts/sonic-gx-50m-largest-display-copy/gx-frame.png` picks a slightly fuller variant with 53,655 nonblack pixels. Both are still the lower title/menu strip rather than a complete scene, which means the final black frame was a selection problem, but the emulator has still not reached/presented a full post-prompt scene by 50M.
- Added `--dump-pointer-table <addr> <count> <stride> <ptr-offset> <target-words>` for compact resource/list diagnostics. Sonic's `0x80116B9C` tail is a 0x500-entry, 0x18-byte resource table lookup loaded from `r13 - 29196/-29192`; at the first late stop around 45.5M it searched for key `0x0089549A` before that object was registered, while the 50M table contains 41 active resources including `0x0089549A..0x008954A9`. A 50M-era stop now searches for `0x007A1201`, and the PC profile is dominated by the variable bitstream/decompression routine at `0x800D2684..0x800D2824`, so the next CPU-side target is understanding resource decode/progression rather than treating the table scan as an idle wait.

Workflow update:

- Added `scripts/run-retail-benchmarks.ps1` and `scripts/run-sonic-check.ps1` for repeatable Sonic/Pikmin compatibility probes. The harness applies a wall-clock watchdog, emits progress updates, captures auto-selected GX frames, EXI traces, GX copy CSVs, per-target `run.json` ledgers, and suite-level `summary.csv` / `summary.json`.
- Added `scripts/summarize-exi-trace.ps1` and `scripts/summarize-gx-copies.ps1` so old artifacts can be converted into compact milestone JSON without rerunning retail images. The EXI summary tracks memory-card command milestones such as `0x52` read-array sector reads; the GX summary tracks display/texture copy counts, nonblack display-copy milestones, and per-XFB destination lifecycles so black overwrites after useful frames are visible at a glance.
- Tuned the default `sonic-20m` benchmark to use a lighter GX frame budget and skip copy CSV generation. The full copy-heavy path remains available through `-DeepGx`, but the routine four-target suite now stays useful as a bounded regression loop.
- Added `--run-summary <json-path>` and wired it into the retail benchmark harness. Each target now writes `emulator-summary.json`, and `summary.csv` includes executed instructions, stop reason, and final PC without requiring console-log parsing.
- Added a narrow Pikmin heap-wait fast-forward for the `0x800466C0..0x800466C8` poll loop. It advances emulated hardware time to video interrupt opportunities instead of modifying the waited-on game flag directly. The 20M Pikmin benchmark now moves from the heap wait at `0x800466C8` to runtime code around `0x80045B90`, with GX FIFO capture increasing from roughly 230K to 549K bytes.
- Added optional `mariokart-debug-5m` and `mariokart-debug-20m` benchmark targets. The debug build first exposed missing CPU `mffs`; after implementing it, the next blocker was its JAS/DSP task-ready callback at `0x800A4880`, which now completes through a recognized byte-set callback shape instead of spinning at `0x800A49A0`/`0x800A5040`. The current wall is the debug image's MetroTRK/NUB path, so the Mario Kart targets skip GX frame/copy dumps until execution reaches game-rendering code.
- Mario Kart's debug image is useful as a CPU/DSP/debug-runtime probe, but not yet as a rendering benchmark: MetroTRK/NUB startup dominates before GX FIFO traffic appears. Added exact fast-forwards for the SDK `strlen` leaf, the varargs register-save stub, and trivial `return 0` leaves; a 20M probe now reports roughly 2.9M string-length and 5.8M small-leaf instructions skipped while still ending inside debug monitor startup with zero GX FIFO bytes.

Next useful targets:

- For Sonic, inspect whether the dim/low-alpha UI is expected fade behavior or a TEV/blend accuracy issue. Good next probes are TEV sample diagnostics for representative early and late windows, display-copy YUYV range comparison, and windowed `last-display-copy` sweeps across draw ranges instead of one huge global raster budget.
- Profile the current `0x800D2684..0x800D2824` decompressor/resource decode path and map its callers/inputs before adding any exact fast-forward; it has variable bitstream control flow and is tied to resource table growth.
- Analyze the `0x80027D3C..0x80027E68` nested scan carefully before considering any exact fast-forward; it has enough control flow that a subsystem/CPU correctness bug would be easier to hide.
- Inspect allocator/accounting behavior around `0x80046FE4..0x8004713C`; if it turns into a hot loop, prefer implementing the missing lower-level memory/runtime behavior over broad allocation fast-forwards.
- For the current `0x801B12EC..0x801B138C` tail, trace the virtual target at `lwz r12, 52(r12)` and improve the underlying behavior it calls; do not skip the list walk wholesale because each callback may mutate gameplay/object state.
- Improve GX diagnostics by summarizing copy boundaries, copy destinations, clear flags, and nonblack draw counts across the full FIFO.
