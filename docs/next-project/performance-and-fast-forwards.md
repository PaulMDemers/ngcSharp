# Performance And Fast-Forwards

## Measure Before Optimizing

Interpreter and software renderer slowness can block development. Still, optimize from evidence.

Add counters and timings for:

- CPU emulation.
- Device advancement.
- Memory scanning.
- Trace/profile output.
- Renderer replay.
- Rasterization.
- Texture sampling.
- Copy/presentation.
- Image writing.

Then optimize the top bucket.

## Fast-Forward Taxonomy

Useful fast-forwards fall into several categories:

- Pure CPU library leaves: `strlen`, `memcpy`, division helper, timebase read.
- Deterministic memory loops: clear/fill/copy/cache maintenance.
- Known wait loops where advancing hardware time is equivalent.
- Exact decompression/format conversion routines with bounded input/output.
- Renderer diagnostic fast paths that preserve output.

Dangerous fast-forwards:

- Broad gameplay/resource functions.
- Polling loops whose owner is unknown.
- State transitions with callbacks.
- Allocation/accounting paths.
- Anything that writes game state "because the game wants it."

## Requirements For A Safe Fast-Forward

Before adding one, answer:

- Is the code pattern exact?
- Are all memory reads/writes understood?
- Are registers, flags, link register, count register, stack pointer, and condition register preserved as real execution would leave them?
- Does it advance time/decrementer/device clocks correctly?
- Does it stop before crossing an interrupt edge when required?
- Is input/output bounded?
- Is there a unit test?
- Is there a benchmark showing the intended effect?
- Is it documented?

If any answer is no, add diagnostics first.

## Fast-Forward Counters Are Mandatory

Every fast-forward bucket should have a counter in the run summary. This turns shortcuts into observable behavior.

Track by category:

- Generic leaf helpers.
- Timebase reads.
- External interrupt leaves.
- Memory copies.
- Cache maintenance.
- Decompression.
- Game-specific exact wrappers.
- Wait-loop cycles.

When a compatibility target relies on a helper, add a minimum counter expectation. This catches accidental pattern breakage.

## Do Not Hide Missing Hardware

If software is waiting for a device event, prefer implementing or approximating the device event over forcing the waited-on variable.

Examples of better fixes:

- Advance video timing to an interrupt opportunity.
- Complete a storage transfer and raise the right interrupt.
- Return accurate controller status bits.
- Make audio callback state progress.
- Implement message queue semantics.

Examples of worse fixes:

- Set the game's flag directly.
- Skip a whole task scheduler function.
- Return success from an unknown resource manager path.

## Build A Cached Interpreter Before A JIT

For a young emulator, a cached interpreter can deliver speed without the complexity cliff of a JIT:

- Decode basic blocks once.
- Cache instruction metadata.
- Keep interpreter semantics shared with the slow path.
- Add invalidation only where needed.
- Preserve trace/debug mode.

A JIT is worthwhile later, but only after correctness and profiling justify it.

## Optimization Validation Checklist

Before committing an optimization:

- Unit tests pass.
- Focused target passes.
- Quick matrix passes.
- Relevant retail/homebrew benchmark passes.
- Summary compare shows expected counter/timing change.
- No unexpected frame hash/counter drift.
- Docs mention any new diagnostic or fast-forward bucket.
