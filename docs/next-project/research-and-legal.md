# Research And Legal Hygiene

## Keep The Repo Clean

Never commit commercial games, firmware, proprietary SDK files, extracted assets, generated captures from commercial games, or local emulator binaries. Keep them in ignored artifact/workspace locations.

Acceptable committed assets usually include:

- Original synthetic tests.
- Source-built homebrew fixtures where the source is included and licensed appropriately.
- Small generated binaries created from repo source.
- Text documentation of local filenames without bundling the files.
- Unit tests using synthetic data.

When in doubt, document how to reproduce locally rather than committing the artifact.

## Use Other Emulators Responsibly

Mature open-source emulators are invaluable references. Use them as:

- Behavioral oracles.
- Sources of terminology.
- Guides to which subsystems matter.
- Reference capture tools.
- Comparison targets for public/homebrew software.

Do not copy code unless the license is compatible and the project intentionally accepts the obligation. For example, studying a GPL emulator is fine; copying its implementation into a permissively licensed project is not.

Safer research pattern:

1. Observe behavior in the reference emulator.
2. Write your own notes in project language.
3. Build a small test or diagnostic.
4. Implement from hardware docs, observed behavior, and your own reasoning.
5. Cite the source category, not copied code.

## Prefer Primary And Public Sources

Useful source categories:

- Public hardware documentation.
- Public homebrew SDK headers and examples, where licenses permit.
- Patents and architecture manuals.
- Test suites and homebrew demos.
- Public reverse-engineering notes with clear provenance.
- Behavior observed from legally obtained software.

Avoid relying on unsourced forum claims for exact behavior unless you can test them.

## Separate Facts From Inference

Emulator work produces many tempting guesses. Write them down carefully:

- Fact: "This register read returned `0x20` in the trace."
- Inference: "This bit may indicate input data ready."
- Hypothesis: "Setting it after poll should make the game read controller buffers."
- Result: "The game read all four channels and observed the expected button mask."

This habit prevents "we know" from creeping into places where the project only has one observed data point.

## Retail Images Are Local Benchmarks

Commercial software can be a useful compatibility benchmark when the developer legally owns the image. Treat it as local input:

- Store outside git or under ignored `artifacts/`.
- Do not publish screenshots unless the project has a clear policy.
- Do not extract and commit assets.
- Keep benchmarks reproducible through commands and summary fields.
- Promote homebrew equivalents whenever possible.

## Record Licensing Decisions

Every external component should have a reason and a license trail:

- Homebrew fixtures.
- Test ROMs or demos.
- Reference tools.
- Image/video libraries.
- Compression/container libraries.

The earlier this is documented, the less painful public release becomes.
