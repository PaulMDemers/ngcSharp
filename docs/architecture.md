# Architecture Overview

ngcSharp is organized around a small core emulator model plus a diagnostics-heavy CLI. The implementation favors debuggability over speed while compatibility work is still exploratory.

## Projects

### `NgcSharp.Core`

Core owns the system model:

- Main RAM and address translation.
- Big-endian helpers.
- DOL and disc-image parsing.
- GameCube boot/HLE setup.
- The memory bus and MMIO register blocks.
- Early models for DI, SI, EXI, VI, AI, DSP-facing mailboxes, ARAM, GX FIFO ingress, and interrupt routing.

`GameCubeBus` is currently the main integration point. It is intentionally broad while the emulator is young; once behavior stabilizes, devices can be split behind smaller interfaces.

### `NgcSharp.Cpu`

CPU owns the PowerPC/Gekko interpreter and disassembler:

- CPU architectural state.
- Instruction decoding.
- Integer, branch, load/store, floating-point, and paired-single execution.
- Isolated instruction tests.

The current CPU backend is an interpreter. Cached interpretation or JIT work should wait until correctness and subsystem behavior are less volatile.

### `NgcSharp.Hw`

Hardware contains smaller device abstractions that are independent enough to test away from the full bus. This project is expected to grow as monolithic bus behavior is extracted into devices.

### `NgcSharp.App`

The app project is the command-line host and diagnostics layer:

- `run-dol` and `run-disc` execution.
- Disc inspection commands.
- Trace, watch, profile, and stop conditions.
- GX FIFO software rendering.
- GX draw/copy/coverage/TEV/texture diagnostics.
- PNG image comparison.
- Synthetic DOL generation for smoke tests.

## Execution Model

The emulator runs bounded instruction budgets. This makes retail debugging practical even before real-time performance exists.

At a high level:

1. Load DOL or disc boot content into emulated memory.
2. Initialize HLE boot state.
3. Execute PowerPC instructions.
4. Route memory accesses through RAM, locked cache, or MMIO.
5. Advance device timing through `GameCubeBus.Advance`.
6. Raise device interrupts through the processor interface.
7. Optionally dump traces, memory, GX frames, and diagnostic CSVs.

The app also contains exact fast-forwards for some known SDK/game hot loops. These are compatibility aids, not a substitute for implementing missing hardware behavior. New fast-forwards should be narrow, validated, and documented.

## Device Coverage

Current device model highlights:

- PI interrupt cause/mask routing.
- VI timing and simple framebuffer presentation diagnostics.
- SI controller packets, read-status bits, and periodic polling.
- EXI SRAM, RTC, formatted memory card identity/status/read/write/erase behavior.
- DI disc reads from ISO/GCM/RVZ through the disc reader.
- AI/DSP/ARAM stubs sufficient for several boot paths.
- GX FIFO capture and software-side diagnostics.

The next architectural target is extracting mature pieces from `GameCubeBus` into device classes without changing behavior.

## Rendering Diagnostics

The GX renderer in `NgcSharp.App` is not a production GPU backend. It is a software diagnostic renderer used to answer questions like:

- Did the game write FIFO commands?
- Which draw produced visible pixels?
- Which EFB copies wrote nonblack display output?
- What did TEV evaluate for representative triangles?
- Which texture address/format/filter state fed a suspicious draw?

This is why the CLI has many capture options. They are meant to isolate boot and rendering problems before a real renderer exists.

## Data And Artifacts

Generated diagnostics go under `artifacts/` and `traces/`. These folders are ignored because they can contain huge PNG/CSV dumps and data derived from user-provided games.

Commercial game images and local Dolphin builds are workspace-only inputs. They must not be committed.

