# ngcSharp GameCube Emulator Plan

## Goal

Build a clean-room Nintendo GameCube emulator in C#/.NET that starts with homebrew DOL execution and grows toward commercial-disc compatibility.

This is a large, multi-year emulator project if the target is broad game compatibility. The practical path is staged: first a debuggable interpreter and minimal hardware, then graphics/audio/input, then correctness and performance work.

## Research Summary

Primary references:

- Dolphin: mature open-source GameCube/Wii emulator, C/C++, GPLv2+. Use as behavioral reference and test oracle, not as copy-paste source unless we intentionally accept GPL-compatible licensing.
- YAGCD / GC-Forever: public GameCube hardware notes, memory map, DOL/GCM/GCI formats, Gekko paired-single instruction notes, MMIO register documentation.
- WiiBrew memory map: useful cross-check for memory and hardware register ranges.
- Rodrigo Copetti's GameCube architecture write-up: useful system-level explanation of how Gekko, Flipper, DSP, DI, SI, EXI, AI, VI, and PI relate.
- Nintendo GX documentation mirrors: useful to understand the GX API abstraction that games use to feed Flipper command streams.

Key findings:

- CPU: IBM Gekko, a PowerPC 750-family CPU with GameCube-specific paired-single instructions.
- Main memory: 24 MiB RAM, cached at `0x80000000..0x817fffff`, uncached at `0xC0000000..0xC17fffff`.
- Other important regions: EFB at `0xC8000000`, hardware registers at `0xCC000000`, IPL boot ROM at `0xFFF00000`.
- Hardware register blocks include CP, PE, VI, PI, MI, AI, DI, SI, EXI, streaming interface, and GX FIFO.
- Disc images contain an apploader, main DOL executable, and FST. Standalone DOL support is the best first milestone.
- Mature emulators use multiple CPU backends. Dolphin has interpreter, cached interpreter, and JIT paths; ngcSharp should begin with an interpreter, then add a cached interpreter, then possibly a JIT.
- DSP has HLE and LLE tradeoffs. HLE is the likely first path for audio; LLE can come later.

## Proposed Repository Shape

- `src/NgcSharp.Core`
  - CPU, memory bus, scheduler, MMIO devices, loader formats, save states.
- `src/NgcSharp.Cpu`
  - PowerPC/Gekko decoder, interpreter, disassembler, instruction tests.
- `src/NgcSharp.Hw`
  - PI, MI, VI, AI, DI, SI, EXI, DSP, GX FIFO-facing device models.
- `src/NgcSharp.Video`
  - GX command parser, texture decoding, renderer backends.
- `src/NgcSharp.Audio`
  - DSP HLE, mixer, output buffering.
- `src/NgcSharp.Input`
  - GameCube controller abstraction and mappings.
- `src/NgcSharp.App`
  - UI/CLI host, game loading, debugger panes.
- `tests/`
  - CPU golden tests, loader tests, MMIO tests, homebrew smoke tests.
- `tools/`
  - Test ROM runners, trace comparison tooling, fixture generators.

Recommended target: .NET 10 LTS for a greenfield project in 2026.

## Architecture

### Core Loop

Use a deterministic scheduler:

1. CPU executes a bounded number of cycles.
2. Devices advance by cycle budget or event deadline.
3. Interrupts are delivered through PI.
4. VI frame timing, AI audio timing, DMA completions, and DI/SI/EXI callbacks are scheduled events.

Keep global mutable state explicit in a `ConsoleState` object so save states and tests are tractable.

### CPU

Phase 1 CPU should be an interpreter:

- Big-endian instruction fetch and data access.
- GPRs, FPRs, CR, LR, CTR, XER, FPSCR, MSR, SRs, time base, decrementer, relevant SPRs.
- Integer arithmetic/logical instructions.
- Load/store, byte-reversed load/store, multiple/string load/store.
- Branching and condition register instructions.
- Floating point and FPSCR behavior.
- Exceptions needed by homebrew and SDK code.
- Gekko paired-single instructions and quantized loads/stores.

Implementation style:

- Table-driven decode from primary opcode and extended opcode.
- `ref struct` or plain structs for hot decode results to avoid allocations.
- Separate disassembler from executor.
- Unit-test instructions in isolation with before/after CPU states.

Later CPU stages:

- Cached interpreter: decode basic blocks once.
- Optional JIT: dynamic IL or native code generation. Do this only after interpreter correctness is good.

### Memory And MMIO

Start with a fast bus:

- RAM as a single byte array with big-endian helpers.
- Address translation for physical, cached, and uncached mirrors.
- MMIO dispatch by register-page table for `0xCC00xxxx`.
- Access logging for unknown/unimplemented registers.

