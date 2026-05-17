# GX Fixture Ladder

This repo now has a repeatable set of GameCube GX homebrew DOLs for renderer work.

## Build

```powershell
.\scripts\build-gx-fixtures.ps1
```

Outputs are copied to `artifacts/devkitpro`.

## Local Deterministic Fixture

`fixtures/devkitpro/gx-ladder` is a small source-built fixture for emulator debugging. It renders:

- direct colored triangle
- direct colored quad
- textured quad using `GX_TF_I4`
- textured quad using `GX_TF_I8`
- textured quad using `GX_TF_IA4`
- textured quad using `GX_TF_IA8`
- textured quad using `GX_TF_RGB565`
- textured quad using `GX_TF_RGB5A3`
- textured quad using `GX_TF_RGBA8`
- textured quad using `GX_TF_CMPR`
- textured quad using `GX_TF_CI4` with a `GX_TL_RGB565` TLUT
- textured quad using `GX_TF_CI8` with a `GX_TL_RGB565` TLUT
- textured quad using `GX_REPLACE` with a non-white vertex color
- textured quad using `GX_MODULATE` with a non-white vertex color

The texture bytes are generated in code using GameCube tiled layouts and loaded through `GX_InitTexObj`/`GX_LoadTexObj`, so this exercises CP/VAT/VCD, BP texture image state, tex0 coordinates, TEV texture order, EFB copy, and VI presentation without external asset conversion.
The color-index cases also exercise `GX_InitTlutObj`, `GX_LoadTlut`, and `GX_InitTexObjCI`.
The final TEV panels use the same RGB565 texture with different vertex colors so the diagnostic renderer can prove `GX_REPLACE` ignores raster color and `GX_MODULATE` tints by raster color.

Useful ngcSharp smoke run:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- `
  run-dol artifacts/devkitpro/gx-ladder.dol `
  --max-instructions 12000000 `
  --quiet `
  --no-registers `
  --fast-forward-idle `
  --dump-gx-draws artifacts/devkitpro/gx-ladder-draws.txt `
  --dump-gx-frame artifacts/devkitpro/gx-ladder-gx.png `
  --dump-frame artifacts/devkitpro/gx-ladder-xfb.png `
  --frame-width 640 `
  --frame-height 480 `
  --frame-format yuyv
```

## Stock devkitPro GX Examples

The build script also copies these known-good libogc examples:

- `triangle.dol`
- `texturetest.dol`
- `gxSprites.dol`
- `acube.dol`
- `lesson01.dol` through `lesson12.dol`
- `lesson19.dol`

These are broader compatibility targets than `gx-ladder`; use them after the deterministic fixture is stable.

## GX Demo Sweep

