using System.Buffers.Binary;
using System.IO.Compression;
using NgcSharp.App;
using NgcSharp.Core;

namespace NgcSharp.Tests;

public sealed class GxFifoSoftwareRendererTests
{
    [Fact]
    public void RejectsOversizedDiagnosticFramesBeforeRendering()
    {
        List<MmioAccess> accesses = [new(MmioAccessKind.Write, 0xCC00_8000, 1, 0, "GX FIFO")];

        bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, "oversized.png", width: 4096, height: 4096, out GxFifoSoftwareRenderResult? result, out string? error);

        Assert.False(rendered);
        Assert.Null(result);
        Assert.Contains("too large", error);
    }

    [Fact]
    public void RendersDirectPositionColorQuads()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        try
        {
            List<byte> bytes =
            [
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 1, 1, 255, 0, 0, 255);
            AddVertex(bytes, 3, 1, 255, 0, 0, 255);
            AddVertex(bytes, 3, 3, 255, 0, 0, 255);
            AddVertex(bytes, 1, 3, 255, 0, 0, 255);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, path, width: 5, height: 5, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.Draws);
            Assert.Equal(1, result.RenderedQuads);
            Assert.True(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RendersIndexedPositionColorTrianglesFromMainRam()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        WritePosition(memory, 0x8000_0100, 1, 1, 0);
        WritePosition(memory, 0x8000_010C, 3, 1, 0);
        WritePosition(memory, 0x8000_0118, 2, 3, 0);
        memory.Load(0x8000_0200, [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255]);

        try
        {
            List<byte> bytes =
            [
                0x08, 0x50, 0x00, 0x00, 0x44, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0xA0, 0x00, 0x00, 0x01, 0x00,
                0x08, 0xB0, 0x00, 0x00, 0x00, 0x0C,
                0x08, 0xA2, 0x00, 0x00, 0x02, 0x00,
                0x08, 0xB2, 0x00, 0x00, 0x00, 0x04,
                0x90, 0x00, 0x03,
                0x00, 0x00,
                0x01, 0x01,
                0x02, 0x02,
            ];

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 5, height: 5, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.Draws);
            Assert.Equal(1, result.RenderedTriangles);
            Assert.True(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RendersNegativeScreenSpaceTriangleStrips()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        try
        {
            List<byte> bytes =
            [
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x98, 0x00, 0x04,
            ];
            AddVertex(bytes, -4, -4, 32, 192, 64, 255);
            AddVertex(bytes, -4, -2, 32, 192, 64, 255);
            AddVertex(bytes, 0, -4, 32, 192, 64, 255);
            AddVertex(bytes, 0, -2, 32, 192, 64, 255);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, path, width: 5, height: 5, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.Draws);
            Assert.Equal(2, result.RenderedTriangles);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WritesDrawDiagnosticsForDirectDraws()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        try
        {
            List<byte> bytes =
            [
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 1, 1, 255, 0, 0, 255);
            AddVertex(bytes, 3, 1, 255, 0, 0, 255);
            AddVertex(bytes, 3, 3, 255, 0, 0, 255);
            AddVertex(bytes, 1, 3, 255, 0, 0, 255);
            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteDrawDiagnostics(accesses, memory: null, path, maxDraws: 1, out GxFifoDrawDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.TotalDraws);
            Assert.Equal(1, result.DrawsWritten);
            string text = File.ReadAllText(path);
            Assert.Contains("layout=pos:direct/12, col0:direct/4", text);
            Assert.Contains("v0: pos=(1, 1)", text);
            Assert.Contains("all-zero-xy=False", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WritesPipelineStateInDrawDiagnostics()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        try
        {
            List<byte> bytes =
            [
                0x10, 0x00, 0x05, 0x10, 0x1A,
            ];
            AddSingle(bytes, 320);
            AddSingle(bytes, -240);
            AddSingle(bytes, 1);
            AddSingle(bytes, 320);
            AddSingle(bytes, 240);
            AddSingle(bytes, 1);
            bytes.AddRange(
            [
                0x61, 0x20, 0x12, 0x34, 0x56,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ]);
            AddVertex(bytes, 1, 1, 255, 0, 0, 255);
            AddVertex(bytes, 3, 1, 255, 0, 0, 255);
            AddVertex(bytes, 3, 3, 255, 0, 0, 255);
            AddVertex(bytes, 1, 3, 255, 0, 0, 255);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteDrawDiagnostics(accesses, memory: null, path, maxDraws: 1, out GxFifoDrawDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.TotalDraws);
            string text = File.ReadAllText(path);
            Assert.Contains("XF viewport: scale=(320, -240, 1), origin=(320, 240, 1)", text);
            Assert.Contains("BP scissor raw: top-left=0x123456, bottom-right=0x000000", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WritesIndirectTevStateInDrawDiagnostics()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        try
        {
            List<byte> bytes =
            [
                0x61, 0x00, 0x02, 0x00, 0x00,
                0x61, 0x10, 0x06, 0xC0, 0x00,
                0x61, 0x11, 0x1C, 0x63, 0x35,
                0x61, 0x25, 0x00, 0x54, 0x32,
                0x61, 0x27, 0x00, 0x0B, 0x13,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 1, 1, 255, 0, 0, 255);
            AddVertex(bytes, 3, 1, 255, 0, 0, 255);
            AddVertex(bytes, 3, 3, 255, 0, 0, 255);
            AddVertex(bytes, 1, 3, 255, 0, 0, 255);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteDrawDiagnostics(accesses, memory: null, path, maxDraws: 1, out GxFifoDrawDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            string text = File.ReadAllText(path);
            Assert.Contains("BP gen mode raw: 0x020000, tev-stages=1, ind-stages=2", text);
            Assert.Contains("BP indirect TEV stage0 raw: 0x06C000, direct, indtex0, fmt=8, bias=none, mtx=off, wrap=(0, 0), addprev=False, utc-lod=False, alpha=off", text);
            Assert.Contains("BP indirect TEV stage1 raw: 0x1C6335, indirect, indtex1, fmt=5, bias=st, mtx=0, wrap=(64, 32), addprev=True, utc-lod=True, alpha=t", text);
            Assert.Contains("BP indirect order stage0: texcoord2/texmap3, scale=(4, 8)", text);
            Assert.Contains("BP indirect order stage1: texcoord5/texmap4, scale=(16, 32)", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WritesDirectTextureCoordinatesInDrawDiagnostics()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        try
        {
            List<byte> bytes =
            [
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x90, 0x00, 0x03,
            ];
            AddTexturedVertex(bytes, 1, 1, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 3, 1, 255, 255, 255, 255, 1, 0);
            AddTexturedVertex(bytes, 2, 3, 255, 255, 255, 255, 0.5f, 1);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteDrawDiagnostics(accesses, memory: null, path, maxDraws: 1, out GxFifoDrawDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.TotalDraws);
            string text = File.ReadAllText(path);
            Assert.Contains("layout=pos:direct/12, col0:direct/4, tex0:direct/8", text);
            Assert.Contains("v0: pos=(1, 1) color=(255,255,255) tex0=(0, 0)", text);
            Assert.Contains("v2: pos=(2, 3) color=(255,255,255) tex0=(0.5, 1)", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppliesTex0MatrixGenerationInDrawDiagnostics()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        try
        {
            List<byte> bytes =
            [
                0x10, 0x00, 0x00, 0x10, 0x18,
            ];
            AddUInt32(bytes, 30u << 6);
            bytes.AddRange([0x10, 0x00, 0x00, 0x10, 0x40]);
            AddUInt32(bytes, 5u << 7);
            bytes.AddRange([0x10, 0x00, 0x07, 0x00, 0x1E]);
            AddSingle(bytes, 2);
            AddSingle(bytes, 0);
            AddSingle(bytes, 0);
            AddSingle(bytes, 1);
            AddSingle(bytes, 0);
            AddSingle(bytes, 3);
            AddSingle(bytes, 0);
            AddSingle(bytes, -1);
            bytes.AddRange(
            [
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x90, 0x00, 0x03,
            ]);
            AddTexturedVertex(bytes, 1, 1, 255, 255, 255, 255, 0.25f, 0.5f);
            AddTexturedVertex(bytes, 3, 1, 255, 255, 255, 255, 1, 0);
            AddTexturedVertex(bytes, 2, 3, 255, 255, 255, 255, 0.5f, 1);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteDrawDiagnostics(accesses, memory: null, path, maxDraws: 1, out GxFifoDrawDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            string text = File.ReadAllText(path);
            Assert.Contains("XF matrix index low raw: 0x00000780, pos-base=0x0000, tex0-base=0x001E", text);
            Assert.Contains("XF texgen0 raw: 0x00000280, projection=mtx2x4, source=tex0, type=regular", text);
            Assert.Contains("v0: pos=(1, 1) color=(255,255,255) tex0=(1.5, 0.5) raw-tex0=(0.25, 0.5)", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DecodesBlendTevAndPixelModeInDrawDiagnostics()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        try
        {
            List<byte> bytes =
            [
                0x61, 0x41, 0x00, 0xF4, 0x3D,
                0x61, 0xF3, 0x3F, 0x00, 0x00,
                0x61, 0xC0, 0x08, 0xAC, 0x8F,
                0x61, 0xC1, 0x08, 0xF2, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x90, 0x00, 0x03,
            ];
            AddTexturedVertex(bytes, 1, 1, 255, 128, 0, 128, 0, 0);
            AddTexturedVertex(bytes, 3, 1, 255, 128, 0, 128, 1, 0);
            AddTexturedVertex(bytes, 2, 3, 255, 128, 0, 128, 0.5f, 1);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteDrawDiagnostics(accesses, memory: null, path, maxDraws: 1, out GxFifoDrawDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            string text = File.ReadAllText(path);
            Assert.Contains("blend-mode=blend, src=src-alpha, dst=one, logic=set, color-update=True, alpha-update=True", text);
            Assert.Contains("alpha-test=(always 0) and (always 0)", text);
            Assert.Contains("BP TEV stage0 raw: color=0x08AC8F, alpha=0x08F2D0, mode=Blend", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DecodesTextureFilterAndLodStateInDrawDiagnostics()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        try
        {
            List<byte> bytes =
            [
                0x61, 0x80, 0x21, 0x51, 0xD8,
                0x61, 0x84, 0x00, 0x30, 0x10,
                0x61, 0x88, 0xE0, 0x7C, 0xFF,
                0x61, 0x94, 0x00, 0x10, 0x00,
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, 0, 0);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteDrawDiagnostics(accesses, memory: null, path, maxDraws: 1, out GxFifoDrawDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            string text = File.ReadAllText(path);
            Assert.Contains("Texture map 0: 256x32 CMPR", text);
            Assert.Contains("wrap=(clamp, mirror)", text);
            Assert.Contains("filter=(mag=linear, min=linear-mipmap-linear)", text);
            Assert.Contains("lod=(bias=-2.75, min=1, max=3", text);
            Assert.Contains("diag=True", text);
            Assert.Contains("clamp=True", text);
            Assert.Contains("aniso=1", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RendersFromSelectedStage0TextureMap()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        memory.Write16(0x0002_0000, 0xF800);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x28, 0x00, 0x00, 0x41,
                0x61, 0x81, 0x00, 0x00, 0x00,
                0x61, 0x89, 0x40, 0x00, 0x00,
                0x61, 0x95, 0x00, 0x10, 0x00,
                0x61, 0xC0, 0x08, 0xFF, 0xF8,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, 0, 0);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r > 240, $"Expected red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g < 16, $"Expected red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b < 16, $"Expected red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WritesActiveTextureDiagnostics()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        GameCubeMemory memory = new();
        memory.Write16(0x0002_0000, 0xF800);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x28, 0x00, 0x00, 0x41,
                0x61, 0x81, 0x00, 0x00, 0x00,
                0x61, 0x89, 0x40, 0x00, 0x00,
                0x61, 0x95, 0x00, 0x10, 0x00,
                0x61, 0xC0, 0x08, 0xFF, 0xF8,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, 0, 0);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteTextureDiagnostics(accesses, memory, directory, skipDraws: 0, maxDraws: 1, out GxFifoTextureDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.TexturesWritten);
            string indexPath = Path.Combine(directory, "index.csv");
            Assert.True(File.Exists(indexPath));
            string[] lines = File.ReadAllLines(indexPath);
            Assert.Equal(2, lines.Length);
            Assert.Contains(",1,", lines[1]);
            Assert.Contains(",RGB565,", lines[1]);
            Assert.Contains("0x00020000", lines[1]);
            Assert.Contains("_alpha.png", lines[1]);
            string png = Directory.GetFiles(directory, "*.png").Single(file => !Path.GetFileNameWithoutExtension(file).EndsWith("_alpha", StringComparison.Ordinal));
            (byte r, byte g, byte b) = ReadPngPixel(png, x: 0, y: 0, width: 1, height: 1);
            Assert.True(r > 240, $"Expected texture diagnostic red pixel, got ({r},{g},{b}).");
            Assert.True(g < 16, $"Expected texture diagnostic red pixel, got ({r},{g},{b}).");
            Assert.True(b < 16, $"Expected texture diagnostic red pixel, got ({r},{g},{b}).");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void TextureDiagnosticsPreferDrawTimeMemorySnapshots()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        GameCubeMemory memory = new();
        memory.Write16(0x0002_0000, 0x07E0);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x28, 0x00, 0x00, 0x41,
                0x61, 0x81, 0x00, 0x00, 0x00,
                0x61, 0x89, 0x40, 0x00, 0x00,
                0x61, 0x95, 0x00, 0x10, 0x00,
                0x61, 0xC0, 0x08, 0xFF, 0xF8,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
            ];
            int drawOffset = bytes.Count;
            bytes.AddRange([0x80, 0x00, 0x04]);
            AddTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, 0, 0);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();
            byte[] snapshotBytes = new byte[32];
            snapshotBytes[0] = 0xF8;
            GxMemorySnapshotSet snapshots = new([new GxMemorySnapshot(drawOffset, 0x0002_0000, snapshotBytes)]);

            bool wrote = GxFifoSoftwareRenderer.TryWriteTextureDiagnostics(accesses, memory, directory, skipDraws: 0, maxDraws: 1, out GxFifoTextureDiagnosticResult? result, out string? error, snapshots);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.TexturesWritten);
            string[] lines = File.ReadAllLines(Path.Combine(directory, "index.csv"));
            Assert.Contains($"checkpoint+0x{drawOffset:X}", lines[1]);
            string png = Directory.GetFiles(directory, "*.png").Single(file => !Path.GetFileNameWithoutExtension(file).EndsWith("_alpha", StringComparison.Ordinal));
            (byte r, byte g, byte b) = ReadPngPixel(png, x: 0, y: 0, width: 1, height: 1);
            Assert.True(r > 240, $"Expected snapshot red pixel, got ({r},{g},{b}).");
            Assert.True(g < 16, $"Expected snapshot red pixel, got ({r},{g},{b}).");
            Assert.True(b < 16, $"Expected snapshot red pixel, got ({r},{g},{b}).");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void LiveTextureSnapshotCollectorCapturesDrawWindowTextureMemory()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        GameCubeMemory memory = new();
        memory.Write16(0x0002_0000, 0xF800);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x28, 0x00, 0x00, 0x41,
                0x61, 0x81, 0x00, 0x00, 0x00,
                0x61, 0x89, 0x40, 0x00, 0x00,
                0x61, 0x95, 0x00, 0x10, 0x00,
                0x61, 0xC0, 0x08, 0xFF, 0xF8,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
            ];
            int drawOffset = bytes.Count;
            bytes.AddRange([0x80, 0x00, 0x04]);
            AddTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, 0, 0);

            GxLiveTextureSnapshotCollector collector = GxLiveTextureSnapshotCollector.CreateForDrawWindow(skipDraws: 0, maxDraws: 1);
            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();
            foreach (MmioAccess access in accesses)
            {
                collector.Feed(access, memory);
            }

            memory.Write16(0x0002_0000, 0x07E0);
            GxMemorySnapshot snapshot = Assert.Single(collector.Snapshots);
            Assert.Equal(drawOffset, snapshot.FifoOffset);
            Assert.Equal(0x0002_0000u, snapshot.Address);
            Assert.Equal(32, snapshot.Bytes.Length);
            GxMemorySnapshotSet snapshots = new(collector.Snapshots);

            bool wrote = GxFifoSoftwareRenderer.TryWriteTextureDiagnostics(accesses, memory, directory, skipDraws: 0, maxDraws: 1, out GxFifoTextureDiagnosticResult? result, out string? error, snapshots);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            string[] lines = File.ReadAllLines(Path.Combine(directory, "index.csv"));
            Assert.Contains($"checkpoint+0x{drawOffset:X}", lines[1]);
            string png = Directory.GetFiles(directory, "*.png").Single(file => !Path.GetFileNameWithoutExtension(file).EndsWith("_alpha", StringComparison.Ordinal));
            (byte r, byte g, byte b) = ReadPngPixel(png, x: 0, y: 0, width: 1, height: 1);
            Assert.True(r > 240, $"Expected live snapshot red pixel, got ({r},{g},{b}).");
            Assert.True(g < 16, $"Expected live snapshot red pixel, got ({r},{g},{b}).");
            Assert.True(b < 16, $"Expected live snapshot red pixel, got ({r},{g},{b}).");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void RendersFromSelectedStage0TexCoord()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        memory.Write16(0x0002_0000, 0xF800);
        memory.Write16(0x0002_0002, 0x07E0);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x28, 0x00, 0x00, 0x49,
                0x61, 0x81, 0x00, 0x00, 0x00,
                0x61, 0x89, 0x40, 0x00, 0x01,
                0x61, 0x95, 0x00, 0x10, 0x00,
                0x61, 0xC0, 0x08, 0xFF, 0xF8,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x05,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x00, 0x00, 0x00, 0x09,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddDualTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, tex0S: 1, tex0T: 0, tex1S: 0, tex1T: 0);
            AddDualTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, tex0S: 1, tex0T: 0, tex1S: 0, tex1T: 0);
            AddDualTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, tex0S: 1, tex0T: 0, tex1S: 0, tex1T: 0);
            AddDualTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, tex0S: 1, tex0T: 0, tex1S: 0, tex1T: 0);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r > 240, $"Expected texcoord1 red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g < 16, $"Expected texcoord1 red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b < 16, $"Expected texcoord1 red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TreatsCmprSelectorThreeAsTransparentWhenColor0IsNotGreaterThanColor1()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        memory.Write16(0x0002_0000, 0x0000);
        memory.Write16(0x0002_0002, 0xFFFF);
        memory.Write32(0x0002_0004, 0x0000_0003);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x28, 0x00, 0x00, 0x40,
                0x61, 0x80, 0x00, 0x00, 0x00,
                0x61, 0x88, 0xE0, 0x1C, 0x07,
                0x61, 0x94, 0x00, 0x10, 0x00,
                0x61, 0xC0, 0x08, 0xFF, 0xF8,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, 0.4f, 0.4f);
            AddTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, 0.4f, 0.4f);
            AddTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, 0.4f, 0.4f);
            AddTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, 0.4f, 0.4f);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r < 16, $"Expected transparent CMPR selector to render black, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g < 16, $"Expected transparent CMPR selector to render black, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b < 16, $"Expected transparent CMPR selector to render black, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppliesLinearTextureFilterWhenMagFilterIsLinear()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        memory.Write16(0x0002_0000, 0xF800);
        memory.Write16(0x0002_0002, 0x07E0);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x28, 0x00, 0x00, 0x40,
                0x61, 0x80, 0x00, 0x00, 0x10,
                0x61, 0x88, 0x40, 0x00, 0x01,
                0x61, 0x94, 0x00, 0x10, 0x00,
                0x61, 0xC0, 0x08, 0xFF, 0xF8,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, 0.5f, 0);
            AddTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, 0.5f, 0);
            AddTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, 0.5f, 0);
            AddTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, 0.5f, 0);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.InRange(r, 120, 140);
            Assert.InRange(g, 120, 140);
            Assert.True(b < 16, $"Expected red/green linear blend, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppliesSimpleIndirectTextureOffset()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        memory.Write16(0x0002_0000, 0xF800);
        memory.Write16(0x0002_0002, 0x07E0);
        memory.Write16(0x0002_0100, 0xF800);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x00, 0x01, 0x00, 0x00,
                0x61, 0x10, 0x00, 0x00, 0x30,
                0x61, 0x27, 0x00, 0x00, 0x01,
                0x61, 0x28, 0x00, 0x00, 0x40,
                0x61, 0x80, 0x00, 0x00, 0x00,
                0x61, 0x88, 0x40, 0x00, 0x01,
                0x61, 0x94, 0x00, 0x10, 0x00,
                0x61, 0x81, 0x00, 0x00, 0x00,
                0x61, 0x89, 0x40, 0x00, 0x00,
                0x61, 0x95, 0x00, 0x10, 0x08,
                0x61, 0xC0, 0x08, 0xFF, 0xF8,
                0x61, 0xC1, 0x08, 0xFF, 0xC0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, 0, 0);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r < 16, $"Expected indirect-offset green center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g > 240, $"Expected indirect-offset green center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b < 16, $"Expected indirect-offset green center pixel, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppliesIndirectTextureMatrix()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        memory.Write16(0x0002_0000, 0xF800);
        memory.Write16(0x0002_0002, 0x07E0);
        memory.Write16(0x0002_0100, 0xF800);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x00, 0x01, 0x00, 0x00,
                0x61, 0x06, 0x40, 0x03, 0x00,
                0x61, 0x07, 0x00, 0x00, 0x00,
                0x61, 0x08, 0x40, 0x00, 0x00,
                0x61, 0x10, 0x00, 0x02, 0x30,
                0x61, 0x27, 0x00, 0x00, 0x01,
                0x61, 0x28, 0x00, 0x00, 0x40,
                0x61, 0x80, 0x00, 0x00, 0x00,
                0x61, 0x88, 0x40, 0x00, 0x01,
                0x61, 0x94, 0x00, 0x10, 0x00,
                0x61, 0x81, 0x00, 0x00, 0x00,
                0x61, 0x89, 0x40, 0x00, 0x00,
                0x61, 0x95, 0x00, 0x10, 0x08,
                0x61, 0xC0, 0x08, 0xFF, 0xF8,
                0x61, 0xC1, 0x08, 0xFF, 0xC0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, 0, 0);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r < 16, $"Expected matrix-scaled indirect green center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g > 240, $"Expected matrix-scaled indirect green center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b < 16, $"Expected matrix-scaled indirect green center pixel, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppliesIndirectZeroWrapBeforeOffset()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        memory.Write16(0x0002_0000, 0xF800);
        memory.Write16(0x0002_0002, 0x07E0);
        memory.Load(0x0002_0100, [128]);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x00, 0x01, 0x00, 0x00,
                0x61, 0x10, 0x00, 0xC0, 0x30,
                0x61, 0x27, 0x00, 0x00, 0x01,
                0x61, 0x28, 0x00, 0x00, 0x40,
                0x61, 0x80, 0x00, 0x00, 0x00,
                0x61, 0x88, 0x40, 0x00, 0x01,
                0x61, 0x94, 0x00, 0x10, 0x00,
                0x61, 0x81, 0x00, 0x00, 0x00,
                0x61, 0x89, 0x10, 0x00, 0x00,
                0x61, 0x95, 0x00, 0x10, 0x08,
                0x61, 0xC0, 0x08, 0xFF, 0xF8,
                0x61, 0xC1, 0x08, 0xFF, 0xC0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, 1, 0);
            AddTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, 1, 0);
            AddTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, 1, 0);
            AddTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, 1, 0);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r > 240, $"Expected zero-wrapped red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g < 16, $"Expected zero-wrapped red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b < 16, $"Expected zero-wrapped red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AccumulatesIndirectAddPreviousOffset()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        memory.Write16(0x0002_0000, 0xF800);
        memory.Write16(0x0002_0002, 0x07E0);
        memory.Write16(0x0002_0004, 0x001F);
        memory.Write16(0x0002_0006, 0xFFFF);
        memory.Load(0x0002_0100, [192]);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x00, 0x01, 0x04, 0x00,
                0x61, 0x10, 0x00, 0x00, 0x30,
                0x61, 0x11, 0x10, 0x00, 0x30,
                0x61, 0x27, 0x00, 0x00, 0x01,
                0x61, 0x28, 0x04, 0x00, 0x40,
                0x61, 0x80, 0x00, 0x00, 0x00,
                0x61, 0x88, 0x40, 0x00, 0x03,
                0x61, 0x94, 0x00, 0x10, 0x00,
                0x61, 0x81, 0x00, 0x00, 0x00,
                0x61, 0x89, 0x10, 0x00, 0x00,
                0x61, 0x95, 0x00, 0x10, 0x08,
                0x61, 0xC0, 0x08, 0xFF, 0xF8,
                0x61, 0xC1, 0x08, 0xFF, 0xC0,
                0x61, 0xC2, 0x08, 0xFF, 0xF8,
                0x61, 0xC3, 0x08, 0xFF, 0xC0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, 0, 0);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r > 240, $"Expected accumulated indirect white center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g > 240, $"Expected accumulated indirect white center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b > 240, $"Expected accumulated indirect white center pixel, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RepeatsPreviousIndirectOffset()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        memory.Write16(0x0002_0000, 0xF800);
        memory.Write16(0x0002_0002, 0x07E0);
        memory.Write16(0x0002_0004, 0x001F);
        memory.Write16(0x0002_0006, 0xFFFF);
        memory.Load(0x0002_0100, [193]);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x00, 0x01, 0x04, 0x00,
                0x61, 0x10, 0x00, 0x00, 0x30,
                0x61, 0x11, 0x16, 0xC0, 0x00,
                0x61, 0x27, 0x00, 0x00, 0x01,
                0x61, 0x28, 0x04, 0x00, 0x40,
                0x61, 0x80, 0x00, 0x00, 0x00,
                0x61, 0x88, 0x40, 0x00, 0x03,
                0x61, 0x94, 0x00, 0x10, 0x00,
                0x61, 0x81, 0x00, 0x00, 0x00,
                0x61, 0x89, 0x10, 0x00, 0x00,
                0x61, 0x95, 0x00, 0x10, 0x08,
                0x61, 0xC0, 0x08, 0xFF, 0xF8,
                0x61, 0xC1, 0x08, 0xFF, 0xC0,
                0x61, 0xC2, 0x08, 0xFF, 0xF8,
                0x61, 0xC3, 0x08, 0xFF, 0xC0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, 0, 0);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r < 16, $"Expected repeated indirect blue center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g < 16, $"Expected repeated indirect blue center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b > 240, $"Expected repeated indirect blue center pixel, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppliesDynamicIndirectSMatrix()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        memory.Write16(0x0002_0000, 0xF800);
        memory.Write16(0x0002_0002, 0x07E0);
        memory.Write16(0x0002_0004, 0x001F);
        memory.Write16(0x0002_0006, 0xFFFF);
        memory.Load(0x0002_0100, [193]);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x00, 0x01, 0x04, 0x00,
                0x61, 0x06, 0x40, 0x03, 0xFF,
                0x61, 0x07, 0x00, 0x00, 0x00,
                0x61, 0x08, 0x40, 0x00, 0x00,
                0x61, 0x10, 0x06, 0xCA, 0x30,
                0x61, 0x11, 0x16, 0xC0, 0x00,
                0x61, 0x27, 0x00, 0x00, 0x01,
                0x61, 0x28, 0x04, 0x00, 0x40,
                0x61, 0x80, 0x00, 0x00, 0x00,
                0x61, 0x88, 0x40, 0x00, 0x03,
                0x61, 0x94, 0x00, 0x10, 0x00,
                0x61, 0x81, 0x00, 0x00, 0x00,
                0x61, 0x89, 0x10, 0x00, 0x00,
                0x61, 0x95, 0x00, 0x10, 0x08,
                0x61, 0xC0, 0x08, 0xFF, 0xF8,
                0x61, 0xC1, 0x08, 0xFF, 0xC0,
                0x61, 0xC2, 0x08, 0xFF, 0xF8,
                0x61, 0xC3, 0x08, 0xFF, 0xC0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, 0, 0);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r < 16, $"Expected dynamic indirect blue center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g < 16, $"Expected dynamic indirect blue center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b > 240, $"Expected dynamic indirect blue center pixel, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppliesIndirectCoordinateScale()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        memory.Write16(0x0002_0000, 0xF800);
        memory.Write16(0x0002_0002, 0x07E0);
        memory.Write16(0x0002_0004, 0x001F);
        memory.Write16(0x0002_0006, 0xFFFF);
        memory.Load(0x0002_0100, [128, 192, 255]);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x00, 0x01, 0x00, 0x00,
                0x61, 0x10, 0x00, 0x00, 0x30,
                0x61, 0x25, 0x00, 0x00, 0x01,
                0x61, 0x27, 0x00, 0x00, 0x09,
                0x61, 0x28, 0x00, 0x00, 0x40,
                0x61, 0x80, 0x00, 0x00, 0x00,
                0x61, 0x88, 0x40, 0x00, 0x03,
                0x61, 0x94, 0x00, 0x10, 0x00,
                0x61, 0x81, 0x00, 0x00, 0x00,
                0x61, 0x89, 0x10, 0x00, 0x02,
                0x61, 0x95, 0x00, 0x10, 0x08,
                0x61, 0xC0, 0x08, 0xFF, 0xF8,
                0x61, 0xC1, 0x08, 0xFF, 0xC0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x05,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x00, 0x00, 0x00, 0x09,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddDualTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, tex0S: 0, tex0T: 0, tex1S: 1, tex1T: 0);
            AddDualTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, tex0S: 0, tex0T: 0, tex1S: 1, tex1T: 0);
            AddDualTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, tex0S: 0, tex0T: 0, tex1S: 1, tex1T: 0);
            AddDualTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, tex0S: 0, tex0T: 0, tex1S: 1, tex1T: 0);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r < 16, $"Expected scaled indirect blue center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g < 16, $"Expected scaled indirect blue center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b > 240, $"Expected scaled indirect blue center pixel, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UsesIndirectBumpAlphaAsRasterColor()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        memory.Load(0x0002_0100, [31]);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x00, 0x01, 0x04, 0x00,
                0x61, 0x11, 0x00, 0x00, 0x80,
                0x61, 0x27, 0x00, 0x00, 0x01,
                0x61, 0x28, 0x28, 0x00, 0x00,
                0x61, 0x81, 0x00, 0x00, 0x00,
                0x61, 0x89, 0x10, 0x00, 0x00,
                0x61, 0x95, 0x00, 0x10, 0x08,
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x61, 0xC2, 0x08, 0xFF, 0xFA,
                0x61, 0xC3, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, 0, 0);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r is >= 248 and <= 255, $"Expected bump alpha gray center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g is >= 248 and <= 255, $"Expected bump alpha gray center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b is >= 248 and <= 255, $"Expected bump alpha gray center pixel, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UsesTevColorRegisterConstants()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        try
        {
            List<byte> bytes =
            [
                0x61, 0xE2, 0x0F, 0xF0, 0xFF,
                0x61, 0xE3, 0x00, 0x00, 0x00,
                0x61, 0xC0, 0x08, 0xFF, 0xF2,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 0, 0, 255, 255);
            AddVertex(bytes, 4, 2, 0, 0, 255, 255);
            AddVertex(bytes, 4, 4, 0, 0, 255, 255);
            AddVertex(bytes, 2, 4, 0, 0, 255, 255);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r > 240, $"Expected TEV register red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g < 16, $"Expected TEV register red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b < 16, $"Expected TEV register red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CopiesEfbToRgb565TextureMemoryForLaterSampling()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x18, 0x06,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x02,
                0x61, 0x52, 0x01, 0x00, 0x40,
                0x61, 0x28, 0x00, 0x00, 0x40,
                0x61, 0x80, 0x00, 0x00, 0x00,
                0x61, 0x88, 0x40, 0x18, 0x06,
                0x61, 0x94, 0x00, 0x10, 0x00,
                0x61, 0xC0, 0x08, 0xFF, 0xF8,
                0x61, 0xC1, 0x08, 0xFF, 0xC0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ]);
            AddTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, 0.5f, 0.5f);
            AddTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, 0.5f, 0.5f);
            AddTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, 0.5f, 0.5f);
            AddTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, 0.5f, 0.5f);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(2, result.RenderedQuads);
            Assert.Equal(0xF800, memory.Read16(0x0002_0000 + 30));
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r > 240, $"Expected copied EFB texture red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g < 16, $"Expected copied EFB texture red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b < 16, $"Expected copied EFB texture red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CopiesEfbTextureUsingDestinationWidthStride()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 0, 0, 255, 0, 0, 255);
            AddVertex(bytes, 3, 0, 255, 0, 0, 255);
            AddVertex(bytes, 3, 7, 255, 0, 0, 255);
            AddVertex(bytes, 0, 7, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x1C, 0x03,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x02,
                0x61, 0x52, 0x01, 0x00, 0x40,
            ]);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 8, height: 8, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            Assert.Equal(0xF800, memory.Read16(0x0002_0000 + 78));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EfbCopyClearClearsDepthBuffer()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0x40, 0x00, 0x00, 0x13,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 10, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 10, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 10, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 10, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x18, 0x06,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x02,
                0x61, 0x51, 0xFF, 0xFF, 0xFF,
                0x61, 0x52, 0x01, 0x08, 0x40,
                0x80, 0x00, 0x04,
            ]);
            AddVertex(bytes, 2, 2, 100, 0, 255, 0, 255);
            AddVertex(bytes, 4, 2, 100, 0, 255, 0, 255);
            AddVertex(bytes, 4, 4, 100, 0, 255, 0, 255);
            AddVertex(bytes, 2, 4, 100, 0, 255, 0, 255);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r < 16, $"Expected post-clear depth test to allow green center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g > 240, $"Expected post-clear depth test to allow green center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b < 16, $"Expected post-clear depth test to allow green center pixel, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DecodesEfbCopyStateInDrawDiagnostics()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        try
        {
            List<byte> bytes =
            [
                0x61, 0x49, 0x00, 0x04, 0x02,
                0x61, 0x4A, 0x00, 0x0C, 0x03,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x01,
                0x61, 0x4F, 0x00, 0xFF, 0x20,
                0x61, 0x50, 0x00, 0xA0, 0xE0,
                0x61, 0x51, 0x00, 0x12, 0x34,
                0x61, 0x52, 0x01, 0x08, 0x40,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 1, 1, 255, 0, 0, 255);
            AddVertex(bytes, 3, 1, 255, 0, 0, 255);
            AddVertex(bytes, 3, 3, 255, 0, 0, 255);
            AddVertex(bytes, 1, 3, 255, 0, 0, 255);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteDrawDiagnostics(accesses, memory: null, path, maxDraws: 1, out GxFifoDrawDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            string text = File.ReadAllText(path);
            Assert.Contains("BP EFB copy texture: src=(2,1) 4x4, dst=0x00020000, dst-raw=0x000001, tiles=1, fmt=RGB565, clear=True, mipmap=False, control=0x010840", text);
            Assert.Contains("BP EFB copy clear: color=(32,160,224,255), z=0x001234", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WritesEfbCopyCoverageDiagnostics()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x18, 0x06,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x00,
                0x61, 0x52, 0x00, 0x48, 0x00,
            ]);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteCopyDiagnostics(accesses, memory, path, width: 7, height: 7, maxRasterizedPixels: null, out GxFifoCopyDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.CopiesWritten);
            string[] lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            string[] headers = lines[0].Split(',');
            string[] values = lines[1].Split(',');
            Assert.Equal("display", values[Array.IndexOf(headers, "kind")]);
            Assert.Equal("0x00020000", values[Array.IndexOf(headers, "destination_address")]);
            Assert.Equal("True", values[Array.IndexOf(headers, "clear")]);
            Assert.Equal("True", values[Array.IndexOf(headers, "copied")]);
            Assert.True(int.Parse(values[Array.IndexOf(headers, "display_nonblack")]) > 0);
            Assert.Equal("2/2-3/3", values[Array.IndexOf(headers, "display_nonblack_bounds")]);
            Assert.Contains("center@3/3:", values[Array.IndexOf(headers, "display_samples")]);
            Assert.Equal("49", values[Array.IndexOf(headers, "before_pixels")]);
            Assert.True(int.Parse(values[Array.IndexOf(headers, "before_nonblack")]) > 0);
            Assert.Equal("2/2-3/3", values[Array.IndexOf(headers, "before_nonblack_bounds")]);
            Assert.Equal("2/2-3/3", values[Array.IndexOf(headers, "before_alpha_bounds")]);
            Assert.Equal("0", values[Array.IndexOf(headers, "after_nonblack")]);
            Assert.Equal("", values[Array.IndexOf(headers, "after_nonblack_bounds")]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WritesCopyEventTimelineWithoutRasterizing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        try
        {
            List<byte> bytes =
            [
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x18, 0x06,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x00,
                0x61, 0x52, 0x00, 0x48, 0x00,
            ]);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteCopyEventTimeline(accesses, path, skipDraws: 0, maxDraws: null, out GxFifoCopyEventTimelineResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.EventsWritten);
            Assert.Equal(1, result.TotalDraws);
            string[] lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            string[] headers = lines[0].Split(',');
            string[] values = lines[1].Split(',');
            Assert.Equal("display", values[Array.IndexOf(headers, "kind")]);
            Assert.Equal("1", values[Array.IndexOf(headers, "draws_seen")]);
            Assert.Equal("0x00020000", values[Array.IndexOf(headers, "destination_address")]);
            Assert.Equal("7", values[Array.IndexOf(headers, "src_width")]);
            Assert.Equal("7", values[Array.IndexOf(headers, "src_height")]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WritesTextureCopyReadbackSamples()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x18, 0x06,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x01,
                0x61, 0x52, 0x01, 0x00, 0x40,
            ]);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteCopyDiagnostics(accesses, memory, path, width: 7, height: 7, maxRasterizedPixels: null, out GxFifoCopyDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            string[] lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            string[] headers = lines[0].Split(',');
            string[] values = lines[1].Split(',');
            Assert.Equal("texture", values[Array.IndexOf(headers, "kind")]);
            Assert.Equal("0", values[Array.IndexOf(headers, "texture_readback_mismatches")]);
            string text = File.ReadAllText(path);
            Assert.Contains("center@3/3:src=255/0/0/255,tex=255/0/0/255", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WritesDrawCoverageDiagnostics()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x18, 0x06,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x00,
                0x61, 0x52, 0x00, 0x48, 0x00,
            ]);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteDrawCoverageDiagnostics(accesses, memory, path, width: 7, height: 7, maxRasterizedPixels: null, ignoreEfbCopyClear: false, out GxFifoCoverageDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.DrawsWritten);
            Assert.Equal(1, result.CopiesSeen);
            string[] lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            string[] headers = lines[0].Split(',');
            string[] values = lines[1].Split(',');
            Assert.Equal("1", values[Array.IndexOf(headers, "draw_index")]);
            Assert.Equal("0", values[Array.IndexOf(headers, "before_nonblack")]);
            Assert.True(int.Parse(values[Array.IndexOf(headers, "after_nonblack")]) > 0);
            Assert.True(int.Parse(values[Array.IndexOf(headers, "raster_spent")]) > 0);
            Assert.Equal("1", values[Array.IndexOf(headers, "rendered_quads_delta")]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WritesTevSampleDiagnostics()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 64);
            AddVertex(bytes, 4, 2, 255, 0, 0, 64);
            AddVertex(bytes, 4, 4, 255, 0, 0, 64);
            AddVertex(bytes, 2, 4, 255, 0, 0, 64);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteTevSampleDiagnostics(accesses, memory: null, path, skipDraws: 0, maxDraws: 1, out GxFifoTevSampleDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            Assert.Equal(8, result.SamplesWritten);
            string text = File.ReadAllText(path);
            Assert.Contains("triangle_index,sample_name,sample_a_weight,sample_b_weight,sample_c_weight", text);
            Assert.Contains("raster_rgba,tev_evaluated,tev_rgba", text);
            Assert.Contains("255/0/0/64,True,255/0/0/64", text);
            Assert.Contains("centroid", text);
            Assert.Contains("near_a", text);
            Assert.Contains("PassColor", text);
            Assert.Contains("stage0{", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TevSampleDiagnosticsClassifyAlternatePassColorStage()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFA, 0xCF,
                0x61, 0xC1, 0x08, 0xF7, 0x70,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 32, 16, 110);
            AddVertex(bytes, 4, 2, 255, 32, 16, 110);
            AddVertex(bytes, 4, 4, 255, 32, 16, 110);
            AddVertex(bytes, 2, 4, 255, 32, 16, 110);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteTevSampleDiagnostics(accesses, memory: null, path, skipDraws: 0, maxDraws: 1, out GxFifoTevSampleDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            Assert.Equal(8, result.SamplesWritten);
            string text = File.ReadAllText(path);
            Assert.Contains("PassColor", text);
            Assert.Contains("255/32/16/110,True,255/32/16/110", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CopiesEfbDisplayToYuyvFramebufferMemory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x18, 0x06,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x00,
                0x61, 0x52, 0x00, 0x40, 0x00,
            ]);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            byte[] rgb = FramebufferDumper.CaptureRgb(memory, 0x0002_0000, width: 7, height: 7, FramebufferPixelFormat.Yuyv);
            int offset = (3 * 7 + 3) * 3;
            Assert.True(rgb[offset] > 200, $"Expected display-copy framebuffer red pixel, got ({rgb[offset]},{rgb[offset + 1]},{rgb[offset + 2]}).");
            Assert.True(rgb[offset + 1] < 80, $"Expected display-copy framebuffer red pixel, got ({rgb[offset]},{rgb[offset + 1]},{rgb[offset + 2]}).");
            Assert.True(rgb[offset + 2] < 80, $"Expected display-copy framebuffer red pixel, got ({rgb[offset]},{rgb[offset + 1]},{rgb[offset + 2]}).");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CanDumpLastEfbDisplayCopyAsGxFrameSource()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x18, 0x06,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x00,
                0x61, 0x52, 0x00, 0x48, 0x00,
            ]);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, maxDraws: null, skipDraws: 0, stopAfterMaxDraws: false, maxRasterizedPixels: null, ignoreEfbCopyClear: false, source: GxFrameDumpSource.LastDisplayCopy, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(GxFrameDumpSource.LastDisplayCopy, result.Source);
            Assert.Equal(0x0002_0000u, result.SourceAddress);
            Assert.Equal(FramebufferPixelFormat.Yuyv, result.SourceFormat);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            Assert.True(r > 200, $"Expected display-copy red pixel, got ({r},{g},{b}).");
            Assert.True(g < 80, $"Expected display-copy red pixel, got ({r},{g},{b}).");
            Assert.True(b < 80, $"Expected display-copy red pixel, got ({r},{g},{b}).");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CanDumpViSelectedFramebufferAsGxFrameSource()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x18, 0x06,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x00,
                0x61, 0x52, 0x00, 0x48, 0x00,
            ]);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();
            accesses.Add(new MmioAccess(MmioAccessKind.Write, 0xCC00_201C, 2, 0x1002, "VI"));
            accesses.Add(new MmioAccess(MmioAccessKind.Write, 0xCC00_201E, 2, 0x0000, "VI"));

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, maxDraws: null, skipDraws: 0, stopAfterMaxDraws: false, maxRasterizedPixels: null, ignoreEfbCopyClear: false, source: GxFrameDumpSource.ViFramebuffer, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(GxFrameDumpSource.ViFramebuffer, result.Source);
            Assert.Equal(0x0002_0000u, result.SourceAddress);
            Assert.Equal(FramebufferPixelFormat.Yuyv, result.SourceFormat);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            Assert.True(r > 200, $"Expected VI-selected red pixel, got ({r},{g},{b}).");
            Assert.True(g < 80, $"Expected VI-selected red pixel, got ({r},{g},{b}).");
            Assert.True(b < 80, $"Expected VI-selected red pixel, got ({r},{g},{b}).");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CanDumpDisplayCopyByGlobalCopyIndex()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x18, 0x06,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x00,
                0x61, 0x52, 0x00, 0x08, 0x00,
                0x61, 0x52, 0x00, 0x48, 0x00,
            ]);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, maxDraws: null, skipDraws: 0, stopAfterMaxDraws: false, maxRasterizedPixels: null, ignoreEfbCopyClear: true, source: GxFrameDumpSource.CopyIndex, displayCopyIndex: 2, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(GxFrameDumpSource.CopyIndex, result.Source);
            Assert.Equal(2, result.SourceCopyIndex);
            Assert.Equal(0x0002_0000u, result.SourceAddress);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            Assert.True(r > 200, $"Expected copy-index red pixel, got ({r},{g},{b}).");
            Assert.True(g < 80, $"Expected copy-index red pixel, got ({r},{g},{b}).");
            Assert.True(b < 80, $"Expected copy-index red pixel, got ({r},{g},{b}).");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CanDumpEfbCopySourceByGlobalCopyIndex()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x04, 0x02,
                0x61, 0x4A, 0x00, 0x0C, 0x03,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x00,
                0x61, 0x52, 0x00, 0x48, 0x00,
            ]);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, maxDraws: null, skipDraws: 0, stopAfterMaxDraws: false, maxRasterizedPixels: null, ignoreEfbCopyClear: false, source: GxFrameDumpSource.CopySourceIndex, displayCopyIndex: 1, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(GxFrameDumpSource.CopySourceIndex, result.Source);
            Assert.Equal(1, result.SourceCopyIndex);
            Assert.Equal(4, result.Width);
            Assert.Equal(4, result.Height);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 1, y: 1, width: 4, height: 4);
            Assert.True(r > 200, $"Expected EFB copy-source red pixel, got ({r},{g},{b}).");
            Assert.True(g < 80, $"Expected EFB copy-source red pixel, got ({r},{g},{b}).");
            Assert.True(b < 80, $"Expected EFB copy-source red pixel, got ({r},{g},{b}).");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CanDumpLastNonBlackDisplayCopyAsGxFrameSource()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x18, 0x06,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x00,
                0x61, 0x52, 0x00, 0x48, 0x03,
                0x61, 0x52, 0x00, 0x48, 0x03,
            ]);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, maxDraws: null, skipDraws: 0, stopAfterMaxDraws: false, maxRasterizedPixels: null, ignoreEfbCopyClear: false, source: GxFrameDumpSource.LastNonBlackDisplayCopy, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(GxFrameDumpSource.LastNonBlackDisplayCopy, result.Source);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            Assert.True(r > 200, $"Expected last nonblack display-copy red pixel, got ({r},{g},{b}).");
            Assert.True(g < 80, $"Expected last nonblack display-copy red pixel, got ({r},{g},{b}).");
            Assert.True(b < 80, $"Expected last nonblack display-copy red pixel, got ({r},{g},{b}).");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AutoGxFrameSourceFallsBackToLargestDisplayCopy()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x18, 0x06,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x00,
                0x61, 0x52, 0x00, 0x48, 0x03,
                0x61, 0x52, 0x00, 0x48, 0x03,
            ]);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, maxDraws: null, skipDraws: 0, stopAfterMaxDraws: false, maxRasterizedPixels: null, ignoreEfbCopyClear: false, source: GxFrameDumpSource.Auto, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(GxFrameDumpSource.LargestDisplayCopy, result.Source);
            Assert.Equal(1, result.SourceCopyIndex);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            Assert.True(r > 200, $"Expected auto-selected display-copy red pixel, got ({r},{g},{b}).");
            Assert.True(g < 80, $"Expected auto-selected display-copy red pixel, got ({r},{g},{b}).");
            Assert.True(b < 80, $"Expected auto-selected display-copy red pixel, got ({r},{g},{b}).");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AutoGxFrameSourcePrefersLargestDisplayCopyOverLaterSmallerCopy()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 0, 0, 255, 0, 0, 255);
            AddVertex(bytes, 7, 0, 255, 0, 0, 255);
            AddVertex(bytes, 7, 7, 255, 0, 0, 255);
            AddVertex(bytes, 0, 7, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x18, 0x06,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x00,
                0x61, 0x52, 0x00, 0x48, 0x03,
                0x80, 0x00, 0x04,
            ]);
            AddVertex(bytes, 3, 3, 0, 0, 255, 255);
            AddVertex(bytes, 4, 3, 0, 0, 255, 255);
            AddVertex(bytes, 4, 4, 0, 0, 255, 255);
            AddVertex(bytes, 3, 4, 0, 0, 255, 255);
            bytes.AddRange(
            [
                0x61, 0x52, 0x00, 0x48, 0x03,
            ]);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, maxDraws: null, skipDraws: 0, stopAfterMaxDraws: false, maxRasterizedPixels: null, ignoreEfbCopyClear: false, source: GxFrameDumpSource.Auto, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(GxFrameDumpSource.LargestDisplayCopy, result.Source);
            Assert.Equal(1, result.SourceCopyIndex);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CanDumpLastNonBlackEfbCopySourceAsGxFrameSource()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x04, 0x02,
                0x61, 0x4A, 0x00, 0x0C, 0x03,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x00,
                0x61, 0x52, 0x00, 0x48, 0x03,
                0x61, 0x52, 0x00, 0x48, 0x03,
            ]);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, maxDraws: null, skipDraws: 0, stopAfterMaxDraws: false, maxRasterizedPixels: null, ignoreEfbCopyClear: false, source: GxFrameDumpSource.LastNonBlackEfb, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(GxFrameDumpSource.LastNonBlackEfb, result.Source);
            Assert.Equal(1, result.SourceCopyIndex);
            Assert.Equal(4, result.Width);
            Assert.Equal(4, result.Height);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 1, y: 1, width: 4, height: 4);
            Assert.True(r > 200, $"Expected last nonblack EFB source red pixel, got ({r},{g},{b}).");
            Assert.True(g < 80, $"Expected last nonblack EFB source red pixel, got ({r},{g},{b}).");
            Assert.True(b < 80, $"Expected last nonblack EFB source red pixel, got ({r},{g},{b}).");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CopiesEfbDisplayToScaledYuyvFramebufferMemory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x18, 0x06,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x01,
                0x61, 0x4E, 0x00, 0x00, 0x80,
                0x61, 0x52, 0x00, 0x44, 0x00,
            ]);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            byte[] rgb = FramebufferDumper.CaptureRgb(memory, 0x0002_0000, width: 16, height: 13, FramebufferPixelFormat.Yuyv);
            int offset = (6 * 16 + 8) * 3;
            Assert.True(rgb[offset] > 200, $"Expected scaled display-copy framebuffer red pixel, got ({rgb[offset]},{rgb[offset + 1]},{rgb[offset + 2]}).");
            Assert.True(rgb[offset + 1] < 80, $"Expected scaled display-copy framebuffer red pixel, got ({rgb[offset]},{rgb[offset + 1]},{rgb[offset + 2]}).");
            Assert.True(rgb[offset + 2] < 80, $"Expected scaled display-copy framebuffer red pixel, got ({rgb[offset]},{rgb[offset + 1]},{rgb[offset + 2]}).");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DecodesDisplayCopyScaleStateInDrawDiagnostics()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        try
        {
            List<byte> bytes =
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x0C, 0x03,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x01,
                0x61, 0x4E, 0x00, 0x00, 0x80,
                0x61, 0x53, 0x00, 0x10, 0x00,
                0x61, 0x54, 0x00, 0x00, 0x04,
                0x61, 0x52, 0x00, 0x44, 0x80,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteDrawDiagnostics(accesses, memory: null, path, maxDraws: 1, out GxFifoDrawDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            string text = File.ReadAllText(path);
            Assert.Contains("BP EFB display copy scale: xfb=16x7, y-scale=128, gamma=1.7, clamp=(False,False), field=progressive, vfilter=0,0,1,0,4,0,0", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ClearsEfbAfterDisplayCopyWhenRequested()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x18, 0x06,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x00,
                0x61, 0x52, 0x00, 0x48, 0x00,
            ]);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            Assert.Equal(0, r);
            Assert.Equal(0, g);
            Assert.Equal(0, b);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ClearsEfbAfterDisplayCopyToConfiguredCopyClearColor()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        try
        {
            List<byte> bytes =
            [
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);
            bytes.AddRange(
            [
                0x61, 0x49, 0x00, 0x00, 0x00,
                0x61, 0x4A, 0x00, 0x18, 0x06,
                0x61, 0x4B, 0x00, 0x10, 0x00,
                0x61, 0x4D, 0x00, 0x00, 0x00,
                0x61, 0x4F, 0x00, 0xFF, 0x20,
                0x61, 0x50, 0x00, 0xA0, 0xE0,
                0x61, 0x52, 0x00, 0x48, 0x00,
            ]);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            Assert.Equal(32, r);
            Assert.Equal(160, g);
            Assert.Equal(224, b);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RendersSecondTevStage()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        memory.Write16(0x0002_0000, 0xF800);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x00, 0x00, 0x04, 0x00,
                0x61, 0x28, 0x04, 0x00, 0x00,
                0x61, 0x80, 0x00, 0x00, 0x00,
                0x61, 0x88, 0x40, 0x00, 0x00,
                0x61, 0x94, 0x00, 0x10, 0x00,
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x61, 0xC2, 0x08, 0xFF, 0xF8,
                0x61, 0xC3, 0x08, 0xFF, 0xC0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddTexturedVertex(bytes, 2, 2, 0, 0, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 2, 0, 0, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 4, 0, 0, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 2, 4, 0, 0, 255, 255, 0, 0);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r > 240, $"Expected second-stage red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g < 16, $"Expected second-stage red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b < 16, $"Expected second-stage red center pixel, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HonorsGenModeTevStageCount()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        memory.Write16(0x0002_0000, 0xF800);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x00, 0x00, 0x00, 0x00,
                0x61, 0x28, 0x04, 0x00, 0x00,
                0x61, 0x80, 0x00, 0x00, 0x00,
                0x61, 0x88, 0x40, 0x00, 0x00,
                0x61, 0x94, 0x00, 0x10, 0x00,
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x61, 0xC2, 0x08, 0xFF, 0xF8,
                0x61, 0xC3, 0x08, 0xFF, 0xC0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddTexturedVertex(bytes, 2, 2, 0, 0, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 2, 0, 0, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 4, 0, 0, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 2, 4, 0, 0, 255, 255, 0, 0);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r < 16, $"Expected stage-count-limited blue center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g < 16, $"Expected stage-count-limited blue center pixel, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b > 240, $"Expected stage-count-limited blue center pixel, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RendersKonstFractionColorInput()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        try
        {
            List<byte> bytes =
            [
                0x61, 0x00, 0x00, 0x00, 0x00,
                0x61, 0xF6, 0x00, 0x00, 0x40,
                0x61, 0xC0, 0x08, 0xFF, 0xFE,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 2, 255, 0, 0, 255);
            AddVertex(bytes, 4, 4, 255, 0, 0, 255);
            AddVertex(bytes, 2, 4, 255, 0, 0, 255);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.InRange(r, 124, 132);
            Assert.InRange(g, 124, 132);
            Assert.InRange(b, 124, 132);
            Assert.DoesNotContain("(255,000,000)", pixels);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RendersKonstRegisterColorInput()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        try
        {
            List<byte> bytes =
            [
                0x61, 0x00, 0x00, 0x00, 0x00,
                0x61, 0xE0, 0x8F, 0x00, 0x2A,
                0x61, 0xE1, 0x86, 0x00, 0xC0,
                0x61, 0xF6, 0x00, 0x00, 0xC0,
                0x61, 0xC0, 0x08, 0xFF, 0xFE,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 0, 0, 0, 255);
            AddVertex(bytes, 4, 2, 0, 0, 0, 255);
            AddVertex(bytes, 4, 4, 0, 0, 0, 255);
            AddVertex(bytes, 2, 4, 0, 0, 0, 255);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r is >= 38 and <= 46, $"Expected K0 red component, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g is >= 92 and <= 100, $"Expected K0 green component, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b is >= 188 and <= 196, $"Expected K0 blue component, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppliesTevRasterSwapModeTable()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        try
        {
            List<byte> bytes =
            [
                0x61, 0x00, 0x00, 0x00, 0x00,
                0x61, 0xF8, 0x00, 0x00, 0x09,
                0x61, 0xF9, 0x00, 0x00, 0x0C,
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x08, 0xFF, 0xD1,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 32, 160, 240, 255);
            AddVertex(bytes, 4, 2, 32, 160, 240, 255);
            AddVertex(bytes, 4, 4, 32, 160, 240, 255);
            AddVertex(bytes, 2, 4, 32, 160, 240, 255);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r is >= 156 and <= 164, $"Expected custom raster swap R=G, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g is >= 236 and <= 244, $"Expected custom raster swap G=B, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b is >= 28 and <= 36, $"Expected custom raster swap B=R, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppliesTevTextureSwapMode()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        GameCubeMemory memory = new();
        memory.Write16(0x0002_0000, 0x001F);
        try
        {
            List<byte> bytes =
            [
                0x61, 0x00, 0x00, 0x00, 0x00,
                0x61, 0x28, 0x00, 0x00, 0x40,
                0x61, 0x80, 0x00, 0x00, 0x00,
                0x61, 0x88, 0x40, 0x00, 0x00,
                0x61, 0x94, 0x00, 0x10, 0x00,
                0x61, 0xC0, 0x08, 0xFF, 0xF8,
                0x61, 0xC1, 0x08, 0xFF, 0xCC,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x70, 0x41, 0x21, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddTexturedVertex(bytes, 2, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 2, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 4, 4, 255, 255, 255, 255, 0, 0);
            AddTexturedVertex(bytes, 2, 4, 255, 255, 255, 255, 0, 0);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, memory, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r > 240, $"Expected blue-replicated texture swap, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g > 240, $"Expected blue-replicated texture swap, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b > 240, $"Expected blue-replicated texture swap, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppliesTevRgb8CompareColorOp()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        try
        {
            List<byte> bytes =
            [
                0x61, 0x00, 0x00, 0x00, 0x00,
                0x61, 0xC0, 0x3B, 0xAD, 0xCF,
                0x61, 0xC1, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 200, 20, 200, 255);
            AddVertex(bytes, 4, 2, 200, 20, 200, 255);
            AddVertex(bytes, 4, 4, 200, 20, 200, 255);
            AddVertex(bytes, 2, 4, 200, 20, 200, 255);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r > 240, $"Expected RGB8 compare red pass, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g < 16, $"Expected RGB8 compare green fail, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b > 240, $"Expected RGB8 compare blue pass, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppliesTevAlphaCompareOp()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        try
        {
            List<byte> bytes =
            [
                0x61, 0x00, 0x00, 0x04, 0x00,
                0x61, 0xC0, 0x08, 0xFF, 0xFA,
                0x61, 0xC1, 0x3B, 0xBF, 0x70,
                0x61, 0xC2, 0x08, 0xFF, 0xF1,
                0x61, 0xC3, 0x08, 0xFF, 0xD0,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 2, 2, 0, 0, 0, 96);
            AddVertex(bytes, 4, 2, 0, 0, 0, 96);
            AddVertex(bytes, 4, 4, 0, 0, 0, 96);
            AddVertex(bytes, 2, 4, 0, 0, 0, 96);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool rendered = GxFifoSoftwareRenderer.TryRender(accesses, path, width: 7, height: 7, out GxFifoSoftwareRenderResult? result, out string? error);

            Assert.True(rendered, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.RenderedQuads);
            (byte r, byte g, byte b) = ReadPngPixel(path, x: 3, y: 3, width: 7, height: 7);
            string pixels = DescribePngPixels(path, width: 7, height: 7);
            Assert.True(r > 240, $"Expected alpha compare result replicated to red, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(g > 240, $"Expected alpha compare result replicated to green, got ({r},{g},{b}). Pixels: {pixels}");
            Assert.True(b > 240, $"Expected alpha compare result replicated to blue, got ({r},{g},{b}). Pixels: {pixels}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WritesTextureImageStateInDrawDiagnostics()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        try
        {
            uint image0 = (64u - 1) | ((32u - 1) << 10) | (4u << 20);
            List<byte> bytes =
            [
                0x61, 0x80, 0x00, 0x00, 0x09,
                0x61, 0x88, (byte)(image0 >> 16), (byte)(image0 >> 8), (byte)image0,
                0x61, 0x8C, 0x00, 0x12, 0x34,
                0x61, 0x90, 0x00, 0x56, 0x78,
                0x61, 0x94, 0x00, 0x10, 0x00,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x90, 0x00, 0x03,
            ];
            AddVertex(bytes, 1, 1, 255, 255, 255, 255);
            AddVertex(bytes, 3, 1, 255, 255, 255, 255);
            AddVertex(bytes, 2, 3, 255, 255, 255, 255);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteDrawDiagnostics(accesses, memory: null, path, maxDraws: 1, out GxFifoDrawDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            Assert.Equal(1, result.TotalDraws);
            string text = File.ReadAllText(path);
            Assert.Contains("Texture map 0: 64x32 RGB565, src=0x00020000, tmem-even=0x001234, tmem-odd=0x005678", text);
            Assert.Contains("wrap=(repeat, mirror)", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WritesFirstNonBlackDiagnosticPastRequestedPreview()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        try
        {
            List<byte> bytes =
            [
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x80, 0x00, 0x04,
            ];
            AddVertex(bytes, 0, 0, 0, 0, 0, 255);
            AddVertex(bytes, 1, 0, 0, 0, 0, 255);
            AddVertex(bytes, 1, 1, 0, 0, 0, 255);
            AddVertex(bytes, 0, 1, 0, 0, 0, 255);
            bytes.AddRange([0x80, 0x00, 0x04]);
            AddVertex(bytes, 1, 1, 255, 0, 0, 255);
            AddVertex(bytes, 3, 1, 255, 0, 0, 255);
            AddVertex(bytes, 3, 3, 255, 0, 0, 255);
            AddVertex(bytes, 1, 3, 255, 0, 0, 255);
            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteDrawDiagnostics(accesses, memory: null, path, maxDraws: 1, out GxFifoDrawDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            Assert.Equal(2, result.TotalDraws);
            Assert.Equal(2, result.DrawsWritten);
            string text = File.ReadAllText(path);
            Assert.Contains("Reason: first nonblack RGB draw", text);
            Assert.Contains("nonblack RGB draws: 1", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WritesDecodedScissorInDrawDiagnostics()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        try
        {
            List<byte> bytes =
            [
                0x61, 0x20, 0x15, 0x61, 0x56,
                0x61, 0x21, 0x3D, 0x53, 0x35,
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x90, 0x00, 0x03,
            ];
            AddVertex(bytes, 1, 1, 255, 0, 0, 255);
            AddVertex(bytes, 3, 1, 255, 0, 0, 255);
            AddVertex(bytes, 2, 3, 255, 0, 0, 255);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteDrawDiagnostics(accesses, memory: null, path, maxDraws: 1, out GxFifoDrawDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            string text = File.ReadAllText(path);
            Assert.Contains("BP scissor raw: top-left=0x156156, bottom-right=0x3D5335, decoded=(0,0)-(639,479)", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MarksUnprojectableVerticesAsClippedWhenProjectionStateIsComplete()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        try
        {
            List<byte> bytes =
            [
                0x10, 0x00, 0x0B, 0x00, 0x00,
            ];
            AddSingle(bytes, 1);
            AddSingle(bytes, 0);
            AddSingle(bytes, 0);
            AddSingle(bytes, 0);
            AddSingle(bytes, 0);
            AddSingle(bytes, 1);
            AddSingle(bytes, 0);
            AddSingle(bytes, 0);
            AddSingle(bytes, 0);
            AddSingle(bytes, 0);
            AddSingle(bytes, 1);
            AddSingle(bytes, 0);
            bytes.AddRange([0x10, 0x00, 0x05, 0x10, 0x1A]);
            AddSingle(bytes, 320);
            AddSingle(bytes, -240);
            AddSingle(bytes, 16_777_215);
            AddSingle(bytes, 662);
            AddSingle(bytes, 582);
            AddSingle(bytes, 16_777_215);
            bytes.AddRange([0x10, 0x00, 0x06, 0x10, 0x20]);
            AddSingle(bytes, 1);
            AddSingle(bytes, 0);
            AddSingle(bytes, 1);
            AddSingle(bytes, 0);
            AddSingle(bytes, 1);
            AddSingle(bytes, 0);
            AddUInt32(bytes, 0);
            bytes.AddRange(
            [
                0x08, 0x50, 0x00, 0x00, 0x22, 0x00,
                0x08, 0x60, 0x00, 0x00, 0x00, 0x00,
                0x08, 0x70, 0x40, 0x01, 0x60, 0x09,
                0x08, 0x80, 0x80, 0x00, 0x00, 0x00,
                0x08, 0x90, 0x00, 0x00, 0x00, 0x00,
                0x90, 0x00, 0x03,
            ]);
            AddVertex(bytes, 1, 1, 1, 255, 0, 0, 255);
            AddVertex(bytes, 3, 1, 1, 255, 0, 0, 255);
            AddVertex(bytes, 2, 3, 1, 255, 0, 0, 255);

            List<MmioAccess> accesses = bytes
                .Select(value => new MmioAccess(MmioAccessKind.Write, 0xCC00_8000, 1, value, "GX FIFO"))
                .ToList();

            bool wrote = GxFifoSoftwareRenderer.TryWriteDrawDiagnostics(accesses, memory: null, path, maxDraws: 1, out GxFifoDrawDiagnosticResult? result, out string? error);

            Assert.True(wrote, error);
            Assert.NotNull(result);
            string text = File.ReadAllText(path);
            Assert.Contains("clipped raw=", text);
            Assert.Contains("clipped=3", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AddVertex(List<byte> bytes, float x, float y, byte r, byte g, byte b, byte a)
    {
        AddVertex(bytes, x, y, 0, r, g, b, a);
    }

    private static void AddVertex(List<byte> bytes, float x, float y, float z, byte r, byte g, byte b, byte a)
    {
        AddSingle(bytes, x);
        AddSingle(bytes, y);
        AddSingle(bytes, z);
        bytes.AddRange([r, g, b, a]);
    }

    private static void AddTexturedVertex(List<byte> bytes, float x, float y, byte r, byte g, byte b, byte a, float s, float t)
    {
        AddVertex(bytes, x, y, r, g, b, a);
        AddSingle(bytes, s);
        AddSingle(bytes, t);
    }

    private static void AddDualTexturedVertex(List<byte> bytes, float x, float y, byte r, byte g, byte b, byte a, float tex0S, float tex0T, float tex1S, float tex1T)
    {
        AddTexturedVertex(bytes, x, y, r, g, b, a, tex0S, tex0T);
        AddSingle(bytes, tex1S);
        AddSingle(bytes, tex1T);
    }

    private static void AddSingle(List<byte> bytes, float value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, BitConverter.SingleToInt32Bits(value));
        bytes.AddRange(buffer.ToArray());
    }

    private static void AddUInt32(List<byte> bytes, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        bytes.AddRange(buffer.ToArray());
    }

    private static (byte R, byte G, byte B) ReadPngPixel(string path, int x, int y, int width, int height)
    {
        byte[] bytes = ReadPngRgb(path, width, height);
        int pixelOffset = y * width * 3 + x * 3;
        return (bytes[pixelOffset], bytes[pixelOffset + 1], bytes[pixelOffset + 2]);
    }

    private static string DescribePngPixels(string path, int width, int height)
    {
        byte[] bytes = ReadPngRgb(path, width, height);
        return string.Join(" ", Enumerable.Range(0, width * height).Select(index =>
        {
            int offset = index * 3;
            return $"{index}:{bytes[offset]:X2}{bytes[offset + 1]:X2}{bytes[offset + 2]:X2}";
        }));
    }

    private static byte[] ReadPngRgb(string path, int width, int height)
    {
        byte[] png = File.ReadAllBytes(path);
        using MemoryStream compressed = new();
        int offset = 8;
        while (offset < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, sizeof(int)));
            ReadOnlySpan<byte> type = png.AsSpan(offset + 4, 4);
            ReadOnlySpan<byte> data = png.AsSpan(offset + 8, length);
            if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(data);
            }

            offset += 12 + length;
        }

        compressed.Position = 0;
        using ZLibStream zlib = new(compressed, CompressionMode.Decompress);
        using MemoryStream raw = new();
        zlib.CopyTo(raw);
        byte[] bytes = raw.ToArray();
        int stride = width * 3 + 1;
        Assert.Equal(height * stride, bytes.Length);
        byte[] rgb = new byte[width * height * 3];
        for (int row = 0; row < height; row++)
        {
            Assert.Equal(0, bytes[row * stride]);
            Array.Copy(bytes, row * stride + 1, rgb, row * width * 3, width * 3);
        }

        return rgb;
    }

    private static void WritePosition(GameCubeMemory memory, uint address, float x, float y, float z)
    {
        Span<byte> buffer = stackalloc byte[12];
        BinaryPrimitives.WriteInt32BigEndian(buffer[..4], BitConverter.SingleToInt32Bits(x));
        BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(4, 4), BitConverter.SingleToInt32Bits(y));
        BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(8, 4), BitConverter.SingleToInt32Bits(z));
        memory.Load(address, buffer);
    }
}