Implement boot low-memory initialization enough to run DOLs without IPL.

### Loaders

Milestones:

1. Standalone DOL loader.
2. ELF loader for homebrew/debugging.
3. GCM/ISO parser enough to extract disc header, apploader, FST, and main DOL.
4. Memory card image/GCI support.

Do not include copyrighted IPL/BIOS or game assets. Support user-provided IPL later, plus HLE boot for normal development.

### Devices

Initial device order:

1. PI/MI interrupts and interrupt masks.
2. VI timing and framebuffer presentation.
3. SI controller reads for port 1.
4. EXI stub for SRAM, RTC, memory card identity, and simple card operations.
5. DI enough for disc reads from ISO/GCM.
6. AI audio DMA timing.
7. DSP HLE.
8. GX FIFO command ingestion.

### Graphics

First renderer should favor visibility over accuracy:

- Parse GX FIFO commands.
- Track CP, BP, XF state.
- Decode vertex arrays and indexed attributes.
- Decode common texture formats.
- Implement TEV in a simplified shader path first.
- Present XFB copies through VI.

Recommended backend:

- Use Silk.NET with Vulkan or OpenGL, depending on quickest local bring-up.
- Keep renderer behind an interface so a null/software renderer can exist for tests.

Accuracy work later:

- EFB behavior, embedded framebuffer formats, copies, depth, fog, alpha, bounding box, texture cache invalidation, unusual TEV cases.

### Audio

Start with silence plus correct timing. Then:

- AI DMA buffering.
- DSP HLE for common ucode.
- ADPCM stream handling.
- Later optional DSP LLE once CPU and scheduling are stable.

### Input

Expose a device-level controller state:

- Digital buttons.
- Analog sticks and triggers.
- Rumble command handling.
- Keyboard/gamepad mappings in app layer.

### Debugger

Build debugger support early:

- CPU trace log.
- Breakpoints and watchpoints.
- Disassembly view.
- MMIO access log.
- Memory viewer.
- Frame/event timeline.
- Dolphin trace comparison mode where possible.

## Milestones

### M0: Project Bootstrap

- Create .NET solution and projects.
- Add formatting/analyzers.
- Add CLI host.
- Add test project and CI.
- Define clean-room contribution policy and fixture policy.

Done when `dotnet test` passes and CLI can print version/help.

### M1: DOL Loader And Memory

- Implement big-endian binary reader.
- Implement DOL parser.
- Map text/data sections into emulated RAM.
- Initialize low-memory globals needed by simple homebrew.

Done when a DOL fixture loads at the expected entry point.

### M2: PowerPC Interpreter Foundation

- Implement integer core, branches, load/store, CR/LR/CTR/XER.
- Add disassembler.
- Add instruction unit tests.
- Add trace output.

Done when small hand-authored PPC programs execute correctly in RAM.

### M3: Exceptions, Timers, And Interrupts

- Implement MSR, exception vectors, time base, decrementer.
- Implement PI interrupt routing.
- Implement minimal VI periodic interrupt.

Done when homebrew using OS init/timing reaches its main loop.

Current status: partial but useful. The bus now advances a lightweight VI scanline clock during CPU execution, reads from `0xCC00206C` return a moving vertical count, VI vblank raises PI external interrupts, and CPU exception entry clears both `EE` and `RI` before vectoring to `0x80000500`. This is enough for libogc vblank waits to wake correctly in the devkitPro framebuffer/pageflip examples.

### M4: Minimal SI/EXI/DI

- Implement controller polling enough for one standard pad.
- Implement RTC/SRAM stubs.
- Implement basic memory card responses.
- Implement ISO/GCM reads through DI.

Done when simple homebrew can read input and basic disc metadata.

Current status: SI now has a stateful standard-controller happy path. It reports a connected GameCube controller type, returns neutral/configured button data, completes simple SI command transfers, models `SIPOLL`, `SICOMCSR`, `SISR`, the SI EXI lock bit, and the `0xCC006480` communication buffer enough for SDK-style controller polling. EXI now has stateful channel registers, immediate/DMA transfer completion, interrupt masking/acknowledge behavior, an internal channel 0 device for RTC and SRAM reads/writes, and optional memory-card devices for slot A/B with identity, status, clear-status, block read, block write, sector erase, and card erase backed by deterministic in-memory storage. DI now covers the normal retail command surface: Inquiry, Read Sector, Read Disc ID/Init, Seek, Request Error, Audio Status, Stop Motor, audio enable/disable, command pending state, transfer-complete/device-error/break interrupt status, and RVZ/GCM-backed DMA reads. This is enough for devkitPro framebuffer examples using `PAD_Init`, `PAD_ScanPads`, and `PAD_Read` to run through smoke windows, and it gives retail IPL/OS startup code real controller, RTC, SRAM, DVD, and basic memory-card services instead of generic MMIO echoes. Real input mapping, deeper SI error states, GBA link, keyboard, rumble semantics, memory-card directory/FAT formatting and persistence, strict DVD alignment/error timing, debug drive commands, and EXI timing/error edge cases are still future work.

