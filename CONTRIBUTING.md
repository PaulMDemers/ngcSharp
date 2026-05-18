# Contributing

Thanks for helping with ngcSharp. The project is early, so the most valuable contributions are small, well-observed improvements that make one subsystem more correct.

## Before You Start

Read:

- [README.md](README.md)
- [docs/development.md](docs/development.md)
- [docs/architecture.md](docs/architecture.md)
- [docs/legal.md](docs/legal.md)

## Good First Contributions

- Add missing CPU instruction tests.
- Improve parser coverage for DOL, FST, or disc metadata.
- Add a minimal homebrew fixture for one hardware behavior.
- Tighten SI/EXI/DI/VI behavior with a test.
- Improve GX diagnostic output or document an existing diagnostic.
- Reduce a retail symptom to a homebrew or unit test repro.

## Pull Request Expectations

Please include:

- What changed.
- Why it changed.
- How you tested it.
- Any known remaining gaps.
- For compatibility changes, the exact command used to reproduce the issue.

Run:

```powershell
dotnet test --no-restore
```

Focused tests are fine while iterating, but final PRs should explain if the full suite was not run.

## Emulator Accuracy Work

Prefer implementing the underlying device behavior over adding game-specific bypasses.

Fast-forwards are allowed for research when they are:

- Exact to an observed instruction pattern.
- Narrowly scoped.
- Covered by tests where practical.
- Named as fast-forwards, not hidden as general emulation.

## Documentation

Docs should be useful to someone without the chat history or local artifacts.

When adding a command example:

- Use PowerShell if it calls a repository script.
- Keep generated output under `artifacts/` or `traces/`.
- Use placeholder paths for commercial game images.
- Do not include copyrighted game data.

## Assets

Do not commit commercial games, IPL/BIOS dumps, extracted assets, or proprietary SDK material. See [docs/legal.md](docs/legal.md).