Run a timed diagnostic sweep against selected DOLs:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,gxSprites,lesson06,lesson07 -OutputDirectory artifacts\gx-sweep-fast -MaxInstructions 3000000 -GxFrameMaxDraws 120 -TimeoutSeconds 35
```

Each demo gets its own output directory with:

- `gx-frame.png`
- `xfb-frame.png`
- `gx-draws.txt`
- `stdout.txt`
- `stderr.txt`

The sweep writes `summary.tsv` with status, draw counts, first visible draw, and PNG sizes. `-GxFrameMaxDraws` caps diagnostic rasterization work for PNG generation; draw diagnostics still scan the captured FIFO. Slow demos are marked `timeout` so one long-running case does not block the whole batch.

Recent useful sweep:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,gxSprites,lesson06,lesson07,lesson08,lesson09,lesson10,lesson11,lesson12,lesson19 -OutputDirectory artifacts\gx-sweep-wide1 -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

That pass confirmed the diagnostic renderer can produce nonblank GX PNGs across the selected stock demos. `lesson10` is a good texture-addressing regression target because it uses CMPR texture coordinates outside `[0,1]`; mode0 `GX_REPEAT` is now decoded and shown in `gx-draws.txt` as `wrap=(repeat, repeat)`.

After the first diagnostic Z-buffer pass, a smaller depth check is useful:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson06,lesson07,lesson10 -OutputDirectory artifacts\gx-sweep-depth-check -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

This should keep the simpler texture demos nonblank while showing less overdraw in `lesson10`.

For the scissor and perspective-correct UV pass, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson06,lesson07,lesson10 -OutputDirectory artifacts\gx-sweep-perspective-scissor -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

`lesson10/gx-draws.txt` should show the full-screen scissor as `decoded=(0,0)-(639,479)`.

For the conservative projection clip guard, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson06,lesson07,lesson10 -OutputDirectory artifacts\gx-sweep-clip-guard -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

`lesson10/gx-draws.txt` should include `clipped=<n>` in decoded bounds for triangles with vertices behind the camera.

For the view-space camera-plane clipping pass, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson06,lesson07,lesson10 -OutputDirectory artifacts\gx-sweep-nearclip -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

Compared with `artifacts/gx-sweep-clip-guard`, `lesson10/gx-frame.png` should preserve more of the visible room geometry while keeping the simple texture demos stable.

For the first XF texture-coordinate generation pass, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson06,lesson07,lesson08,lesson10,gx-ladder -OutputDirectory artifacts\gx-sweep-texgen2 -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

`lesson08/gx-draws.txt` should show `tex0-base=0x001E` and `projection=mtx3x4`, while `gx-ladder/gx-draws.txt` should show identity `tex0-base=0x003C` with `projection=mtx2x4`. The sweep should keep all six targets `ok` with nonzero GX PNG sizes.

For the first TEV blend and PE blend pass, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols lesson08,lesson09,gxSprites,gx-ladder -OutputDirectory artifacts\gx-sweep-tev-blend-fix -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

`lesson08/gx-draws.txt` should decode stage 0 as `mode=Blend` and its additive PE state as `blend-mode=blend, src=src-alpha, dst=one`. `gxSprites/gx-draws.txt` should decode the more common sprite path as `src=src-alpha, dst=inv-src-alpha`.

For stage-0 TEV order, texture-map selection, and texcoord selection, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson08,lesson09,gxSprites,gx-ladder,lesson10 -OutputDirectory artifacts\gx-sweep-texcoord-select -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

All six targets should report `ok`. Draw diagnostics should include `order=texcoord.../texmap.../color.../tex-enabled=...` beside the stage-0 TEV mode, and the software renderer has regression coverage proving stage 0 can sample a selected nonzero texture map and nonzero texcoord instead of always forcing map 0 and tex0.

For the first generic multi-stage TEV evaluator pass and `GEN_MODE` TEV-stage-count decode, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson08,lesson09,gxSprites,gx-ladder,lesson10 -OutputDirectory artifacts\gx-sweep-tev-genmode -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

All six targets should report `ok`. Draw diagnostics should include `BP gen mode raw: ... tev-stages=...`. The unit suite includes a two-stage regression where stage 0 writes raster color and stage 1 replaces it with a texture sample, plus a stale-stage regression proving `GEN_MODE` limits active TEV evaluation.

For TEV konst color/alpha selection, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson08,lesson09,gxSprites,gx-ladder,lesson10 -OutputDirectory artifacts\gx-sweep-tev-konst1 -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

All six targets should report `ok`. The software renderer decodes TEV KSEL registers `0xF6..0xFD`, supports fractional konst selectors, and tracks `GX_SetTevKColor`-style K color writes through BP registers `0xE0..0xE7` when bit 23 is set. Unit coverage includes `GX_CC_KONST` rendering from both a fractional selector and K0 register color.

For TEV raster/texture swap modes, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson08,lesson09,gxSprites,gx-ladder,lesson10 -OutputDirectory artifacts\gx-sweep-tev-swap1 -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

All six targets should report `ok`. The evaluator applies `GX_SetTevSwapMode` raster/texture table selectors from the TEV alpha env low nibble and decodes `GX_SetTevSwapModeTable` channel mappings from the low nibbles of BP `0xF6..0xFD`, while preserving the common libogc default tables.

For TEV compare color/alpha operations, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson08,lesson09,gxSprites,gx-ladder,lesson10 -OutputDirectory artifacts\gx-sweep-tev-compare1 -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

All six targets should report `ok`. The evaluator recognizes compare ops through the TEV bias field and supports color R8/GR16/BGR24/RGB8 comparisons plus scalar alpha A8 comparisons, with unit coverage that makes RGB8 and alpha-compare results visible through rendered pixels.

For the first indirect TEV diagnostic pass, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson08,lesson09,gxSprites,gx-ladder,lesson10 -OutputDirectory artifacts\gx-sweep-tev-indirect-diag1 -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

All six targets should report `ok`. Draw diagnostics decode `GEN_MODE` indirect-stage count, per-TEV-stage indirect registers `0x10..0x1F`, direct/no-op indirect encodings, indirect texture order from BP `0x27`, and indirect coordinate scale from BP `0x25..0x26`. This pass is visibility only; indirect offsets are not applied to texture coordinates yet.

For the first simple indirect TEV sampling pass, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson08,lesson09,gxSprites,gx-ladder,lesson10 -OutputDirectory artifacts\gx-sweep-tev-indirect-offset1 -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

All six targets should report `ok`. The renderer applies a deliberately narrow indirect offset path for `GX_ITF_8`, `GX_ITB_ST`, matrix off, no addprev, no utc-lod, and wrap off, using BP `0x27` to sample the configured indirect texture stage before the regular texture lookup. Unit coverage verifies that an indirect RGB565 offset texture can shift a regular texture sample from red to green.

For indirect TEV normal matrix support, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson08,lesson09,gxSprites,gx-ladder,lesson10 -OutputDirectory artifacts\gx-sweep-tev-indirect-matrix1 -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

All six targets should report `ok`. The renderer decodes indirect matrix BP registers `0x06..0x0E`, reconstructs the signed 11-bit coefficients and scale exponent, and applies normal matrices `GX_ITM_0..GX_ITM_2` to the simple indirect ST-offset path. Unit coverage verifies a matrix-scaled indirect offset that shifts the regular texture sample.

For indirect TEV wrap support, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson08,lesson09,gxSprites,gx-ladder,lesson10 -OutputDirectory artifacts\gx-sweep-tev-indirect-wrap1 -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

All six targets should report `ok`. The renderer applies indirect wrap modes before adding the indirect offset: `GX_ITW_0` zeroes the regular coordinate, while `GX_ITW_16..GX_ITW_256` wrap in texel space relative to the regular texture dimensions. Unit coverage verifies the zero-wrap path.

For indirect TEV addprev/repeat support, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson08,lesson09,gxSprites,gx-ladder,lesson10 -OutputDirectory artifacts\gx-sweep-tev-indirect-addprev1 -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

All six targets should report `ok`. The renderer now carries a per-pixel accumulated indirect offset through active TEV stages, applies `addprev` after the current simple ST offset and optional normal indirect matrix, and recognizes the common repeat-previous form used by `GX_SetTevIndRepeat`. Unit coverage verifies both additive accumulation and repeat-without-new-sample behavior.

For the first dynamic indirect matrix pass, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson08,lesson09,gxSprites,gx-ladder,lesson10 -OutputDirectory artifacts\gx-sweep-tev-indirect-dynmtx1 -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

All six targets should report `ok`. The simple indirect offset path now decodes bias as an S/T component mask, accepts the low-bit-count indirect formats `GX_ITF_5`, `GX_ITF_4`, and `GX_ITF_3` by extracting offset bits from the high end of the sampled component, and supports dynamic S/T matrix selectors `GX_ITM_S0..S2` and `GX_ITM_T0..T2` as diagnostic approximations. Unit coverage verifies a dynamic S-matrix offset feeding repeat.

For the practical TEV close-out pass, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson08,lesson09,gxSprites,gx-ladder,lesson10 -OutputDirectory artifacts\gx-sweep-tev-closeout1 -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

All six targets should report `ok`. This pass adds unsigned `GX_SetTevColor` register constants for `GX_TEVREG0..2`, applies the TEV order raster-color selector instead of always using vertex color, supports `GX_COLORZERO`, diagnostic `GX_ALPHA_BUMP`/`GX_ALPHA_BUMPN` raster color from the current stage's indirect alpha selector, and applies `GX_SetIndTexCoordScale` before indirect texture lookup. Unit coverage verifies register constants, indirect coordinate scale, and bump alpha as raster color.

For the first EFB-to-texture copy pass, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson08,lesson09,gxSprites,gx-ladder,lesson10 -OutputDirectory artifacts\gx-sweep-efb-copy1 -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

All six targets should report `ok`. Draw diagnostics now decode the common BP copy path: source rectangle registers `0x49/0x4A`, destination register `0x4B`, destination metadata `0x4D`, and copy control `0x52`. The software renderer treats texture copies (`0x52` without display-copy bit 14) as a mid-FIFO operation and writes the current diagnostic RGB/alpha framebuffer into emulated main RAM in tiled `I4`, `I8`, `IA4`, `IA8`, `RGB565`, `RGB5A3`, or `RGBA8` layout. Later texture image registers that point at the copied address can sample those bytes in the same render pass. Unit coverage verifies RGB565 EFB copy reuse and copy-state diagnostics.

For the first display/XFB copy pass, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson08,lesson09,gxSprites,gx-ladder,lesson10 -OutputDirectory artifacts\gx-sweep-efb-display-copy1 -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

All six targets should report `ok`. Display copies (`0x52` with bit 14 set) now write a diagnostic YUYV framebuffer into emulated main RAM, and `run-dol` runs the GX diagnostic renderer before `--dump-frame` when both dumps are requested so the XFB PNG can observe that copy. Copy-clear currently clears the diagnostic RGB/alpha EFB region to black. Unit coverage verifies display-copy bytes through `FramebufferDumper.CaptureRgb` and clear-after-copy behavior.

For the copy-clear and texture destination-width pass, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson08,lesson09,gxSprites,gx-ladder,lesson10 -OutputDirectory artifacts\gx-sweep-efb-copy-clear1 -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

All six targets should report `ok`. Copy-clear now decodes the `GX_SetCopyClear` BP registers: `0x4F` for red/alpha, `0x50` for blue/green, and `0x51` for the diagnostic 24-bit Z clear value. Clear-after-copy fills the diagnostic RGB/alpha EFB with the configured clear color instead of always black. Texture EFB copies also derive their destination row stride from the `0x4D` tile metadata for supported formats, so copies whose destination width is wider than the source rectangle land in the same tiled layout later texture fetches expect. Unit coverage verifies copy-clear diagnostics, configured clear color, and RGB565 destination-width stride.

For the display-copy scale/filter pass, use:

```powershell
.\scripts\run-gx-demo-sweep.ps1 -Dols texturetest,lesson08,lesson09,gxSprites,gx-ladder,lesson10 -OutputDirectory artifacts\gx-sweep-display-scale1 -MaxInstructions 3000000 -GxFrameMaxDraws 160 -TimeoutSeconds 45
```

All six targets should report `ok`. Display EFB copies now derive XFB row width from display copy destination metadata `0x4D`, derive output line count from vertical scale register `0x4E` using libogc's line-count formula, decode copy gamma/clamp/frame-field bits from copy control `0x52`, and apply a diagnostic vertical filter from BP `0x53/0x54` when taps are present. Unit coverage verifies scaled YUYV XFB writes and display-copy scale/filter diagnostics.
