# Compatibility Matrix

The compatibility matrix is the high-level workflow for turning local test binaries into repeatable regression targets.

It does not commit binaries or retail assets. The checked-in manifest names local paths under ignored folders such as `artifacts/`, plus user-provided retail image names. Run outputs stay under `artifacts/compat-matrix/`.

## Manifest

The curated manifest lives at:

```powershell
compat/targets.json
```

Each target records:

- `id`: stable target name for filters and summaries.
- `type`: `dol` or `disc`.
- `path`: local path to the binary or user-provided image.
- `suites`: coarse run groups such as `quick`, `homebrew`, `gx`, `retail`, `sonic`, or `pikmin`.
- `tags`: subsystem coverage such as `cpu`, `vi`, `xfb`, `gx`, `tev`, `exi-card`, `si`, `di`, or `dsp`.
- `maxInstructions` and `timeoutSeconds`: bounded run controls.
- Optional `gxFrame`, `xfbFrame`, `trace`, and `extraArgs` sections.
- Optional `expected` milestones such as `stopReason`, final `pc`, `topPc`, `minTopPcCount`, `nonExternalInterruptTopPc`, `minNonExternalInterruptTopPcCount`, `minPrsDecompressInstructions`, `minResourceLookupInstructions`, `minExternalInterruptLeafInstructions`, `minGxFifoBytes`, `minRenderedQuads`, `minRenderedTriangles`, `frameSource`, `frameSourceAddress`, `frameSourceCopyIndex`, `minDisplayCopies`, `minTextureCopies`, `minNonblackDisplayCopies`, `minMaxDisplayNonblack`, or `frameSha256`.

## Inventory Local Binaries

Generate a deduplicated inventory of local executable images:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/update-compat-inventory.ps1
```

This writes:

- `artifacts/compat-matrix/inventory.json`
- `artifacts/compat-matrix/inventory.csv`

The inventory is intentionally generated, not checked in. Use it to decide which loose DOLs should be promoted into `compat/targets.json`.

## List Targets

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-compat-matrix.ps1 -List
powershell -ExecutionPolicy Bypass -File scripts/run-compat-matrix.ps1 -Suites gx -List
powershell -ExecutionPolicy Bypass -File scripts/run-compat-matrix.ps1 -Tags exi-card -List
```

## Run Targets

Quick smoke pass:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-compat-matrix.ps1 -Suites quick -NoBuild -SkipMissing
```

Subsystem-focused pass:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-compat-matrix.ps1 -Tags gx,tev -NoBuild -SkipMissing
```

Named retail probes:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-compat-matrix.ps1 -Targets sonic-20m,pikmin-5m -NoBuild -SkipMissing
```

Each run writes:

- `summary.csv`: compact pass/fail/regression rollup.
- `summary.json`: full machine-readable run detail.
- Per-target `stdout.txt`, `stderr.txt`, and `run-summary.json`.
- Optional PNGs, traces, `gx-copies.csv`, and `gx-copies.summary.json` when requested by the target manifest.
- Profile targets surface structured `topPc`, `topPcCount`, filtered non-external-interrupt top PCs, PRS decompression, resource-lookup, and external-interrupt leaf counters in `summary.csv`.

## Interpreting Status

- `ok`: command completed and all expected milestones matched.
- `regressed`: command completed, but an expected milestone changed.
- `timeout`: watchdog killed the target.
- `exit-N`: emulator process returned nonzero exit code `N`.
- `missing`: target path was absent and `-SkipMissing` was used.
- `known-failing`: target is documented as currently failing; keep it in the matrix as a canary.
- `known-failing-ok`: a known-failing target unexpectedly met its current command-level expectations; inspect it and update the manifest.

Use this matrix for compatibility work before adding new bespoke scripts. Keep the manifest small and intentional; the generated inventory is where the messy local pile belongs.
