# Ten-Pass Compatibility Summary

This report summarizes the repeated compatibility loop run on May 22, 2026 against every ROM present in the local `roms` directory.

Each pass used this sequence:

1. Run a compatibility sweep across all ROMs.
2. Apply/review code optimization.
3. Run a second compatibility sweep across all ROMs.
4. Refresh repository diagnostic screenshots and README links.
5. Prepare the code for push to origin.

The automated loop used a 2,000,000-instruction budget per ROM per phase, with `--fast-forward-idle`, `--fast-forward-write-watch`, `--profile-pc 8`, EXI tracing, quiet output, and run summaries enabled. The deeper single-pass 15,000,000-instruction sweep remains available under `artifacts/retail-compat-sweep/20260522-all-roms-15m-optimized/`.

## Optimization

The source optimization focused on `DolRunner`'s hot fast-forward dispatch path. Expensive signature matchers now have cheap first-instruction guards before they inspect multi-instruction patterns in emulated memory.

Guarded probes include:

- Sunshine flag wait and byte-run copy loops.
- Interleaved word fill and byte fill loops.
- Resource table scans.
- Null-terminated byte scan loops.
- Aligned/simple string compare helpers.
- CTR delay, CTR byte copy, single-byte copy, word copy, string length, and texture sample helpers.

## Aggregate Result

- Total sweep phases: 20
- Total ROM executions: 300
- ROMs per phase: 15
- Instruction budget per ROM: 2,000,000
- Successful runs: 300
- Failures/timeouts: 0
- Maximum GX FIFO bytes observed: 0

The zero GX FIFO count means these repeated probes are still exercising early boot/runtime paths and not visible GX presentation for the current instruction window.

## Pass Results

| Pass | Phase | ROMs | OK | Failures | Total Seconds | Max GX FIFO Bytes |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 1 | pre | 15 | 15 | 0 | 212.461 | 0 |
| 1 | post | 15 | 15 | 0 | 195.525 | 0 |
| 2 | pre | 15 | 15 | 0 | 195.539 | 0 |
| 2 | post | 15 | 15 | 0 | 195.902 | 0 |
| 3 | pre | 15 | 15 | 0 | 198.835 | 0 |
| 3 | post | 15 | 15 | 0 | 198.440 | 0 |
| 4 | pre | 15 | 15 | 0 | 199.915 | 0 |
| 4 | post | 15 | 15 | 0 | 200.532 | 0 |
| 5 | pre | 15 | 15 | 0 | 192.048 | 0 |
| 5 | post | 15 | 15 | 0 | 191.853 | 0 |
| 6 | pre | 15 | 15 | 0 | 191.609 | 0 |
| 6 | post | 15 | 15 | 0 | 191.933 | 0 |
| 7 | pre | 15 | 15 | 0 | 191.664 | 0 |
| 7 | post | 15 | 15 | 0 | 191.268 | 0 |
| 8 | pre | 15 | 15 | 0 | 195.280 | 0 |
| 8 | post | 15 | 15 | 0 | 194.199 | 0 |
| 9 | pre | 15 | 15 | 0 | 193.556 | 0 |
| 9 | post | 15 | 15 | 0 | 194.175 | 0 |
| 10 | pre | 15 | 15 | 0 | 194.862 | 0 |
| 10 | post | 15 | 15 | 0 | 198.653 | 0 |

Full generated run data is under `artifacts/retail-compat-sweep/ten-pass-20260522/`.
