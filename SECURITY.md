# Security Policy

ngcSharp parses untrusted binary formats such as DOL, ISO/GCM/RVZ, PNG, and emulator trace inputs. Treat crashes, uncontrolled memory growth, path traversal, or denial-of-service behavior as security-relevant even though this is not a network service.

## Reporting

Open a private security advisory if the hosting platform supports it. If not, open an issue with minimal public detail and offer to share the reproducer privately.

Please include:

- A short description of the issue.
- The command that triggers it.
- Whether the input is shareable.
- Expected versus actual behavior.
- Host OS and .NET SDK version.

Do not attach copyrighted game images or proprietary files to reports. If the issue requires a commercial game to reproduce, describe the title/version and the observed behavior without uploading the image.

## Scope

In scope:

- Crashes or hangs from malformed files.
- Path traversal in scripts or output paths.
- Excessive memory allocation from malformed metadata.
- Unsafe handling of generated artifacts.

Out of scope:

- Compatibility bugs without a security impact.
- Bugs that require modifying local source code.
- Issues in third-party tools such as Dolphin or devkitPro.

