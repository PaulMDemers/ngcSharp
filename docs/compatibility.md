# Compatibility Status

ngcSharp is pre-alpha. This page is a plain-language snapshot of what works and what does not.

## Summary

| Area | Status |
| --- | --- |
| Standalone DOL loading | Functional for small fixtures |
| Disc image parsing | Functional for ISO/GCM/RVZ inspection and reads |
| PowerPC interpreter | Broad but incomplete |
| Paired-single support | In progress |
| Interrupts and timing | Partial |
| SI controller | Partial, including polling and basic controller packets |
| EXI SRAM/RTC | Partial |
| EXI memory card | Basic formatted 251-block card, read/write/erase/status |
| DI/DVD | Enough for current HLE boot probes, incomplete overall |
| VI/framebuffer | Diagnostic support, incomplete presentation accuracy |
| GX FIFO | Captured and software-rendered for diagnostics, not a production renderer |
| TEV/textures | Partial, with active diagnostics |
| DSP/audio | Mostly stubbed/HLE placeholders |
| Retail gameplay | Not expected yet |

## Homebrew

Small homebrew fixtures are the best compatibility target right now. They are narrow enough to isolate one subsystem at a time and safe to include when source is original or permissively licensed.

Current fixture areas:

- XFB smoke tests.
- GX ladder/TEV/texture-oriented diagnostics.
- Demo sweeps driven by local scripts.

See [gx-fixtures.md](gx-fixtures.md), [demo-sweep.md](demo-sweep.md), and [build-devkitpro-fixtures.md](build-devkitpro-fixtures.md).

## Retail Benchmarks

Retail testing is local-only and requires user-provided game images.

Current benchmark games used during development:

- Sonic Adventure 2 Battle, USA, RVZ.
- Pikmin, USA, RVZ.

These files are not part of the repository and must not be committed.

High-level current state:

- Sonic reaches real FIFO rendering paths and has visible UI/title/prompt captures in diagnostics, but boot progression is still blocked by incomplete subsystem behavior and timing.
- Sonic memory card behavior has advanced from "not inserted" and "damaged card" branches to formatted-card EXI reads and SI polling investigation.
- Pikmin reaches GX setup and real draw/copy paths in bounded probes, but presentation and boot progression are still incomplete.

For the detailed running log, see [retail-benchmarks.md](retail-benchmarks.md).

## What Counts As A Compatibility Improvement

Good compatibility work usually includes at least one of:

- A new or improved unit test.
- A small homebrew DOL that demonstrates the behavior.
- A bounded `run-dol` or `run-disc` command that reproduces the before/after.
- A diagnostic artifact path under `artifacts/` for local review.
- A Dolphin reference comparison when visual behavior is the question.

Avoid broad game-specific bypasses. If a workaround is needed to keep research moving, name it as a diagnostic fast-forward and keep it exact.

