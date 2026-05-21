# Compatibility Workflow

## Treat Compatibility As A Sequence Of Milestones

Do not ask "does the game work?" Ask:

- Does the binary load?
- Does startup reach the scheduler?
- Does input initialize?
- Does storage probe complete?
- Does the first media read happen?
- Does the first render command arrive?
- Does the first nonblack frame appear?
- Does the same scene match a reference?

Each milestone should have a bounded command and a summary field.

## The Investigation Loop

Use a tight loop:

1. Run a bounded target.
2. Read the summary, not just the console.
3. Identify the next blocker or hotspot.
4. Add the smallest diagnostic to prove the hypothesis.
5. Implement the missing CPU/device behavior or exact helper.
6. Add a unit/fixture test.
7. Rerun the focused target.
8. Rerun a broader matrix only after the focused target is clean.
9. Document the result and next blocker.

This keeps the project moving without pretending every fix is a broad compatibility breakthrough.

## Promote Stable Probes Into A Matrix

Ad hoc commands are fine while exploring. Once a command matters, promote it into a compatibility matrix.

A good matrix target includes:

- Stable id.
- Type and local path.
- Suites and tags.
- Instruction/cycle budget.
- Timeout.
- Input/storage setup.
- Diagnostics to collect.
- Expected stop reason and key counters.

Use suites to control cost: `quick`, `homebrew`, `graphics`, `retail`, `focused`, `slow`, `deep`.

## Expected Values Should Match Intent

Do not overfit broad runs to unstable details. Choose expectations that reflect the target's purpose.

Good expectations:

- Synthetic target halts at exact PC.
- Framebuffer fixture hash matches.
- Resource profile reaches expected stop reason.
- Top PC/caller remains in the known blocker.
- Fast-forward counter stays above a threshold.
- FIFO bytes or draw count exceeds a minimum.

Bad expectations:

- Exact final PC for a broad run where timing improvements legitimately move the endpoint.
- Screenshot hash for a scene selected by heuristic.
- Massive raw trace text as the pass/fail signal.

## Focused Targets Save Time

When a long retail target repeatedly reaches the same hot path, create a focused target:

- Same input and setup.
- Lower timeout.
- Stop on hot PC or specific write.
- Include the branch/caller profiles relevant to that blocker.
- Keep expected values light.

The focused target becomes the inner loop. The full target becomes validation.

## Regressions Are Information

A matrix "regression" is not always bad. It may mean:

- A real bug was introduced.
- The emulator advanced to a new phase.
- A timing change moved the endpoint.
- A diagnostic expectation is now obsolete.

Read the counters before reacting. If subsystem counters remain stable and only the endpoint advances, update the baseline and document why.

## Avoid Broad Game-Specific Hacks

When retail software spins, first determine what it is waiting for:

- Interrupt?
- DMA completion?
- Media transfer?
- Controller status?
- Audio callback?
- Timer tick?
- Resource registration?
- Thread/message queue?

Skipping the loop may get farther while hiding the missing device behavior that many other titles need. If a fast-forward is used, make it exact and name it as diagnostic or compatibility scaffolding.

## Know When To Switch Subsystems

If repeated CPU profiling only exposes more wait loops, the blocker may be a missing device. If graphics captures are black but FIFO/copy counters are healthy, the blocker may be presentation or frame selection, not TEV. Let summaries decide where to work next.
