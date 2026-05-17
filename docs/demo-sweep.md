# Demo Sweep Harness

The emulator now has a repeatable harness for legal homebrew DOLs and self-built examples. It keeps binaries and logs under `artifacts/`, which is ignored by git.

## Prepare Fixtures

Build/copy local devkitPro examples and download larger public homebrew apps:

```powershell
.\scripts\prepare-demo-sweep.ps1 -BuildDevkitProExamples
```

For a faster local-only pass:

```powershell
.\scripts\prepare-demo-sweep.ps1 -SkipDownloads
```

The script writes `artifacts/demo-dols/manifest.json`.

## Run The Sweep

```powershell
$env:DOTNET_ROOT=(Join-Path (Get-Location) '.dotnet')
$env:PATH="$env:DOTNET_ROOT;$env:PATH"
.\scripts\run-demo-sweep.ps1 -MaxInstructions 2000000 -TimeoutSeconds 60
```

Each run creates `artifacts/demo-sweep/<timestamp>/summary.csv` plus per-DOL stdout, stderr, and trace-tail files.

Latest local sweep:

- Run directory: `artifacts/demo-sweep/20260512-223020`
- Fixture count: 35 DOLs
- Result buckets at 2,000,000 instructions: 33 max-instructions, 1 completed/self-loop, 1 memory-fault, 0 unsupported-instruction
- Remaining failure: Visual Boy Advance GX reaches an unmapped access at `0x81800000`.

Useful filters:

```powershell
.\scripts\run-demo-sweep.ps1 -Filter "devkitpro" -MaxInstructions 500000
.\scripts\run-demo-sweep.ps1 -Filter "consoletest|pageflip|triangle"
.\scripts\run-demo-sweep.ps1 -Filter "swiss|gcmm|240p|meese"
```

## Included Sources

- devkitPro GameCube examples: https://github.com/devkitPro/gamecube-examples
- Swiss: https://github.com/emukidid/swiss-gc
- GCMM: https://github.com/suloku/gcmm
- Visual Boy Advance GX GameCube build: https://github.com/dborth/vbagx
- Meese Engine GameCube demo: https://meese4.github.io/

## Manual Candidates

These are good targets but may require manual download or source-specific build steps:

- 240p Test Suite GameCube/Wii release: https://artemiourbina.itch.io/240p-test-suite
- 240p Test Suite source: https://github.com/ArtemioUrbina/240pTestSuite
- Game Boy Interface: https://gc-forever.com/wiki/index.php?title=Game_Boy_Interface

Keep this ladder in mind when interpreting failures:

1. Synthetic smoke DOL.
2. devkitPro template/application.
3. framebuffer console/pageflip examples.
4. controller, filesystem, and device examples.
5. simple GX examples.
6. large homebrew utilities and demos.