### M5: First Pixels

- Implement VI framebuffer presentation.
- Implement GX FIFO parser skeleton.
- Implement enough CP/BP/XF state and vertex decoding to draw basic primitives.
- Add null and debug renderers.

Done when a simple GX homebrew renders geometry.

Current status: partial but useful. The CLI can dump RGB565, YUYV, UYVY, and XRGB8888 framebuffers from explicit addresses or VI framebuffer registers. The source-built `xfb-smoke` fixture and devkitPro `graphics/fb/pageflip` and `graphics/fb/consoletest` examples produce PNG output. There is also a diagnostic GX FIFO software renderer that captures CP/VCD/VAT state, tracks BP and XF setup for diagnostics, decodes direct and indexed position/color/texcoord streams from FIFO or main RAM, tracks texture-map image state from BP texture registers, handles the positive and negative screen-space conventions seen in the Sonic and Pikmin traces, rasterizes quads, triangles, triangle strips, and triangle fans into PNGs, and performs nearest-neighbor texture sampling for tiled `I4`, `I8`, `IA4`, `IA8`, `RGB565`, `RGB5A3`, `RGBA8`, `CMPR`, `CI4`, and `CI8` textures. Stage-0 TEV order is now decoded in diagnostics, and stage sampling follows the selected texture map, texcoord, and raster-color selector instead of always forcing map 0, tex0, and vertex color. The generic non-indirect TEV evaluator decodes `GEN_MODE` TEV stage count, TEV color/alpha envs for multiple stages, evaluates the standard `d + lerp(a,b,c)` path with add/subtract, bias, scale, clamp, basic destination registers, samples each stage through its own TEV order, supports unsigned `GX_SetTevColor` constants for `GX_TEVREG0..2`, supports `GX_CC_KONST`/`GX_CA_KONST` through TEV KSEL registers plus `GX_SetTevKColor`-style K color writes, applies raster/texture TEV swap mode tables, and handles TEV compare ops for color and alpha. Indirect TEV state is now decoded in draw diagnostics, including `GEN_MODE` indirect-stage count, per-stage indirect registers, direct/no-op encodings, indirect texture order, indirect coordinate scales, and indirect matrices; a first narrow indirect offset path applies simple coordinate offsets for `GX_ITF_8/5/4/3`, applies `GX_SetIndTexCoordScale`, decodes S/T bias masks, supports matrix off, normal matrices `GX_ITM_0..GX_ITM_2`, and diagnostic dynamic S/T matrices `GX_ITM_S0..S2`/`GX_ITM_T0..T2`, applies indirect wrap modes before regular texture lookup, accumulates `addprev` offsets across active TEV stages, handles the common repeat-previous form, and exposes indirect alpha-select bits through diagnostic `GX_ALPHA_BUMP`/`GX_ALPHA_BUMPN` raster color. Texture mode0 wrap state is now decoded for `CLAMP`, `REPEAT`, and `MIRROR`, which fixes repeat-heavy stock demos such as NeHe lesson 10 from degenerating into smeared edge texels. A first diagnostic EFB copy path decodes BP copy registers `0x49/0x4A/0x4B/0x4D/0x4E/0x4F/0x50/0x51/0x52/0x53/0x54`; texture copies are mid-FIFO operations that write the current diagnostic RGB/alpha framebuffer to main RAM in tiled `I4`, `I8`, `IA4`, `IA8`, `RGB565`, `RGB5A3`, or `RGBA8` layout using the `0x4D` tile metadata for destination row stride, while display copies write a diagnostic YUYV XFB to main RAM, derive XFB width from display destination metadata, derive line count from `GX_SetDispCopyYScale`, decode copy gamma/clamp/frame-field bits, and apply a diagnostic vertical copy filter from `GX_SetCopyFilter` taps. Copy-clear now fills the diagnostic RGB/alpha EFB region from `GX_SetCopyClear` color registers, with the Z clear value decoded for diagnostics. A first diagnostic Z path decodes BP `ZMODE` (`0x40`), carries projected viewport depth through vertices, and applies `LEQUAL`/other compare functions against a 24-bit-style software depth buffer, which reduces overdraw artifacts in triangle-heavy demos such as lesson 10. The rasterizer now also decodes BP scissor registers and clips software raster bounds to the decoded rectangle, and texture coordinates use per-vertex `1/w` for perspective-correct interpolation when XF projection data is available. Vertices carry both raw and generated TEX0 coordinates; the common XF texgen path now decodes texture matrix selection from `0x1018`, recognizes regular `GX_TG_TEX0` generation, and applies identity, 2x4, and 3x4 texture matrices for stage-0 sampling and draw diagnostics. When complete XF projection state is present, vertices retain view-space coordinates; triangles crossing the camera plane are clipped into a triangle or quad before rasterization, while fully unprojectable vertices remain marked as `clipped` in draw diagnostics. TLUT loads through BP registers `0x64`/`0x65` and texture-map TLUT bindings through `0x98`/`0x99`/etc. are tracked for `IA8`, `RGB565`, and `RGB5A3` palette entries. The stage-0 TEV diagnostic path now recognizes the common libogc `GX_SetTevOp` BP patterns for `PASSCLR`, `REPLACE`, `DECAL`, `BLEND`, and `MODULATE`; raster color and alpha are interpolated per pixel, texture alpha is decoded for alpha-capable texture and TLUT formats, `DECAL` uses texture alpha over raster color, `BLEND` handles the NeHe lesson 08 color blend equation, and the PE path now decodes/applies alpha compare, color/alpha update enables, subtract/logic modes, and common `SRCALPHA` blend factors. The diagnostic renderer also handles the common position path for the current XF position matrix from `0x1018` plus loaded projection and viewport, enough to center/project stock demos such as `texturetest`, NeHe lesson 06, and NeHe lesson 07. A timed `scripts/run-gx-demo-sweep.ps1` harness writes per-demo GX/XFB PNGs, draw logs, and a TSV summary while recording slow cases as `timeout`; `--gx-frame-max-draws` caps diagnostic rasterization work for long captured FIFO streams. This is intentionally still a visibility/debugging path: exact GX clip-space semantics, non-regular texture coordinate generation modes, fully precise indirect TEV hardware semantics, Z-texture, fog, CI14, antialias sample-pattern resolving, real copy-depth clears, deeper PE edge cases, and direct VI presentation from GX output are not implemented yet.

