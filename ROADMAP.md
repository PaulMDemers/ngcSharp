# Roadmap

ngcSharp is currently in the "make behavior observable and correct enough to boot deeper" phase. The order below is approximate and may change as diagnostics expose better leverage points.

## Near Term

- Stabilize SI controller polling and packet semantics across all four ports.
- Continue EXI memory card validation against SDK/libogc behavior.
- Improve DI/DVD command coverage needed by retail boot paths.
- Split mature bus behavior into smaller device classes with tests.
- Add more homebrew fixtures for isolated VI, SI, EXI, DI, GX, and TEV behavior.
- Improve GX frame selection, copy diagnostics, and TEV sample summaries.
- Keep Dolphin-reference comparison scripts easy to run locally.

## Medium Term

- Broaden PowerPC/Gekko instruction correctness.
- Reduce exact fast-forwards by implementing missing hardware/runtime behavior.
- Add more accurate scheduler timing for VI/SI/EXI/DI/AI interactions.
- Improve memory card persistence and GCI/import/export support.
- Expand texture formats, TEV combiner coverage, alpha/blend behavior, and copy filters.
- Build a clearer compatibility dashboard from demo and benchmark sweeps.

## Longer Term

- Replace diagnostic-only GX rendering with a real renderer backend.
- Add DSP/audio HLE good enough for early retail sound paths.
- Add save states once core mutable state is well factored.
- Consider cached interpretation after interpreter correctness improves.
- Consider JIT only after CPU behavior and subsystem timing are much more mature.

## Non-Goals For Now

- Real-time retail gameplay.
- Bundled BIOS/IPL or commercial test assets.
- A polished end-user GUI.
- Broad game-specific hacks that hide missing hardware behavior.

