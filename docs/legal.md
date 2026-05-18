# Legal And Research Guidelines

This project is an emulator and diagnostics toolkit. Keep the repository clean of copyrighted assets and proprietary material.

## Do Not Commit

- Nintendo IPL/BIOS dumps.
- Commercial game images, RVZ/ISO/GCM files, WADs, or extracted game assets.
- Nintendo SDK headers, libraries, documents, or examples that are not legally redistributable.
- Dolphin binaries or generated Dolphin user profiles.
- Generated captures derived from commercial games unless maintainers explicitly approve a tiny, non-infringing diagnostic artifact.

## Acceptable Test Material

Prefer:

- Original homebrew source created for this repository.
- Small generated DOLs.
- Public-domain or clearly licensed homebrew tests.
- Unit tests with synthetic data.
- Locally generated diagnostics that are ignored by git.

## Research Sources

Acceptable research sources include public hardware notes, behavior observed from legally obtained software, and open-source emulator implementations used as references.

When studying another emulator:

- Use it as a behavioral oracle and source of concepts.
- Do not copy code unless the license is compatible and the project intentionally accepts that license obligation.
- Cite important references in docs or PR notes.

Dolphin is GPLv2+. Copying Dolphin code would have licensing consequences for this project. Treat it as an oracle unless the licensing decision is made explicitly.

## User-Provided Games

Users may point local scripts at their own legally obtained dumps. Those paths should stay local and ignored.

The repository should document benchmark filenames only as examples of local inputs, not as distributed assets.

