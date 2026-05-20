# ngcSharp

ngcSharp is an experimental Nintendo GameCube emulator and diagnostics toolkit written in C#/.NET.

The project is still early. It can load standalone DOLs, inspect GameCube disc images, run bounded CPU/device probes, render portions of GX FIFO streams in software, and compare output against reference captures. It is being built with correctness and observability first, then compatibility and speed.

## Status

Current focus areas:

- Gekko PowerPC interpreter coverage, including paired-single and SDK-heavy startup paths.
- GameCube memory map, MMIO bus, interrupts, VI timing, SI controller polling, EXI SRAM/RTC/memory card behavior, and DI disc reads.
- GCM/ISO/RVZ disc inspection and HLE boot paths.
- Software GX FIFO diagnostics, TEV evaluation, texture decoding, EFB/display-copy capture, and reference-image comparison.
- Homebrew fixture sweeps and retail benchmark diagnostics using user-provided game dumps.

Compatibility is not yet "play games." Retail benchmarks are used to drive subsystem work, but expect missing hardware behavior, black frames, prompt loops, and slow interpreter runs.

See [docs/compatibility.md](docs/compatibility.md) for the current compatibility picture.

## Repository Layout

```text
src/NgcSharp.Core   Core memory, bus, disc, boot, and device model pieces
src/NgcSharp.Cpu    PowerPC/Gekko interpreter and disassembler
src/NgcSharp.Hw     Small hardware abstractions
src/NgcSharp.App    CLI runner, diagnostics, GX renderer, image comparison
tests/              xUnit tests for CPU, loaders, bus devices, rendering, CLI parsing
fixtures/           Homebrew source/DOL fixtures used for local test sweeps
scripts/            Fixture, demo, Dolphin reference, and benchmark helper scripts
docs/               Architecture, development, benchmark, and research notes
```

Generated artifacts, traces, local Dolphin builds, and game dumps are intentionally ignored by git.

## Requirements

- Windows PowerShell is the most-tested shell for the scripts in this repository.
- .NET SDK capable of building `net10.0`.
- Optional: devkitPro/devkitPPC for rebuilding homebrew fixtures.
- Optional: a local Dolphin build for visual reference capture.
- Optional: legally obtained GameCube disc images for private compatibility testing.

## Quick Start

Build and test:

```powershell
dotnet restore
dotnet build
dotnet test --no-restore
```

Show CLI help:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- --help
```

Inspect a DOL:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- dol-info fixtures/devkitpro/xfb-smoke/xfb-smoke.dol
```

Run a bounded homebrew probe:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-dol fixtures/devkitpro/xfb-smoke/xfb-smoke.dol --max-instructions 1000000 --dump-gx-frame artifacts/xfb-smoke/frame.png --no-registers
```

Inspect a user-provided disc image:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- disc-info "path\to\game.iso"
```

## Common Workflows

Homebrew fixture rebuild:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-gx-fixtures.ps1
```

Homebrew demo sweep:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-demo-sweep.ps1
```

Dolphin reference capture, with a local Dolphin build and user-provided benchmark images:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/generate-dolphin-reference.ps1 -Seconds 16
```

Retail reference comparison:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-retail-reference-compare.ps1 -NoBuild
```

More command examples are in [docs/cli.md](docs/cli.md), [docs/gx-fixtures.md](docs/gx-fixtures.md), and [docs/retail-benchmarks.md](docs/retail-benchmarks.md).

For console-agnostic lessons from this project, see the [next emulator project playbook](docs/next-project/README.md).

## Legal And Assets

This repository must not include copyrighted Nintendo IPL/BIOS files, SDK files, commercial game images, or assets extracted from commercial games.

The emulator supports user-provided disc images for private testing. Contributors should use homebrew fixtures, public-domain test programs, or independently created test cases whenever possible.

See [docs/legal.md](docs/legal.md) for contribution rules around assets and emulator research.

## Contributing

Contributions are welcome while the project is still forming, especially:

- Focused CPU instruction tests.
- MMIO/device behavior fixes with small repros.
- Homebrew DOL fixtures that exercise one subsystem at a time.
- GX/TEV texture and copy-path diagnostics.
- Documentation that makes a tricky subsystem easier to reason about.

Start with [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/development.md](docs/development.md).

## License

ngcSharp is licensed under the [MIT License](LICENSE).
