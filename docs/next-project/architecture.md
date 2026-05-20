# Architecture

## Start With Clear Ownership Boundaries

A practical emulator can begin with coarse modules, but the ownership boundaries should be clear from day one.

Recommended shape:

- `Core`: memory map, loaders, media formats, device interfaces, shared data structures.
- `Cpu`: decoder, interpreter, disassembler, CPU state, instruction tests.
- `Hw` or `Devices`: timers, interrupts, DMA, input, storage, audio, video-facing devices.
- `App` or `Tools`: CLI runner, diagnostics, renderers, image comparison, benchmark harnesses.
- `Tests`: instruction, loader, device, renderer, and integration tests.
- `Scripts`: repeatable fixture builds, sweeps, reference capture, summary comparison.
- `Docs`: architecture, compatibility state, workflow notes, legal rules.

This separation is less about purity than survival. Emulator work constantly bounces between CPU, devices, media, graphics, and tooling. Module boundaries make it easier to avoid smearing every workaround through the entire codebase.

## CPU First, But Not CPU Only

The CPU interpreter is the first center of gravity because every subsystem depends on it. Build:

- A decoder and disassembler together.
- Instruction-level unit tests with before/after CPU state.
- Exact exception behavior where known.
- A clear bus interface for memory and MMIO.
- Time/cycle advancement at a consistent point in instruction execution.

Do not wait for perfect CPU coverage before implementing devices. Real software quickly exposes missing MMIO, interrupts, controller state, storage, and timers. The CPU and bus need to grow together.

## The Bus Is A Temporary Integration Hub

Early on, a broad bus object is useful. It can route reads/writes, own device instances, expose trace hooks, and coordinate time advancement. Keep it disciplined:

- Centralize address decoding.
- Log MMIO accesses through a consistent observer.
- Keep device state in device objects where possible.
- Avoid letting unrelated devices directly mutate each other.
- Prefer named helper methods over scattered magic addresses.

As behavior stabilizes, split devices behind smaller interfaces. The monolithic bus should be a scaffold, not the final architecture.

## Model Time Deliberately

Most compatibility failures eventually involve time: interrupts, polling loops, DMA completion, audio buffers, video refresh, media latency, or controller reads.

Define early:

- What increments every CPU instruction.
- How device clocks advance.
- How scheduled events are represented.
- How interrupts are requested, masked, acknowledged, and cleared.
- How fast-forwards advance device time.

If an optimization skips 10,000 instructions but does not advance timers, it is not equivalent. This matters even before cycle accuracy.

## Loaders And Boot Paths

Separate "load executable" from "boot console." A healthy project supports multiple entry points:

- Raw executable/homebrew loading.
- Disc/container inspection.
- Executable extraction from media.
- High-level boot for retail software.
- Eventually BIOS/IPL boot if legally supported by user-provided firmware.

Standalone executable loading is the best early target because it avoids media, firmware, and OS startup all at once. Retail media should enter after the core loop and basic devices have observable behavior.

## Diagnostics Belong Beside The Emulator

Diagnostic code is not throwaway. Keep it in the app/tools layer and make it reusable:

- Run summaries.
- Trace writers.
- Memory dumps.
- Device state dumps.
- Frame/image dumps.
- Profile collectors.
- Summary comparison tools.

The emulator core should expose enough hooks for diagnostics without becoming a logging framework itself.

## Keep Renderers Pluggable

For graphics-heavy consoles, use at least two conceptual renderers:

- A null or capture renderer for fast CPU/device work.
- A diagnostic software renderer for correctness investigation.
- Later, a production renderer if real-time graphics becomes a goal.

The diagnostic renderer should answer questions, not win benchmarks. It can be slower if it produces precise draw logs, coverage maps, texture samples, copy timelines, and hashes.
