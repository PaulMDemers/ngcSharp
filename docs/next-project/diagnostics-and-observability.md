# Diagnostics And Observability

## A Run Summary Is The Primary Artifact

Every nontrivial run should produce a machine-readable summary. It should be small enough to inspect quickly and stable enough to diff.

Recommended fields:

- Schema/version.
- Input path or target id.
- Instruction/cycle budget.
- Executed instruction/cycle count.
- Exit code and stop reason.
- Final PC and last instruction.
- Selected registers.
- Device counters.
- Fast-forward/optimization counters.
- Timings by phase.
- Profile summaries.
- Optional diagnostic failure details.

Once a field becomes useful twice, add it to the summary.

## Stop Reasons Matter

Use explicit stop reasons:

- `halted`
- `max-instructions`
- `pc`
- `hot-pc`
- `write-watch`
- `load-watch`
- `fifo-offset`
- `unsupported-instruction`
- `address-translation`
- `timeout`

This is more useful than only reporting "ok" or "failed." A target that reaches the same `hot-pc` stop reason can be a good focused probe.

## Profiles Turn Slow Runs Into Maps

Low-overhead profiles are some of the best tools in emulator development.

Useful profiles:

- Top PC samples.
- Top PC samples with known leaf/helper addresses filtered out.
- PC-to-LR caller profiles.
- Branch-site target profiles.
- Indirect-call target profiles.
- Device register access counts.
- Media read offsets and sizes.
- Graphics draw/copy timelines.

Profiles should be bounded and summarized. Massive raw traces are a last resort.

## Watches Beat Full Traces

Full instruction traces are expensive and noisy. Prefer targeted watches:

- Stop on a PC after a warmup.
- Stop when a PC becomes hot.
- Stop on writes to a range.
- Stop on loads from a range.
- Stop on MMIO register access.
- Stop on FIFO offset.
- Trace only selected PCs.
- Emit only the first N hits.

Watches let you ask one sharp question per run.

## Compare Summaries, Not Memories

After each change, compare before and after:

- Stop reason.
- Final PC.
- Total/emulation/diagnostic time.
- Fast-forward counters.
- Device counters.
- Top PCs and callers.
- Branch targets.
- Frame source and hashes.

A small compare tool saves hours because it moves attention directly to what changed.

## Timings Should Have Buckets

"This run took 180 seconds" is too vague. Split time into:

- Emulation.
- Post-emulation diagnostics.
- Memory scanning.
- Profile formatting.
- Renderer replay.
- Rasterization.
- Texture sampling.
- Copy handling.
- Image writing.

For renderers, add deeper buckets once needed: setup, coverage, depth, texture/TEV, alpha, blending, copy, output encoding.

## Diagnostic Output Must Be Bounded

Every trace feature needs a limit:

- Max rows.
- Max draws.
- Max pixels.
- Max memory bytes.
- Max watch hits.
- Wall-clock watchdog.

Unbounded diagnostics can make the emulator feel hung even when it is working.

## The Best Diagnostic Is Reusable

If you write a one-off command by hand three times, promote it:

- Add a CLI flag.
- Add a script.
- Add a matrix target.
- Add a summary field.
- Add docs with the command and expected interpretation.

This is how exploratory debugging becomes a development workflow.
