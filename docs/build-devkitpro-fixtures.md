# Building devkitPro GameCube Fixtures

The next compatibility step is to run source-built GameCube homebrew DOLs and implement whatever the trace exposes.

This repo is set up to build fixtures from devkitPro examples. On this machine, devkitPro is available under `C:\devkitPro`; the helper script also auto-detects that location if the current PowerShell session does not have `DEVKITPRO` or `DEVKITPPC` set.

## Install devkitPro

Use the official devkitPro instructions:

- devkitPPC getting started: <https://devkitpro.org/wiki/Getting_Started/devkitPPC>
- devkitPro pacman setup: <https://devkitpro.org/wiki/devkitPro_pacman>

On Windows, devkitPro provides a 64-bit Windows installer that sets up its customized MSYS2/pacman environment. From that environment, install the GameCube development packages:

```sh
pacman -Syu
pacman -S gamecube-dev gamecube-examples
```

After installation, verify:

```sh
powerpc-eabi-gcc --version
echo $DEVKITPRO
echo $DEVKITPPC
```

In PowerShell, the equivalent checks are:

```powershell
Get-Command powerpc-eabi-gcc
Get-ChildItem Env:DEVKITPRO,Env:DEVKITPPC
```

## Build An Example

From this repository, once the toolchain is visible in the current shell:

```powershell
.\scripts\build-devkitpro-example.ps1
```

The helper defaults to:

- examples root: `$env:DEVKITPRO\examples\gamecube`
- example: `templates/application`
- output: `artifacts/devkitpro/`

Override those when needed:

```powershell
.\scripts\build-devkitpro-example.ps1 `
  -ExamplesRoot "C:\devkitPro\examples\gamecube" `
  -Example "graphics\gx\triangle" `
  -OutputDirectory "artifacts\devkitpro"
```

The repo also includes a tiny local framebuffer fixture:

```powershell
.\scripts\build-devkitpro-example.ps1 `
  -ExamplesRoot "fixtures\devkitpro" `
  -Example "xfb-smoke" `
  -OutputDirectory "artifacts\devkitpro"
```

Run it with automatic VI framebuffer detection:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- `
  run-dol artifacts/devkitpro/xfb-smoke.dol `
  --max-instructions 200000 `
  --dump-frame artifacts/frames/xfb-smoke.png `
  --frame-width 4 `
  --frame-height 2 `
  --frame-format yuyv `
  --no-registers
```

Expected result: the DOL halts on its terminal self-branch after startup and writes a tiny 4x2 PNG. The fixture stores its XFB at `0x81200000` and writes the shifted VI XFB register, so this validates the emulator's framebuffer auto-detection path.

## Run In ngcSharp

Use trace files for real homebrew because stdout gets noisy quickly:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- `
  run-dol artifacts/devkitpro/boot.dol `
  --max-instructions 5000 `
  --trace-file traces/devkitpro-template.trace `
  --dump-mmio
```

If the emulator stops on an unsupported instruction, add that instruction with a focused unit test. If it stops on MMIO behavior, add the smallest device behavior needed to let startup continue.

To inspect framebuffer output, dump a PNG after the run:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- `
  run-dol artifacts/devkitpro/boot.dol `
  --max-instructions 1000000 `
  --dump-frame artifacts/frames/devkitpro.png `
  --frame-width 640 `
  --frame-height 480 `
  --frame-format yuyv `
  --no-registers
```

If the DOL has not configured VI framebuffer registers yet, add `--frame-address <addr>` explicitly.

The devkitPro framebuffer pageflip example is a stronger libogc/VI interrupt smoke test:

```powershell
.\scripts\build-devkitpro-example.ps1 `
  -ExamplesRoot "C:\devkitPro\examples\gamecube" `
  -Example "graphics\fb\pageflip" `
  -OutputDirectory "artifacts\devkitpro"

dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- `
  run-dol artifacts/devkitpro/pageflip.dol `
  --max-instructions 12000000 `
  --dump-frame artifacts/frames/pageflip-auto.png `
  --frame-width 640 `
  --frame-height 480 `
  --frame-format yuyv `
  --no-registers
```

Expected result: a 640x480 blue checkerboard PNG with a white square. This validates DOL startup, libogc thread sleep/wake, VI vblank interrupt delivery through PI, and automatic VI framebuffer register detection.

The framebuffer console example validates a broader libc/libogc path, including console text output and basic `PAD_*` calls against ngcSharp's neutral standard-controller SI stub:

```powershell
.\scripts\build-devkitpro-example.ps1 `
  -ExamplesRoot "C:\devkitPro\examples\gamecube" `
  -Example "graphics\fb\consoletest" `
  -OutputDirectory "artifacts\devkitpro"

dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- `
  run-dol artifacts/devkitpro/consoletest.dol `
  --max-instructions 3000000 `
  --dump-frame artifacts/frames/consoletest.png `
  --frame-width 640 `
  --frame-height 480 `
  --frame-format yuyv `
  --no-registers
```

Expected result: a black 640x480 console frame with white text such as `testing console`, `Hello World`, and an RTC line.

## Fixture Policy

- Keep generated DOLs under `artifacts/`; this folder is ignored.
- Keep trace logs under `traces/`; this folder is ignored.
- Prefer source-built examples over downloaded binaries.
- Do not commit proprietary SDK files, commercial DOLs, ISOs, IPL/BIOS images, or large third-party binaries.
