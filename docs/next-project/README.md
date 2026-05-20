# Emulator Project Playbook

This directory distills the reusable lessons from building ngcSharp. It is written for a future emulator project that may target a different console, CPU, GPU, media format, or operating environment.

The central lesson is simple: build the emulator as a diagnostics platform first. Compatibility work becomes much faster when every run produces structured evidence, every subsystem can be exercised by small legal fixtures, and every shortcut is measured against an artifact instead of a feeling.

## Guides

- [principles.md](principles.md): the operating principles that kept the project moving.
- [architecture.md](architecture.md): how to structure the emulator so it remains testable while hardware knowledge is incomplete.
- [research-and-legal.md](research-and-legal.md): how to use references, specs, open-source emulators, and commercial software responsibly.
- [test-assets-and-fixtures.md](test-assets-and-fixtures.md): how to build a useful ladder of synthetic, homebrew, demo, and user-provided tests.
- [diagnostics-and-observability.md](diagnostics-and-observability.md): what to log, summarize, and compare.
- [compatibility-workflow.md](compatibility-workflow.md): how to turn failures into repeatable milestones.
- [graphics-workflow.md](graphics-workflow.md): how to approach an unfamiliar rendering pipeline.
- [performance-and-fast-forwards.md](performance-and-fast-forwards.md): when optimization helps, and how to avoid hiding correctness bugs.
- [project-operations.md](project-operations.md): repo hygiene, documentation habits, commits, and day-to-day workflow.

## Suggested Reading Order

1. Start with [principles.md](principles.md).
2. Read [architecture.md](architecture.md) before writing the first subsystem.
3. Set up [test-assets-and-fixtures.md](test-assets-and-fixtures.md) and [diagnostics-and-observability.md](diagnostics-and-observability.md) before chasing retail compatibility.
4. Use [compatibility-workflow.md](compatibility-workflow.md), [graphics-workflow.md](graphics-workflow.md), and [performance-and-fast-forwards.md](performance-and-fast-forwards.md) as active working checklists.

## Non-Goals

These notes are not a hardware manual for any specific console. They deliberately avoid copying proprietary information, commercial assets, or implementation details from incompatible codebases. Treat them as process scaffolding for your own clean-room research.
