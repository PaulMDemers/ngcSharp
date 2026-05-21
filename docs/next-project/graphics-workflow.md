# Graphics Workflow

## Start With Capture Before Rendering

For a graphics-heavy console, the first win is not a beautiful renderer. It is knowing what commands the game sent.

Capture:

- Command FIFO bytes.
- Register writes.
- Vertex descriptors and formats.
- Texture state.
- Shader/combiner state.
- Copy/present commands.
- Framebuffer addresses.
- Per-draw summaries.

Only then render.

## Build A Diagnostic Renderer

A diagnostic renderer should favor explanations over speed. It should answer:

- Which draws happened?
- Which state was active?
- Which textures were sampled?
- Which pixels passed depth/alpha tests?
- Which copy became the visible frame?
- Did later clears overwrite useful content?
- Is the issue command decode, rasterization, texture decode, blending, or presentation?

This renderer can be software-only and bounded by max draws/pixels.

## Use Purpose-Built Graphics Fixtures

Build fixtures for:

- Solid colors.
- Vertex colors.
- Texture formats.
- Palette formats.
- Wrapping modes.
- Filtering modes.
- Depth tests.
- Alpha tests.
- Blending.
- Copy/resolve paths.
- Viewport/scissor/projection.

For each fixture, lock either an image hash or structural metrics. These fixtures will catch regressions faster than retail screenshots.

## Separate Capture, Replay, And Presentation

Do not assume "black screenshot" means "renderer broken." It may mean:

- The game has not reached drawing.
- Draws happened but were later cleared.
- Content was copied to a non-current buffer.
- The emulator selected the wrong framebuffer.
- The real output is in a texture-copy path.
- Alpha/color update rules hid content.
- Raster budget stopped too early.

Separate these phases:

- FIFO capture.
- Command replay.
- Software framebuffer/EFB state.
- Copy operations.
- Display buffer selection.
- Final image encoding.

Track each in summaries.

## Frame Selection Needs Evidence

Modern-ish consoles and late 3D systems often use multiple buffers, copies, clears, and intermediate render targets. The "last framebuffer" may be black or transitional.

Useful frame-source strategies:

- Explicit address.
- Current display register.
- Last display copy.
- Last nonblack display copy.
- Largest nonblack display copy.
- Copy index.
- EFB/current render target.

Expose which source was selected and why.

## Sample The Pipeline

When a rendered image is wrong, add sample diagnostics:

- Representative triangles.
- Barycentric sample positions.
- Interpolated attributes.
- Texture coordinates and texel addresses.
- Texture sample values.
- Combiner/TEV/shader inputs and outputs.
- Alpha/depth/blend decisions.

One row per sampled point can explain more than a thousand lines of draw logs.

## Compare Against A Reference Carefully

Reference screenshots are valuable only when comparing the same scene/window.

Before comparing pixels, verify:

- Same input sequence.
- Same boot duration or frame number.
- Same region/version.
- Same aspect/scaling policy.
- Same framebuffer source.
- No loading/prompt state mismatch.

If scenes differ, treat the comparison as a progression problem first, not a renderer accuracy problem.

## Optimize Renderer Work With Hash Discipline

Renderer optimizations are risky. Keep a before/after image hash or structural metric for every fast path:

- Same image hash for deterministic fixtures.
- Same selected frame source.
- Same draw/copy counts.
- Same nonblack bounds/counts.
- Same sampled pipeline rows when possible.

Only optimize the bucket that timings identify.
