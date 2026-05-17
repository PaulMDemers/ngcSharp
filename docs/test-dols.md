# Test And Demo DOL Sources

Use legal homebrew and self-built fixtures only. Do not download commercial GameCube DOLs, extracted main executables, ISOs, SDK samples from leaked SDKs, or IPL/BIOS images.

## Best First Fixture

Use ngcSharp's built-in synthetic smoke-test DOL:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- write-test-dol artifacts/smoke.dol
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-dol artifacts/smoke.dol --max-instructions 3 --trace --dump-mmio
```

This fixture uses only a few instructions:

- `addi r3, r0, 7`
- `stw r3, 0x100(r0)`
- `addi r4, r3, 35`
- `b 0`

It is deliberately boring. That makes it useful as a loader, bus, trace, and interpreter sanity check before real homebrew pulls in SDK startup, interrupts, graphics, controller, and EXI behavior.

You can also exercise the framebuffer dump path with an explicit address. The smoke DOL stores a word at `0x100`, so this creates a tiny one-pixel PNG:

```powershell
dotnet run --project src/NgcSharp.App/NgcSharp.App.csproj -- run-dol artifacts/smoke.dol --max-instructions 4 --dump-frame artifacts/frames/smoke.png --frame-address 0x100 --frame-width 1 --frame-height 1 --frame-format xrgb8888 --no-registers
```

For real homebrew, `--dump-frame <png-path>` can auto-detect a framebuffer after VI framebuffer registers have been written. During early bring-up, use `--frame-address`, `--frame-width`, `--frame-height`, and `--frame-format` to override missing or incomplete VI state. Supported formats are `rgb565`, `yuyv`, `uyvy`, and `xrgb8888`.

## Good External Sources

For bulk fixture setup and compatibility sweeps, see [demo-sweep.md](demo-sweep.md).

### devkitPro GameCube Examples

Repository/source: <https://github.com/devkitPro/gamecube-examples>

devkitPro is the standard public homebrew toolchain family for GameCube/Wii development. The examples are source-first, so they are best used by installing devkitPro/devkitPPC and building DOLs locally. This gives us clean, reproducible fixtures with source we can inspect.

Use these before larger apps:

- minimal template examples
- console/video init examples
- controller input examples
- simple GX examples once VI/GX work begins

### Swiss

Repository/releases: <https://github.com/emukidid/swiss-gc>

Swiss is a mature GPL-2.0 GameCube homebrew utility. Its releases include DOL files, and the README says to download the latest release and copy the Swiss DOL from the release's `DOL` folder.

Swiss is a useful stress test later, but it is not a gentle first target. It touches many hardware devices and storage paths.

### Meese Engine

Download page: <https://meese4.github.io/>

Meese Engine is a free GameCube homebrew voxel demo. The project provides a `game.dol` in its release zip and documents running it in Dolphin.

This is a later graphics/audio/input target, not an early CPU-core target.

## Suggested Compatibility Ladder

1. ngcSharp synthetic DOL.
2. Self-built assembly/C DOL with no libogc startup.
3. devkitPro minimal template DOL.
4. devkitPro console/video examples.
5. devkitPro controller input examples.
6. Simple GX examples.
7. Swiss.
8. Larger homebrew demos such as Meese Engine.

## Notes

- Prefer source-built fixtures whenever possible.
- Keep downloaded DOLs out of git unless their license clearly allows redistribution.
- Store local test downloads under `artifacts/` or another ignored scratch folder.
- Add trace logs only when they are small and intentionally curated.