### M6: Early Commercial Boot

- Implement apploader/FST/main DOL path.
- Expand CPU instruction coverage, paired singles, cache-control semantics as no-ops where safe.
- Expand MMIO stubs based on real boot traces.
- Track Sonic/Pikmin smoke commands and current benchmark baselines in `docs/retail-benchmarks.md`.

Done when selected commercial titles reach visible boot screens or log a known unsupported feature.

### M7: Audio And Compatibility

- AI timing and output.
- DSP HLE for common games.
- More accurate GX/TEV/texture behavior.
- Memory card persistence.

Done when a small compatibility list is playable with documented issues.

### M8: Performance

- Cached interpreter.
- Hot-path memory translation.
- Renderer batching and shader cache.
- Optional JIT feasibility spike.

Done when interpreter bottlenecks are measured and one faster CPU backend is proven.

## Testing Strategy

- Instruction tests: known before/after CPU states.
- Loader tests: DOL/GCM/FST fixtures built from homebrew or synthetic binaries.
- MMIO tests: register read/write behavior and interrupt side effects.
- Scheduler tests: event ordering and cycle deadlines.
- Graphics tests: command-stream fixtures and image snapshots.
- Integration tests: run homebrew for N frames and assert logs/framebuffer hashes.
- Differential tests: compare CPU traces against Dolphin for legal homebrew fixtures.

## Risks

- CPU correctness: paired-single and floating-point edge cases can stall compatibility.
- GPU complexity: TEV, EFB, copies, texture formats, and timing are substantial.
- DSP audio: HLE is faster to build but may fail games with unusual microcode.
- Performance: pure C# interpreter will be slow for commercial games until cached/JIT work lands.
- Legal hygiene: avoid proprietary SDK leaks, copyrighted BIOS/game files, and copied GPL code unless the project intentionally adopts compatible licensing.

## Near-Term Recommendation

Start with a narrow vertical slice:

1. Scaffold .NET solution.
2. Implement DOL loader and memory bus.
3. Implement enough PowerPC interpreter to run synthetic tests.
4. Add trace logging and a simple CLI runner.
5. Run a public-domain or self-built GameCube homebrew DOL.

This gives the project a real executable heartbeat before we tackle Flipper/DSP complexity.
