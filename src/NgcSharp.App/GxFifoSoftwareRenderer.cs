using System.Diagnostics;
using System.Globalization;
using System.Text;
using NgcSharp.Core;

namespace NgcSharp.App;

public sealed record GxFifoSoftwareRenderTimings(double TotalMs, double FifoExpansionMs, double BufferInitMs, double ViResolveMs, double ReplayMs, double RegisterWriteMs, double VertexDecodeMs, double RasterizeMs, double RasterSetupMs, double RasterCoverageMs, double RasterDepthTestMs, double RasterTevTextureMs, double RasterAlphaTestMs, double RasterBlendWriteMs, double EfbCoverageMs, double EfbCopyMs, double SourceCaptureMs, double DisplayCaptureMs, double SourceSelectionMs, double PngWriteMs);
public sealed record GxFifoSoftwareRenderResult(string Path, int Width, int Height, int Draws, int RenderedQuads, int DegenerateQuads, int RenderedTriangles = 0, int DegenerateTriangles = 0, GxFrameDumpSource Source = GxFrameDumpSource.Efb, uint? SourceAddress = null, FramebufferPixelFormat? SourceFormat = null, int? SourceCopyIndex = null, bool RasterBudgetExhausted = false, GxFifoSoftwareRenderTimings? Timings = null);
public sealed record GxFifoDrawDiagnosticResult(string Path, int TotalDraws, int DrawsWritten);
public sealed record GxFifoCopyDiagnosticResult(string Path, int CopiesWritten, int TotalDraws, bool RasterBudgetExhausted = false);
public sealed record GxFifoCoverageDiagnosticResult(string Path, int DrawsWritten, int CopiesSeen, bool RasterBudgetExhausted);
public sealed record GxFifoTevSampleDiagnosticResult(string Path, int SamplesWritten, int TotalDraws, int CopiesSeen);
public sealed record GxFifoTextureDiagnosticResult(string DirectoryPath, string IndexPath, int TexturesWritten, int TotalDraws, int CopiesSeen);

public static class GxFifoSoftwareRenderer
{
    private const float FarDepthValue = 16_777_215f;
    private const float NearClipW = 0.0001f;
    private const int MaxDiagnosticPixels = 4_194_304;
    private static readonly (uint High, uint Low)[] ViFramebufferRegisterPairs =
    [
        (0xCC00_201C, 0xCC00_201E),
        (0xCC00_2024, 0xCC00_2026),
        (0xCC00_2020, 0xCC00_2022),
        (0xCC00_2028, 0xCC00_202A),
    ];

    private static readonly uint[] ViFramebufferRegisters =
    [
        0xCC00_201C,
        0xCC00_2024,
        0xCC00_2020,
        0xCC00_2028,
    ];

    public readonly record struct DisplayCopyResult(uint DestinationAddress, int Width, int Height, FramebufferPixelFormat Format);
    private readonly record struct EfbCopyInfo(uint Control, bool IsDisplayCopy, bool Clear, bool Mipmap, int Format, int SourceLeft, int SourceTop, int SourceWidth, int SourceHeight, uint DestinationAddress, uint DestinationRaw, int DestinationTiles, int OutputWidth, int OutputHeight);
    private readonly record struct EfbCoverage(int Pixels, int NonBlackPixels, int AlphaVisibleNonBlackPixels, int MinX, int MinY, int MaxX, int MaxY, int AlphaMinX, int AlphaMinY, int AlphaMaxX, int AlphaMaxY)
    {
        public bool HasNonBlackBounds => MinX >= 0;

        public bool HasAlphaVisibleBounds => AlphaMinX >= 0;
    }
    private readonly record struct IndexedDisplayCopy(int CopyIndex, DisplayCopyResult Copy);
    private sealed record SelectedDisplayCopy(int CopyIndex, DisplayCopyResult Copy, EfbCoverage Coverage, byte[] Rgb);
    private readonly record struct TextureDumpSnapshot(
        int Slot,
        int Width,
        int Height,
        string FormatName,
        uint SourceAddress,
        uint TmemEven,
        uint TmemOdd,
        string WrapS,
        string WrapT,
        string MagFilter,
        string MinFilter,
        string Tlut,
        uint Mode0,
        uint Mode1,
        uint Image0,
        uint Image1,
        uint Image2,
        uint Image3,
        string SampleSource,
        int SourceByteLength,
        uint SourceHash,
        int NonBlackPixels,
        int NonTransparentPixels,
        int TransparentPixels,
        int MinAlpha,
        int MaxAlpha,
        string Samples);

    public static bool TryRender(IReadOnlyList<MmioAccess> accesses, string path, int width, int height, out GxFifoSoftwareRenderResult? result, out string? error)
    {
        return TryRender(accesses, memory: null, path, width, height, maxDraws: null, out result, out error);
    }

    public static bool TryRender(IReadOnlyList<MmioAccess> accesses, GameCubeMemory? memory, string path, int width, int height, out GxFifoSoftwareRenderResult? result, out string? error)
    {
        return TryRender(accesses, memory, path, width, height, maxDraws: null, out result, out error);
    }

    public static bool TryRender(IReadOnlyList<MmioAccess> accesses, GameCubeMemory? memory, string path, int width, int height, int? maxDraws, out GxFifoSoftwareRenderResult? result, out string? error)
    {
        return TryRender(accesses, memory, path, width, height, maxDraws, skipDraws: 0, stopAfterMaxDraws: false, maxRasterizedPixels: null, out result, out error);
    }

    public static bool TryRender(IReadOnlyList<MmioAccess> accesses, GameCubeMemory? memory, string path, int width, int height, int? maxDraws, int skipDraws, bool stopAfterMaxDraws, int? maxRasterizedPixels, out GxFifoSoftwareRenderResult? result, out string? error)
    {
        return TryRender(accesses, memory, path, width, height, maxDraws, skipDraws, stopAfterMaxDraws, maxRasterizedPixels, ignoreEfbCopyClear: false, out result, out error);
    }

    public static bool TryRender(IReadOnlyList<MmioAccess> accesses, GameCubeMemory? memory, string path, int width, int height, int? maxDraws, int skipDraws, bool stopAfterMaxDraws, int? maxRasterizedPixels, bool ignoreEfbCopyClear, out GxFifoSoftwareRenderResult? result, out string? error)
    {
        return TryRender(accesses, memory, path, width, height, maxDraws, skipDraws, stopAfterMaxDraws, maxRasterizedPixels, ignoreEfbCopyClear, GxFrameDumpSource.Efb, out result, out error);
    }

    public static bool TryRender(IReadOnlyList<MmioAccess> accesses, GameCubeMemory? memory, string path, int width, int height, int? maxDraws, int skipDraws, bool stopAfterMaxDraws, int? maxRasterizedPixels, bool ignoreEfbCopyClear, GxFrameDumpSource source, out GxFifoSoftwareRenderResult? result, out string? error)
    {
        return TryRender(accesses, memory, path, width, height, maxDraws, skipDraws, stopAfterMaxDraws, maxRasterizedPixels, ignoreEfbCopyClear, source, displayCopyIndex: null, out result, out error);
    }

    public static bool TryRender(IReadOnlyList<MmioAccess> accesses, GameCubeMemory? memory, string path, int width, int height, int? maxDraws, int skipDraws, bool stopAfterMaxDraws, int? maxRasterizedPixels, bool ignoreEfbCopyClear, GxFrameDumpSource source, int? displayCopyIndex, out GxFifoSoftwareRenderResult? result, out string? error, GxMemorySnapshotSet? memorySnapshots = null)
    {
        long totalStartTimestamp = Stopwatch.GetTimestamp();
        long fifoExpansionTicks = 0;
        long bufferInitTicks = 0;
        long viResolveTicks = 0;
        long replayTicks = 0;
        long registerWriteTicks = 0;
        long vertexDecodeTicks = 0;
        long rasterizeTicks = 0;
        RasterTimingAccumulator rasterTimings = new();
        long efbCoverageTicks = 0;
        long efbCopyTicks = 0;
        long sourceCaptureTicks = 0;
        long displayCaptureTicks = 0;
        long sourceSelectionTicks = 0;
        long pngWriteTicks = 0;

        GxFifoSoftwareRenderTimings BuildTimings()
        {
            return new GxFifoSoftwareRenderTimings(
                TotalMs: RoundMilliseconds(StopwatchTicksToMilliseconds(Stopwatch.GetTimestamp() - totalStartTimestamp)),
                FifoExpansionMs: RoundMilliseconds(StopwatchTicksToMilliseconds(fifoExpansionTicks)),
                BufferInitMs: RoundMilliseconds(StopwatchTicksToMilliseconds(bufferInitTicks)),
                ViResolveMs: RoundMilliseconds(StopwatchTicksToMilliseconds(viResolveTicks)),
                ReplayMs: RoundMilliseconds(StopwatchTicksToMilliseconds(replayTicks)),
                RegisterWriteMs: RoundMilliseconds(StopwatchTicksToMilliseconds(registerWriteTicks)),
                VertexDecodeMs: RoundMilliseconds(StopwatchTicksToMilliseconds(vertexDecodeTicks)),
                RasterizeMs: RoundMilliseconds(StopwatchTicksToMilliseconds(rasterizeTicks)),
                RasterSetupMs: RoundMilliseconds(StopwatchTicksToMilliseconds(rasterTimings.SetupTicks)),
                RasterCoverageMs: RoundMilliseconds(StopwatchTicksToMilliseconds(rasterTimings.CoverageTicks)),
                RasterDepthTestMs: RoundMilliseconds(StopwatchTicksToMilliseconds(rasterTimings.DepthTestTicks)),
                RasterTevTextureMs: RoundMilliseconds(StopwatchTicksToMilliseconds(rasterTimings.TevTextureTicks)),
                RasterAlphaTestMs: RoundMilliseconds(StopwatchTicksToMilliseconds(rasterTimings.AlphaTestTicks)),
                RasterBlendWriteMs: RoundMilliseconds(StopwatchTicksToMilliseconds(rasterTimings.BlendWriteTicks)),
                EfbCoverageMs: RoundMilliseconds(StopwatchTicksToMilliseconds(efbCoverageTicks)),
                EfbCopyMs: RoundMilliseconds(StopwatchTicksToMilliseconds(efbCopyTicks)),
                SourceCaptureMs: RoundMilliseconds(StopwatchTicksToMilliseconds(sourceCaptureTicks)),
                DisplayCaptureMs: RoundMilliseconds(StopwatchTicksToMilliseconds(displayCaptureTicks)),
                SourceSelectionMs: RoundMilliseconds(StopwatchTicksToMilliseconds(sourceSelectionTicks)),
                PngWriteMs: RoundMilliseconds(StopwatchTicksToMilliseconds(pngWriteTicks)));
        }

        ArgumentNullException.ThrowIfNull(accesses);

        result = null;
        error = null;
        if (width <= 0 || height <= 0)
        {
            error = "GX frame dimensions must be positive.";
            return false;
        }

        if (!ValidateDiagnosticDimensions(width, height, out error))
        {
            return false;
        }

        if (maxDraws is <= 0)
        {
            error = "GX frame draw limit must be positive.";
            return false;
        }

        if (skipDraws < 0)
        {
            error = "GX frame skipped draw count must be non-negative.";
            return false;
        }

        if (maxRasterizedPixels is <= 0)
        {
            error = "GX frame raster pixel budget must be positive.";
            return false;
        }

        long phaseStart = Stopwatch.GetTimestamp();
        byte[] fifo = accesses
            .Where(access => access.DeviceName == "GX FIFO" && access.Kind == MmioAccessKind.Write)
            .SelectMany(ExpandMmioWriteBytes)
            .ToArray();
        fifoExpansionTicks += Stopwatch.GetTimestamp() - phaseStart;
        if (fifo.Length == 0)
        {
            error = "no GX FIFO writes were captured.";
            return false;
        }

        phaseStart = Stopwatch.GetTimestamp();
        byte[] rgb = new byte[checked(width * height * 3)];
        byte[] alpha = Enumerable.Repeat((byte)255, checked(width * height)).ToArray();
        float[] depth = Enumerable.Repeat(FarDepthValue, checked(width * height)).ToArray();
        GxVertexState state = new()
        {
            MemorySnapshots = memorySnapshots is { IsEmpty: false } ? memorySnapshots : null,
        };
        bufferInitTicks += Stopwatch.GetTimestamp() - phaseStart;
        uint? requestedViAddress = null;
        if (source is GxFrameDumpSource.ViFramebuffer or GxFrameDumpSource.LastNonBlackViFramebuffer)
        {
            phaseStart = Stopwatch.GetTimestamp();
            if (!TryResolveViFramebufferAddress(accesses, out uint resolvedViAddress))
            {
                viResolveTicks += Stopwatch.GetTimestamp() - phaseStart;
                error = "no VI framebuffer register candidate resolved to main RAM.";
                return false;
            }

            viResolveTicks += Stopwatch.GetTimestamp() - phaseStart;
            requestedViAddress = resolvedViAddress;
        }

        int offset = 0;
        int draws = 0;
        int renderedQuads = 0;
        int degenerateQuads = 0;
        int renderedTriangles = 0;
        int degenerateTriangles = 0;
        int rasterPixelsRemaining = maxRasterizedPixels ?? int.MaxValue;
        int copiesSeen = 0;
        List<IndexedDisplayCopy> displayCopies = [];
        SelectedDisplayCopy? lastDisplayCopy = null;
        SelectedDisplayCopy? lastNonBlackDisplayCopy = null;
        SelectedDisplayCopy? largestDisplayCopy = null;
        SelectedDisplayCopy? lastNonBlackEfb = null;
        SelectedDisplayCopy? indexedDisplayCopy = null;
        SelectedDisplayCopy? lastNonBlackViDisplayCopy = null;
        bool stopReplayAfterCommand = false;

        long replayStart = Stopwatch.GetTimestamp();
        while (offset < fifo.Length && !stopReplayAfterCommand)
        {
            int commandOffset = offset;
            byte command = fifo[offset++];
            if (command is 0x00 or 0x44 or 0x48)
            {
                continue;
            }

            if (command == 0x08 && TryReadBigEndian(fifo, offset, 1, out uint cpRegister) && TryReadBigEndian(fifo, offset + 1, 4, out uint cpValue))
            {
                long registerWriteStart = Stopwatch.GetTimestamp();
                state.WriteCpRegister((byte)cpRegister, cpValue);
                registerWriteTicks += Stopwatch.GetTimestamp() - registerWriteStart;
                offset += 5;
                continue;
            }

            if (command == 0x10 && TryReadBigEndian(fifo, offset, 4, out uint xfHeader))
            {
                ushort xfRegister = (ushort)(xfHeader & 0xFFFF);
                uint wordCount = ((xfHeader >> 16) & 0xFFFF) + 1;
                int xfPayloadBytes = AvailablePayloadBytes(fifo, offset + 4, wordCount);
                long registerWriteStart = Stopwatch.GetTimestamp();
                state.WriteXfRegisters(fifo, offset + 4, xfRegister, xfPayloadBytes / sizeof(uint));
                registerWriteTicks += Stopwatch.GetTimestamp() - registerWriteStart;
                offset += 4 + xfPayloadBytes;
                continue;
            }

            if (command is 0x20 or 0x28 or 0x30 or 0x38)
            {
                offset += Math.Min(4, fifo.Length - offset);
                continue;
            }

            if (command == 0x40)
            {
                offset += Math.Min(8, fifo.Length - offset);
                continue;
            }

            if (command == 0x61)
            {
                if (TryReadBigEndian(fifo, offset, 4, out uint bpValue))
                {
                    long registerWriteStart = Stopwatch.GetTimestamp();
                    state.WriteBpRegister(bpValue);
                    registerWriteTicks += Stopwatch.GetTimestamp() - registerWriteStart;
                    if ((bpValue >> 24) == 0x52)
                    {
                        if (!state.TryGetEfbCopyInfo(out EfbCopyInfo copyInfo))
                        {
                            offset += Math.Min(4, fifo.Length - offset);
                            continue;
                        }

                        copiesSeen++;
                        EfbCoverage? displayCoverage = null;
                        bool needsDisplayCoverage = copyInfo.IsDisplayCopy
                            && source is GxFrameDumpSource.Auto
                                or GxFrameDumpSource.LastNonBlackDisplayCopy
                                or GxFrameDumpSource.LargestDisplayCopy
                                or GxFrameDumpSource.LastNonBlackViFramebuffer;
                        if (needsDisplayCoverage)
                        {
                            long coverageStart = Stopwatch.GetTimestamp();
                            displayCoverage = CountNonBlackRegion(rgb, alpha, width, height, copyInfo.SourceLeft, copyInfo.SourceTop, copyInfo.SourceWidth, copyInfo.SourceHeight);
                            efbCoverageTicks += Stopwatch.GetTimestamp() - coverageStart;
                        }

                        EfbCoverage? sourceCoverage = null;
                        if (source is GxFrameDumpSource.CopySourceIndex or GxFrameDumpSource.LastNonBlackEfb)
                        {
                            if (displayCoverage is EfbCoverage existingCoverage)
                            {
                                sourceCoverage = existingCoverage;
                            }
                            else
                            {
                                long coverageStart = Stopwatch.GetTimestamp();
                                sourceCoverage = CountNonBlackRegion(rgb, alpha, width, height, copyInfo.SourceLeft, copyInfo.SourceTop, copyInfo.SourceWidth, copyInfo.SourceHeight);
                                efbCoverageTicks += Stopwatch.GetTimestamp() - coverageStart;
                            }
                        }

                        if (source == GxFrameDumpSource.LastNonBlackEfb && sourceCoverage is EfbCoverage efbCoverage && efbCoverage.NonBlackPixels > 0)
                        {
                            long captureStart = Stopwatch.GetTimestamp();
                            byte[] sourceRgb = CaptureEfbSourceRgb(rgb, width, height, copyInfo.SourceLeft, copyInfo.SourceTop, copyInfo.SourceWidth, copyInfo.SourceHeight);
                            sourceCaptureTicks += Stopwatch.GetTimestamp() - captureStart;
                            lastNonBlackEfb = new SelectedDisplayCopy(copiesSeen, new DisplayCopyResult(copyInfo.DestinationAddress, copyInfo.SourceWidth, copyInfo.SourceHeight, FramebufferPixelFormat.Xrgb8888), efbCoverage, sourceRgb);
                        }

                        if (source == GxFrameDumpSource.CopySourceIndex && copiesSeen == displayCopyIndex)
                        {
                            EfbCoverage coverage;
                            if (sourceCoverage is EfbCoverage existingCoverage)
                            {
                                coverage = existingCoverage;
                            }
                            else if (displayCoverage is EfbCoverage existingDisplayCoverage)
                            {
                                coverage = existingDisplayCoverage;
                            }
                            else
                            {
                                long coverageStart = Stopwatch.GetTimestamp();
                                coverage = CountNonBlackRegion(rgb, alpha, width, height, copyInfo.SourceLeft, copyInfo.SourceTop, copyInfo.SourceWidth, copyInfo.SourceHeight);
                                efbCoverageTicks += Stopwatch.GetTimestamp() - coverageStart;
                            }

                            long captureStart = Stopwatch.GetTimestamp();
                            byte[] sourceRgb = CaptureEfbSourceRgb(rgb, width, height, copyInfo.SourceLeft, copyInfo.SourceTop, copyInfo.SourceWidth, copyInfo.SourceHeight);
                            sourceCaptureTicks += Stopwatch.GetTimestamp() - captureStart;
                            indexedDisplayCopy = new SelectedDisplayCopy(copiesSeen, new DisplayCopyResult(copyInfo.DestinationAddress, copyInfo.SourceWidth, copyInfo.SourceHeight, FramebufferPixelFormat.Xrgb8888), coverage, sourceRgb);
                            stopReplayAfterCommand = true;
                        }

                        long efbCopyStart = Stopwatch.GetTimestamp();
                        if (state.TryCopyEfb(memory, rgb, alpha, depth, width, height, clearAfterCopy: !ignoreEfbCopyClear, out DisplayCopyResult? displayCopy))
                        {
                            efbCopyTicks += Stopwatch.GetTimestamp() - efbCopyStart;
                            if (displayCopy is DisplayCopyResult capturedCopy)
                            {
                                EfbCoverage coverage = displayCoverage ?? default;
                                displayCopies.Add(new IndexedDisplayCopy(copiesSeen, capturedCopy));
                                bool hasNonBlackCoverage = coverage.NonBlackPixels > 0;
                                bool shouldCaptureDisplayCopy = source switch
                                {
                                    GxFrameDumpSource.Auto => hasNonBlackCoverage,
                                    GxFrameDumpSource.LastNonBlackDisplayCopy => hasNonBlackCoverage,
                                    GxFrameDumpSource.LargestDisplayCopy => hasNonBlackCoverage && (largestDisplayCopy is not SelectedDisplayCopy largest || coverage.NonBlackPixels > largest.Coverage.NonBlackPixels),
                                    GxFrameDumpSource.LastNonBlackViFramebuffer => hasNonBlackCoverage && requestedViAddress == capturedCopy.DestinationAddress,
                                    GxFrameDumpSource.CopyIndex => copiesSeen == displayCopyIndex,
                                    GxFrameDumpSource.Efb => false,
                                    GxFrameDumpSource.CopySourceIndex or GxFrameDumpSource.ViFramebuffer => false,
                                    _ => true,
                                };
                                if (shouldCaptureDisplayCopy
                                    && TryTimedCaptureDisplayCopyRgb(memory, capturedCopy, ref displayCaptureTicks, out byte[]? displayRgb)
                                    && displayRgb is not null)
                                {
                                    SelectedDisplayCopy selected = new(copiesSeen, capturedCopy, coverage, displayRgb);
                                    if (source == GxFrameDumpSource.LastNonBlackViFramebuffer
                                        && requestedViAddress == capturedCopy.DestinationAddress
                                        && RgbHasNonBlackPixels(displayRgb))
                                    {
                                        lastNonBlackViDisplayCopy = selected;
                                    }
                                    else if (source == GxFrameDumpSource.CopyIndex)
                                    {
                                        indexedDisplayCopy = selected;
                                        stopReplayAfterCommand = true;
                                    }
                                    else
                                    {
                                        lastDisplayCopy = selected;
                                        if (coverage.NonBlackPixels > 0)
                                        {
                                            lastNonBlackDisplayCopy = selected;
                                        }

                                        if (largestDisplayCopy is not SelectedDisplayCopy largest || coverage.NonBlackPixels > largest.Coverage.NonBlackPixels)
                                        {
                                            largestDisplayCopy = selected;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            efbCopyTicks += Stopwatch.GetTimestamp() - efbCopyStart;
                        }
                    }
                }

                offset += Math.Min(4, fifo.Length - offset);
                continue;
            }

            if ((command & 0x80) == 0 || !TryReadBigEndian(fifo, offset, 2, out uint vertexCount))
            {
                break;
            }

            int format = command & 7;
            if (!state.TryGetVertexStride(format, out int stride))
            {
                break;
            }

            offset += 2;
            long payloadBytes = (long)vertexCount * stride;
            if (payloadBytes > fifo.Length - offset)
            {
                break;
            }

            draws++;
            state.CurrentFifoOffset = commandOffset;
            long renderEndDraw = maxDraws is int maxDrawLimit ? (long)skipDraws + maxDrawLimit : long.MaxValue;
            bool renderDraw = draws > skipDraws && draws <= renderEndDraw;
            if (!renderDraw)
            {
                offset += (int)payloadBytes;
                if (stopAfterMaxDraws && maxDraws is not null && draws >= renderEndDraw)
                {
                    break;
                }

                continue;
            }

            if (state.IsRenderablePositionFormat(format))
            {
                if (vertexCount > int.MaxValue)
                {
                    offset += (int)payloadBytes;
                    continue;
                }

                int vertexOffset = offset;
                int vertexTotal = (int)vertexCount;
                GxVertex[] vertices = new GxVertex[vertexTotal];
                bool decodedVertices = true;
                long vertexDecodeStart = Stopwatch.GetTimestamp();
                for (int vertex = 0; vertex < vertexTotal; vertex++)
                {
                    if (!state.TryReadVertex(fifo, ref vertexOffset, format, memory, out vertices[vertex]))
                    {
                        decodedVertices = false;
                        break;
                    }
                }
                vertexDecodeTicks += Stopwatch.GetTimestamp() - vertexDecodeStart;

                if (decodedVertices)
                {
                    long rasterizeStart = Stopwatch.GetTimestamp();
                    RenderPrimitiveBounds(rgb, alpha, depth, width, height, command, vertices, state, memory, ref renderedQuads, ref degenerateQuads, ref renderedTriangles, ref degenerateTriangles, ref rasterPixelsRemaining, counters: null, rasterTimings);
                    rasterizeTicks += Stopwatch.GetTimestamp() - rasterizeStart;
                }
            }

            offset += (int)payloadBytes;
        }
        replayTicks += Stopwatch.GetTimestamp() - replayStart;

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        long sourceSelectionStart = Stopwatch.GetTimestamp();
        int outputWidth = width;
        int outputHeight = height;
        uint? sourceAddress = null;
        FramebufferPixelFormat? sourceFormat = null;
        int? sourceCopyIndex = null;
        GxFrameDumpSource resultSource = source;
        if (source == GxFrameDumpSource.Auto)
        {
            if (RgbHasNonBlackPixels(rgb))
            {
                resultSource = GxFrameDumpSource.Efb;
            }
            else if (lastNonBlackDisplayCopy is SelectedDisplayCopy selectedNonBlackDisplay)
            {
                DisplayCopyResult copy = selectedNonBlackDisplay.Copy;
                rgb = selectedNonBlackDisplay.Rgb;
                outputWidth = copy.Width;
                outputHeight = copy.Height;
                sourceAddress = copy.DestinationAddress;
                sourceFormat = copy.Format;
                sourceCopyIndex = selectedNonBlackDisplay.CopyIndex;
                resultSource = GxFrameDumpSource.LastNonBlackDisplayCopy;
            }
            else if (largestDisplayCopy is SelectedDisplayCopy selectedLargestDisplay)
            {
                DisplayCopyResult copy = selectedLargestDisplay.Copy;
                rgb = selectedLargestDisplay.Rgb;
                outputWidth = copy.Width;
                outputHeight = copy.Height;
                sourceAddress = copy.DestinationAddress;
                sourceFormat = copy.Format;
                sourceCopyIndex = selectedLargestDisplay.CopyIndex;
                resultSource = GxFrameDumpSource.LargestDisplayCopy;
            }
            else if (lastDisplayCopy is SelectedDisplayCopy selectedLastDisplay)
            {
                DisplayCopyResult copy = selectedLastDisplay.Copy;
                rgb = selectedLastDisplay.Rgb;
                outputWidth = copy.Width;
                outputHeight = copy.Height;
                sourceAddress = copy.DestinationAddress;
                sourceFormat = copy.Format;
                sourceCopyIndex = selectedLastDisplay.CopyIndex;
                resultSource = GxFrameDumpSource.LastDisplayCopy;
            }
            else
            {
                resultSource = GxFrameDumpSource.Efb;
            }
        }
        else if (source != GxFrameDumpSource.Efb)
        {
            if (memory is null)
            {
                if (source != GxFrameDumpSource.LastNonBlackEfb)
                {
                    error = $"GX {FormatFrameSource(source)} frame source requires emulated memory.";
                    return false;
                }
            }

            if (source is GxFrameDumpSource.ViFramebuffer or GxFrameDumpSource.LastNonBlackViFramebuffer)
            {
                uint viAddress = requestedViAddress.GetValueOrDefault();

                DisplayCopyResult? selectedViCopy = null;
                if (source == GxFrameDumpSource.LastNonBlackViFramebuffer)
                {
                    if (lastNonBlackViDisplayCopy is not SelectedDisplayCopy selectedNonBlackVi)
                    {
                        error = $"no nonblack EFB display copy was captured for VI framebuffer 0x{viAddress:X8}.";
                        return false;
                    }

                    rgb = selectedNonBlackVi.Rgb;
                    outputWidth = selectedNonBlackVi.Copy.Width;
                    outputHeight = selectedNonBlackVi.Copy.Height;
                    sourceAddress = selectedNonBlackVi.Copy.DestinationAddress;
                    sourceFormat = selectedNonBlackVi.Copy.Format;
                    sourceCopyIndex = selectedNonBlackVi.CopyIndex;
                    sourceSelectionTicks += Stopwatch.GetTimestamp() - sourceSelectionStart;
                    long pngWriteStart = Stopwatch.GetTimestamp();
                    FramebufferDumper.WriteRgbPng(fullPath, outputWidth, outputHeight, rgb);
                    pngWriteTicks += Stopwatch.GetTimestamp() - pngWriteStart;
                    result = new GxFifoSoftwareRenderResult(fullPath, outputWidth, outputHeight, draws, renderedQuads, degenerateQuads, renderedTriangles, degenerateTriangles, source, sourceAddress, sourceFormat, sourceCopyIndex, rasterPixelsRemaining <= 0, BuildTimings());
                    return true;
                }

                for (int index = displayCopies.Count - 1; index >= 0; index--)
                {
                    if (displayCopies[index].Copy.DestinationAddress == viAddress)
                    {
                        selectedViCopy = displayCopies[index].Copy;
                        sourceCopyIndex = displayCopies[index].CopyIndex;
                        break;
                    }
                }

                DisplayCopyResult copy = selectedViCopy ?? new DisplayCopyResult(viAddress, width, height, FramebufferPixelFormat.Yuyv);
                if (!TryTimedCaptureDisplayCopyRgb(memory, copy, ref displayCaptureTicks, out byte[]? displayRgb) || displayRgb is null)
                {
                    error = $"VI framebuffer 0x{viAddress:X8} could not be captured as {copy.Format}.";
                    return false;
                }

                rgb = displayRgb;
                outputWidth = copy.Width;
                outputHeight = copy.Height;
                sourceAddress = copy.DestinationAddress;
                sourceFormat = copy.Format;
            }
            else if (source == GxFrameDumpSource.LastNonBlackEfb)
            {
                if (lastNonBlackEfb is not SelectedDisplayCopy selectedEfb)
                {
                    error = "no nonblack EFB copy source was captured.";
                    return false;
                }

                rgb = selectedEfb.Rgb;
                outputWidth = selectedEfb.Copy.Width;
                outputHeight = selectedEfb.Copy.Height;
                sourceAddress = selectedEfb.Copy.DestinationAddress;
                sourceFormat = selectedEfb.Copy.Format;
                sourceCopyIndex = selectedEfb.CopyIndex;
            }
            else if (source is GxFrameDumpSource.CopyIndex or GxFrameDumpSource.CopySourceIndex)
            {
                if (displayCopyIndex is null)
                {
                    error = $"GX {FormatFrameSource(source)} frame source requires --gx-frame-copy-index.";
                    return false;
                }

                if (indexedDisplayCopy is not SelectedDisplayCopy indexedCopy)
                {
                    error = source == GxFrameDumpSource.CopySourceIndex
                        ? $"no EFB copy source with copy index {displayCopyIndex.Value} was captured."
                        : $"no EFB display copy with copy index {displayCopyIndex.Value} was captured.";
                    return false;
                }

                rgb = indexedCopy.Rgb;
                outputWidth = indexedCopy.Copy.Width;
                outputHeight = indexedCopy.Copy.Height;
                sourceAddress = indexedCopy.Copy.DestinationAddress;
                sourceFormat = indexedCopy.Copy.Format;
                sourceCopyIndex = indexedCopy.CopyIndex;
            }
            else
            {
                SelectedDisplayCopy? selected = source switch
                {
                    GxFrameDumpSource.LastDisplayCopy => lastDisplayCopy,
                    GxFrameDumpSource.LastNonBlackDisplayCopy => lastNonBlackDisplayCopy,
                    GxFrameDumpSource.LargestDisplayCopy => largestDisplayCopy,
                    _ => null,
                };

                if (selected is not SelectedDisplayCopy selectedDisplay)
                {
                    error = source switch
                    {
                        GxFrameDumpSource.LastNonBlackDisplayCopy => "no nonblack EFB display copy was captured.",
                        GxFrameDumpSource.LargestDisplayCopy => "no EFB display copy was captured for largest-display-copy selection.",
                        _ => "no EFB display copy was captured.",
                    };
                    return false;
                }

                DisplayCopyResult copy = selectedDisplay.Copy;
                rgb = selectedDisplay.Rgb;
                outputWidth = copy.Width;
                outputHeight = copy.Height;
                sourceAddress = copy.DestinationAddress;
                sourceFormat = copy.Format;
                sourceCopyIndex = selectedDisplay.CopyIndex;
            }
        }

        sourceSelectionTicks += Stopwatch.GetTimestamp() - sourceSelectionStart;
        long finalPngWriteStart = Stopwatch.GetTimestamp();
        FramebufferDumper.WriteRgbPng(fullPath, outputWidth, outputHeight, rgb);
        pngWriteTicks += Stopwatch.GetTimestamp() - finalPngWriteStart;
        result = new GxFifoSoftwareRenderResult(fullPath, outputWidth, outputHeight, draws, renderedQuads, degenerateQuads, renderedTriangles, degenerateTriangles, resultSource, sourceAddress, sourceFormat, sourceCopyIndex, rasterPixelsRemaining <= 0, BuildTimings());
        return true;
    }

    private static string FormatFrameSource(GxFrameDumpSource source) =>
        source switch
        {
            GxFrameDumpSource.Auto => "auto",
            GxFrameDumpSource.LastDisplayCopy => "last-display-copy",
            GxFrameDumpSource.LastNonBlackDisplayCopy => "last-nonblack-display-copy",
            GxFrameDumpSource.LargestDisplayCopy => "largest-display-copy",
            GxFrameDumpSource.LastNonBlackEfb => "last-nonblack-efb",
            GxFrameDumpSource.ViFramebuffer => "vi-framebuffer",
            GxFrameDumpSource.LastNonBlackViFramebuffer => "last-nonblack-vi-framebuffer",
            GxFrameDumpSource.CopyIndex => "copy-index",
            GxFrameDumpSource.CopySourceIndex => "copy-source-index",
            _ => "efb",
        };

    private static bool RgbHasNonBlackPixels(byte[] rgb)
    {
        for (int offset = 0; offset < rgb.Length; offset += 3)
        {
            if (rgb[offset] != 0 || rgb[offset + 1] != 0 || rgb[offset + 2] != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryCaptureDisplayCopyRgb(GameCubeMemory? memory, DisplayCopyResult copy, out byte[]? rgb)
    {
        rgb = null;
        if (memory is null)
        {
            return false;
        }

        try
        {
            rgb = FramebufferDumper.CaptureRgb(memory, copy.DestinationAddress, copy.Width, copy.Height, copy.Format);
            return true;
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool TryTimedCaptureDisplayCopyRgb(GameCubeMemory? memory, DisplayCopyResult copy, ref long elapsedTicks, out byte[]? rgb)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        bool captured = TryCaptureDisplayCopyRgb(memory, copy, out rgb);
        elapsedTicks += Stopwatch.GetTimestamp() - startTimestamp;
        return captured;
    }

    private static double StopwatchTicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static double RoundMilliseconds(double milliseconds)
    {
        return Math.Round(milliseconds, 3);
    }

    private static byte[] CaptureEfbSourceRgb(byte[] rgb, int frameWidth, int frameHeight, int left, int top, int width, int height)
    {
        byte[] source = new byte[checked(width * height * 3)];
        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Clamp(top + y, 0, frameHeight - 1);
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Clamp(left + x, 0, frameWidth - 1);
                int inputOffset = (sourceY * frameWidth + sourceX) * 3;
                int outputOffset = (y * width + x) * 3;
                source[outputOffset] = rgb[inputOffset];
                source[outputOffset + 1] = rgb[inputOffset + 1];
                source[outputOffset + 2] = rgb[inputOffset + 2];
            }
        }

        return source;
    }

    private static bool TryResolveViFramebufferAddress(IReadOnlyList<MmioAccess> accesses, out uint address)
    {
        Dictionary<uint, MmioAccess> finalViWrites = accesses
            .Where(access => access.Kind == MmioAccessKind.Write && access.DeviceName == "VI")
            .GroupBy(access => access.Address)
            .ToDictionary(group => group.Key, group => group.Last());

        foreach ((uint highRegister, uint lowRegister) in ViFramebufferRegisterPairs)
        {
            if (finalViWrites.TryGetValue(highRegister, out MmioAccess? high)
                && finalViWrites.TryGetValue(lowRegister, out MmioAccess? low)
                && TryNormalizeVideoInterfaceAddress(CombineVideoInterfaceAddress(high.Value, low.Value), preferShifted: false, out address))
            {
                return true;
            }
        }

        foreach (uint register in ViFramebufferRegisters)
        {
            if (finalViWrites.TryGetValue(register, out MmioAccess? access)
                && access.Width == sizeof(uint)
                && TryNormalizeVideoInterfaceAddress(access.Value, preferShifted: true, out address))
            {
                return true;
            }
        }

        address = 0;
        return false;
    }

    private static uint CombineVideoInterfaceAddress(uint highValue, uint lowValue) =>
        ((highValue & 0xFF) << 16) | (lowValue & 0xFFFF);

    private static bool TryNormalizeVideoInterfaceAddress(uint value, bool preferShifted, out uint address)
    {
        uint shifted = (value & 0x00FF_FFFF) << 5;
        if (preferShifted && shifted != value && GameCubeAddress.TryTranslateMainRam(shifted, out _))
        {
            address = shifted;
            return shifted != 0;
        }

        if (GameCubeAddress.TryTranslateMainRam(value, out _))
        {
            address = value;
            return value != 0;
        }

        if (!preferShifted && shifted != value && GameCubeAddress.TryTranslateMainRam(shifted, out _))
        {
            address = shifted;
            return shifted != 0;
        }

        address = 0;
        return false;
    }

    private static bool ValidateDiagnosticDimensions(int width, int height, out string? error)
    {
        long pixels = (long)width * height;
        if (pixels > MaxDiagnosticPixels)
        {
            error = $"GX diagnostic frame is too large ({width}x{height}); maximum is {MaxDiagnosticPixels} pixels.";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryWriteDrawDiagnostics(IReadOnlyList<MmioAccess> accesses, GameCubeMemory? memory, string path, int maxDraws, out GxFifoDrawDiagnosticResult? result, out string? error)
    {
        return TryWriteDrawDiagnostics(accesses, memory, path, skipDraws: 0, maxDraws, out result, out error);
    }

    public static bool TryWriteDrawDiagnostics(IReadOnlyList<MmioAccess> accesses, GameCubeMemory? memory, string path, int skipDraws, int maxDraws, out GxFifoDrawDiagnosticResult? result, out string? error)
    {
        ArgumentNullException.ThrowIfNull(accesses);

        result = null;
        error = null;
        if (skipDraws < 0)
        {
            error = "GX draw diagnostic skipped draw count must be non-negative.";
            return false;
        }

        if (maxDraws <= 0)
        {
            error = "GX draw diagnostic count must be positive.";
            return false;
        }

        byte[] fifo = accesses
            .Where(access => access.DeviceName == "GX FIFO" && access.Kind == MmioAccessKind.Write)
            .SelectMany(ExpandMmioWriteBytes)
            .ToArray();
        if (fifo.Length == 0)
        {
            error = "no GX FIFO writes were captured.";
            return false;
        }

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        GxVertexState state = new();
        int offset = 0;
        int totalDraws = 0;
        int drawsWritten = 0;
        int requestedDrawsWritten = 0;
        int decodedDraws = 0;
        int allZeroPositionDraws = 0;
        int nonZeroPositionDraws = 0;
        int allBlackColorDraws = 0;
        int nonBlackColorDraws = 0;
        string? firstNonZeroPositionDraw = null;
        string? firstNonBlackColorDraw = null;
        using StreamWriter writer = new(fullPath);
        writer.WriteLine("GX draw diagnostics");
        writer.WriteLine($"FIFO bytes: {fifo.Length}");
        writer.WriteLine($"Skip draws: {skipDraws}");
        writer.WriteLine($"Max draws: {maxDraws}");
        writer.WriteLine();

        while (offset < fifo.Length)
        {
            int commandOffset = offset;
            byte command = fifo[offset++];
            if (command is 0x00 or 0x44 or 0x48)
            {
                continue;
            }

            if (command == 0x08 && TryReadBigEndian(fifo, offset, 1, out uint cpRegister) && TryReadBigEndian(fifo, offset + 1, 4, out uint cpValue))
            {
                state.WriteCpRegister((byte)cpRegister, cpValue);
                offset += 5;
                continue;
            }

            if (command == 0x10 && TryReadBigEndian(fifo, offset, 4, out uint xfHeader))
            {
                ushort xfRegister = (ushort)(xfHeader & 0xFFFF);
                uint wordCount = ((xfHeader >> 16) & 0xFFFF) + 1;
                int xfPayloadBytes = AvailablePayloadBytes(fifo, offset + 4, wordCount);
                state.WriteXfRegisters(fifo, offset + 4, xfRegister, xfPayloadBytes / sizeof(uint));
                offset += 4 + xfPayloadBytes;
                continue;
            }

            if (command is 0x20 or 0x28 or 0x30 or 0x38)
            {
                offset += Math.Min(4, fifo.Length - offset);
                continue;
            }

            if (command == 0x40)
            {
                offset += Math.Min(8, fifo.Length - offset);
                continue;
            }

            if (command == 0x61)
            {
                if (TryReadBigEndian(fifo, offset, 4, out uint bpValue))
                {
                    state.WriteBpRegister(bpValue);
                }

                offset += Math.Min(4, fifo.Length - offset);
                continue;
            }

            if ((command & 0x80) == 0 || !TryReadBigEndian(fifo, offset, 2, out uint vertexCount))
            {
                writer.WriteLine($"Stopped at +0x{commandOffset:X}: command 0x{command:X2} is not a recognized GX FIFO command.");
                break;
            }

            int format = command & 7;
            if (!state.TryGetVertexStride(format, out int stride))
            {
                writer.WriteLine($"Stopped at +0x{commandOffset:X}: draw command 0x{command:X2} has unsupported vertex format {format}.");
                writer.WriteLine(state.DescribeFormat(format));
                break;
            }

            offset += 2;
            long payloadBytes = (long)vertexCount * stride;
            if (payloadBytes > fifo.Length - offset)
            {
                writer.WriteLine($"Stopped at +0x{commandOffset:X}: draw payload truncated; wanted {payloadBytes} byte(s), had {fifo.Length - offset}.");
                break;
            }

            totalDraws++;
            bool writeFirstNonBlackDraw = false;
            if (TryAnalyzeDraw(fifo, offset, format, vertexCount, state, memory, out int decodedVertices, out bool allZeroXy, out bool allBlackRgb, out float minX, out float maxX, out float minY, out float maxY))
            {
                decodedDraws++;
                if (allZeroXy)
                {
                    allZeroPositionDraws++;
                }
                else
                {
                    nonZeroPositionDraws++;
                    firstNonZeroPositionDraw ??= $"+0x{commandOffset:X} {PrimitiveName(command)}, fmt={format}, vertices={vertexCount}, decoded={decodedVertices}, x=[{FormatFloat(minX)}, {FormatFloat(maxX)}], y=[{FormatFloat(minY)}, {FormatFloat(maxY)}]";
                }

                if (allBlackRgb)
                {
                    allBlackColorDraws++;
                }
                else
                {
                    nonBlackColorDraws++;
                    if (firstNonBlackColorDraw is null)
                    {
                        firstNonBlackColorDraw = $"+0x{commandOffset:X} {PrimitiveName(command)}, fmt={format}, vertices={vertexCount}";
                        writeFirstNonBlackDraw = true;
                    }
                }
            }

            bool inRequestedWindow = totalDraws > skipDraws && requestedDrawsWritten < maxDraws;
            if (inRequestedWindow || writeFirstNonBlackDraw)
            {
                string? reason = inRequestedWindow ? null : "first nonblack RGB draw";
                WriteDrawDiagnostic(writer, fifo, commandOffset, offset, command, format, vertexCount, stride, state, memory, reason);
                drawsWritten++;
                if (inRequestedWindow)
                {
                    requestedDrawsWritten++;
                }
            }

            offset += (int)payloadBytes;
        }

        writer.WriteLine("Draw position summary:");
        writer.WriteLine($"  decoded draws: {decodedDraws}");
        writer.WriteLine($"  all-zero XY draws: {allZeroPositionDraws}");
        writer.WriteLine($"  nonzero XY draws: {nonZeroPositionDraws}");
        writer.WriteLine($"  first nonzero XY draw: {firstNonZeroPositionDraw ?? "none"}");
        writer.WriteLine($"  all-black RGB draws: {allBlackColorDraws}");
        writer.WriteLine($"  nonblack RGB draws: {nonBlackColorDraws}");
        writer.WriteLine($"  first nonblack RGB draw: {firstNonBlackColorDraw ?? "none"}");
        writer.WriteLine($"Total draws seen: {totalDraws}");
        writer.WriteLine($"Draws written: {drawsWritten}");
        result = new GxFifoDrawDiagnosticResult(fullPath, totalDraws, drawsWritten);
        return true;
    }

    public static bool TryWriteCopyDiagnostics(IReadOnlyList<MmioAccess> accesses, GameCubeMemory? memory, string path, int width, int height, int? maxRasterizedPixels, out GxFifoCopyDiagnosticResult? result, out string? error)
    {
        return TryWriteCopyDiagnostics(accesses, memory, path, width, height, maxRasterizedPixels, ignoreEfbCopyClear: false, out result, out error);
    }

    public static bool TryWriteCopyDiagnostics(IReadOnlyList<MmioAccess> accesses, GameCubeMemory? memory, string path, int width, int height, int? maxRasterizedPixels, bool ignoreEfbCopyClear, out GxFifoCopyDiagnosticResult? result, out string? error)
    {
        ArgumentNullException.ThrowIfNull(accesses);

        result = null;
        error = null;
        if (width <= 0 || height <= 0)
        {
            error = "GX copy diagnostic dimensions must be positive.";
            return false;
        }

        if (!ValidateDiagnosticDimensions(width, height, out error))
        {
            return false;
        }

        if (maxRasterizedPixels is <= 0)
        {
            error = "GX copy diagnostic raster pixel budget must be positive.";
            return false;
        }

        byte[] fifo = accesses
            .Where(access => access.DeviceName == "GX FIFO" && access.Kind == MmioAccessKind.Write)
            .SelectMany(ExpandMmioWriteBytes)
            .ToArray();
        if (fifo.Length == 0)
        {
            error = "no GX FIFO writes were captured.";
            return false;
        }

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        byte[] rgb = new byte[checked(width * height * 3)];
        byte[] alpha = Enumerable.Repeat((byte)255, checked(width * height)).ToArray();
        float[] depth = Enumerable.Repeat(FarDepthValue, checked(width * height)).ToArray();
        GxVertexState state = new();
        int offset = 0;
        int totalDraws = 0;
        int renderedQuads = 0;
        int degenerateQuads = 0;
        int renderedTriangles = 0;
        int degenerateTriangles = 0;
        int rasterPixelsRemaining = maxRasterizedPixels ?? int.MaxValue;
        int copiesWritten = 0;

        using StreamWriter writer = new(fullPath);
        writer.WriteLine("copy_index,fifo_offset,draws_seen,kind,destination_address,destination_raw,destination_tiles,format,control,src_left,src_top,src_width,src_height,output_width,output_height,clear,clear_applied,mipmap,copied,display_address,display_width,display_height,display_format,display_nonblack,display_nonblack_percent,display_nonblack_bounds,display_samples,before_pixels,before_nonblack,before_alpha_nonblack,before_nonblack_percent,before_nonblack_bounds,before_alpha_bounds,after_pixels,after_nonblack,after_alpha_nonblack,after_nonblack_percent,after_nonblack_bounds,after_alpha_bounds,texture_readback_mismatches,copy_samples");

        while (offset < fifo.Length)
        {
            int commandOffset = offset;
            byte command = fifo[offset++];
            if (command is 0x00 or 0x44 or 0x48)
            {
                continue;
            }

            if (command == 0x08 && TryReadBigEndian(fifo, offset, 1, out uint cpRegister) && TryReadBigEndian(fifo, offset + 1, 4, out uint cpValue))
            {
                state.WriteCpRegister((byte)cpRegister, cpValue);
                offset += 5;
                continue;
            }

            if (command == 0x10 && TryReadBigEndian(fifo, offset, 4, out uint xfHeader))
            {
                ushort xfRegister = (ushort)(xfHeader & 0xFFFF);
                uint wordCount = ((xfHeader >> 16) & 0xFFFF) + 1;
                int xfPayloadBytes = AvailablePayloadBytes(fifo, offset + 4, wordCount);
                state.WriteXfRegisters(fifo, offset + 4, xfRegister, xfPayloadBytes / sizeof(uint));
                offset += 4 + xfPayloadBytes;
                continue;
            }

            if (command is 0x20 or 0x28 or 0x30 or 0x38)
            {
                offset += Math.Min(4, fifo.Length - offset);
                continue;
            }

            if (command == 0x40)
            {
                offset += Math.Min(8, fifo.Length - offset);
                continue;
            }

            if (command == 0x61)
            {
                if (TryReadBigEndian(fifo, offset, 4, out uint bpValue))
                {
                    state.WriteBpRegister(bpValue);
                    if ((bpValue >> 24) == 0x52 && state.TryGetEfbCopyInfo(out EfbCopyInfo copy))
                    {
                        EfbCoverage before = CountNonBlackRegion(rgb, alpha, width, height, copy.SourceLeft, copy.SourceTop, copy.SourceWidth, copy.SourceHeight);
                        CopySourceSample[] sourceSamples = CaptureCopySourceSamples(rgb, alpha, width, height, copy);
                        bool clearAfterCopy = !ignoreEfbCopyClear;
                        bool copied = state.TryCopyEfb(memory, rgb, alpha, depth, width, height, clearAfterCopy, out DisplayCopyResult? displayCopy);
                        EfbCoverage after = CountNonBlackRegion(rgb, alpha, width, height, copy.SourceLeft, copy.SourceTop, copy.SourceWidth, copy.SourceHeight);
                        string copySamples = DescribeCopySamples(memory, copy, copied, sourceSamples, out int textureReadbackMismatches);
                        DisplayReadback displayReadback = CaptureDisplayReadback(memory, displayCopy);
                        copiesWritten++;
                        WriteCopyDiagnosticCsv(writer, copiesWritten, commandOffset, totalDraws, copy, clearAfterCopy && copy.Clear, copied, displayCopy, displayReadback, before, after, textureReadbackMismatches, copySamples);
                    }
                }

                offset += Math.Min(4, fifo.Length - offset);
                continue;
            }

            if ((command & 0x80) == 0 || !TryReadBigEndian(fifo, offset, 2, out uint vertexCount))
            {
                break;
            }

            int format = command & 7;
            if (!state.TryGetVertexStride(format, out int stride))
            {
                break;
            }

            offset += 2;
            long payloadBytes = (long)vertexCount * stride;
            if (payloadBytes > fifo.Length - offset)
            {
                break;
            }

            totalDraws++;
            if (state.IsRenderablePositionFormat(format) && vertexCount <= int.MaxValue)
            {
                int vertexOffset = offset;
                GxVertex[] vertices = new GxVertex[(int)vertexCount];
                bool decodedVertices = true;
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                {
                    if (!state.TryReadVertex(fifo, ref vertexOffset, format, memory, out vertices[vertex]))
                    {
                        decodedVertices = false;
                        break;
                    }
                }

                if (decodedVertices)
                {
                    RenderPrimitiveBounds(rgb, alpha, depth, width, height, command, vertices, state, memory, ref renderedQuads, ref degenerateQuads, ref renderedTriangles, ref degenerateTriangles, ref rasterPixelsRemaining, counters: null);
                }
            }

            offset += (int)payloadBytes;
        }

        result = new GxFifoCopyDiagnosticResult(fullPath, copiesWritten, totalDraws, rasterPixelsRemaining <= 0);
        return true;
    }

    private static EfbCoverage CountNonBlackRegion(byte[] rgb, byte[] alpha, int frameWidth, int frameHeight, int left, int top, int width, int height)
    {
        int pixels = 0;
        int nonBlack = 0;
        int alphaVisibleNonBlack = 0;
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;
        int alphaMinX = int.MaxValue;
        int alphaMinY = int.MaxValue;
        int alphaMaxX = int.MinValue;
        int alphaMaxY = int.MinValue;
        for (int y = 0; y < height; y++)
        {
            int py = top + y;
            if (py < 0 || py >= frameHeight)
            {
                continue;
            }

            for (int x = 0; x < width; x++)
            {
                int px = left + x;
                if (px < 0 || px >= frameWidth)
                {
                    continue;
                }

                pixels++;
                int pixel = py * frameWidth + px;
                int rgbOffset = pixel * 3;
                bool rgbNonBlack = rgb[rgbOffset] != 0 || rgb[rgbOffset + 1] != 0 || rgb[rgbOffset + 2] != 0;
                if (rgbNonBlack)
                {
                    nonBlack++;
                    if (px < minX)
                    {
                        minX = px;
                    }
                    if (py < minY)
                    {
                        minY = py;
                    }
                    if (px > maxX)
                    {
                        maxX = px;
                    }
                    if (py > maxY)
                    {
                        maxY = py;
                    }
                    if (alpha[pixel] != 0)
                    {
                        alphaVisibleNonBlack++;
                        if (px < alphaMinX)
                        {
                            alphaMinX = px;
                        }
                        if (py < alphaMinY)
                        {
                            alphaMinY = py;
                        }
                        if (px > alphaMaxX)
                        {
                            alphaMaxX = px;
                        }
                        if (py > alphaMaxY)
                        {
                            alphaMaxY = py;
                        }
                    }
                }
            }
        }

        return new EfbCoverage(
            pixels,
            nonBlack,
            alphaVisibleNonBlack,
            nonBlack == 0 ? -1 : minX,
            nonBlack == 0 ? -1 : minY,
            nonBlack == 0 ? -1 : maxX,
            nonBlack == 0 ? -1 : maxY,
            alphaVisibleNonBlack == 0 ? -1 : alphaMinX,
            alphaVisibleNonBlack == 0 ? -1 : alphaMinY,
            alphaVisibleNonBlack == 0 ? -1 : alphaMaxX,
            alphaVisibleNonBlack == 0 ? -1 : alphaMaxY);
    }

    private static CopySourceSample[] CaptureCopySourceSamples(byte[] rgb, byte[] alpha, int frameWidth, int frameHeight, EfbCopyInfo copy)
    {
        if (copy.SourceWidth <= 0 || copy.SourceHeight <= 0)
        {
            return [];
        }

        (string Name, int X, int Y)[] samplePoints =
        [
            ("top_left", 0, 0),
            ("center", copy.SourceWidth / 2, copy.SourceHeight / 2),
            ("bottom_right", copy.SourceWidth - 1, copy.SourceHeight - 1),
        ];
        CopySourceSample[] samples = new CopySourceSample[samplePoints.Length];
        for (int index = 0; index < samplePoints.Length; index++)
        {
            (string name, int x, int y) = samplePoints[index];
            SampleDiagnosticEfbPixel(rgb, alpha, frameWidth, frameHeight, copy.SourceLeft + x, copy.SourceTop + y, out byte r, out byte g, out byte b, out byte a);
            samples[index] = new CopySourceSample(name, x, y, r, g, b, a);
        }

        return samples;
    }

    private static string DescribeCopySamples(GameCubeMemory? memory, EfbCopyInfo copy, bool copied, IReadOnlyList<CopySourceSample> sourceSamples, out int textureReadbackMismatches)
    {
        textureReadbackMismatches = 0;
        if (sourceSamples.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        bool canReadBackTexture = copied && !copy.IsDisplayCopy && memory is not null;
        for (int index = 0; index < sourceSamples.Count; index++)
        {
            CopySourceSample sample = sourceSamples[index];
            if (builder.Length != 0)
            {
                builder.Append(" | ");
            }

            builder.Append(sample.Name)
                .Append('@').Append(sample.X).Append('/').Append(sample.Y)
                .Append(":src=").Append(FormatColor(sample.R, sample.G, sample.B, sample.A));
            if (!canReadBackTexture)
            {
                continue;
            }

            if (TryReadCopiedTexturePixel(memory!, copy.DestinationAddress, copy.OutputWidth, copy.OutputHeight, copy.Format, sample.X, sample.Y, out byte r, out byte g, out byte b, out byte a))
            {
                builder.Append(",tex=").Append(FormatColor(r, g, b, a));
                if (!IsExpectedTextureRoundTrip(sample, copy.Format, r, g, b, a))
                {
                    textureReadbackMismatches++;
                    builder.Append(",mismatch");
                }
            }
            else
            {
                textureReadbackMismatches++;
                builder.Append(",tex=unreadable");
            }
        }

        return builder.ToString();
    }

    private static bool TryReadCopiedTexturePixel(GameCubeMemory memory, uint baseAddress, int width, int height, int format, int x, int y, out byte r, out byte g, out byte b, out byte a)
    {
        r = 0;
        g = 0;
        b = 0;
        a = 255;
        if (width <= 0 || height <= 0 || x < 0 || y < 0 || x >= width || y >= height)
        {
            return false;
        }

        try
        {
            switch (format)
            {
                case 0:
                    {
                        int blockColumns = (width + 7) / 8;
                        int block = (y / 8) * blockColumns + x / 8;
                        int offset = block * 32 + (y & 7) * 4 + (x & 7) / 2;
                        byte packed = memory.Read8(baseAddress + (uint)offset);
                        int nibble = (x & 1) == 0 ? packed >> 4 : packed & 0x0F;
                        r = g = b = Expand4Diagnostic(nibble);
                        return true;
                    }
                case 1:
                    {
                        int blockColumns = (width + 7) / 8;
                        int block = (y / 4) * blockColumns + x / 8;
                        int offset = block * 32 + (y & 3) * 8 + (x & 7);
                        r = g = b = memory.Read8(baseAddress + (uint)offset);
                        return true;
                    }
                case 2:
                    {
                        int blockColumns = (width + 7) / 8;
                        int block = (y / 4) * blockColumns + x / 8;
                        int offset = block * 32 + (y & 3) * 8 + (x & 7);
                        byte packed = memory.Read8(baseAddress + (uint)offset);
                        r = g = b = Expand4Diagnostic(packed & 0x0F);
                        a = Expand4Diagnostic(packed >> 4);
                        return true;
                    }
                case 3:
                    {
                        ushort value = memory.Read16(DiagnosticTiled16Address(baseAddress, width, x, y));
                        a = (byte)(value >> 8);
                        r = g = b = (byte)value;
                        return true;
                    }
                case 4:
                    DecodeRgb565Diagnostic(memory.Read16(DiagnosticTiled16Address(baseAddress, width, x, y)), out r, out g, out b);
                    return true;
                case 5:
                    DecodeRgb5A3Diagnostic(memory.Read16(DiagnosticTiled16Address(baseAddress, width, x, y)), out r, out g, out b, out a);
                    return true;
                case 6:
                    {
                        int blockColumns = (width + 3) / 4;
                        int block = (y / 4) * blockColumns + x / 4;
                        int texel = (y & 3) * 4 + (x & 3);
                        uint address = baseAddress + (uint)(block * 64 + texel * 2);
                        a = memory.Read8(address);
                        r = memory.Read8(address + 1);
                        g = memory.Read8(address + 32);
                        b = memory.Read8(address + 33);
                        return true;
                    }
                default:
                    return false;
            }
        }
        catch (AddressTranslationException)
        {
            return false;
        }
    }

    private static bool IsExpectedTextureRoundTrip(CopySourceSample source, int format, byte r, byte g, byte b, byte a)
    {
        int tolerance = format switch
        {
            0 or 2 or 5 => 18,
            1 or 3 => 4,
            4 => 8,
            _ => 0,
        };

        if (format is 0 or 1 or 2 or 3)
        {
            byte luma = DiagnosticLuma(source.R, source.G, source.B);
            return ChannelClose(luma, r, tolerance)
                && ChannelClose(luma, g, tolerance)
                && ChannelClose(luma, b, tolerance)
                && (format is 0 or 1 || ChannelClose(source.A, a, tolerance));
        }

        return ChannelClose(source.R, r, tolerance)
            && ChannelClose(source.G, g, tolerance)
            && ChannelClose(source.B, b, tolerance)
            && (format is 4 || ChannelClose(source.A, a, tolerance));
    }

    private static bool ChannelClose(byte expected, byte actual, int tolerance) =>
        Math.Abs(expected - actual) <= tolerance;

    private static void SampleDiagnosticEfbPixel(byte[] rgb, byte[] alpha, int frameWidth, int frameHeight, int x, int y, out byte r, out byte g, out byte b, out byte a)
    {
        x = Math.Clamp(x, 0, frameWidth - 1);
        y = Math.Clamp(y, 0, frameHeight - 1);
        int pixel = y * frameWidth + x;
        int rgbOffset = pixel * 3;
        r = rgb[rgbOffset];
        g = rgb[rgbOffset + 1];
        b = rgb[rgbOffset + 2];
        a = alpha[pixel];
    }

    private static uint DiagnosticTiled16Address(uint baseAddress, int width, int x, int y)
    {
        int blockColumns = (width + 3) / 4;
        int block = (y / 4) * blockColumns + x / 4;
        return baseAddress + (uint)(block * 32 + ((y & 3) * 4 + (x & 3)) * 2);
    }

    private static byte DiagnosticLuma(byte r, byte g, byte b) =>
        (byte)((r * 299 + g * 587 + b * 114 + 500) / 1000);

    private static void DecodeRgb565Diagnostic(ushort value, out byte r, out byte g, out byte b)
    {
        r = Expand5Diagnostic((value >> 11) & 0x1F);
        g = Expand6Diagnostic((value >> 5) & 0x3F);
        b = Expand5Diagnostic(value & 0x1F);
    }

    private static void DecodeRgb5A3Diagnostic(ushort value, out byte r, out byte g, out byte b, out byte a)
    {
        if ((value & 0x8000) != 0)
        {
            r = Expand5Diagnostic((value >> 10) & 0x1F);
            g = Expand5Diagnostic((value >> 5) & 0x1F);
            b = Expand5Diagnostic(value & 0x1F);
            a = 255;
            return;
        }

        a = Expand3Diagnostic((value >> 12) & 0x07);
        r = Expand4Diagnostic((value >> 8) & 0x0F);
        g = Expand4Diagnostic((value >> 4) & 0x0F);
        b = Expand4Diagnostic(value & 0x0F);
    }

    private static byte Expand3Diagnostic(int value) => (byte)((value << 5) | (value << 2) | (value >> 1));

    private static byte Expand4Diagnostic(int value) => (byte)((value << 4) | value);

    private static byte Expand5Diagnostic(int value) => (byte)((value << 3) | (value >> 2));

    private static byte Expand6Diagnostic(int value) => (byte)((value << 2) | (value >> 4));

    private static void WriteCopyDiagnosticCsv(StreamWriter writer, int copyIndex, int fifoOffset, int drawsSeen, EfbCopyInfo copy, bool clearApplied, bool copied, DisplayCopyResult? displayCopy, DisplayReadback displayReadback, EfbCoverage before, EfbCoverage after, int textureReadbackMismatches, string copySamples)
    {
        string kind = copy.IsDisplayCopy ? "display" : "texture";
        string displayAddress = displayCopy is DisplayCopyResult display ? $"0x{display.DestinationAddress:X8}" : string.Empty;
        string displayWidth = displayCopy is DisplayCopyResult displayWidthCopy ? displayWidthCopy.Width.ToString(CultureInfo.InvariantCulture) : string.Empty;
        string displayHeight = displayCopy is DisplayCopyResult displayHeightCopy ? displayHeightCopy.Height.ToString(CultureInfo.InvariantCulture) : string.Empty;
        string displayFormat = displayCopy is DisplayCopyResult displayFormatCopy ? displayFormatCopy.Format.ToString() : string.Empty;
        writer.WriteLine(string.Join(',',
            copyIndex.ToString(CultureInfo.InvariantCulture),
            $"+0x{fifoOffset:X}",
            drawsSeen.ToString(CultureInfo.InvariantCulture),
            kind,
            $"0x{copy.DestinationAddress:X8}",
            $"0x{copy.DestinationRaw:X6}",
            copy.DestinationTiles.ToString(CultureInfo.InvariantCulture),
            GxVertexState.TextureFormatName(copy.Format),
            $"0x{copy.Control:X6}",
            copy.SourceLeft.ToString(CultureInfo.InvariantCulture),
            copy.SourceTop.ToString(CultureInfo.InvariantCulture),
            copy.SourceWidth.ToString(CultureInfo.InvariantCulture),
            copy.SourceHeight.ToString(CultureInfo.InvariantCulture),
            copy.OutputWidth.ToString(CultureInfo.InvariantCulture),
            copy.OutputHeight.ToString(CultureInfo.InvariantCulture),
            copy.Clear ? "True" : "False",
            clearApplied ? "True" : "False",
            copy.Mipmap ? "True" : "False",
            copied ? "True" : "False",
            displayAddress,
            displayWidth,
            displayHeight,
            displayFormat,
            displayReadback.Coverage.NonBlackPixels.ToString(CultureInfo.InvariantCulture),
            FormatPercent(displayReadback.Coverage),
            CsvField(FormatBounds(displayReadback.Coverage)),
            CsvField(displayReadback.Samples ?? string.Empty),
            before.Pixels.ToString(CultureInfo.InvariantCulture),
            before.NonBlackPixels.ToString(CultureInfo.InvariantCulture),
            before.AlphaVisibleNonBlackPixels.ToString(CultureInfo.InvariantCulture),
            FormatPercent(before),
            CsvField(FormatBounds(before)),
            CsvField(FormatAlphaBounds(before)),
            after.Pixels.ToString(CultureInfo.InvariantCulture),
            after.NonBlackPixels.ToString(CultureInfo.InvariantCulture),
            after.AlphaVisibleNonBlackPixels.ToString(CultureInfo.InvariantCulture),
            FormatPercent(after),
            CsvField(FormatBounds(after)),
            CsvField(FormatAlphaBounds(after)),
            textureReadbackMismatches.ToString(CultureInfo.InvariantCulture),
            CsvField(copySamples)));
    }

    private readonly record struct CopySourceSample(string Name, int X, int Y, byte R, byte G, byte B, byte A);
    private readonly record struct DisplayReadback(EfbCoverage Coverage, string Samples);

    private static DisplayReadback CaptureDisplayReadback(GameCubeMemory? memory, DisplayCopyResult? displayCopy)
    {
        if (memory is null || displayCopy is not DisplayCopyResult copy)
        {
            return default;
        }

        try
        {
            byte[] displayRgb = FramebufferDumper.CaptureRgb(memory, copy.DestinationAddress, copy.Width, copy.Height, copy.Format);
            byte[] displayAlpha = Enumerable.Repeat((byte)255, checked(copy.Width * copy.Height)).ToArray();
            EfbCoverage coverage = CountNonBlackRegion(displayRgb, displayAlpha, copy.Width, copy.Height, 0, 0, copy.Width, copy.Height);
            string samples = DescribeDisplaySamples(displayRgb, copy.Width, copy.Height);
            return new DisplayReadback(coverage, samples);
        }
        catch (AddressTranslationException)
        {
            return default;
        }
    }

    private static string DescribeDisplaySamples(byte[] rgb, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return string.Empty;
        }

        (string Name, int X, int Y)[] samplePoints =
        [
            ("top_left", 0, 0),
            ("center", width / 2, height / 2),
            ("bottom_right", width - 1, height - 1),
        ];

        StringBuilder builder = new();
        for (int index = 0; index < samplePoints.Length; index++)
        {
            (string name, int x, int y) = samplePoints[index];
            int pixel = y * width + x;
            int rgbOffset = pixel * 3;
            if (index != 0)
            {
                builder.Append(" | ");
            }

            builder
                .Append(name)
                .Append('@')
                .Append(x.ToString(CultureInfo.InvariantCulture))
                .Append('/')
                .Append(y.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(rgb[rgbOffset].ToString(CultureInfo.InvariantCulture))
                .Append('/')
                .Append(rgb[rgbOffset + 1].ToString(CultureInfo.InvariantCulture))
                .Append('/')
                .Append(rgb[rgbOffset + 2].ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string FormatPercent(EfbCoverage coverage) =>
        coverage.Pixels == 0
            ? "0"
            : ((double)coverage.NonBlackPixels * 100.0 / coverage.Pixels).ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatBounds(EfbCoverage coverage) =>
        coverage.HasNonBlackBounds
            ? $"{coverage.MinX}/{coverage.MinY}-{coverage.MaxX}/{coverage.MaxY}"
            : string.Empty;

    private static string FormatAlphaBounds(EfbCoverage coverage) =>
        coverage.HasAlphaVisibleBounds
            ? $"{coverage.AlphaMinX}/{coverage.AlphaMinY}-{coverage.AlphaMaxX}/{coverage.AlphaMaxY}"
            : string.Empty;

    public static bool TryWriteDrawCoverageDiagnostics(IReadOnlyList<MmioAccess> accesses, GameCubeMemory? memory, string path, int width, int height, int? maxRasterizedPixels, bool ignoreEfbCopyClear, out GxFifoCoverageDiagnosticResult? result, out string? error)
    {
        return TryWriteDrawCoverageDiagnostics(accesses, memory, path, width, height, maxRasterizedPixels, ignoreEfbCopyClear, skipDraws: 0, maxDraws: null, out result, out error);
    }

    public static bool TryWriteDrawCoverageDiagnostics(IReadOnlyList<MmioAccess> accesses, GameCubeMemory? memory, string path, int width, int height, int? maxRasterizedPixels, bool ignoreEfbCopyClear, int skipDraws, int? maxDraws, out GxFifoCoverageDiagnosticResult? result, out string? error)
    {
        ArgumentNullException.ThrowIfNull(accesses);

        result = null;
        error = null;
        if (width <= 0 || height <= 0)
        {
            error = "GX coverage diagnostic dimensions must be positive.";
            return false;
        }

        if (!ValidateDiagnosticDimensions(width, height, out error))
        {
            return false;
        }

        if (maxRasterizedPixels is <= 0)
        {
            error = "GX coverage diagnostic raster pixel budget must be positive.";
            return false;
        }

        if (skipDraws < 0)
        {
            error = "GX coverage diagnostic skipped draw count must be non-negative.";
            return false;
        }

        if (maxDraws is <= 0)
        {
            error = "GX coverage diagnostic draw limit must be positive.";
            return false;
        }

        byte[] fifo = accesses
            .Where(access => access.DeviceName == "GX FIFO" && access.Kind == MmioAccessKind.Write)
            .SelectMany(ExpandMmioWriteBytes)
            .ToArray();
        if (fifo.Length == 0)
        {
            error = "no GX FIFO writes were captured.";
            return false;
        }

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        byte[] rgb = new byte[checked(width * height * 3)];
        byte[] alpha = Enumerable.Repeat((byte)255, checked(width * height)).ToArray();
        float[] depth = Enumerable.Repeat(FarDepthValue, checked(width * height)).ToArray();
        GxVertexState state = new();
        int offset = 0;
        int totalDraws = 0;
        int drawsWritten = 0;
        int copiesSeen = 0;
        int renderedQuads = 0;
        int degenerateQuads = 0;
        int renderedTriangles = 0;
        int degenerateTriangles = 0;
        int rasterPixelsRemaining = maxRasterizedPixels ?? int.MaxValue;
        EfbCoverage currentCoverage = CountNonBlackRegion(rgb, alpha, width, height, 0, 0, width, height);

        using StreamWriter writer = new(fullPath);
        writer.WriteLine("draw_index,fifo_offset,primitive,format,vertices,decoded,all_zero_xy,all_black_rgb,min_x,max_x,min_y,max_y,copies_seen,before_nonblack,after_nonblack,delta_nonblack,before_alpha_nonblack,after_alpha_nonblack,delta_alpha_nonblack,before_percent,after_percent,raster_before,raster_after,raster_spent,covered_pixels,depth_rejected,alpha_rejected,color_update_disabled,color_writes,black_color_writes,alpha_updates,depth_updates,rendered_quads_delta,rendered_triangles_delta,degenerate_quads_delta,degenerate_triangles_delta");

        while (offset < fifo.Length)
        {
            int commandOffset = offset;
            byte command = fifo[offset++];
            if (command is 0x00 or 0x44 or 0x48)
            {
                continue;
            }

            if (command == 0x08 && TryReadBigEndian(fifo, offset, 1, out uint cpRegister) && TryReadBigEndian(fifo, offset + 1, 4, out uint cpValue))
            {
                state.WriteCpRegister((byte)cpRegister, cpValue);
                offset += 5;
                continue;
            }

            if (command == 0x10 && TryReadBigEndian(fifo, offset, 4, out uint xfHeader))
            {
                ushort xfRegister = (ushort)(xfHeader & 0xFFFF);
                uint wordCount = ((xfHeader >> 16) & 0xFFFF) + 1;
                int xfPayloadBytes = AvailablePayloadBytes(fifo, offset + 4, wordCount);
                state.WriteXfRegisters(fifo, offset + 4, xfRegister, xfPayloadBytes / sizeof(uint));
                offset += 4 + xfPayloadBytes;
                continue;
            }

            if (command is 0x20 or 0x28 or 0x30 or 0x38)
            {
                offset += Math.Min(4, fifo.Length - offset);
                continue;
            }

            if (command == 0x40)
            {
                offset += Math.Min(8, fifo.Length - offset);
                continue;
            }

            if (command == 0x61)
            {
                if (TryReadBigEndian(fifo, offset, 4, out uint bpValue))
                {
                    state.WriteBpRegister(bpValue);
                    if ((bpValue >> 24) == 0x52)
                    {
                        copiesSeen++;
                        state.TryCopyEfb(memory, rgb, alpha, depth, width, height, clearAfterCopy: !ignoreEfbCopyClear, out _);
                        currentCoverage = CountNonBlackRegion(rgb, alpha, width, height, 0, 0, width, height);
                    }
                }

                offset += Math.Min(4, fifo.Length - offset);
                continue;
            }

            if ((command & 0x80) == 0 || !TryReadBigEndian(fifo, offset, 2, out uint vertexCount))
            {
                break;
            }

            int format = command & 7;
            if (!state.TryGetVertexStride(format, out int stride))
            {
                break;
            }

            offset += 2;
            long payloadBytes = (long)vertexCount * stride;
            if (payloadBytes > fifo.Length - offset)
            {
                break;
            }

            totalDraws++;
            long renderEndDraw = maxDraws is int maxDrawLimit ? (long)skipDraws + maxDrawLimit : long.MaxValue;
            bool renderDraw = totalDraws > skipDraws && totalDraws <= renderEndDraw;
            bool decoded = TryAnalyzeDraw(fifo, offset, format, vertexCount, state, memory, out _, out bool allZeroXy, out bool allBlackRgb, out float minX, out float maxX, out float minY, out float maxY);
            EfbCoverage before = currentCoverage;
            int rasterBefore = rasterPixelsRemaining;
            int quadsBefore = renderedQuads;
            int trianglesBefore = renderedTriangles;
            int degenerateQuadsBefore = degenerateQuads;
            int degenerateTrianglesBefore = degenerateTriangles;
            RasterDiagnosticCounters counters = new();

            if (renderDraw && state.IsRenderablePositionFormat(format) && vertexCount <= int.MaxValue)
            {
                int vertexOffset = offset;
                GxVertex[] vertices = new GxVertex[(int)vertexCount];
                bool decodedVertices = true;
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                {
                    if (!state.TryReadVertex(fifo, ref vertexOffset, format, memory, out vertices[vertex]))
                    {
                        decodedVertices = false;
                        break;
                    }
                }

                if (decodedVertices)
                {
                    RenderPrimitiveBounds(rgb, alpha, depth, width, height, command, vertices, state, memory, ref renderedQuads, ref degenerateQuads, ref renderedTriangles, ref degenerateTriangles, ref rasterPixelsRemaining, counters);
                }
            }

            currentCoverage = CountNonBlackRegion(rgb, alpha, width, height, 0, 0, width, height);
            if (renderDraw)
            {
                drawsWritten++;
                WriteDrawCoverageCsv(writer, totalDraws, commandOffset, command, format, vertexCount, decoded, allZeroXy, allBlackRgb, minX, maxX, minY, maxY, copiesSeen, before, currentCoverage, rasterBefore, rasterPixelsRemaining, counters, renderedQuads - quadsBefore, renderedTriangles - trianglesBefore, degenerateQuads - degenerateQuadsBefore, degenerateTriangles - degenerateTrianglesBefore);
            }

            offset += (int)payloadBytes;
            if (maxDraws is not null && totalDraws >= renderEndDraw)
            {
                break;
            }
        }

        result = new GxFifoCoverageDiagnosticResult(fullPath, drawsWritten, copiesSeen, rasterPixelsRemaining <= 0);
        return true;
    }

    private static void WriteDrawCoverageCsv(StreamWriter writer, int drawIndex, int fifoOffset, byte command, int format, uint vertexCount, bool decoded, bool allZeroXy, bool allBlackRgb, float minX, float maxX, float minY, float maxY, int copiesSeen, EfbCoverage before, EfbCoverage after, int rasterBefore, int rasterAfter, RasterDiagnosticCounters counters, int renderedQuadsDelta, int renderedTrianglesDelta, int degenerateQuadsDelta, int degenerateTrianglesDelta)
    {
        writer.WriteLine(string.Join(',',
            drawIndex.ToString(CultureInfo.InvariantCulture),
            $"+0x{fifoOffset:X}",
            PrimitiveName(command).Replace(",", ";", StringComparison.Ordinal),
            format.ToString(CultureInfo.InvariantCulture),
            vertexCount.ToString(CultureInfo.InvariantCulture),
            decoded ? "True" : "False",
            decoded && allZeroXy ? "True" : "False",
            decoded && allBlackRgb ? "True" : "False",
            decoded ? FormatFloat(minX) : string.Empty,
            decoded ? FormatFloat(maxX) : string.Empty,
            decoded ? FormatFloat(minY) : string.Empty,
            decoded ? FormatFloat(maxY) : string.Empty,
            copiesSeen.ToString(CultureInfo.InvariantCulture),
            before.NonBlackPixels.ToString(CultureInfo.InvariantCulture),
            after.NonBlackPixels.ToString(CultureInfo.InvariantCulture),
            (after.NonBlackPixels - before.NonBlackPixels).ToString(CultureInfo.InvariantCulture),
            before.AlphaVisibleNonBlackPixels.ToString(CultureInfo.InvariantCulture),
            after.AlphaVisibleNonBlackPixels.ToString(CultureInfo.InvariantCulture),
            (after.AlphaVisibleNonBlackPixels - before.AlphaVisibleNonBlackPixels).ToString(CultureInfo.InvariantCulture),
            FormatPercent(before),
            FormatPercent(after),
            rasterBefore.ToString(CultureInfo.InvariantCulture),
            rasterAfter.ToString(CultureInfo.InvariantCulture),
            (rasterBefore - rasterAfter).ToString(CultureInfo.InvariantCulture),
            counters.CoveredPixels.ToString(CultureInfo.InvariantCulture),
            counters.DepthRejected.ToString(CultureInfo.InvariantCulture),
            counters.AlphaRejected.ToString(CultureInfo.InvariantCulture),
            counters.ColorUpdateDisabled.ToString(CultureInfo.InvariantCulture),
            counters.ColorWrites.ToString(CultureInfo.InvariantCulture),
            counters.BlackColorWrites.ToString(CultureInfo.InvariantCulture),
            counters.AlphaUpdates.ToString(CultureInfo.InvariantCulture),
            counters.DepthUpdates.ToString(CultureInfo.InvariantCulture),
            renderedQuadsDelta.ToString(CultureInfo.InvariantCulture),
            renderedTrianglesDelta.ToString(CultureInfo.InvariantCulture),
            degenerateQuadsDelta.ToString(CultureInfo.InvariantCulture),
            degenerateTrianglesDelta.ToString(CultureInfo.InvariantCulture)));
    }

    public static bool TryWriteTevSampleDiagnostics(IReadOnlyList<MmioAccess> accesses, GameCubeMemory? memory, string path, int skipDraws, int maxDraws, out GxFifoTevSampleDiagnosticResult? result, out string? error)
    {
        ArgumentNullException.ThrowIfNull(accesses);

        result = null;
        error = null;
        if (skipDraws < 0)
        {
            error = "GX TEV sample skipped draw count must be non-negative.";
            return false;
        }

        if (maxDraws <= 0)
        {
            error = "GX TEV sample draw count must be positive.";
            return false;
        }

        byte[] fifo = accesses
            .Where(access => access.DeviceName == "GX FIFO" && access.Kind == MmioAccessKind.Write)
            .SelectMany(ExpandMmioWriteBytes)
            .ToArray();
        if (fifo.Length == 0)
        {
            error = "no GX FIFO writes were captured.";
            return false;
        }

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        GxVertexState state = new();
        int offset = 0;
        int totalDraws = 0;
        int copiesSeen = 0;
        int samplesWritten = 0;
        using StreamWriter writer = new(fullPath);
        writer.WriteLine("draw_index,fifo_offset,primitive,format,vertices,copies_seen,triangle_index,sample_name,sample_a_weight,sample_b_weight,sample_c_weight,sample_x,sample_y,raster_rgba,tev_evaluated,tev_rgba,alpha_test,color_update,alpha_update,stage0_mode,stage_summary");

        while (offset < fifo.Length)
        {
            int commandOffset = offset;
            byte command = fifo[offset++];
            if (command is 0x00 or 0x44 or 0x48)
            {
                continue;
            }

            if (command == 0x08 && TryReadBigEndian(fifo, offset, 1, out uint cpRegister) && TryReadBigEndian(fifo, offset + 1, 4, out uint cpValue))
            {
                state.WriteCpRegister((byte)cpRegister, cpValue);
                offset += 5;
                continue;
            }

            if (command == 0x10 && TryReadBigEndian(fifo, offset, 4, out uint xfHeader))
            {
                ushort xfRegister = (ushort)(xfHeader & 0xFFFF);
                uint wordCount = ((xfHeader >> 16) & 0xFFFF) + 1;
                int xfPayloadBytes = AvailablePayloadBytes(fifo, offset + 4, wordCount);
                state.WriteXfRegisters(fifo, offset + 4, xfRegister, xfPayloadBytes / sizeof(uint));
                offset += 4 + xfPayloadBytes;
                continue;
            }

            if (command is 0x20 or 0x28 or 0x30 or 0x38)
            {
                offset += Math.Min(4, fifo.Length - offset);
                continue;
            }

            if (command == 0x40)
            {
                offset += Math.Min(8, fifo.Length - offset);
                continue;
            }

            if (command == 0x61)
            {
                if (TryReadBigEndian(fifo, offset, 4, out uint bpValue))
                {
                    state.WriteBpRegister(bpValue);
                    if ((bpValue >> 24) == 0x52)
                    {
                        copiesSeen++;
                    }
                }

                offset += Math.Min(4, fifo.Length - offset);
                continue;
            }

            if ((command & 0x80) == 0 || !TryReadBigEndian(fifo, offset, 2, out uint vertexCount))
            {
                break;
            }

            int format = command & 7;
            if (!state.TryGetVertexStride(format, out int stride))
            {
                break;
            }

            offset += 2;
            long payloadBytes = (long)vertexCount * stride;
            if (payloadBytes > fifo.Length - offset)
            {
                break;
            }

            totalDraws++;
            long renderEndDraw = (long)skipDraws + maxDraws;
            bool inWindow = totalDraws > skipDraws && totalDraws <= renderEndDraw;
            if (inWindow && vertexCount <= int.MaxValue && state.IsRenderablePositionFormat(format))
            {
                int vertexOffset = offset;
                GxVertex[] vertices = new GxVertex[(int)vertexCount];
                bool decodedVertices = true;
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                {
                    if (!state.TryReadVertex(fifo, ref vertexOffset, format, memory, out vertices[vertex]))
                    {
                        decodedVertices = false;
                        break;
                    }
                }

                if (decodedVertices)
                {
                    foreach (DiagnosticTriangle triangle in SelectDiagnosticTriangles(command, vertices))
                    {
                        foreach (DiagnosticSamplePoint sample in DiagnosticSamplePoints)
                        {
                            float sampleX = triangle.A.X * sample.AWeight + triangle.B.X * sample.BWeight + triangle.C.X * sample.CWeight;
                            float sampleY = triangle.A.Y * sample.AWeight + triangle.B.Y * sample.BWeight + triangle.C.Y * sample.CWeight;
                            byte rasterR = InterpolateByte(triangle.A.R, triangle.B.R, triangle.C.R, sample.AWeight, sample.BWeight, sample.CWeight);
                            byte rasterG = InterpolateByte(triangle.A.G, triangle.B.G, triangle.C.G, sample.AWeight, sample.BWeight, sample.CWeight);
                            byte rasterB = InterpolateByte(triangle.A.B, triangle.B.B, triangle.C.B, sample.AWeight, sample.BWeight, sample.CWeight);
                            byte rasterA = InterpolateByte(triangle.A.A, triangle.B.A, triangle.C.A, sample.AWeight, sample.BWeight, sample.CWeight);
                            bool tevEvaluated = state.TryEvaluateTevStagesDetailed(memory, triangle.A, triangle.B, triangle.C, sample.AWeight, sample.BWeight, sample.CWeight, rasterR, rasterG, rasterB, rasterA, out byte tevR, out byte tevG, out byte tevB, out byte tevA, out string stageSummary);
                            if (!tevEvaluated)
                            {
                                tevR = rasterR;
                                tevG = rasterG;
                                tevB = rasterB;
                                tevA = rasterA;
                            }

                            writer.WriteLine(string.Join(',',
                                totalDraws.ToString(CultureInfo.InvariantCulture),
                                $"+0x{commandOffset:X}",
                                PrimitiveName(command).Replace(",", ";", StringComparison.Ordinal),
                                format.ToString(CultureInfo.InvariantCulture),
                                vertexCount.ToString(CultureInfo.InvariantCulture),
                                copiesSeen.ToString(CultureInfo.InvariantCulture),
                                triangle.Index.ToString(CultureInfo.InvariantCulture),
                                sample.Name,
                                FormatFloat(sample.AWeight),
                                FormatFloat(sample.BWeight),
                                FormatFloat(sample.CWeight),
                                FormatFloat(sampleX),
                                FormatFloat(sampleY),
                                FormatColor(rasterR, rasterG, rasterB, rasterA),
                                tevEvaluated ? "True" : "False",
                                FormatColor(tevR, tevG, tevB, tevA),
                                state.AlphaTestPasses(tevA) ? "True" : "False",
                                state.ColorUpdateEnabled ? "True" : "False",
                                state.AlphaUpdateEnabled ? "True" : "False",
                                state.Stage0Mode.ToString(),
                                CsvField(stageSummary)));
                            samplesWritten++;
                        }
                    }
                }
            }

            offset += (int)payloadBytes;
            if (totalDraws >= renderEndDraw)
            {
                break;
            }
        }

        result = new GxFifoTevSampleDiagnosticResult(fullPath, samplesWritten, totalDraws, copiesSeen);
        return true;
    }

    public static bool TryWriteTextureDiagnostics(IReadOnlyList<MmioAccess> accesses, GameCubeMemory? memory, string directoryPath, int skipDraws, int maxDraws, out GxFifoTextureDiagnosticResult? result, out string? error, GxMemorySnapshotSet? memorySnapshots = null)
    {
        ArgumentNullException.ThrowIfNull(accesses);

        result = null;
        error = null;
        if (memory is null)
        {
            error = "GX texture diagnostics require emulated memory.";
            return false;
        }

        if (skipDraws < 0)
        {
            error = "GX texture diagnostic skipped draw count must be non-negative.";
            return false;
        }

        if (maxDraws <= 0)
        {
            error = "GX texture diagnostic draw count must be positive.";
            return false;
        }

        byte[] fifo = accesses
            .Where(access => access.DeviceName == "GX FIFO" && access.Kind == MmioAccessKind.Write)
            .SelectMany(ExpandMmioWriteBytes)
            .ToArray();
        if (fifo.Length == 0)
        {
            error = "no GX FIFO writes were captured.";
            return false;
        }

        string fullDirectory = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(fullDirectory);
        string indexPath = Path.Combine(fullDirectory, "index.csv");

        GxVertexState state = new()
        {
            MemorySnapshots = memorySnapshots is { IsEmpty: false } ? memorySnapshots : null,
        };
        int offset = 0;
        int totalDraws = 0;
        int copiesSeen = 0;
        int texturesWritten = 0;
        using StreamWriter writer = new(indexPath);
        writer.WriteLine("draw_index,fifo_offset,slot,path,alpha_path,width,height,format,source_address,tmem_even,tmem_odd,wrap_s,wrap_t,mag_filter,min_filter,tlut,mode0,mode1,image0,image1,image2,image3,sample_source,source_bytes,source_hash,nonblack_pixels,nontransparent_pixels,transparent_pixels,min_alpha,max_alpha,samples");

        while (offset < fifo.Length)
        {
            int commandOffset = offset;
            byte command = fifo[offset++];
            if (command is 0x00 or 0x44 or 0x48)
            {
                continue;
            }

            if (command == 0x08 && TryReadBigEndian(fifo, offset, 1, out uint cpRegister) && TryReadBigEndian(fifo, offset + 1, 4, out uint cpValue))
            {
                state.WriteCpRegister((byte)cpRegister, cpValue);
                offset += 5;
                continue;
            }

            if (command == 0x10 && TryReadBigEndian(fifo, offset, 4, out uint xfHeader))
            {
                ushort xfRegister = (ushort)(xfHeader & 0xFFFF);
                uint wordCount = ((xfHeader >> 16) & 0xFFFF) + 1;
                int xfPayloadBytes = AvailablePayloadBytes(fifo, offset + 4, wordCount);
                state.WriteXfRegisters(fifo, offset + 4, xfRegister, xfPayloadBytes / sizeof(uint));
                offset += 4 + xfPayloadBytes;
                continue;
            }

            if (command is 0x20 or 0x28 or 0x30 or 0x38)
            {
                offset += Math.Min(4, fifo.Length - offset);
                continue;
            }

            if (command == 0x40)
            {
                offset += Math.Min(8, fifo.Length - offset);
                continue;
            }

            if (command == 0x61)
            {
                if (TryReadBigEndian(fifo, offset, 4, out uint bpValue))
                {
                    state.WriteBpRegister(bpValue);
                    if ((bpValue >> 24) == 0x52)
                    {
                        copiesSeen++;
                    }
                }

                offset += Math.Min(4, fifo.Length - offset);
                continue;
            }

            if ((command & 0x80) == 0 || !TryReadBigEndian(fifo, offset, 2, out uint vertexCount))
            {
                break;
            }

            int format = command & 7;
            if (!state.TryGetVertexStride(format, out int stride))
            {
                break;
            }

            offset += 2;
            long payloadBytes = (long)vertexCount * stride;
            if (payloadBytes > fifo.Length - offset)
            {
                break;
            }

            totalDraws++;
            state.CurrentFifoOffset = commandOffset;
            long renderEndDraw = (long)skipDraws + maxDraws;
            bool inWindow = totalDraws > skipDraws && totalDraws <= renderEndDraw;
            if (inWindow)
            {
                for (int slot = 0; slot < 8; slot++)
                {
                    if (!state.TryDumpTextureMap(memory, slot, out TextureDumpSnapshot snapshot, out byte[]? rgb, out byte[]? alphaRgb, out _)
                        || rgb is null)
                    {
                        continue;
                    }

                    string fileName = $"draw{totalDraws:D5}_tex{slot}_{snapshot.FormatName}_{snapshot.Width}x{snapshot.Height}.png";
                    string alphaFileName = $"draw{totalDraws:D5}_tex{slot}_{snapshot.FormatName}_{snapshot.Width}x{snapshot.Height}_alpha.png";
                    string filePath = Path.Combine(fullDirectory, fileName);
                    FramebufferDumper.WriteRgbPng(filePath, snapshot.Width, snapshot.Height, rgb);
                    if (alphaRgb is not null)
                    {
                        FramebufferDumper.WriteRgbPng(Path.Combine(fullDirectory, alphaFileName), snapshot.Width, snapshot.Height, alphaRgb);
                    }

                    writer.WriteLine(string.Join(',',
                        totalDraws.ToString(CultureInfo.InvariantCulture),
                        $"+0x{commandOffset:X}",
                        slot.ToString(CultureInfo.InvariantCulture),
                        CsvField(fileName),
                        alphaRgb is null ? string.Empty : CsvField(alphaFileName),
                        snapshot.Width.ToString(CultureInfo.InvariantCulture),
                        snapshot.Height.ToString(CultureInfo.InvariantCulture),
                        snapshot.FormatName,
                        $"0x{snapshot.SourceAddress:X8}",
                        $"0x{snapshot.TmemEven:X6}",
                        $"0x{snapshot.TmemOdd:X6}",
                        snapshot.WrapS,
                        snapshot.WrapT,
                        snapshot.MagFilter,
                        snapshot.MinFilter,
                        CsvField(snapshot.Tlut),
                        $"0x{snapshot.Mode0:X6}",
                        $"0x{snapshot.Mode1:X6}",
                        $"0x{snapshot.Image0:X6}",
                        $"0x{snapshot.Image1:X6}",
                        $"0x{snapshot.Image2:X6}",
                        $"0x{snapshot.Image3:X6}",
                        snapshot.SampleSource,
                        snapshot.SourceByteLength.ToString(CultureInfo.InvariantCulture),
                        $"0x{snapshot.SourceHash:X8}",
                        snapshot.NonBlackPixels.ToString(CultureInfo.InvariantCulture),
                        snapshot.NonTransparentPixels.ToString(CultureInfo.InvariantCulture),
                        snapshot.TransparentPixels.ToString(CultureInfo.InvariantCulture),
                        snapshot.MinAlpha.ToString(CultureInfo.InvariantCulture),
                        snapshot.MaxAlpha.ToString(CultureInfo.InvariantCulture),
                        CsvField(snapshot.Samples)));
                    texturesWritten++;
                }
            }

            offset += (int)payloadBytes;
            if (totalDraws >= renderEndDraw)
            {
                break;
            }
        }

        result = new GxFifoTextureDiagnosticResult(fullDirectory, indexPath, texturesWritten, totalDraws, copiesSeen);
        return true;
    }

    private static readonly DiagnosticSamplePoint[] DiagnosticSamplePoints =
    [
        new("centroid", 1f / 3f, 1f / 3f, 1f / 3f),
        new("near_a", 0.6f, 0.2f, 0.2f),
        new("near_b", 0.2f, 0.6f, 0.2f),
        new("near_c", 0.2f, 0.2f, 0.6f),
    ];

    private static IReadOnlyList<DiagnosticTriangle> SelectDiagnosticTriangles(byte command, GxVertex[] vertices)
    {
        if (vertices.Length < 3)
        {
            return [];
        }

        List<DiagnosticTriangle> triangles = [];
        switch (command & 0xF8)
        {
            case 0x80:
                for (int vertex = 0; vertex + 3 < vertices.Length; vertex += 4)
                {
                    triangles.Add(new DiagnosticTriangle(triangles.Count, vertices[vertex], vertices[vertex + 1], vertices[vertex + 2]));
                    triangles.Add(new DiagnosticTriangle(triangles.Count, vertices[vertex], vertices[vertex + 2], vertices[vertex + 3]));
                }

                break;
            case 0x90:
                for (int vertex = 0; vertex + 2 < vertices.Length; vertex += 3)
                {
                    triangles.Add(new DiagnosticTriangle(triangles.Count, vertices[vertex], vertices[vertex + 1], vertices[vertex + 2]));
                }

                break;
            case 0x98:
                for (int vertex = 0; vertex + 2 < vertices.Length; vertex++)
                {
                    triangles.Add(new DiagnosticTriangle(triangles.Count, vertices[vertex], vertices[vertex + 1], vertices[vertex + 2]));
                }

                break;
            case 0xA0:
                for (int vertex = 2; vertex < vertices.Length; vertex++)
                {
                    triangles.Add(new DiagnosticTriangle(triangles.Count, vertices[0], vertices[vertex - 1], vertices[vertex]));
                }

                break;
        }

        return SelectRepresentativeTriangles(triangles);
    }

    private static IReadOnlyList<DiagnosticTriangle> SelectRepresentativeTriangles(IReadOnlyList<DiagnosticTriangle> triangles)
    {
        if (triangles.Count <= 3)
        {
            return triangles;
        }

        int middle = triangles.Count / 2;
        return [triangles[0], triangles[middle], triangles[^1]];
    }

    private readonly record struct DiagnosticTriangle(int Index, GxVertex A, GxVertex B, GxVertex C);

    private readonly record struct DiagnosticSamplePoint(string Name, float AWeight, float BWeight, float CWeight);

    private static string FormatColor(byte r, byte g, byte b, byte a) => $"{r}/{g}/{b}/{a}";

    private static string CsvField(string text)
    {
        if (text.Contains('"', StringComparison.Ordinal))
        {
            text = text.Replace("\"", "\"\"", StringComparison.Ordinal);
        }

        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{text}\""
            : text;
    }

    private static bool TryAnalyzeDraw(byte[] fifo, int payloadOffset, int format, uint vertexCount, GxVertexState state, GameCubeMemory? memory, out int decodedVertices, out bool allZeroXy, out bool allBlackRgb, out float minX, out float maxX, out float minY, out float maxY)
    {
        decodedVertices = 0;
        allZeroXy = true;
        allBlackRgb = true;
        minX = float.PositiveInfinity;
        maxX = float.NegativeInfinity;
        minY = float.PositiveInfinity;
        maxY = float.NegativeInfinity;
        if (vertexCount > int.MaxValue)
        {
            return false;
        }

        int offset = payloadOffset;
        for (int vertexIndex = 0; vertexIndex < (int)vertexCount; vertexIndex++)
        {
            if (!state.TryReadVertex(fifo, ref offset, format, memory, out GxVertex vertex))
            {
                return false;
            }

            decodedVertices++;
            allZeroXy &= vertex.X == 0 && vertex.Y == 0;
            allBlackRgb &= vertex.R == 0 && vertex.G == 0 && vertex.B == 0;
            minX = MathF.Min(minX, vertex.X);
            maxX = MathF.Max(maxX, vertex.X);
            minY = MathF.Min(minY, vertex.Y);
            maxY = MathF.Max(maxY, vertex.Y);
        }

        return decodedVertices > 0;
    }

    private static void WriteDrawDiagnostic(StreamWriter writer, byte[] fifo, int commandOffset, int payloadOffset, byte command, int format, uint vertexCount, int stride, GxVertexState state, GameCubeMemory? memory, string? reason)
    {
        writer.WriteLine($"Draw #{commandOffset:X}");
        if (!string.IsNullOrEmpty(reason))
        {
            writer.WriteLine($"  Reason: {reason}");
        }

        writer.WriteLine($"  Offset: +0x{commandOffset:X}");
        writer.WriteLine($"  Command: 0x{command:X2} {PrimitiveName(command)}, fmt={format}, vertices={vertexCount}, stride={stride}, payload=0x{(long)vertexCount * stride:X}");
        writer.WriteLine($"  {state.DescribeFormat(format)}");
        foreach (string arrayLine in state.DescribeArrays())
        {
            writer.WriteLine($"  {arrayLine}");
        }

        foreach (string stateLine in state.DescribePipelineState())
        {
            writer.WriteLine($"  {stateLine}");
        }

        writer.WriteLine("  Raw draw bytes:");
        WriteHexPreview(writer, fifo, commandOffset, Math.Min(160, fifo.Length - commandOffset), indent: "    ");

        int decodeOffset = payloadOffset;
        int verticesToDump = checked((int)Math.Min(vertexCount, 8u));
        GxVertex[] decoded = new GxVertex[verticesToDump];
        bool decodedAll = true;
        writer.WriteLine("  Decoded vertices:");
        for (int vertex = 0; vertex < verticesToDump; vertex++)
        {
            int vertexOffset = decodeOffset;
            if (!state.TryReadVertex(fifo, ref decodeOffset, format, memory, out decoded[vertex]))
            {
                writer.WriteLine($"    v{vertex}: decode failed at +0x{vertexOffset:X}");
                decodedAll = false;
                break;
            }

            string tex0 = decoded[vertex].HasTex0
                ? $" tex0=({FormatFloat(decoded[vertex].Tex0S)}, {FormatFloat(decoded[vertex].Tex0T)})"
                : string.Empty;
            string rawTex0 = decoded[vertex].HasRawTex0 && (!decoded[vertex].HasTex0 || decoded[vertex].RawTex0S != decoded[vertex].Tex0S || decoded[vertex].RawTex0T != decoded[vertex].Tex0T)
                ? $" raw-tex0=({FormatFloat(decoded[vertex].RawTex0S)}, {FormatFloat(decoded[vertex].RawTex0T)})"
                : string.Empty;
            string extraTexCoords = DescribeExtraTexCoords(decoded[vertex]);
            string clip = decoded[vertex].ClipRejected ? " clipped" : string.Empty;
            writer.WriteLine($"    v{vertex}: pos=({FormatFloat(decoded[vertex].X)}, {FormatFloat(decoded[vertex].Y)}) color=({decoded[vertex].R},{decoded[vertex].G},{decoded[vertex].B}){tex0}{extraTexCoords}{rawTex0} alpha={decoded[vertex].A}{clip} raw={FormatHexInline(fifo, vertexOffset, Math.Min(stride, 48))}");
        }

        if (decodedAll && verticesToDump > 0)
        {
            float minX = decoded.Min(vertex => vertex.X);
            float maxX = decoded.Max(vertex => vertex.X);
            float minY = decoded.Min(vertex => vertex.Y);
            float maxY = decoded.Max(vertex => vertex.Y);
            bool allPositionsZero = decoded.All(vertex => vertex.X == 0 && vertex.Y == 0);
            int clippedVertices = decoded.Count(vertex => vertex.ClipRejected);
            writer.WriteLine($"  Decoded bounds: x=[{FormatFloat(minX)}, {FormatFloat(maxX)}] y=[{FormatFloat(minY)}, {FormatFloat(maxY)}] all-zero-xy={allPositionsZero} clipped={clippedVertices}");
        }

        writer.WriteLine();
    }

    private sealed class RasterDiagnosticCounters
    {
        public int CoveredPixels { get; private set; }
        public int DepthRejected { get; private set; }
        public int AlphaRejected { get; private set; }
        public int ColorUpdateDisabled { get; private set; }
        public int ColorWrites { get; private set; }
        public int BlackColorWrites { get; private set; }
        public int AlphaUpdates { get; private set; }
        public int DepthUpdates { get; private set; }

        public void RecordCoveredPixel() => CoveredPixels++;

        public void RecordDepthRejected() => DepthRejected++;

        public void RecordAlphaRejected() => AlphaRejected++;

        public void RecordColorUpdateDisabled() => ColorUpdateDisabled++;

        public void RecordColorWrite(byte r, byte g, byte b)
        {
            ColorWrites++;
            if (r == 0 && g == 0 && b == 0)
            {
                BlackColorWrites++;
            }
        }

        public void RecordAlphaUpdate() => AlphaUpdates++;

        public void RecordDepthUpdate() => DepthUpdates++;
    }

    private sealed class RasterTimingAccumulator
    {
        public long SetupTicks;
        public long CoverageTicks;
        public long DepthTestTicks;
        public long TevTextureTicks;
        public long AlphaTestTicks;
        public long BlendWriteTicks;
    }

    private static void RenderPrimitiveBounds(
        byte[] rgb,
        byte[] alpha,
        float[] depth,
        int width,
        int height,
        byte command,
        GxVertex[] vertices,
        GxVertexState state,
        GameCubeMemory? memory,
        ref int renderedQuads,
        ref int degenerateQuads,
        ref int renderedTriangles,
        ref int degenerateTriangles,
        ref int rasterPixelsRemaining,
        RasterDiagnosticCounters? counters,
        RasterTimingAccumulator? timings = null)
    {
        switch (command & 0xF8)
        {
            case 0x80:
                for (int vertex = 0; vertex + 3 < vertices.Length; vertex += 4)
                {
                    if (FillQuadBounds(rgb, alpha, depth, width, height, vertices[vertex], vertices[vertex + 1], vertices[vertex + 2], vertices[vertex + 3], state, memory, ref rasterPixelsRemaining, counters, timings))
                    {
                        renderedQuads++;
                    }
                    else
                    {
                        degenerateQuads++;
                    }
                }

                break;
            case 0x90:
                for (int vertex = 0; vertex + 2 < vertices.Length; vertex += 3)
                {
                    FillTriangleOrCount(rgb, alpha, depth, width, height, vertices[vertex], vertices[vertex + 1], vertices[vertex + 2], state, memory, ref renderedTriangles, ref degenerateTriangles, ref rasterPixelsRemaining, counters, timings);
                }

                break;
            case 0x98:
                for (int vertex = 0; vertex + 2 < vertices.Length; vertex++)
                {
                    if ((vertex & 1) == 0)
                    {
                        FillTriangleOrCount(rgb, alpha, depth, width, height, vertices[vertex], vertices[vertex + 1], vertices[vertex + 2], state, memory, ref renderedTriangles, ref degenerateTriangles, ref rasterPixelsRemaining, counters, timings);
                    }
                    else
                    {
                        FillTriangleOrCount(rgb, alpha, depth, width, height, vertices[vertex + 1], vertices[vertex], vertices[vertex + 2], state, memory, ref renderedTriangles, ref degenerateTriangles, ref rasterPixelsRemaining, counters, timings);
                    }
                }

                break;
            case 0xA0:
                for (int vertex = 1; vertex + 1 < vertices.Length; vertex++)
                {
                    FillTriangleOrCount(rgb, alpha, depth, width, height, vertices[0], vertices[vertex], vertices[vertex + 1], state, memory, ref renderedTriangles, ref degenerateTriangles, ref rasterPixelsRemaining, counters, timings);
                }

                break;
        }
    }

    private static void FillTriangleOrCount(byte[] rgb, byte[] alpha, float[] depth, int width, int height, GxVertex a, GxVertex b, GxVertex c, GxVertexState state, GameCubeMemory? memory, ref int renderedTriangles, ref int degenerateTriangles, ref int rasterPixelsRemaining, RasterDiagnosticCounters? counters, RasterTimingAccumulator? timings)
    {
        if (FillTriangleBounds(rgb, alpha, depth, width, height, a, b, c, state, memory, ref rasterPixelsRemaining, counters, timings))
        {
            renderedTriangles++;
        }
        else
        {
            degenerateTriangles++;
        }
    }

    private static bool FillQuadBounds(byte[] rgb, byte[] alpha, float[] depth, int width, int height, GxVertex a, GxVertex b, GxVertex c, GxVertex d, GxVertexState state, GameCubeMemory? memory, ref int rasterPixelsRemaining, RasterDiagnosticCounters? counters, RasterTimingAccumulator? timings)
    {
        bool first = FillTriangleBounds(rgb, alpha, depth, width, height, a, b, c, state, memory, ref rasterPixelsRemaining, counters, timings);
        bool second = FillTriangleBounds(rgb, alpha, depth, width, height, a, c, d, state, memory, ref rasterPixelsRemaining, counters, timings);
        return first || second;
    }

    private static bool FillTriangleBounds(byte[] rgb, byte[] alpha, float[] depth, int width, int height, GxVertex a, GxVertex b, GxVertex c, GxVertexState state, GameCubeMemory? memory, ref int rasterPixelsRemaining, RasterDiagnosticCounters? counters, RasterTimingAccumulator? timings)
    {
        long setupStart = timings is null ? 0 : Stopwatch.GetTimestamp();
        if (rasterPixelsRemaining <= 0)
        {
            if (timings is not null)
            {
                timings.SetupTicks += Stopwatch.GetTimestamp() - setupStart;
            }

            return false;
        }

        if (a.ClipRejected || b.ClipRejected || c.ClipRejected)
        {
            if (timings is not null)
            {
                timings.SetupTicks += Stopwatch.GetTimestamp() - setupStart;
            }

            return FillNearClippedTriangleBounds(rgb, alpha, depth, width, height, a, b, c, state, memory, ref rasterPixelsRemaining, counters, timings);
        }

        float minX = MathF.Min(MathF.Min(a.X, b.X), c.X);
        float maxX = MathF.Max(MathF.Max(a.X, b.X), c.X);
        float minY = MathF.Min(MathF.Min(a.Y, b.Y), c.Y);
        float maxY = MathF.Max(MathF.Max(a.Y, b.Y), c.Y);

        if (!float.IsFinite(minX) || !float.IsFinite(maxX) || !float.IsFinite(minY) || !float.IsFinite(maxY))
        {
            if (timings is not null)
            {
                timings.SetupTicks += Stopwatch.GetTimestamp() - setupStart;
            }

            return false;
        }

        if (maxX <= minX || maxY <= minY)
        {
            if (timings is not null)
            {
                timings.SetupTicks += Stopwatch.GetTimestamp() - setupStart;
            }

            return false;
        }

        RenderCoordinateTransform transform = RenderCoordinateTransform.FromBounds(minX, maxX, minY, maxY, width, height);
        int left = transform.ToScreenX(minX, width);
        int right = transform.ToScreenX(maxX, width);
        int top = transform.ToScreenY(minY, height);
        int bottom = transform.ToScreenY(maxY, height);
        if (right < left)
        {
            (left, right) = (right, left);
        }

        if (bottom < top)
        {
            (top, bottom) = (bottom, top);
        }

        left = Math.Clamp(left, 0, width - 1);
        right = Math.Clamp(right, 0, width - 1);
        top = Math.Clamp(top, 0, height - 1);
        bottom = Math.Clamp(bottom, 0, height - 1);
        if (state.TryGetScissor(width, height, out int scissorLeft, out int scissorTop, out int scissorRight, out int scissorBottom))
        {
            left = Math.Max(left, scissorLeft);
            right = Math.Min(right, scissorRight);
            top = Math.Max(top, scissorTop);
            bottom = Math.Min(bottom, scissorBottom);
        }

        if (right <= left || bottom <= top)
        {
            if (timings is not null)
            {
                timings.SetupTicks += Stopwatch.GetTimestamp() - setupStart;
            }

            return false;
        }

        float ax = transform.ToScreenX(a.X, width);
        float ay = transform.ToScreenY(a.Y, height);
        float bx = transform.ToScreenX(b.X, width);
        float by = transform.ToScreenY(b.Y, height);
        float cx = transform.ToScreenX(c.X, width);
        float cy = transform.ToScreenY(c.Y, height);
        float area = Edge(ax, ay, bx, by, cx, cy);
        if (MathF.Abs(area) < 0.0001f)
        {
            if (timings is not null)
            {
                timings.SetupTicks += Stopwatch.GetTimestamp() - setupStart;
            }

            return false;
        }

        float inverseArea = 1f / area;
        float w0StepX = cy - by;
        float w1StepX = ay - cy;
        float w2StepX = by - ay;
        int textureMap = state.Stage0TextureMap;
        int texCoord = state.Stage0TexCoord;
        float aTexS = 0;
        float aTexT = 0;
        float bTexS = 0;
        float bTexT = 0;
        float cTexS = 0;
        float cTexT = 0;
        bool canSampleTexture = textureMap is >= 0 and <= 7
            && texCoord is >= 0 and <= 7
            && a.TryGetTexCoord(texCoord, out aTexS, out aTexT)
            && b.TryGetTexCoord(texCoord, out bTexS, out bTexT)
            && c.TryGetTexCoord(texCoord, out cTexS, out cTexT);
        GxTevStage0Mode stage0Mode = state.Stage0Mode;
        if (timings is not null)
        {
            timings.SetupTicks += Stopwatch.GetTimestamp() - setupStart;
        }

        bool wrotePixel = false;
        for (int y = top; y <= bottom; y++)
        {
            int row = y * width * 3;
            int depthRow = y * width;
            float pxStart = left + 0.5f;
            float py = y + 0.5f;
            float w0 = Edge(bx, by, cx, cy, pxStart, py);
            float w1 = Edge(cx, cy, ax, ay, pxStart, py);
            float w2 = Edge(ax, ay, bx, by, pxStart, py);
            for (int x = left; x <= right; x++)
            {
                if (rasterPixelsRemaining <= 0)
                {
                    return wrotePixel;
                }

                rasterPixelsRemaining--;
                long coverageStart = timings is null ? 0 : Stopwatch.GetTimestamp();
                if (!InsideTriangle(w0, w1, w2, area))
                {
                    if (timings is not null)
                    {
                        timings.CoverageTicks += Stopwatch.GetTimestamp() - coverageStart;
                    }

                    w0 += w0StepX;
                    w1 += w1StepX;
                    w2 += w2StepX;
                    continue;
                }

                counters?.RecordCoveredPixel();
                int index = row + x * 3;
                float aWeight = w0 * inverseArea;
                float bWeight = w1 * inverseArea;
                float cWeight = w2 * inverseArea;
                if (timings is not null)
                {
                    timings.CoverageTicks += Stopwatch.GetTimestamp() - coverageStart;
                }

                long depthTestStart = timings is null ? 0 : Stopwatch.GetTimestamp();
                float z = a.Z * aWeight + b.Z * bWeight + c.Z * cWeight;
                int depthIndex = depthRow + x;
                if (!state.DepthTestPasses(z, depth[depthIndex]))
                {
                    if (timings is not null)
                    {
                        timings.DepthTestTicks += Stopwatch.GetTimestamp() - depthTestStart;
                    }

                    counters?.RecordDepthRejected();
                    w0 += w0StepX;
                    w1 += w1StepX;
                    w2 += w2StepX;
                    continue;
                }

                if (timings is not null)
                {
                    timings.DepthTestTicks += Stopwatch.GetTimestamp() - depthTestStart;
                }

                long tevTextureStart = timings is null ? 0 : Stopwatch.GetTimestamp();
                byte r = InterpolateByte(a.R, b.R, c.R, aWeight, bWeight, cWeight);
                byte g = InterpolateByte(a.G, b.G, c.G, aWeight, bWeight, cWeight);
                byte bValue = InterpolateByte(a.B, b.B, c.B, aWeight, bWeight, cWeight);
                byte sourceAlpha = InterpolateByte(a.A, b.A, c.A, aWeight, bWeight, cWeight);
                byte rasterR = r;
                byte rasterG = g;
                byte rasterB = bValue;
                byte rasterA = sourceAlpha;
                if (!state.TryEvaluateTevStages(memory, a, b, c, aWeight, bWeight, cWeight, rasterR, rasterG, rasterB, rasterA, out r, out g, out bValue, out sourceAlpha)
                    && canSampleTexture)
                {
                    float perspectiveWeight = a.InvW * aWeight + b.InvW * bWeight + c.InvW * cWeight;
                    float s = aTexS * aWeight + bTexS * bWeight + cTexS * cWeight;
                    float t = aTexT * aWeight + bTexT * bWeight + cTexT * cWeight;
                    if (MathF.Abs(perspectiveWeight) > 0.000001f)
                    {
                        s = (aTexS * a.InvW * aWeight + bTexS * b.InvW * bWeight + cTexS * c.InvW * cWeight) / perspectiveWeight;
                        t = (aTexT * a.InvW * aWeight + bTexT * b.InvW * bWeight + cTexT * c.InvW * cWeight) / perspectiveWeight;
                    }

                    if (state.TrySampleTexture(memory, textureMap, s, t, out byte sampledR, out byte sampledG, out byte sampledB, out byte sampledA))
                    {
                        ApplyStage0Mode(stage0Mode, ref r, ref g, ref bValue, ref sourceAlpha, sampledR, sampledG, sampledB, sampledA);
                    }
                }

                if (timings is not null)
                {
                    timings.TevTextureTicks += Stopwatch.GetTimestamp() - tevTextureStart;
                }

                long alphaTestStart = timings is null ? 0 : Stopwatch.GetTimestamp();
                if (!state.AlphaTestPasses(sourceAlpha))
                {
                    if (timings is not null)
                    {
                        timings.AlphaTestTicks += Stopwatch.GetTimestamp() - alphaTestStart;
                    }

                    counters?.RecordAlphaRejected();
                    w0 += w0StepX;
                    w1 += w1StepX;
                    w2 += w2StepX;
                    continue;
                }

                if (timings is not null)
                {
                    timings.AlphaTestTicks += Stopwatch.GetTimestamp() - alphaTestStart;
                }

                long blendWriteStart = timings is null ? 0 : Stopwatch.GetTimestamp();
                if (state.ColorUpdateEnabled)
                {
                    state.ApplyBlend(rgb, alpha, index, depthIndex, ref r, ref g, ref bValue, ref sourceAlpha);
                    rgb[index] = r;
                    rgb[index + 1] = g;
                    rgb[index + 2] = bValue;
                    counters?.RecordColorWrite(r, g, bValue);
                }
                else
                {
                    counters?.RecordColorUpdateDisabled();
                }

                if (state.AlphaUpdateEnabled)
                {
                    alpha[depthIndex] = sourceAlpha;
                    counters?.RecordAlphaUpdate();
                }

                if (state.DepthUpdateEnabled && float.IsFinite(z))
                {
                    depth[depthIndex] = z;
                    counters?.RecordDepthUpdate();
                }

                wrotePixel = true;
                if (timings is not null)
                {
                    timings.BlendWriteTicks += Stopwatch.GetTimestamp() - blendWriteStart;
                }

                w0 += w0StepX;
                w1 += w1StepX;
                w2 += w2StepX;
            }
        }

        return wrotePixel;
    }

    private static bool FillNearClippedTriangleBounds(byte[] rgb, byte[] alpha, float[] depth, int width, int height, GxVertex a, GxVertex b, GxVertex c, GxVertexState state, GameCubeMemory? memory, ref int rasterPixelsRemaining, RasterDiagnosticCounters? counters, RasterTimingAccumulator? timings)
    {
        if (!a.HasViewPosition || !b.HasViewPosition || !c.HasViewPosition)
        {
            return false;
        }

        List<GxVertex> clipped = ClipTriangleToNearPlane([a, b, c], state);
        return clipped.Count switch
        {
            3 => FillTriangleBounds(rgb, alpha, depth, width, height, clipped[0], clipped[1], clipped[2], state, memory, ref rasterPixelsRemaining, counters, timings),
            4 => FillTriangleBounds(rgb, alpha, depth, width, height, clipped[0], clipped[1], clipped[2], state, memory, ref rasterPixelsRemaining, counters, timings)
                | FillTriangleBounds(rgb, alpha, depth, width, height, clipped[0], clipped[2], clipped[3], state, memory, ref rasterPixelsRemaining, counters, timings),
            _ => false,
        };
    }

    private static List<GxVertex> ClipTriangleToNearPlane(IReadOnlyList<GxVertex> vertices, GxVertexState state)
    {
        List<GxVertex> output = [];
        for (int index = 0; index < vertices.Count; index++)
        {
            GxVertex current = vertices[index];
            GxVertex next = vertices[(index + 1) % vertices.Count];
            bool currentInside = IsInsideNearPlane(current);
            bool nextInside = IsInsideNearPlane(next);

            if (currentInside && nextInside)
            {
                output.Add(next);
            }
            else if (currentInside && !nextInside)
            {
                if (TryInterpolateNearClipVertex(current, next, state, out GxVertex intersection))
                {
                    output.Add(intersection);
                }
            }
            else if (!currentInside && nextInside)
            {
                if (TryInterpolateNearClipVertex(current, next, state, out GxVertex intersection))
                {
                    output.Add(intersection);
                }

                output.Add(next);
            }
        }

        return output;
    }

    private static bool IsInsideNearPlane(GxVertex vertex) =>
        vertex.HasViewPosition && -vertex.ViewZ > NearClipW;

    private static bool TryInterpolateNearClipVertex(GxVertex a, GxVertex b, GxVertexState state, out GxVertex vertex)
    {
        vertex = default;
        float aW = -a.ViewZ;
        float bW = -b.ViewZ;
        float denominator = bW - aW;
        if (MathF.Abs(denominator) < 0.000001f)
        {
            return false;
        }

        float targetW = NearClipW * 1.01f;
        float t = Math.Clamp((targetW - aW) / denominator, 0f, 1f);
        float viewX = Lerp(a.ViewX, b.ViewX, t);
        float viewY = Lerp(a.ViewY, b.ViewY, t);
        float viewZ = Lerp(a.ViewZ, b.ViewZ, t);
        if (!state.TryProjectViewPosition(viewX, viewY, viewZ, out float screenX, out float screenY, out float screenZ, out float invW))
        {
            return false;
        }

        bool hasTex0 = a.HasTex0 && b.HasTex0;
        bool hasRawTex0 = a.HasRawTex0 && b.HasRawTex0;
        vertex = new GxVertex(
            screenX,
            screenY,
            screenZ,
            invW,
            InterpolateByte(a.R, b.R, t),
            InterpolateByte(a.G, b.G, t),
            InterpolateByte(a.B, b.B, t),
            InterpolateByte(a.A, b.A, t),
            hasTex0,
            hasTex0 ? Lerp(a.Tex0S, b.Tex0S, t) : 0f,
            hasTex0 ? Lerp(a.Tex0T, b.Tex0T, t) : 0f,
            hasRawTex0,
            hasRawTex0 ? Lerp(a.RawTex0S, b.RawTex0S, t) : 0f,
            hasRawTex0 ? Lerp(a.RawTex0T, b.RawTex0T, t) : 0f,
            ClipRejected: false,
            HasViewPosition: true,
            ViewX: viewX,
            ViewY: viewY,
            ViewZ: viewZ,
            TexCoords: GxTexCoordSet.Interpolate(a.TexCoords, b.TexCoords, t));
        return true;
    }

    private static float Edge(float ax, float ay, float bx, float by, float px, float py) =>
        (px - ax) * (by - ay) - (py - ay) * (bx - ax);

    private static bool InsideTriangle(float w0, float w1, float w2, float area) =>
        area > 0
            ? w0 >= 0 && w1 >= 0 && w2 >= 0
            : w0 <= 0 && w1 <= 0 && w2 <= 0;

    private static byte InterpolateByte(byte a, byte b, byte c, float aWeight, float bWeight, float cWeight) =>
        (byte)Math.Clamp((int)MathF.Round(a * aWeight + b * bWeight + c * cWeight), 0, 255);

    private static byte InterpolateByte(byte a, byte b, float t) =>
        (byte)Math.Clamp((int)MathF.Round(Lerp(a, b, t)), 0, 255);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static void ApplyStage0Mode(GxTevStage0Mode mode, ref byte r, ref byte g, ref byte b, ref byte a, byte textureR, byte textureG, byte textureB, byte textureA)
    {
        switch (mode)
        {
            case GxTevStage0Mode.PassColor:
                return;
            case GxTevStage0Mode.Blend:
                r = BlendTevBlend(r, textureR);
                g = BlendTevBlend(g, textureG);
                b = BlendTevBlend(b, textureB);
                a = MultiplyColor(a, textureA);
                return;
            case GxTevStage0Mode.Modulate:
                r = MultiplyColor(r, textureR);
                g = MultiplyColor(g, textureG);
                b = MultiplyColor(b, textureB);
                a = MultiplyColor(a, textureA);
                return;
            case GxTevStage0Mode.Decal:
                r = BlendTextureOverRaster(r, textureR, textureA);
                g = BlendTextureOverRaster(g, textureG, textureA);
                b = BlendTextureOverRaster(b, textureB, textureA);
                return;
            case GxTevStage0Mode.Replace:
            default:
                r = textureR;
                g = textureG;
                b = textureB;
                a = textureA;
                return;
        }
    }

    private static byte BlendTextureOverRaster(byte raster, byte texture, byte textureAlpha) =>
        (byte)((raster * (255 - textureAlpha) + texture * textureAlpha + 127) / 255);

    private static byte BlendTevBlend(byte raster, byte texture) =>
        (byte)Math.Clamp((raster * (255 - texture) + 127) / 255 + texture, 0, 255);

    private static byte MultiplyColor(byte a, byte b) => (byte)((a * b + 127) / 255);

    private static int ToScreenX(float x, int width) =>
        x is >= -1f and <= 1f ? (int)MathF.Round((x * 0.5f + 0.5f) * (width - 1)) : (int)MathF.Round(x);

    private static int ToScreenY(float y, int height) =>
        y is >= -1f and <= 1f ? (int)MathF.Round((0.5f - y * 0.5f) * (height - 1)) : (int)MathF.Round(y);

    private readonly record struct RenderCoordinateTransform(float OffsetX, float OffsetY)
    {
        public static RenderCoordinateTransform FromBounds(float minX, float maxX, float minY, float maxY, int width, int height)
        {
            float offsetX = maxX <= 1f && minX < -1f ? width : 0f;
            float offsetY = maxY <= 1f && minY < -1f ? height : 0f;
            return new RenderCoordinateTransform(offsetX, offsetY);
        }

        public int ToScreenX(float x, int width) => OffsetX != 0f
            ? (int)MathF.Round(x + OffsetX)
            : GxFifoSoftwareRenderer.ToScreenX(x, width);

        public int ToScreenY(float y, int height) => OffsetY != 0f
            ? (int)MathF.Round(y + OffsetY)
            : GxFifoSoftwareRenderer.ToScreenY(y, height);
    }

    private static string PrimitiveName(byte command) =>
        (command & 0xF8) switch
        {
            0x80 => "quads",
            0x90 => "triangles",
            0x98 => "triangle-strip",
            0xA0 => "triangle-fan",
            0xA8 => "lines",
            0xB0 => "line-strip",
            0xB8 => "points",
            _ => $"primitive-0x{command & 0xF8:X2}",
        };

    private static string DescribeExtraTexCoords(GxVertex vertex)
    {
        StringBuilder builder = new();
        for (int index = 1; index < 8; index++)
        {
            if (vertex.TryGetTexCoord(index, out float s, out float t))
            {
                builder.Append(CultureInfo.InvariantCulture, $" tex{index}=({FormatFloat(s)}, {FormatFloat(t)})");
            }
        }

        return builder.ToString();
    }

    private static string FormatFloat(float value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatHexInline(byte[] bytes, int offset, int length)
    {
        if (offset < 0 || length <= 0 || offset >= bytes.Length)
        {
            return string.Empty;
        }

        int count = Math.Min(length, bytes.Length - offset);
        return string.Join(" ", bytes.Skip(offset).Take(count).Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
    }

    private static void WriteHexPreview(TextWriter writer, byte[] bytes, int offset, int length, string indent)
    {
        int count = Math.Min(length, Math.Max(0, bytes.Length - offset));
        for (int row = 0; row < count; row += 16)
        {
            int rowCount = Math.Min(16, count - row);
            writer.Write($"{indent}+0x{offset + row:X4} ");
            writer.WriteLine(FormatHexInline(bytes, offset + row, rowCount));
        }
    }

    private static float ReadSingleBigEndian(byte[] bytes, int offset) =>
        BitConverter.Int32BitsToSingle((int)(((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3]));

    private static IEnumerable<byte> ExpandMmioWriteBytes(MmioAccess access)
    {
        for (int shift = (access.Width - 1) * 8; shift >= 0; shift -= 8)
        {
            yield return (byte)(access.Value >> shift);
        }
    }

    private static int AvailablePayloadBytes(byte[] bytes, int offset, uint wordCount)
    {
        int availableWords = Math.Max(0, bytes.Length - offset) / sizeof(uint);
        int consumedWords = (int)Math.Min(wordCount, (uint)availableWords);
        return checked(consumedWords * sizeof(uint));
    }

    private static bool TryReadBigEndian(byte[] bytes, int offset, int width, out uint value)
    {
        value = 0;
        if (offset < 0 || width < 0 || offset > bytes.Length - width)
        {
            return false;
        }

        for (int index = 0; index < width; index++)
        {
            value = (value << 8) | bytes[offset + index];
        }

        return true;
    }

    private readonly record struct GxVertex(
        float X,
        float Y,
        float Z,
        float InvW,
        byte R,
        byte G,
        byte B,
        byte A = 255,
        bool HasTex0 = false,
        float Tex0S = 0,
        float Tex0T = 0,
        bool HasRawTex0 = false,
        float RawTex0S = 0,
        float RawTex0T = 0,
        bool ClipRejected = false,
        bool HasViewPosition = false,
        float ViewX = 0,
        float ViewY = 0,
        float ViewZ = 0,
        GxTexCoordSet TexCoords = default)
    {
        public bool TryGetTexCoord(int index, out float s, out float t) =>
            TexCoords.TryGet(index, out s, out t);
    }

    private readonly record struct GxTexCoordSet(
        ushort ValidMask = 0,
        float S0 = 0,
        float T0 = 0,
        float S1 = 0,
        float T1 = 0,
        float S2 = 0,
        float T2 = 0,
        float S3 = 0,
        float T3 = 0,
        float S4 = 0,
        float T4 = 0,
        float S5 = 0,
        float T5 = 0,
        float S6 = 0,
        float T6 = 0,
        float S7 = 0,
        float T7 = 0)
    {
        public bool TryGet(int index, out float s, out float t)
        {
            s = 0;
            t = 0;
            if (index is < 0 or > 7 || (ValidMask & (1 << index)) == 0)
            {
                return false;
            }

            s = index switch
            {
                0 => S0,
                1 => S1,
                2 => S2,
                3 => S3,
                4 => S4,
                5 => S5,
                6 => S6,
                7 => S7,
                _ => 0,
            };
            t = index switch
            {
                0 => T0,
                1 => T1,
                2 => T2,
                3 => T3,
                4 => T4,
                5 => T5,
                6 => T6,
                7 => T7,
                _ => 0,
            };
            return true;
        }

        public GxTexCoordSet With(int index, float s, float t)
        {
            ushort mask = (ushort)(ValidMask | (1 << index));
            return index switch
            {
                0 => this with { ValidMask = mask, S0 = s, T0 = t },
                1 => this with { ValidMask = mask, S1 = s, T1 = t },
                2 => this with { ValidMask = mask, S2 = s, T2 = t },
                3 => this with { ValidMask = mask, S3 = s, T3 = t },
                4 => this with { ValidMask = mask, S4 = s, T4 = t },
                5 => this with { ValidMask = mask, S5 = s, T5 = t },
                6 => this with { ValidMask = mask, S6 = s, T6 = t },
                7 => this with { ValidMask = mask, S7 = s, T7 = t },
                _ => this,
            };
        }

        public static GxTexCoordSet Interpolate(GxTexCoordSet a, GxTexCoordSet b, float t)
        {
            ushort mask = (ushort)(a.ValidMask & b.ValidMask);
            GxTexCoordSet result = default;
            for (int index = 0; index < 8; index++)
            {
                if ((mask & (1 << index)) == 0
                    || !a.TryGet(index, out float aS, out float aT)
                    || !b.TryGet(index, out float bS, out float bT))
                {
                    continue;
                }

                result = result.With(index, Lerp(aS, bS, t), Lerp(aT, bT, t));
            }

            return result;
        }
    }

    private enum GxTevStage0Mode
    {
        Unknown,
        PassColor,
        Replace,
        Decal,
        Blend,
        Modulate,
    }

    private readonly record struct GxTevColor(byte R, byte G, byte B, byte A)
    {
        public static GxTevColor Zero => new(0, 0, 0, 0);
        public static GxTevColor One => new(255, 255, 255, 255);
        public static GxTevColor Half => new(128, 128, 128, 128);
    }

    private sealed class GxVertexState
    {
        private readonly uint[] _vcdLow = new uint[8];
        private readonly uint[] _vcdHigh = new uint[8];
        private readonly uint[] _vatA = new uint[8];
        private readonly uint[] _vatB = new uint[8];
        private readonly uint[] _vatC = new uint[8];
        private readonly uint[] _arrayBases = new uint[16];
        private readonly int[] _arrayStrides = new int[16];
        private readonly uint[] _bpRegisters = new uint[256];
        private readonly bool[] _bpRegisterWritten = new bool[256];
        private readonly GxTextureState[] _textures = Enumerable.Range(0, 8).Select(_ => new GxTextureState()).ToArray();
        private readonly GxTevColor[] _tevRegisterColors = new GxTevColor[4];
        private readonly GxTevColor[] _tevKColors = new GxTevColor[4];
        private readonly Dictionary<int, GxTlutLoadState> _tlutLoads = [];
        private readonly Dictionary<ushort, uint> _xfRegisters = [];
        private uint? _pendingTlutSourceAddress;

        public GxMemorySnapshotSet? MemorySnapshots { get; init; }

        public int CurrentFifoOffset { get; set; }

        public void WriteCpRegister(byte register, uint value)
        {
            if (register is >= 0x50 and <= 0x57)
            {
                _vcdLow[register - 0x50] = value;
            }
            else if (register is >= 0x60 and <= 0x67)
            {
                _vcdHigh[register - 0x60] = value;
            }
            else if (register is >= 0x70 and <= 0x77)
            {
                _vatA[register - 0x70] = value;
            }
            else if (register is >= 0x80 and <= 0x87)
            {
                _vatB[register - 0x80] = value;
            }
            else if (register is >= 0x90 and <= 0x97)
            {
                _vatC[register - 0x90] = value;
            }
            else if (register is >= 0xA0 and <= 0xAF)
            {
                _arrayBases[register - 0xA0] = value;
            }
            else if (register is >= 0xB0 and <= 0xBF)
            {
                _arrayStrides[register - 0xB0] = checked((int)value);
            }
        }

        public void WriteBpRegister(uint value)
        {
            byte register = (byte)(value >> 24);
            _bpRegisters[register] = value & 0x00FF_FFFF;
            _bpRegisterWritten[register] = true;
            WriteTlutLoadRegister(register, _bpRegisters[register]);
            WriteTextureRegister(register, _bpRegisters[register]);
            WriteTevColorRegister(register, _bpRegisters[register]);
            WriteTevKColorRegister(register, _bpRegisters[register]);
        }

        public void WriteXfRegisters(byte[] bytes, int offset, ushort register, int wordCount)
        {
            for (int word = 0; word < wordCount; word++)
            {
                if (!TryReadBigEndian(bytes, offset + word * sizeof(uint), sizeof(uint), out uint value))
                {
                    return;
                }

                _xfRegisters[checked((ushort)(register + word))] = value;
            }
        }

        public bool IsRenderablePositionFormat(int format) =>
            ((_vcdLow[format] >> 9) & 3) != 0
            && PositionComponentBytes((_vatA[format] >> 1) & 7) != 0;

        public bool TryReadVertex(byte[] bytes, ref int offset, int format, GameCubeMemory? memory, out GxVertex vertex)
        {
            vertex = default;
            if (format is < 0 or >= 8)
            {
                return false;
            }

            uint vcdLow = _vcdLow[format];
            uint vcdHigh = _vcdHigh[format];
            uint vatA = _vatA[format];
            uint vatB = _vatB[format];
            uint vatC = _vatC[format];
            uint positionDescriptor = (vcdLow >> 9) & 3;
            uint normalDescriptor = (vcdLow >> 11) & 3;
            uint color0Descriptor = (vcdLow >> 13) & 3;
            uint color1Descriptor = (vcdLow >> 15) & 3;
            byte r = 224;
            byte g = 224;
            byte b = 224;
            byte a = 255;

            for (int bit = 0; bit < 9; bit++)
            {
                if (((vcdLow >> bit) & 1) != 0 && !SkipBytes(bytes, ref offset, 1))
                {
                    return false;
                }
            }

            if (!TryReadPosition(bytes, ref offset, positionDescriptor, vatA, memory, out float x, out float y, out float z)
                || !SkipAttribute(bytes, ref offset, normalDescriptor, ComponentBytes((vatA >> 10) & 7, ((vatA >> 9) & 1) == 0 ? 3 : 9))
                || !TryReadColorOrSkip(bytes, ref offset, color0Descriptor, vatA, memory, out r, out g, out b, out a)
                || !SkipAttribute(bytes, ref offset, color1Descriptor, ColorBytes((vatA >> 18) & 7)))
            {
                return false;
            }

            if (!TryReadTexCoordOrSkip(bytes, ref offset, (vcdHigh >> 0) & 3, vatA, countBit: 21, formatBit: 22, arrayIndex: 4, memory: memory, out bool hasRawTex0, out float rawTex0S, out float rawTex0T)
                || !TryReadTexCoordOrSkip(bytes, ref offset, (vcdHigh >> 2) & 3, vatB, countBit: 0, formatBit: 1, arrayIndex: 5, memory: memory, out bool hasRawTex1, out float rawTex1S, out float rawTex1T)
                || !TryReadTexCoordOrSkip(bytes, ref offset, (vcdHigh >> 4) & 3, vatB, countBit: 9, formatBit: 10, arrayIndex: 6, memory: memory, out bool hasRawTex2, out float rawTex2S, out float rawTex2T)
                || !TryReadTexCoordOrSkip(bytes, ref offset, (vcdHigh >> 6) & 3, vatB, countBit: 18, formatBit: 19, arrayIndex: 7, memory: memory, out bool hasRawTex3, out float rawTex3S, out float rawTex3T)
                || !TryReadTexCoordOrSkip(bytes, ref offset, (vcdHigh >> 8) & 3, vatB, countBit: 27, formatBit: 28, arrayIndex: 8, memory: memory, out bool hasRawTex4, out float rawTex4S, out float rawTex4T)
                || !TryReadTexCoordOrSkip(bytes, ref offset, (vcdHigh >> 10) & 3, vatC, countBit: 5, formatBit: 6, arrayIndex: 9, memory: memory, out bool hasRawTex5, out float rawTex5S, out float rawTex5T)
                || !TryReadTexCoordOrSkip(bytes, ref offset, (vcdHigh >> 12) & 3, vatC, countBit: 14, formatBit: 15, arrayIndex: 10, memory: memory, out bool hasRawTex6, out float rawTex6S, out float rawTex6T)
                || !TryReadTexCoordOrSkip(bytes, ref offset, (vcdHigh >> 14) & 3, vatC, countBit: 23, formatBit: 24, arrayIndex: 11, memory: memory, out bool hasRawTex7, out float rawTex7S, out float rawTex7T))
            {
                return false;
            }

            GxTexCoordSet texCoords = default;
            if (hasRawTex0) texCoords = texCoords.With(0, rawTex0S, rawTex0T);
            if (hasRawTex1) texCoords = texCoords.With(1, rawTex1S, rawTex1T);
            if (hasRawTex2) texCoords = texCoords.With(2, rawTex2S, rawTex2T);
            if (hasRawTex3) texCoords = texCoords.With(3, rawTex3S, rawTex3T);
            if (hasRawTex4) texCoords = texCoords.With(4, rawTex4S, rawTex4T);
            if (hasRawTex5) texCoords = texCoords.With(5, rawTex5S, rawTex5T);
            if (hasRawTex6) texCoords = texCoords.With(6, rawTex6S, rawTex6T);
            if (hasRawTex7) texCoords = texCoords.With(7, rawTex7S, rawTex7T);

            float modelX = x;
            float modelY = y;
            float modelZ = z;
            bool hasTex0 = hasRawTex0;
            float tex0S = rawTex0S;
            float tex0T = rawTex0T;
            if (TryGenerateTex0(modelX, modelY, modelZ, hasRawTex0, rawTex0S, rawTex0T, out float generatedTex0S, out float generatedTex0T))
            {
                hasTex0 = true;
                tex0S = generatedTex0S;
                tex0T = generatedTex0T;
            }

            if (hasTex0)
            {
                texCoords = texCoords.With(0, tex0S, tex0T);
            }

            float invW = 1f;
            bool clipRejected = false;
            bool hasViewPosition = false;
            float viewX = 0f;
            float viewY = 0f;
            float viewZ = 0f;
            if (TryTransformPosition(x, y, z, out float transformedX, out float transformedY, out float transformedZ, out float transformedInvW, out viewX, out viewY, out viewZ))
            {
                x = transformedX;
                y = transformedY;
                z = transformedZ;
                invW = transformedInvW;
                hasViewPosition = true;
            }
            else if (HasCompletePositionTransformState())
            {
                clipRejected = true;
                hasViewPosition = TryTransformModelToView(x, y, z, out viewX, out viewY, out viewZ);
            }

            vertex = new GxVertex(x, y, z, invW, r, g, b, a, hasTex0, tex0S, tex0T, hasRawTex0, rawTex0S, rawTex0T, clipRejected, hasViewPosition, viewX, viewY, viewZ, texCoords);
            return true;
        }

        public bool TryGetVertexStride(int format, out int stride)
        {
            stride = 0;
            if (format is < 0 or >= 8)
            {
                return false;
            }

            return TryGetVertexStride(format, out stride, out _);
        }

        public string DescribeFormat(int format)
        {
            if (format is < 0 or >= 8)
            {
                return "Format: invalid";
            }

            uint vcdLow = _vcdLow[format];
            uint vcdHigh = _vcdHigh[format];
            uint vatA = _vatA[format];
            uint vatB = _vatB[format];
            uint vatC = _vatC[format];
            string layout = TryGetVertexStride(format, out int stride, out string parsedLayout)
                ? parsedLayout
                : "unsupported layout";
            return $"Format {format}: stride={stride}, VCD_LO=0x{vcdLow:X8}, VCD_HI=0x{vcdHigh:X8}, VAT_A=0x{vatA:X8}, VAT_B=0x{vatB:X8}, VAT_C=0x{vatC:X8}, layout={layout}";
        }

        public IEnumerable<string> DescribeArrays()
        {
            for (int index = 0; index < _arrayBases.Length; index++)
            {
                if (_arrayBases[index] != 0 || _arrayStrides[index] != 0)
                {
                    yield return $"Array[{index}]: base=0x{_arrayBases[index]:X8}, stride={_arrayStrides[index]}";
                }
            }
        }

        public IEnumerable<string> DescribePipelineState()
        {
            if (TryGetXfFloat(0x101A, out float viewportScaleX)
                && TryGetXfFloat(0x101B, out float viewportScaleY)
                && TryGetXfFloat(0x101C, out float viewportScaleZ)
                && TryGetXfFloat(0x101D, out float viewportOriginX)
                && TryGetXfFloat(0x101E, out float viewportOriginY)
                && TryGetXfFloat(0x101F, out float viewportOriginZ))
            {
                yield return $"XF viewport: scale=({FormatFloat(viewportScaleX)}, {FormatFloat(viewportScaleY)}, {FormatFloat(viewportScaleZ)}), origin=({FormatFloat(viewportOriginX)}, {FormatFloat(viewportOriginY)}, {FormatFloat(viewportOriginZ)})";
            }

            bool hasScissorTopLeft = TryGetBp(0x20, out uint scissorTopLeft);
            bool hasScissorBottomRight = TryGetBp(0x21, out uint scissorBottomRight);
            if (hasScissorTopLeft || hasScissorBottomRight)
            {
                string decoded = TryDecodeScissor(scissorTopLeft, scissorBottomRight, out int scissorLeft, out int scissorTop, out int scissorRight, out int scissorBottom)
                    ? $", decoded=({scissorLeft},{scissorTop})-({scissorRight},{scissorBottom})"
                    : string.Empty;
                yield return $"BP scissor raw: top-left=0x{scissorTopLeft:X6}, bottom-right=0x{scissorBottomRight:X6}{decoded}";
            }

            bool hasZMode = TryGetBp(0x40, out uint zMode);
            bool hasBlendMode = TryGetBp(0x41, out uint blendMode);
            bool hasConstantAlpha = TryGetBp(0x42, out uint constantAlpha);
            bool hasAlphaCompare = TryGetBp(0xF3, out uint alphaCompare);
            if (hasZMode || hasBlendMode || hasConstantAlpha || hasAlphaCompare)
            {
                string blendDecoded = hasBlendMode ? $", {DescribeBlendMode(blendMode)}" : string.Empty;
                string alphaDecoded = hasAlphaCompare ? $", {DescribeAlphaCompare(alphaCompare)}" : string.Empty;
                yield return $"BP pixel mode raw: z=0x{zMode:X6}, blend=0x{blendMode:X6}, constant-alpha=0x{constantAlpha:X6}, alpha=0x{alphaCompare:X6}{blendDecoded}{alphaDecoded}";
            }

            if (TryGetBp(0x45, out uint peControl))
            {
                yield return $"BP PE control raw: 0x{peControl:X6}";
            }

            foreach (string copyLine in DescribeEfbCopyState())
            {
                yield return copyLine;
            }

            if (TryGetBp(0x00, out uint genMode))
            {
                yield return $"BP gen mode raw: 0x{genMode:X6}, tev-stages={DecodeGenModeTevStageCount(genMode)}, ind-stages={DecodeGenModeIndirectStageCount(genMode)}";
            }

            if (_xfRegisters.TryGetValue(0x1018, out uint matrixIndexLow))
            {
                yield return $"XF matrix index low raw: 0x{matrixIndexLow:X8}, pos-base=0x{CurrentPositionMatrixBaseRegister:X4}, tex0-base=0x{CurrentTex0MatrixBaseRegister:X4}";
            }

            if (_xfRegisters.TryGetValue(0x1040, out uint texGen0))
            {
                yield return $"XF texgen0 raw: 0x{texGen0:X8}, projection={TextureProjectionName((int)((texGen0 >> 1) & 1))}, source={TextureSourceName((int)((texGen0 >> 7) & 0x1F))}, type={TextureGenTypeName((int)((texGen0 >> 4) & 7))}";
            }

            bool hasTevColor = TryGetBp(0xC0, out uint tevColor);
            bool hasTevAlpha = TryGetBp(0xC1, out uint tevAlpha);
            if (hasTevColor || hasTevAlpha)
            {
                string order = TryGetBp(0x28, out uint tevOrder0)
                    ? $", {DescribeTevOrder(tevOrder0)}"
                    : string.Empty;
                yield return $"BP TEV stage0 raw: color=0x{tevColor:X6}, alpha=0x{tevAlpha:X6}, mode={Stage0Mode}{order}";
            }

            foreach (string indirectLine in DescribeIndirectTevState())
            {
                yield return indirectLine;
            }

            foreach (string textureLine in DescribeTextureState())
            {
                yield return textureLine;
            }
        }

        private bool TryGetBp(byte register, out uint value)
        {
            value = _bpRegisters[register];
            return _bpRegisterWritten[register];
        }

        public bool TryGetEfbCopyInfo(out EfbCopyInfo info)
        {
            info = default;
            if (!TryGetBp(0x52, out uint control)
                || !TryGetBp(0x49, out uint topLeft)
                || !TryGetBp(0x4A, out uint size)
                || !TryGetBp(0x4B, out uint address))
            {
                return false;
            }

            DecodeCopyRectangle(topLeft, size, out int sourceLeft, out int sourceTop, out int sourceWidth, out int sourceHeight);
            int format = DecodeCopyTextureFormat(control);
            bool isDisplayCopy = (control & 0x4000) != 0;
            bool clear = (control & 0x0800) != 0;
            bool mipmap = (control & 0x0200) != 0;
            uint destinationRaw = TryGetBp(0x4D, out uint destination) ? destination : 0;
            int outputWidth = sourceWidth;
            int outputHeight = sourceHeight;
            if (isDisplayCopy)
            {
                DisplayCopyOptions options = GetDisplayCopyOptions(sourceWidth, sourceHeight, control);
                outputWidth = options.DestinationWidth;
                outputHeight = options.DestinationHeight;
            }
            else
            {
                outputWidth = GetTextureCopyDestinationWidth(format, sourceWidth);
            }

            info = new EfbCopyInfo(
                control,
                isDisplayCopy,
                clear,
                mipmap,
                format,
                sourceLeft,
                sourceTop,
                sourceWidth,
                sourceHeight,
                (address & 0x00FF_FFFF) << 5,
                destinationRaw,
                (int)(destinationRaw & 0x3FF),
                outputWidth,
                outputHeight);
            return true;
        }

        private bool TryGetXfFloat(ushort register, out float value)
        {
            if (_xfRegisters.TryGetValue(register, out uint rawValue))
            {
                value = BitConverter.Int32BitsToSingle((int)rawValue);
                return true;
            }

            value = 0;
            return false;
        }

        private IEnumerable<string> DescribeEfbCopyState()
        {
            bool hasTopLeft = TryGetBp(0x49, out uint topLeft);
            bool hasSize = TryGetBp(0x4A, out uint size);
            bool hasAddress = TryGetBp(0x4B, out uint address);
            bool hasDestination = TryGetBp(0x4D, out uint destination);
            bool hasControl = TryGetBp(0x52, out uint control);
            bool hasClearAr = TryGetBp(0x4F, out _);
            bool hasClearGb = TryGetBp(0x50, out _);
            bool hasClearZ = TryGetBp(0x51, out uint clearZ);
            if (!hasTopLeft && !hasSize && !hasAddress && !hasDestination && !hasControl && !hasClearAr && !hasClearGb && !hasClearZ)
            {
                yield break;
            }

            if (hasTopLeft || hasSize || hasAddress || hasDestination || hasControl)
            {
                DecodeCopyRectangle(topLeft, size, out int left, out int top, out int width, out int height);
                uint destinationAddress = (address & 0x00FF_FFFF) << 5;
                int format = DecodeCopyTextureFormat(control);
                bool isDisplayCopy = (control & 0x4000) != 0;
                bool clear = (control & 0x0800) != 0;
                bool mipmap = (control & 0x0200) != 0;
                string kind = isDisplayCopy ? "display" : "texture";
                string dst = hasAddress ? $"dst=0x{destinationAddress:X8}" : "dst=unknown";
                string dstMeta = hasDestination ? $", dst-raw=0x{destination:X6}, tiles={destination & 0x3FF}" : string.Empty;
                string fmt = hasControl ? $", fmt={TextureFormatName(format)}, clear={clear}, mipmap={mipmap}, control=0x{control:X6}" : string.Empty;
                yield return $"BP EFB copy {kind}: src=({left},{top}) {width}x{height}, {dst}{dstMeta}{fmt}";

                if (isDisplayCopy)
                {
                    DisplayCopyOptions options = GetDisplayCopyOptions(width, height, control);
                    string filter = options.VerticalFilterEnabled
                        ? string.Join(",", options.VerticalFilter)
                        : "off";
                    yield return $"BP EFB display copy scale: xfb={options.DestinationWidth}x{options.DestinationHeight}, y-scale={options.YScale}, gamma={CopyGammaName(options.Gamma)}, clamp=({options.ClampTop},{options.ClampBottom}), field={CopyFrameFieldName(options.FrameFieldMode)}, vfilter={filter}";
                }
            }

            if (hasClearAr || hasClearGb || hasClearZ)
            {
                GetCopyClearColor(out byte clearR, out byte clearG, out byte clearB, out byte clearA);
                string z = hasClearZ ? $"0x{clearZ & 0x00FF_FFFF:X6}" : "unknown";
                yield return $"BP EFB copy clear: color=({clearR},{clearG},{clearB},{clearA}), z={z}";
            }
        }

        public bool TryCopyEfb(GameCubeMemory? memory, byte[] rgb, byte[] alpha, int frameWidth, int frameHeight, bool clearAfterCopy = true)
        {
            return TryCopyEfb(memory, rgb, alpha, depth: null, frameWidth, frameHeight, clearAfterCopy, out _);
        }

        public bool TryCopyEfb(GameCubeMemory? memory, byte[] rgb, byte[] alpha, int frameWidth, int frameHeight, bool clearAfterCopy, out DisplayCopyResult? displayCopy)
        {
            return TryCopyEfb(memory, rgb, alpha, depth: null, frameWidth, frameHeight, clearAfterCopy, out displayCopy);
        }

        public bool TryCopyEfb(GameCubeMemory? memory, byte[] rgb, byte[] alpha, float[]? depth, int frameWidth, int frameHeight, bool clearAfterCopy, out DisplayCopyResult? displayCopy)
        {
            displayCopy = null;
            if (memory is null
                || rgb.Length < frameWidth * frameHeight * 3
                || alpha.Length < frameWidth * frameHeight
                || (depth is not null && depth.Length < frameWidth * frameHeight)
                || !TryGetBp(0x52, out uint control)
                || !TryGetBp(0x49, out uint topLeft)
                || !TryGetBp(0x4A, out uint size)
                || !TryGetBp(0x4B, out uint destination))
            {
                return false;
            }

            DecodeCopyRectangle(topLeft, size, out int sourceLeft, out int sourceTop, out int copyWidth, out int copyHeight);
            int format = DecodeCopyTextureFormat(control);
            if (!IsSupportedEfbColorCopyFormat(format) || copyWidth <= 0 || copyHeight <= 0)
            {
                return false;
            }

            uint destinationAddress = (destination & 0x00FF_FFFF) << 5;
            try
            {
                if ((control & 0x4000) != 0)
                {
                    DisplayCopyOptions options = GetDisplayCopyOptions(copyWidth, copyHeight, control);
                    WriteEfbDisplayCopy(memory, destinationAddress, rgb, alpha, frameWidth, frameHeight, sourceLeft, sourceTop, copyWidth, copyHeight, options);
                    displayCopy = new DisplayCopyResult(destinationAddress, options.DestinationWidth, options.DestinationHeight, FramebufferPixelFormat.Yuyv);
                }
                else
                {
                    int destinationWidth = GetTextureCopyDestinationWidth(format, copyWidth);
                    WriteEfbCopyTexture(memory, destinationAddress, rgb, alpha, frameWidth, frameHeight, sourceLeft, sourceTop, copyWidth, copyHeight, destinationWidth, format);
                }

                if (clearAfterCopy && (control & 0x0800) != 0)
                {
                    GetCopyClearColor(out byte clearR, out byte clearG, out byte clearB, out byte clearA);
                    ClearEfbRegion(rgb, alpha, frameWidth, frameHeight, sourceLeft, sourceTop, copyWidth, copyHeight, clearR, clearG, clearB, clearA);
                    if (depth is not null)
                    {
                        ClearEfbDepthRegion(depth, frameWidth, frameHeight, sourceLeft, sourceTop, copyWidth, copyHeight, GetCopyClearDepth());
                    }
                }

                return true;
            }
            catch (AddressTranslationException)
            {
                return false;
            }
        }

        private float GetCopyClearDepth() =>
            TryGetBp(0x51, out uint clearZ) ? clearZ & 0x00FF_FFFF : FarDepthValue;

        private static void DecodeCopyRectangle(uint topLeft, uint size, out int left, out int top, out int width, out int height)
        {
            left = (int)(topLeft & 0x3FF);
            top = (int)((topLeft >> 10) & 0x3FF);
            width = (int)(size & 0x3FF) + 1;
            height = (int)((size >> 10) & 0x3FF) + 1;
        }

        private static int DecodeCopyTextureFormat(uint control) =>
            (int)(((control >> 4) & 7) | (control & 8));

        private static bool IsSupportedEfbColorCopyFormat(int format) =>
            format is 0 or 1 or 2 or 3 or 4 or 5 or 6;

        private void GetCopyClearColor(out byte r, out byte g, out byte b, out byte a)
        {
            r = 0;
            g = 0;
            b = 0;
            a = 255;

            if (TryGetBp(0x4F, out uint clearAr))
            {
                r = (byte)(clearAr & 0xFF);
                a = (byte)((clearAr >> 8) & 0xFF);
            }

            if (TryGetBp(0x50, out uint clearGb))
            {
                b = (byte)(clearGb & 0xFF);
                g = (byte)((clearGb >> 8) & 0xFF);
            }
        }

        private int GetTextureCopyDestinationWidth(int format, int copyWidth)
        {
            if (!TryGetBp(0x4D, out uint destination) || !TryGetCopyTileWidth(format, out int tileWidth))
            {
                return copyWidth;
            }

            int tileCount = (int)(destination & 0x3FF);
            if (tileCount <= 0)
            {
                return copyWidth;
            }

            int zPlanes = format == 6 ? 2 : 1;
            int xTiles = Math.Max(1, tileCount / zPlanes);
            int destinationWidth = xTiles * tileWidth;
            return destinationWidth >= copyWidth ? destinationWidth : copyWidth;
        }

        private static bool TryGetCopyTileWidth(int format, out int tileWidth)
        {
            tileWidth = format switch
            {
                0 or 2 => 8,
                1 => 8,
                3 or 4 or 5 or 6 => 4,
                _ => 0,
            };

            return tileWidth != 0;
        }

        private readonly record struct DisplayCopyOptions(
            int DestinationWidth,
            int DestinationHeight,
            int YScale,
            bool VerticalFilterEnabled,
            int[] VerticalFilter,
            int Gamma,
            bool ClampTop,
            bool ClampBottom,
            int FrameFieldMode);

        private DisplayCopyOptions GetDisplayCopyOptions(int copyWidth, int copyHeight, uint control)
        {
            int destinationWidth = copyWidth;
            if (TryGetBp(0x4D, out uint destination))
            {
                int encodedWidth = (int)(destination & 0x3FF) << 4;
                if (encodedWidth > 0)
                {
                    destinationWidth = encodedWidth;
                }
            }

            int yScale = TryGetBp(0x4E, out uint rawYScale)
                ? (int)(rawYScale & 0x1FF)
                : 256;
            if (yScale <= 0)
            {
                yScale = 256;
            }

            int destinationHeight = GetDisplayCopyDestinationHeight(copyHeight, yScale);
            int[] verticalFilter = DecodeVerticalCopyFilter();
            bool verticalFilterEnabled = IsVerticalCopyFilterEnabled(verticalFilter);
            return new DisplayCopyOptions(
                destinationWidth,
                destinationHeight,
                yScale,
                verticalFilterEnabled,
                verticalFilter,
                (int)((control >> 7) & 3),
                (control & 1) != 0,
                (control & 2) != 0,
                (int)((control >> 12) & 3));
        }

        private int[] DecodeVerticalCopyFilter()
        {
            int[] filter = new int[7];
            if (TryGetBp(0x53, out uint filter0))
            {
                filter[0] = (int)(filter0 & 0x3F);
                filter[1] = (int)((filter0 >> 6) & 0x3F);
                filter[2] = (int)((filter0 >> 12) & 0x3F);
                filter[3] = (int)((filter0 >> 18) & 0x3F);
            }

            if (TryGetBp(0x54, out uint filter1))
            {
                filter[4] = (int)(filter1 & 0x3F);
                filter[5] = (int)((filter1 >> 6) & 0x3F);
                filter[6] = (int)((filter1 >> 12) & 0x3F);
            }

            return filter;
        }

        private static bool IsVerticalCopyFilterEnabled(int[] filter)
        {
            int sum = 0;
            for (int i = 0; i < filter.Length; i++)
            {
                sum += filter[i];
            }

            return sum > 0 && (filter[0] != 0 || filter[1] != 0 || filter[2] != 0 || filter[4] != 0 || filter[5] != 0 || filter[6] != 0 || filter[3] != 64);
        }

        private static readonly byte[] CopyGamma17Table = BuildCopyGammaTable(1.0 / 1.7);
        private static readonly byte[] CopyGamma22Table = BuildCopyGammaTable(1.0 / 2.2);

        private static int GetDisplayCopyDestinationHeight(int copyHeight, int yScale)
        {
            int height = (((copyHeight - 1) << 8) / yScale) + 1;
            if (yScale > 128 && yScale < 256)
            {
                int reducedScale = yScale;
                while ((reducedScale & 1) != 0)
                {
                    reducedScale >>= 1;
                }

                int exactHeight = reducedScale * (copyHeight / reducedScale);
                if (copyHeight == exactHeight)
                {
                    height++;
                }
            }

            return Math.Clamp(height, 1, 1024);
        }

        private static string CopyGammaName(int gamma) =>
            gamma switch
            {
                0 => "1.0",
                1 => "1.7",
                2 => "2.2",
                _ => $"gamma{gamma}",
            };

        private static string CopyFrameFieldName(int mode) =>
            mode switch
            {
                0 => "progressive",
                1 => "field",
                2 => "field-even",
                3 => "field-odd",
                _ => $"field{mode}",
            };

        private static void WriteEfbCopyTexture(
            GameCubeMemory memory,
            uint destinationAddress,
            byte[] rgb,
            byte[] alpha,
            int frameWidth,
            int frameHeight,
            int sourceLeft,
            int sourceTop,
            int copyWidth,
            int copyHeight,
            int destinationWidth,
            int format)
        {
            for (int y = 0; y < copyHeight; y++)
            {
                for (int x = 0; x < copyWidth; x++)
                {
                    SampleEfbPixel(rgb, alpha, frameWidth, frameHeight, sourceLeft + x, sourceTop + y, out byte r, out byte g, out byte b, out byte a);
                    WriteTexturePixel(memory, destinationAddress, destinationWidth, copyHeight, format, x, y, r, g, b, a);
                }
            }
        }

        private static void WriteEfbDisplayCopy(
            GameCubeMemory memory,
            uint destinationAddress,
            byte[] rgb,
            byte[] alpha,
            int frameWidth,
            int frameHeight,
            int sourceLeft,
            int sourceTop,
            int copyWidth,
            int copyHeight,
            DisplayCopyOptions options)
        {
            if (!options.VerticalFilterEnabled
                && options.DestinationWidth == copyWidth
                && options.DestinationHeight == copyHeight
                && sourceLeft >= 0
                && sourceTop >= 0
                && sourceLeft + copyWidth <= frameWidth
                && sourceTop + copyHeight <= frameHeight)
            {
                WriteEfbDisplayCopyDirect(memory, destinationAddress, rgb, frameWidth, sourceLeft, sourceTop, copyWidth, copyHeight, options.Gamma);
                return;
            }

            for (int y = 0; y < options.DestinationHeight; y++)
            {
                for (int x = 0; x < options.DestinationWidth; x += 2)
                {
                    SampleDisplayCopyPixel(rgb, alpha, frameWidth, frameHeight, sourceLeft, sourceTop, copyWidth, copyHeight, options, x, y, out byte r0, out byte g0, out byte b0);
                    SampleDisplayCopyPixel(rgb, alpha, frameWidth, frameHeight, sourceLeft, sourceTop, copyWidth, copyHeight, options, Math.Min(x + 1, options.DestinationWidth - 1), y, out byte r1, out byte g1, out byte b1);
                    if (options.Gamma != 0)
                    {
                        ApplyCopyGamma(options.Gamma, ref r0, ref g0, ref b0);
                        ApplyCopyGamma(options.Gamma, ref r1, ref g1, ref b1);
                    }
                    RgbToYcbcr(r0, g0, b0, out byte y0, out byte cb0, out byte cr0);
                    RgbToYcbcr(r1, g1, b1, out byte y1, out byte cb1, out byte cr1);
                    uint address = destinationAddress + (uint)((y * ((options.DestinationWidth + 1) / 2) + x / 2) * 4);
                    memory.Write8(address, y0);
                    memory.Write8(address + 1, (byte)((cb0 + cb1 + 1) / 2));
                    memory.Write8(address + 2, y1);
                    memory.Write8(address + 3, (byte)((cr0 + cr1 + 1) / 2));
                }
            }
        }

        private static void WriteEfbDisplayCopyDirect(
            GameCubeMemory memory,
            uint destinationAddress,
            byte[] rgb,
            int frameWidth,
            int sourceLeft,
            int sourceTop,
            int width,
            int height,
            int gamma)
        {
            int destinationStridePairs = (width + 1) / 2;
            for (int y = 0; y < height; y++)
            {
                int sourcePixel = (sourceTop + y) * frameWidth + sourceLeft;
                int destinationPair = y * destinationStridePairs;
                for (int x = 0; x < width; x += 2)
                {
                    int pixel0 = sourcePixel + x;
                    int pixel1 = sourcePixel + Math.Min(x + 1, width - 1);
                    int rgb0 = pixel0 * 3;
                    int rgb1 = pixel1 * 3;
                    byte r0 = rgb[rgb0];
                    byte g0 = rgb[rgb0 + 1];
                    byte b0 = rgb[rgb0 + 2];
                    byte r1 = rgb[rgb1];
                    byte g1 = rgb[rgb1 + 1];
                    byte b1 = rgb[rgb1 + 2];
                    if (gamma != 0)
                    {
                        ApplyCopyGamma(gamma, ref r0, ref g0, ref b0);
                        ApplyCopyGamma(gamma, ref r1, ref g1, ref b1);
                    }

                    RgbToYcbcr(r0, g0, b0, out byte y0, out byte cb0, out byte cr0);
                    RgbToYcbcr(r1, g1, b1, out byte y1, out byte cb1, out byte cr1);
                    uint address = destinationAddress + (uint)((destinationPair + x / 2) * 4);
                    memory.Write8(address, y0);
                    memory.Write8(address + 1, (byte)((cb0 + cb1 + 1) / 2));
                    memory.Write8(address + 2, y1);
                    memory.Write8(address + 3, (byte)((cr0 + cr1 + 1) / 2));
                }
            }
        }

        private static void SampleDisplayCopyPixel(
            byte[] rgb,
            byte[] alpha,
            int frameWidth,
            int frameHeight,
            int sourceLeft,
            int sourceTop,
            int copyWidth,
            int copyHeight,
            DisplayCopyOptions options,
            int destinationX,
            int destinationY,
            out byte r,
            out byte g,
            out byte b)
        {
            int sourceX = sourceLeft + ScaleCopyCoordinate(destinationX, options.DestinationWidth, copyWidth);
            int sourceY = ScaleCopyCoordinate(destinationY, options.DestinationHeight, copyHeight);
            if (!options.VerticalFilterEnabled)
            {
                SampleEfbPixel(rgb, alpha, frameWidth, frameHeight, sourceX, sourceTop + sourceY, out r, out g, out b, out _);
                return;
            }

            int weightSum = 0;
            int accumR = 0;
            int accumG = 0;
            int accumB = 0;
            for (int tap = 0; tap < options.VerticalFilter.Length; tap++)
            {
                int weight = options.VerticalFilter[tap];
                if (weight == 0)
                {
                    continue;
                }

                int tapY = sourceTop + sourceY + tap - 3;
                SampleEfbPixel(rgb, alpha, frameWidth, frameHeight, sourceX, tapY, out byte sampleR, out byte sampleG, out byte sampleB, out _);
                accumR += sampleR * weight;
                accumG += sampleG * weight;
                accumB += sampleB * weight;
                weightSum += weight;
            }

            if (weightSum == 0)
            {
                SampleEfbPixel(rgb, alpha, frameWidth, frameHeight, sourceX, sourceTop + sourceY, out r, out g, out b, out _);
                return;
            }

            r = Clamp8((accumR + weightSum / 2) / weightSum);
            g = Clamp8((accumG + weightSum / 2) / weightSum);
            b = Clamp8((accumB + weightSum / 2) / weightSum);
        }

        private static int ScaleCopyCoordinate(int destinationCoordinate, int destinationSize, int sourceSize)
        {
            if (destinationSize <= 1 || sourceSize <= 1)
            {
                return 0;
            }

            return Math.Clamp((int)(((long)destinationCoordinate * sourceSize) / destinationSize), 0, sourceSize - 1);
        }

        private static void ApplyCopyGamma(int gamma, ref byte r, ref byte g, ref byte b)
        {
            byte[]? table = gamma switch
            {
                1 => CopyGamma17Table,
                2 => CopyGamma22Table,
                _ => null,
            };
            if (table is null)
            {
                return;
            }

            r = table[r];
            g = table[g];
            b = table[b];
        }

        private static byte[] BuildCopyGammaTable(double exponent)
        {
            byte[] table = new byte[256];
            for (int value = 0; value < table.Length; value++)
            {
                table[value] = Clamp8((int)Math.Round(Math.Pow(value / 255.0, exponent) * 255.0));
            }

            return table;
        }

        private static void ClearEfbRegion(byte[] rgb, byte[] alpha, int frameWidth, int frameHeight, int left, int top, int width, int height, byte clearR, byte clearG, byte clearB, byte clearA)
        {
            for (int y = 0; y < height; y++)
            {
                int py = top + y;
                if (py < 0 || py >= frameHeight)
                {
                    continue;
                }

                for (int x = 0; x < width; x++)
                {
                    int px = left + x;
                    if (px < 0 || px >= frameWidth)
                    {
                        continue;
                    }

                    int pixel = py * frameWidth + px;
                    int rgbOffset = pixel * 3;
                    rgb[rgbOffset] = clearR;
                    rgb[rgbOffset + 1] = clearG;
                    rgb[rgbOffset + 2] = clearB;
                    alpha[pixel] = clearA;
                }
            }
        }

        private static void ClearEfbDepthRegion(float[] depth, int frameWidth, int frameHeight, int left, int top, int width, int height, float clearDepth)
        {
            for (int y = 0; y < height; y++)
            {
                int py = top + y;
                if (py < 0 || py >= frameHeight)
                {
                    continue;
                }

                for (int x = 0; x < width; x++)
                {
                    int px = left + x;
                    if (px < 0 || px >= frameWidth)
                    {
                        continue;
                    }

                    depth[py * frameWidth + px] = clearDepth;
                }
            }
        }

        private static void RgbToYcbcr(byte r, byte g, byte b, out byte y, out byte cb, out byte cr)
        {
            y = Clamp8(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);
            cb = Clamp8(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128);
            cr = Clamp8(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128);
        }

        private static void SampleEfbPixel(byte[] rgb, byte[] alpha, int frameWidth, int frameHeight, int x, int y, out byte r, out byte g, out byte b, out byte a)
        {
            x = Math.Clamp(x, 0, frameWidth - 1);
            y = Math.Clamp(y, 0, frameHeight - 1);
            int pixel = y * frameWidth + x;
            int rgbOffset = pixel * 3;
            r = rgb[rgbOffset];
            g = rgb[rgbOffset + 1];
            b = rgb[rgbOffset + 2];
            a = alpha[pixel];
        }

        private static void WriteTexturePixel(GameCubeMemory memory, uint baseAddress, int width, int height, int format, int x, int y, byte r, byte g, byte b, byte a)
        {
            switch (format)
            {
                case 0:
                    WriteI4Pixel(memory, baseAddress, width, x, y, r, g, b);
                    break;
                case 1:
                    WriteI8Pixel(memory, baseAddress, width, x, y, r, g, b);
                    break;
                case 2:
                    WriteIA4Pixel(memory, baseAddress, width, x, y, r, g, b, a);
                    break;
                case 3:
                    WriteIA8Pixel(memory, baseAddress, width, x, y, r, g, b, a);
                    break;
                case 4:
                    WriteRgb565Pixel(memory, baseAddress, width, x, y, r, g, b);
                    break;
                case 5:
                    WriteRgb5A3Pixel(memory, baseAddress, width, x, y, r, g, b, a);
                    break;
                case 6:
                    WriteRgba8Pixel(memory, baseAddress, width, x, y, r, g, b, a);
                    break;
            }
        }

        private bool TryGenerateTex0(
            float modelX,
            float modelY,
            float modelZ,
            bool hasRawTex0,
            float rawTex0S,
            float rawTex0T,
            out float s,
            out float t)
        {
            s = 0;
            t = 0;
            if (!_xfRegisters.TryGetValue(0x1040, out uint texGen0))
            {
                return false;
            }

            int texGenType = (int)((texGen0 >> 4) & 7);
            int sourceRow = (int)((texGen0 >> 7) & 0x1F);
            if (texGenType != 0)
            {
                return false;
            }

            float sourceX;
            float sourceY;
            float sourceZ;
            switch (sourceRow)
            {
                case 0:
                case 1:
                    sourceX = modelX;
                    sourceY = modelY;
                    sourceZ = modelZ;
                    break;
                case 5:
                    if (!hasRawTex0)
                    {
                        return false;
                    }

                    sourceX = rawTex0S;
                    sourceY = rawTex0T;
                    sourceZ = 1f;
                    break;
                default:
                    return false;
            }

            ushort matrixBase = CurrentTex0MatrixBaseRegister;
            if (matrixBase == 60)
            {
                s = sourceX;
                t = sourceY;
                return float.IsFinite(s) && float.IsFinite(t);
            }

            if (!TryGetXfFloat(matrixBase, out float m00)
                || !TryGetXfFloat((ushort)(matrixBase + 1), out float m01)
                || !TryGetXfFloat((ushort)(matrixBase + 2), out float m02)
                || !TryGetXfFloat((ushort)(matrixBase + 3), out float m03)
                || !TryGetXfFloat((ushort)(matrixBase + 4), out float m10)
                || !TryGetXfFloat((ushort)(matrixBase + 5), out float m11)
                || !TryGetXfFloat((ushort)(matrixBase + 6), out float m12)
                || !TryGetXfFloat((ushort)(matrixBase + 7), out float m13))
            {
                return false;
            }

            s = m00 * sourceX + m01 * sourceY + m02 * sourceZ + m03;
            t = m10 * sourceX + m11 * sourceY + m12 * sourceZ + m13;
            bool usesThreeRows = ((texGen0 >> 1) & 1) != 0;
            if (usesThreeRows)
            {
                if (!TryGetXfFloat((ushort)(matrixBase + 8), out float m20)
                    || !TryGetXfFloat((ushort)(matrixBase + 9), out float m21)
                    || !TryGetXfFloat((ushort)(matrixBase + 10), out float m22)
                    || !TryGetXfFloat((ushort)(matrixBase + 11), out float m23))
                {
                    return false;
                }

                float q = m20 * sourceX + m21 * sourceY + m22 * sourceZ + m23;
                if (MathF.Abs(q) < 0.000001f || !float.IsFinite(q))
                {
                    return false;
                }

                s /= q;
                t /= q;
            }

            return float.IsFinite(s) && float.IsFinite(t);
        }

        private static string TextureProjectionName(int projection) =>
            projection == 0 ? "mtx2x4" : "mtx3x4";

        private static string TextureGenTypeName(int type) =>
            type switch
            {
                0 => "regular",
                1 => "emboss",
                2 => "color0",
                3 => "color1",
                _ => $"unknown-{type}",
            };

        private static string TextureSourceName(int source) =>
            source switch
            {
                0 or 1 => "pos",
                2 => "normal",
                3 => "binormal-t",
                4 => "binormal-b",
                5 => "tex0",
                6 => "tex1",
                7 => "tex2",
                8 => "tex3",
                9 => "tex4",
                10 => "tex5",
                11 => "tex6",
                12 => "tex7",
                _ => $"unknown-{source}",
            };

        private static string DescribeBlendMode(uint blendMode)
        {
            string mode = ((blendMode >> 11) & 1) != 0
                ? "subtract"
                : ((blendMode >> 1) & 1) != 0
                    ? "logic"
                    : (blendMode & 1) != 0 ? "blend" : "none";
            return $"blend-mode={mode}, src={BlendFactorName((int)((blendMode >> 8) & 7))}, dst={BlendFactorName((int)((blendMode >> 5) & 7))}, logic={LogicOpName((int)((blendMode >> 12) & 0xF))}, color-update={(((blendMode >> 3) & 1) != 0)}, alpha-update={(((blendMode >> 4) & 1) != 0)}, dither={(((blendMode >> 2) & 1) != 0)}";
        }

        private static string DescribeAlphaCompare(uint alphaCompare)
        {
            int ref0 = (int)(alphaCompare & 0xFF);
            int ref1 = (int)((alphaCompare >> 8) & 0xFF);
            int comp0 = (int)((alphaCompare >> 16) & 7);
            int comp1 = (int)((alphaCompare >> 19) & 7);
            int op = (int)((alphaCompare >> 22) & 3);
            return $"alpha-test=({CompareName(comp0)} {ref0}) {AlphaOpName(op)} ({CompareName(comp1)} {ref1})";
        }

        private static string DescribeTevOrder(uint tevOrder) =>
            $"order=texcoord{(tevOrder >> 3) & 7}/texmap{tevOrder & 7}/color{(tevOrder >> 7) & 7}/tex-enabled={(((tevOrder >> 6) & 1) != 0)}";

        private IEnumerable<string> DescribeIndirectTevState()
        {
            int indirectStageCount = TryGetBp(0x00, out uint genMode)
                ? DecodeGenModeIndirectStageCount(genMode)
                : 0;

            for (int stage = 0; stage < 16; stage++)
            {
                if (TryGetBp((byte)(0x10 + stage), out uint value))
                {
                    if (value == 0 && indirectStageCount == 0)
                    {
                        continue;
                    }

                    yield return $"BP indirect TEV stage{stage} raw: 0x{value:X6}, {DescribeIndirectTevStage(value)}";
                }
            }

            if (TryGetBp(0x27, out uint order))
            {
                for (int stage = 0; stage < 4; stage++)
                {
                    yield return $"BP indirect order stage{stage}: {DescribeIndirectOrder(stage, order)}{DescribeIndirectScale(stage)}";
                }
            }

            for (int matrix = 0; matrix < 3; matrix++)
            {
                if (TryDescribeIndirectMatrix(matrix, out string? line) && line is not null)
                {
                    yield return line;
                }
            }
        }

        private static string DescribeIndirectTevStage(uint value)
        {
            int indTex = (int)(value & 3);
            int format = (int)((value >> 2) & 3);
            int bias = (int)((value >> 4) & 7);
            int alpha = (int)((value >> 7) & 3);
            int matrix = (int)((value >> 9) & 0xF);
            int wrapS = (int)((value >> 13) & 7);
            int wrapT = (int)((value >> 16) & 7);
            bool useTexCoordLod = ((value >> 19) & 1) != 0;
            bool addPrevious = ((value >> 20) & 1) != 0;
            string kind = IsDirectIndirectTevStage(value) ? "direct" : "indirect";
            return $"{kind}, indtex{indTex}, fmt={IndirectFormatName(format)}, bias={IndirectBiasName(bias)}, mtx={IndirectMatrixName(matrix)}, wrap=({IndirectWrapName(wrapS)}, {IndirectWrapName(wrapT)}), addprev={addPrevious}, utc-lod={useTexCoordLod}, alpha={IndirectAlphaName(alpha)}";
        }

        private static bool IsDirectIndirectTevStage(uint value) =>
            (value & 0x1F_FFFF) == 0x06_C000;

        private static string DescribeIndirectOrder(int stage, uint order)
        {
            int shift = stage * 6;
            int texMap = (int)((order >> shift) & 7);
            int texCoord = (int)((order >> (shift + 3)) & 7);
            return $"texcoord{texCoord}/texmap{texMap}";
        }

        private string DescribeIndirectScale(int stage)
        {
            byte register = (byte)(stage < 2 ? 0x25 : 0x26);
            if (!TryGetBp(register, out uint value))
            {
                return string.Empty;
            }

            int shift = (stage & 1) * 8;
            int scaleS = (int)((value >> shift) & 0xF);
            int scaleT = (int)((value >> (shift + 4)) & 0xF);
            return $", scale=({IndirectScaleName(scaleS)}, {IndirectScaleName(scaleT)})";
        }

        private static string IndirectFormatName(int format) =>
            format switch
            {
                0 => "8",
                1 => "5",
                2 => "4",
                3 => "3",
                _ => $"fmt{format}",
            };

        private static string IndirectBiasName(int bias) =>
            bias switch
            {
                0 => "none",
                1 => "s",
                2 => "t",
                3 => "st",
                4 => "u",
                5 => "su",
                6 => "tu",
                7 => "stu",
                _ => $"bias{bias}",
            };

        private static string IndirectMatrixName(int matrix) =>
            matrix switch
            {
                0 => "off",
                1 => "0",
                2 => "1",
                3 => "2",
                5 => "s0",
                6 => "s1",
                7 => "s2",
                9 => "t0",
                10 => "t1",
                11 => "t2",
                _ => $"mtx{matrix}",
            };

        private static string IndirectWrapName(int wrap) =>
            wrap switch
            {
                0 => "off",
                1 => "256",
                2 => "128",
                3 => "64",
                4 => "32",
                5 => "16",
                6 => "0",
                _ => $"wrap{wrap}",
            };

        private static string IndirectAlphaName(int alpha) =>
            alpha switch
            {
                0 => "off",
                1 => "s",
                2 => "t",
                3 => "u",
                _ => $"alpha{alpha}",
            };

        private static string IndirectScaleName(int scale) =>
            scale switch
            {
                0 => "1",
                1 => "2",
                2 => "4",
                3 => "8",
                4 => "16",
                5 => "32",
                6 => "64",
                7 => "128",
                8 => "256",
                _ => $"scale{scale}",
            };

        private bool TryDescribeIndirectMatrix(int matrixIndex, out string? line)
        {
            line = null;
            if (!TryGetBp((byte)(0x06 + matrixIndex * 3), out uint row0)
                && !TryGetBp((byte)(0x07 + matrixIndex * 3), out _)
                && !TryGetBp((byte)(0x08 + matrixIndex * 3), out _))
            {
                return false;
            }

            bool complete = TryGetIndirectMatrix(matrixIndex, out IndirectMatrix matrix);
            if (!complete)
            {
                line = $"BP indirect matrix{matrixIndex}: incomplete";
                return true;
            }

            TryGetBp((byte)(0x07 + matrixIndex * 3), out uint row1);
            TryGetBp((byte)(0x08 + matrixIndex * 3), out uint row2);
            int packedScale = DecodeIndirectMatrixPackedScale(row0, row1, row2);
            int scaleExp = packedScale - 17;
            line = $"BP indirect matrix{matrixIndex}: raw=(0x{row0:X6},0x{row1:X6},0x{row2:X6}), scale-exp={scaleExp}, rows=(({FormatFloat(matrix.M00)}, {FormatFloat(matrix.M01)}, {FormatFloat(matrix.M02)}), ({FormatFloat(matrix.M10)}, {FormatFloat(matrix.M11)}, {FormatFloat(matrix.M12)}))";
            return true;
        }

        private static string BlendFactorName(int factor) =>
            factor switch
            {
                0 => "zero",
                1 => "one",
                2 => "src-color",
                3 => "inv-src-color",
                4 => "src-alpha",
                5 => "inv-src-alpha",
                6 => "dst-alpha",
                7 => "inv-dst-alpha",
                _ => $"factor{factor}",
            };

        private static string LogicOpName(int operation) =>
            operation switch
            {
                0 => "clear",
                1 => "and",
                2 => "rev-and",
                3 => "copy",
                4 => "inv-and",
                5 => "noop",
                6 => "xor",
                7 => "or",
                8 => "nor",
                9 => "equiv",
                10 => "inv",
                11 => "rev-or",
                12 => "inv-copy",
                13 => "inv-or",
                14 => "nand",
                15 => "set",
                _ => $"logic{operation}",
            };

        private static string CompareName(int compare) =>
            compare switch
            {
                0 => "never",
                1 => "less",
                2 => "equal",
                3 => "lequal",
                4 => "greater",
                5 => "nequal",
                6 => "gequal",
                7 => "always",
                _ => $"cmp{compare}",
            };

        private static string AlphaOpName(int operation) =>
            operation switch
            {
                0 => "and",
                1 => "or",
                2 => "xor",
                3 => "xnor",
                _ => $"op{operation}",
            };

        private bool HasCompletePositionTransformState()
        {
            ushort matrixBase = CurrentPositionMatrixBaseRegister;
            for (ushort index = 0; index < 12; index++)
            {
                if (!_xfRegisters.ContainsKey((ushort)(matrixBase + index)))
                {
                    return false;
                }
            }

            for (ushort register = 0x101A; register <= 0x101F; register++)
            {
                if (!_xfRegisters.ContainsKey(register))
                {
                    return false;
                }
            }

            for (ushort register = 0x1020; register <= 0x1026; register++)
            {
                if (!_xfRegisters.ContainsKey(register))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryTransformModelToView(float x, float y, float z, out float viewX, out float viewY, out float viewZ)
        {
            viewX = 0;
            viewY = 0;
            viewZ = 0;
            ushort matrixBase = CurrentPositionMatrixBaseRegister;
            Span<float> matrix = stackalloc float[12];
            for (ushort index = 0; index < matrix.Length; index++)
            {
                if (!TryGetXfFloat((ushort)(matrixBase + index), out matrix[index]))
                {
                    return false;
                }
            }

            viewX = matrix[0] * x + matrix[1] * y + matrix[2] * z + matrix[3];
            viewY = matrix[4] * x + matrix[5] * y + matrix[6] * z + matrix[7];
            viewZ = matrix[8] * x + matrix[9] * y + matrix[10] * z + matrix[11];
            return float.IsFinite(viewX) && float.IsFinite(viewY) && float.IsFinite(viewZ);
        }

        public bool TryGetScissor(int width, int height, out int left, out int top, out int right, out int bottom)
        {
            left = 0;
            top = 0;
            right = width - 1;
            bottom = height - 1;
            if (!TryGetBp(0x20, out uint topLeft) || !TryGetBp(0x21, out uint bottomRight))
            {
                return false;
            }

            if (!TryDecodeScissor(topLeft, bottomRight, out int decodedLeft, out int decodedTop, out int decodedRight, out int decodedBottom))
            {
                return false;
            }

            left = Math.Clamp(decodedLeft, 0, width - 1);
            top = Math.Clamp(decodedTop, 0, height - 1);
            right = Math.Clamp(decodedRight, 0, width - 1);
            bottom = Math.Clamp(decodedBottom, 0, height - 1);
            return right >= left && bottom >= top;
        }

        private static bool TryDecodeScissor(uint topLeft, uint bottomRight, out int left, out int top, out int right, out int bottom)
        {
            left = checked((int)((topLeft >> 12) & 0x7FF)) - 342;
            top = checked((int)(topLeft & 0x7FF)) - 342;
            right = checked((int)((bottomRight >> 12) & 0x7FF)) - 342;
            bottom = checked((int)(bottomRight & 0x7FF)) - 342;
            return right >= left && bottom >= top;
        }

        private bool TryTransformPosition(float x, float y, float z, out float screenX, out float screenY, out float screenZ, out float invW, out float viewX, out float viewY, out float viewZ)
        {
            screenX = 0;
            screenY = 0;
            screenZ = z;
            invW = 1f;
            viewX = 0;
            viewY = 0;
            viewZ = 0;
            if (!TryTransformModelToView(x, y, z, out viewX, out viewY, out viewZ))
            {
                return false;
            }

            if (!TryGetXfFloat(0x101A, out float viewportScaleX)
                || !TryGetXfFloat(0x101B, out float viewportScaleY)
                || !TryGetXfFloat(0x101C, out float viewportScaleZ)
                || !TryGetXfFloat(0x101D, out float viewportOriginX)
                || !TryGetXfFloat(0x101E, out float viewportOriginY)
                || !TryGetXfFloat(0x101F, out float viewportOriginZ)
                || !TryGetXfFloat(0x1020, out float projection00)
                || !TryGetXfFloat(0x1021, out float projection01Or03)
                || !TryGetXfFloat(0x1022, out float projection11)
                || !TryGetXfFloat(0x1023, out float projection12Or13)
                || !TryGetXfFloat(0x1024, out float projection22)
                || !TryGetXfFloat(0x1025, out float projection23)
                || !_xfRegisters.TryGetValue(0x1026, out uint projectionType))
            {
                return false;
            }

            float ndcX;
            float ndcY;
            float ndcZ;
            if (projectionType == 0)
            {
                float w = -viewZ;
                if (w <= 0.0001f)
                {
                    return false;
                }

                invW = 1f / w;
                ndcX = (projection00 * viewX + projection01Or03 * viewZ) / w;
                ndcY = (projection11 * viewY + projection12Or13 * viewZ) / w;
                ndcZ = (projection22 * viewZ + projection23) / w;
            }
            else
            {
                ndcX = projection00 * viewX + projection01Or03;
                ndcY = projection11 * viewY + projection12Or13;
                ndcZ = projection22 * viewZ + projection23;
            }

            screenX = (viewportOriginX - 342f) + viewportScaleX * ndcX;
            screenY = (viewportOriginY - 342f) + viewportScaleY * ndcY;
            screenZ = Math.Clamp(viewportOriginZ + viewportScaleZ * ndcZ, 0f, FarDepthValue);
            return float.IsFinite(screenX) && float.IsFinite(screenY) && float.IsFinite(screenZ);
        }

        public bool TryProjectViewPosition(float viewX, float viewY, float viewZ, out float screenX, out float screenY, out float screenZ, out float invW)
        {
            screenX = 0;
            screenY = 0;
            screenZ = 0;
            invW = 1f;
            if (!TryGetXfFloat(0x101A, out float viewportScaleX)
                || !TryGetXfFloat(0x101B, out float viewportScaleY)
                || !TryGetXfFloat(0x101C, out float viewportScaleZ)
                || !TryGetXfFloat(0x101D, out float viewportOriginX)
                || !TryGetXfFloat(0x101E, out float viewportOriginY)
                || !TryGetXfFloat(0x101F, out float viewportOriginZ)
                || !TryGetXfFloat(0x1020, out float projection00)
                || !TryGetXfFloat(0x1021, out float projection01Or03)
                || !TryGetXfFloat(0x1022, out float projection11)
                || !TryGetXfFloat(0x1023, out float projection12Or13)
                || !TryGetXfFloat(0x1024, out float projection22)
                || !TryGetXfFloat(0x1025, out float projection23)
                || !_xfRegisters.TryGetValue(0x1026, out uint projectionType))
            {
                return false;
            }

            float ndcX;
            float ndcY;
            float ndcZ;
            if (projectionType == 0)
            {
                float w = -viewZ;
                if (w <= NearClipW)
                {
                    return false;
                }

                invW = 1f / w;
                ndcX = (projection00 * viewX + projection01Or03 * viewZ) / w;
                ndcY = (projection11 * viewY + projection12Or13 * viewZ) / w;
                ndcZ = (projection22 * viewZ + projection23) / w;
            }
            else
            {
                ndcX = projection00 * viewX + projection01Or03;
                ndcY = projection11 * viewY + projection12Or13;
                ndcZ = projection22 * viewZ + projection23;
            }

            screenX = (viewportOriginX - 342f) + viewportScaleX * ndcX;
            screenY = (viewportOriginY - 342f) + viewportScaleY * ndcY;
            screenZ = Math.Clamp(viewportOriginZ + viewportScaleZ * ndcZ, 0f, FarDepthValue);
            return float.IsFinite(screenX) && float.IsFinite(screenY) && float.IsFinite(screenZ);
        }

        private ushort CurrentPositionMatrixBaseRegister
        {
            get
            {
                if (_xfRegisters.TryGetValue(0x1018, out uint matrixIndexLow))
                {
                    return (ushort)((matrixIndexLow & 0x3F) << 2);
                }

                return 0;
            }
        }

        private ushort CurrentTex0MatrixBaseRegister
        {
            get
            {
                if (_xfRegisters.TryGetValue(0x1018, out uint matrixIndexLow))
                {
                    return (ushort)((matrixIndexLow >> 6) & 0x3F);
                }

                return 60;
            }
        }

        private IEnumerable<string> DescribeTextureState()
        {
            for (int slot = 0; slot < _textures.Length; slot++)
            {
                GxTextureState texture = _textures[slot];
                if (!texture.HasAnyState)
                {
                    continue;
                }

                string size = texture.HasImage0
                    ? $"{texture.Width}x{texture.Height} {TextureFormatName(texture.Format)}"
                    : "unknown-size";
                string source = texture.HasImage3
                    ? $"src=0x{texture.SourceAddress:X8}"
                    : "src=unknown";
                string tmem = texture.HasImage1 || texture.HasImage2
                    ? $", tmem-even=0x{texture.TmemEven:X6}, tmem-odd=0x{texture.TmemOdd:X6}"
                    : string.Empty;
                string tlut = texture.HasTlut
                    ? $", tlut=base-0x{texture.TlutBaseIndex:X3}/{TlutFormatName(texture.TlutFormat)}"
                    : string.Empty;
                string wrap = texture.HasMode0
                    ? $", wrap=({TextureWrapName(texture.WrapS)}, {TextureWrapName(texture.WrapT)})"
                    : string.Empty;
                string filter = texture.HasMode0
                    ? $", filter=(mag={TextureMagFilterName(texture.MagFilter)}, min={TextureMinFilterName(texture.MinFilter)})"
                    : string.Empty;
                string lod = texture.HasMode0 || texture.HasMode1
                    ? $", lod=(bias={FormatFloat(texture.LodBias)}, min={FormatFloat(texture.MinLod)}, max={FormatFloat(texture.MaxLod)}, diag={texture.DiagLodEnabled}, clamp={texture.LodClampEnabled}, aniso={texture.MaxAnisotropy})"
                    : string.Empty;
                yield return $"Texture map {slot}: {size}, {source}{tmem}{tlut}{wrap}{filter}{lod}";
            }
        }

        public bool TrySampleTexture0(GameCubeMemory? memory, float s, float t, out byte r, out byte g, out byte b)
        {
            return TrySampleTexture(memory, textureMap: 0, s, t, out r, out g, out b, out _);
        }

        public bool TrySampleTexture0(GameCubeMemory? memory, float s, float t, out byte r, out byte g, out byte b, out byte a)
        {
            return TrySampleTexture(memory, textureMap: 0, s, t, out r, out g, out b, out a);
        }

        public bool TrySampleTexture(GameCubeMemory? memory, int textureMap, float s, float t, out byte r, out byte g, out byte b, out byte a)
        {
            r = 0;
            g = 0;
            b = 0;
            a = 255;
            if (memory is null || textureMap is < 0 or >= 8)
            {
                return false;
            }

            GxTextureState texture = _textures[textureMap];
            if (!texture.HasImage0 || !texture.HasImage3 || texture.Width <= 0 || texture.Height <= 0)
            {
                return false;
            }

            try
            {
                return texture.MagFilter == 1
                    ? TrySampleTextureLinear(memory, texture, s, t, out r, out g, out b, out a)
                    : TrySampleTextureNearest(memory, texture, s, t, out r, out g, out b, out a);
            }
            catch (AddressTranslationException)
            {
                return false;
            }
        }

        public bool TryDumpTextureMap(GameCubeMemory? memory, int textureMap, out TextureDumpSnapshot snapshot, out byte[]? rgb, out byte[]? alphaRgb, out string? error)
        {
            snapshot = default;
            rgb = null;
            alphaRgb = null;
            error = null;
            if (memory is null || textureMap is < 0 or >= 8)
            {
                error = "invalid texture map.";
                return false;
            }

            GxTextureState texture = _textures[textureMap];
            if (!texture.HasImage0 || !texture.HasImage3 || texture.Width <= 0 || texture.Height <= 0)
            {
                error = "texture map is incomplete.";
                return false;
            }

            if ((long)texture.Width * texture.Height > MaxDiagnosticPixels)
            {
                error = "texture is too large.";
                return false;
            }

            rgb = new byte[checked(texture.Width * texture.Height * 3)];
            alphaRgb = new byte[checked(texture.Width * texture.Height * 3)];
            int nonBlack = 0;
            int nonTransparent = 0;
            int transparent = 0;
            int minAlpha = 255;
            int maxAlpha = 0;
            try
            {
                for (int y = 0; y < texture.Height; y++)
                {
                    for (int x = 0; x < texture.Width; x++)
                    {
                        if (!TrySampleTextureTexel(memory, texture, x, y, out byte r, out byte g, out byte b, out byte a))
                        {
                            error = $"texture map {textureMap} uses unsupported format {TextureFormatName(texture.Format)}.";
                            return false;
                        }

                        int offset = (y * texture.Width + x) * 3;
                        rgb[offset] = r;
                        rgb[offset + 1] = g;
                        rgb[offset + 2] = b;
                        alphaRgb[offset] = a;
                        alphaRgb[offset + 1] = a;
                        alphaRgb[offset + 2] = a;
                        if (r != 0 || g != 0 || b != 0)
                        {
                            nonBlack++;
                        }

                        if (a == 0)
                        {
                            transparent++;
                        }
                        else
                        {
                            nonTransparent++;
                        }

                        minAlpha = Math.Min(minAlpha, a);
                        maxAlpha = Math.Max(maxAlpha, a);
                    }
                }
            }
            catch (AddressTranslationException exception)
            {
                error = exception.Message;
                return false;
            }

            string tlut = texture.HasTlut
                ? $"base-0x{texture.TlutBaseIndex:X3}/{TlutFormatName(texture.TlutFormat)}"
                : string.Empty;
            int sourceByteLength = TextureSourceByteLength(texture.Width, texture.Height, texture.Format);
            uint sourceHash = sourceByteLength > 0
                ? HashTextureRange(memory, texture.SourceAddress, sourceByteLength)
                : 0;
            string sampleSource = MemorySnapshots?.DescribeSource(CurrentFifoOffset, texture.SourceAddress, sourceByteLength) ?? "main-ram";
            snapshot = new TextureDumpSnapshot(
                textureMap,
                texture.Width,
                texture.Height,
                TextureFormatName(texture.Format),
                texture.SourceAddress,
                texture.TmemEven,
                texture.TmemOdd,
                TextureWrapName(texture.WrapS),
                TextureWrapName(texture.WrapT),
                TextureMagFilterName(texture.MagFilter),
                TextureMinFilterName(texture.MinFilter),
                tlut,
                texture.Mode0,
                texture.Mode1,
                texture.Image0,
                texture.Image1,
                texture.Image2,
                texture.Image3,
                sampleSource,
                sourceByteLength,
                sourceHash,
                nonBlack,
                nonTransparent,
                transparent,
                minAlpha,
                maxAlpha,
                DescribeTextureSamples(rgb, texture.Width, texture.Height));
            return true;
        }

        private bool TrySampleTextureNearest(GameCubeMemory memory, GxTextureState texture, float s, float t, out byte r, out byte g, out byte b, out byte a)
        {
            int x = TextureCoordinateToIndex(s, texture.Width, texture.WrapS);
            int y = TextureCoordinateToIndex(t, texture.Height, texture.WrapT);
            return TrySampleTextureTexel(memory, texture, x, y, out r, out g, out b, out a);
        }

        private static string DescribeTextureSamples(byte[] rgb, int width, int height)
        {
            (string Name, int X, int Y)[] points =
            [
                ("top_left", 0, 0),
                ("center", width / 2, height / 2),
                ("bottom_right", width - 1, height - 1),
            ];

            return string.Join(" | ", points.Select(point =>
            {
                int offset = (Math.Clamp(point.Y, 0, height - 1) * width + Math.Clamp(point.X, 0, width - 1)) * 3;
                return $"{point.Name}@{point.X}/{point.Y}:{rgb[offset]}/{rgb[offset + 1]}/{rgb[offset + 2]}";
            }));
        }

        private static int TextureSourceByteLength(int width, int height, int format)
        {
            if (width <= 0 || height <= 0)
            {
                return 0;
            }

            return format switch
            {
                0 or 8 => checked(((width + 7) / 8) * ((height + 7) / 8) * 32),
                1 or 2 or 9 => checked(((width + 7) / 8) * ((height + 3) / 4) * 32),
                3 or 4 or 5 => checked(((width + 3) / 4) * ((height + 3) / 4) * 32),
                6 => checked(((width + 3) / 4) * ((height + 3) / 4) * 64),
                14 => checked(((width + 7) / 8) * ((height + 7) / 8) * 32),
                _ => 0,
            };
        }

        private uint HashTextureRange(GameCubeMemory memory, uint address, int length)
        {
            if (MemorySnapshots is not null && MemorySnapshots.TryHashRange(CurrentFifoOffset, address, length, out uint snapshotHash))
            {
                return snapshotHash;
            }

            int offset = memory.TranslateMainRam(address, length);
            ReadOnlySpan<byte> data = memory.MainRam.Span.Slice(offset, length);
            uint hash = 2166136261u;
            for (int index = 0; index < data.Length; index++)
            {
                hash ^= data[index];
                hash *= 16777619u;
            }

            return hash;
        }

        private bool TrySampleTextureLinear(GameCubeMemory memory, GxTextureState texture, float s, float t, out byte r, out byte g, out byte b, out byte a)
        {
            SampleCoordinate sampleS = TextureCoordinateToLinearSample(s, texture.Width, texture.WrapS);
            SampleCoordinate sampleT = TextureCoordinateToLinearSample(t, texture.Height, texture.WrapT);
            if (!TrySampleTextureTexel(memory, texture, sampleS.Index0, sampleT.Index0, out byte r00, out byte g00, out byte b00, out byte a00)
                || !TrySampleTextureTexel(memory, texture, sampleS.Index1, sampleT.Index0, out byte r10, out byte g10, out byte b10, out byte a10)
                || !TrySampleTextureTexel(memory, texture, sampleS.Index0, sampleT.Index1, out byte r01, out byte g01, out byte b01, out byte a01)
                || !TrySampleTextureTexel(memory, texture, sampleS.Index1, sampleT.Index1, out byte r11, out byte g11, out byte b11, out byte a11))
            {
                r = 0;
                g = 0;
                b = 0;
                a = 255;
                return false;
            }

            r = BilinearByte(r00, r10, r01, r11, sampleS.Fraction, sampleT.Fraction);
            g = BilinearByte(g00, g10, g01, g11, sampleS.Fraction, sampleT.Fraction);
            b = BilinearByte(b00, b10, b01, b11, sampleS.Fraction, sampleT.Fraction);
            a = BilinearByte(a00, a10, a01, a11, sampleS.Fraction, sampleT.Fraction);
            return true;
        }

        private bool TrySampleTextureTexel(GameCubeMemory memory, GxTextureState texture, int x, int y, out byte r, out byte g, out byte b, out byte a)
        {
            r = 0;
            g = 0;
            b = 0;
            a = 255;
            return texture.Format switch
            {
                0 => TrySampleI4(memory, texture, x, y, out r, out g, out b, out a),
                1 => TrySampleI8(memory, texture, x, y, out r, out g, out b, out a),
                2 => TrySampleIA4(memory, texture, x, y, out r, out g, out b, out a),
                3 => TrySampleIA8(memory, texture, x, y, out r, out g, out b, out a),
                4 => TrySampleRgb565(memory, texture, x, y, out r, out g, out b, out a),
                5 => TrySampleRgb5A3(memory, texture, x, y, out r, out g, out b, out a),
                6 => TrySampleRgba8(memory, texture, x, y, out r, out g, out b, out a),
                8 => TrySampleCI4(memory, texture, x, y, out r, out g, out b, out a),
                9 => TrySampleCI8(memory, texture, x, y, out r, out g, out b, out a),
                14 => TrySampleCmpr(memory, texture, x, y, out r, out g, out b, out a),
                _ => false,
            };
        }

        public bool TryEvaluateTevStages(
            GameCubeMemory? memory,
            GxVertex a,
            GxVertex b,
            GxVertex c,
            float aWeight,
            float bWeight,
            float cWeight,
            byte rasterR,
            byte rasterG,
            byte rasterB,
            byte rasterA,
            out byte r,
            out byte g,
            out byte bValue,
            out byte alpha)
        {
            r = rasterR;
            g = rasterG;
            bValue = rasterB;
            alpha = rasterA;

            int stageCount = TevStageCount;
            if (stageCount <= 0)
            {
                return false;
            }

            GxTevColor rasterBase = new(rasterR, rasterG, rasterB, rasterA);
            GxTevColor[] registers =
            [
                rasterBase,
                _tevRegisterColors[1],
                _tevRegisterColors[2],
                _tevRegisterColors[3],
            ];

            bool evaluatedAny = false;
            IndirectOffsetState indirectOffset = default;
            for (int stage = 0; stage < stageCount; stage++)
            {
                bool hasColorEnv = TryGetTevColorEnv(stage, out uint colorEnv);
                bool hasAlphaEnv = TryGetTevAlphaEnv(stage, out uint alphaEnv);
                if (!hasColorEnv && !hasAlphaEnv)
                {
                    continue;
                }

                TevOrder order = GetTevOrder(stage);
                GxTevColor texture = SampleTevTexture(memory, stage, order, a, b, c, aWeight, bWeight, cWeight, ref indirectOffset);
                GxTevColor raster = SelectTevRasterColor(stage, order, rasterBase, indirectOffset);
                raster = ApplyTevSwap(raster, TevRasterSwapTable(alphaEnv));
                texture = ApplyTevSwap(texture, TevTextureSwapTable(alphaEnv));
                GxTevColor konstColor = GetKonstColor(stage);
                int konstAlpha = GetKonstAlpha(stage);
                GxTevColor previous = registers[0];
                byte outR = previous.R;
                byte outG = previous.G;
                byte outB = previous.B;
                byte outA = previous.A;
                int destination = hasColorEnv ? TevDestination(colorEnv) : hasAlphaEnv ? TevDestination(alphaEnv) : 0;

                if (hasColorEnv)
                {
                    outR = EvaluateTevColorChannel(colorEnv, channel: 0, previous, registers, texture, raster, konstColor);
                    outG = EvaluateTevColorChannel(colorEnv, channel: 1, previous, registers, texture, raster, konstColor);
                    outB = EvaluateTevColorChannel(colorEnv, channel: 2, previous, registers, texture, raster, konstColor);
                }

                if (hasAlphaEnv)
                {
                    outA = EvaluateTevAlpha(alphaEnv, previous, registers, texture, raster, konstAlpha);
                }

                registers[destination] = new GxTevColor(outR, outG, outB, outA);
                evaluatedAny = true;
            }

            if (!evaluatedAny)
            {
                return false;
            }

            GxTevColor output = registers[0];
            r = output.R;
            g = output.G;
            bValue = output.B;
            alpha = output.A;
            return true;
        }

        public bool TryEvaluateTevStagesDetailed(
            GameCubeMemory? memory,
            GxVertex a,
            GxVertex b,
            GxVertex c,
            float aWeight,
            float bWeight,
            float cWeight,
            byte rasterR,
            byte rasterG,
            byte rasterB,
            byte rasterA,
            out byte r,
            out byte g,
            out byte bValue,
            out byte alpha,
            out string stageSummary)
        {
            r = rasterR;
            g = rasterG;
            bValue = rasterB;
            alpha = rasterA;
            stageSummary = string.Empty;

            int stageCount = TevStageCount;
            if (stageCount <= 0)
            {
                return false;
            }

            GxTevColor rasterBase = new(rasterR, rasterG, rasterB, rasterA);
            GxTevColor[] registers =
            [
                rasterBase,
                _tevRegisterColors[1],
                _tevRegisterColors[2],
                _tevRegisterColors[3],
            ];

            StringBuilder summary = new();
            bool evaluatedAny = false;
            IndirectOffsetState indirectOffset = default;
            for (int stage = 0; stage < stageCount; stage++)
            {
                bool hasColorEnv = TryGetTevColorEnv(stage, out uint colorEnv);
                bool hasAlphaEnv = TryGetTevAlphaEnv(stage, out uint alphaEnv);
                if (!hasColorEnv && !hasAlphaEnv)
                {
                    continue;
                }

                TevOrder order = GetTevOrder(stage);
                GxTevColor texture = SampleTevTexture(memory, stage, order, a, b, c, aWeight, bWeight, cWeight, ref indirectOffset, out string textureDetail);
                GxTevColor raster = SelectTevRasterColor(stage, order, rasterBase, indirectOffset);
                raster = ApplyTevSwap(raster, TevRasterSwapTable(alphaEnv));
                texture = ApplyTevSwap(texture, TevTextureSwapTable(alphaEnv));
                GxTevColor konstColor = GetKonstColor(stage);
                int konstAlpha = GetKonstAlpha(stage);
                GxTevColor previous = registers[0];
                byte outR = previous.R;
                byte outG = previous.G;
                byte outB = previous.B;
                byte outA = previous.A;
                int destination = hasColorEnv ? TevDestination(colorEnv) : hasAlphaEnv ? TevDestination(alphaEnv) : 0;

                if (hasColorEnv)
                {
                    outR = EvaluateTevColorChannel(colorEnv, channel: 0, previous, registers, texture, raster, konstColor);
                    outG = EvaluateTevColorChannel(colorEnv, channel: 1, previous, registers, texture, raster, konstColor);
                    outB = EvaluateTevColorChannel(colorEnv, channel: 2, previous, registers, texture, raster, konstColor);
                }

                if (hasAlphaEnv)
                {
                    outA = EvaluateTevAlpha(alphaEnv, previous, registers, texture, raster, konstAlpha);
                }

                if (summary.Length != 0)
                {
                    summary.Append(" | ");
                }

                summary.Append("stage").Append(stage)
                    .Append("{color=0x").Append(colorEnv.ToString("X6", CultureInfo.InvariantCulture))
                    .Append(";alpha=0x").Append(alphaEnv.ToString("X6", CultureInfo.InvariantCulture))
                    .Append(";dst=").Append(destination)
                    .Append(";order=texcoord").Append(order.TexCoord)
                    .Append("/texmap").Append(order.TexMap)
                    .Append("/tex=").Append(order.TextureEnabled ? "on" : "off")
                    .Append("/chan").Append(order.ColorChannel)
                    .Append(";prev=").Append(FormatTevColor(previous))
                    .Append(";raster=").Append(FormatTevColor(raster))
                    .Append(";texture=").Append(FormatTevColor(texture))
                    .Append(";texinfo=").Append(textureDetail)
                    .Append(";konst=").Append(FormatTevColor(konstColor)).Append('/').Append(konstAlpha)
                    .Append(";out=").Append(FormatTevColor(new GxTevColor(outR, outG, outB, outA)))
                    .Append('}');

                registers[destination] = new GxTevColor(outR, outG, outB, outA);
                evaluatedAny = true;
            }

            if (!evaluatedAny)
            {
                return false;
            }

            GxTevColor output = registers[0];
            r = output.R;
            g = output.G;
            bValue = output.B;
            alpha = output.A;
            stageSummary = summary.ToString();
            return true;
        }

        private static string FormatTevColor(GxTevColor color) => $"{color.R}/{color.G}/{color.B}/{color.A}";

        private int TevStageCount
        {
            get
            {
                if (TryGetBp(0x00, out uint genMode))
                {
                    return DecodeGenModeTevStageCount(genMode);
                }

                int count = 0;
                for (int stage = 0; stage < 16; stage++)
                {
                    if (TryGetTevColorEnv(stage, out _)
                        || TryGetTevAlphaEnv(stage, out _))
                    {
                        count = stage + 1;
                    }
                }

                return count;
            }
        }

        private static int DecodeGenModeTevStageCount(uint genMode) =>
            Math.Clamp((int)((genMode >> 10) & 0xF) + 1, 1, 16);

        private static int DecodeGenModeIndirectStageCount(uint genMode) =>
            Math.Clamp((int)((genMode >> 16) & 7), 0, 4);

        private bool TryGetTevColorEnv(int stage, out uint value)
        {
            value = 0;
            if (stage is < 0 or >= 16)
            {
                return false;
            }

            return TryGetBp((byte)(0xC0 + stage * 2), out value);
        }

        private bool TryGetTevAlphaEnv(int stage, out uint value)
        {
            value = 0;
            if (stage is < 0 or >= 16)
            {
                return false;
            }

            return TryGetBp((byte)(0xC1 + stage * 2), out value);
        }

        private GxTevColor SampleTevTexture(GameCubeMemory? memory, int stage, TevOrder order, GxVertex a, GxVertex b, GxVertex c, float aWeight, float bWeight, float cWeight, ref IndirectOffsetState indirectOffset)
        {
            return SampleTevTexture(memory, stage, order, a, b, c, aWeight, bWeight, cWeight, ref indirectOffset, out _);
        }

        private GxTevColor SampleTevTexture(GameCubeMemory? memory, int stage, TevOrder order, GxVertex a, GxVertex b, GxVertex c, float aWeight, float bWeight, float cWeight, ref IndirectOffsetState indirectOffset, out string textureDetail)
        {
            textureDetail = $"texmap{order.TexMap}/texcoord{order.TexCoord}";
            bool hasRegularTexCoord = TryInterpolateTexCoord(a, b, c, order.TexCoord, aWeight, bWeight, cWeight, out float s, out float t);
            if (!hasRegularTexCoord)
            {
                s = 0;
                t = 0;
            }

            TryApplySimpleIndirectOffset(memory, stage, order.TexMap, a, b, c, aWeight, bWeight, cWeight, ref indirectOffset, ref s, ref t);
            if (!order.TextureEnabled || !hasRegularTexCoord)
            {
                textureDetail += order.TextureEnabled ? "/missing-coord" : "/disabled";
                return GxTevColor.One;
            }

            if (order.TexMap is < 0 or >= 8)
            {
                textureDetail += $"/invalid s={FormatFloat(s)} t={FormatFloat(t)}";
                return GxTevColor.One;
            }

            GxTextureState textureState = _textures[order.TexMap];
            if (!textureState.HasImage0 || !textureState.HasImage3 || textureState.Width <= 0 || textureState.Height <= 0)
            {
                textureDetail += $"/unconfigured s={FormatFloat(s)} t={FormatFloat(t)}";
                return GxTevColor.One;
            }

            int x = TextureCoordinateToIndex(s, textureState.Width, textureState.WrapS);
            int y = TextureCoordinateToIndex(t, textureState.Height, textureState.WrapT);
            textureDetail += $"/addr=0x{textureState.SourceAddress:X8}/fmt={TextureFormatName(textureState.Format)}/size={textureState.Width}x{textureState.Height}/wrap={TextureWrapName(textureState.WrapS)}:{TextureWrapName(textureState.WrapT)}/filter={TextureMagFilterName(textureState.MagFilter)}:{TextureMinFilterName(textureState.MinFilter)}/lod=bias{FormatFloat(textureState.LodBias)}:min{FormatFloat(textureState.MinLod)}:max{FormatFloat(textureState.MaxLod)}/s={FormatFloat(s)}/t={FormatFloat(t)}/xy={x}:{y}";
            if (!TrySampleTexture(memory, order.TexMap, s, t, out byte r, out byte g, out byte bValue, out byte alpha))
            {
                textureDetail += "/sample-failed";
                return GxTevColor.One;
            }

            return new GxTevColor(r, g, bValue, alpha);
        }

        private bool TryApplySimpleIndirectOffset(
            GameCubeMemory? memory,
            int tevStage,
            int regularTexMap,
            GxVertex a,
            GxVertex b,
            GxVertex c,
            float aWeight,
            float bWeight,
            float cWeight,
            ref IndirectOffsetState previousOffset,
            ref float s,
            ref float t)
        {
            if (!TryGetIndirectTevStage(tevStage, out IndirectTevStage indirect))
            {
                return false;
            }

            if (indirect.IsRepeatPreviousOffset)
            {
                s = ApplyIndirectWrap(s, indirect.WrapS, regularTexMap, coordinateIsS: true);
                t = ApplyIndirectWrap(t, indirect.WrapT, regularTexMap, coordinateIsS: false);
                s += previousOffset.S;
                t += previousOffset.T;
                previousOffset = new IndirectOffsetState(previousOffset.S, previousOffset.T, BumpAlphaBits: 0);
                return true;
            }

            if (memory is null
                || !indirect.IsSimpleStOffset
                || !TryGetIndirectOrder(indirect.IndTexStage, out IndirectOrder order)
                || !TryInterpolateTexCoord(a, b, c, order.TexCoord, aWeight, bWeight, cWeight, out float indS, out float indT)
                || !TrySampleTexture(memory, order.TexMap, ApplyIndirectCoordScale(indS, indirect.IndTexStage, coordinateIsS: true), ApplyIndirectCoordScale(indT, indirect.IndTexStage, coordinateIsS: false), out byte offsetS, out byte offsetT, out byte offsetU, out _))
            {
                return false;
            }

            s = ApplyIndirectWrap(s, indirect.WrapS, regularTexMap, coordinateIsS: true);
            t = ApplyIndirectWrap(t, indirect.WrapT, regularTexMap, coordinateIsS: false);

            float sOffset = DecodeIndirectOffset(offsetS, indirect.Format, (indirect.Bias & 1) != 0);
            float tOffset = DecodeIndirectOffset(offsetT, indirect.Format, (indirect.Bias & 2) != 0);
            if (!TryApplyIndirectMatrix(indirect.Matrix, sOffset, tOffset, out sOffset, out tOffset))
            {
                return false;
            }

            if (indirect.AddPrevious)
            {
                sOffset += previousOffset.S;
                tOffset += previousOffset.T;
            }

            int bumpAlphaBits = DecodeIndirectBumpAlphaBits(indirect.Alpha, offsetS, offsetT, offsetU);
            previousOffset = new IndirectOffsetState(sOffset, tOffset, bumpAlphaBits);
            s += sOffset;
            t += tOffset;
            return true;
        }

        private float ApplyIndirectCoordScale(float coordinate, int indirectStage, bool coordinateIsS)
        {
            if (!TryGetIndirectCoordScale(indirectStage, out int scaleS, out int scaleT))
            {
                return coordinate;
            }

            int scale = coordinateIsS ? scaleS : scaleT;
            return scale is >= 0 and <= 8
                ? coordinate / (1 << scale)
                : coordinate;
        }

        private static float DecodeIndirectOffset(byte component, int format, bool applyBias)
        {
            if (format == 0)
            {
                int value = component;
                if (applyBias)
                {
                    value -= 128;
                }

                return value / 128f;
            }

            int bits = format switch
            {
                1 => 5,
                2 => 4,
                3 => 3,
                _ => 8,
            };
            int valueWithoutBumpAlpha = component >> (8 - bits);
            if (applyBias)
            {
                valueWithoutBumpAlpha += 1;
            }

            return valueWithoutBumpAlpha / (float)(1 << (bits - 1));
        }

        private static int DecodeIndirectBumpAlphaBits(int selector, byte s, byte t, byte u)
        {
            byte component = selector switch
            {
                1 => s,
                2 => t,
                3 => u,
                _ => 0,
            };
            return component & 0x1F;
        }

        private static GxTevColor SelectTevRasterColor(int stage, TevOrder order, GxTevColor rasterBase, IndirectOffsetState indirectOffset)
        {
            return order.ColorChannel switch
            {
                5 => ReplicateColorComponent(stage == 0 ? (byte)0 : (byte)(indirectOffset.BumpAlphaBits << 3)),
                6 => ReplicateColorComponent(stage == 0 ? (byte)0 : Expand5(indirectOffset.BumpAlphaBits)),
                7 => GxTevColor.Zero,
                _ => rasterBase,
            };
        }

        private float ApplyIndirectWrap(float coordinate, int wrap, int textureMap, bool coordinateIsS)
        {
            if (wrap == 0)
            {
                return coordinate;
            }

            if (wrap == 6)
            {
                return 0;
            }

            if (textureMap is < 0 or >= 8)
            {
                return coordinate;
            }

            GxTextureState texture = _textures[textureMap];
            int size = coordinateIsS ? texture.Width : texture.Height;
            int wrapTexels = wrap switch
            {
                1 => 256,
                2 => 128,
                3 => 64,
                4 => 32,
                5 => 16,
                _ => 0,
            };

            if (size <= 0 || wrapTexels <= 0)
            {
                return coordinate;
            }

            float texelCoordinate = coordinate * size;
            float wrapped = texelCoordinate % wrapTexels;
            if (wrapped < 0)
            {
                wrapped += wrapTexels;
            }

            return wrapped / size;
        }

        private bool TryApplyIndirectMatrix(int matrixSelection, float s, float t, out float outS, out float outT)
        {
            outS = s;
            outT = t;
            if (matrixSelection == 0)
            {
                return true;
            }

            int matrixIndex = matrixSelection switch
            {
                >= 1 and <= 3 => matrixSelection - 1,
                >= 5 and <= 7 => matrixSelection - 5,
                >= 9 and <= 11 => matrixSelection - 9,
                _ => -1,
            };
            if (matrixIndex < 0 || !TryGetIndirectMatrix(matrixIndex, out IndirectMatrix matrix))
            {
                return false;
            }

            float matrixS = (matrix.M00 * s) + (matrix.M01 * t) + matrix.M02;
            float matrixT = (matrix.M10 * s) + (matrix.M11 * t) + matrix.M12;
            if (matrixSelection is >= 5 and <= 7)
            {
                outS = matrixS;
                outT = 0;
            }
            else if (matrixSelection is >= 9 and <= 11)
            {
                outS = 0;
                outT = matrixT;
            }
            else
            {
                outS = matrixS;
                outT = matrixT;
            }

            return float.IsFinite(outS) && float.IsFinite(outT);
        }

        private static bool TryInterpolateTexCoord(GxVertex a, GxVertex b, GxVertex c, int texCoord, float aWeight, float bWeight, float cWeight, out float s, out float t)
        {
            s = 0;
            t = 0;
            if (texCoord is < 0 or > 7
                || !a.TryGetTexCoord(texCoord, out float aTexS, out float aTexT)
                || !b.TryGetTexCoord(texCoord, out float bTexS, out float bTexT)
                || !c.TryGetTexCoord(texCoord, out float cTexS, out float cTexT))
            {
                return false;
            }

            float perspectiveWeight = a.InvW * aWeight + b.InvW * bWeight + c.InvW * cWeight;
            s = aTexS * aWeight + bTexS * bWeight + cTexS * cWeight;
            t = aTexT * aWeight + bTexT * bWeight + cTexT * cWeight;
            if (MathF.Abs(perspectiveWeight) > 0.000001f)
            {
                s = (aTexS * a.InvW * aWeight + bTexS * b.InvW * bWeight + cTexS * c.InvW * cWeight) / perspectiveWeight;
                t = (aTexT * a.InvW * aWeight + bTexT * b.InvW * bWeight + cTexT * c.InvW * cWeight) / perspectiveWeight;
            }

            return true;
        }

        private TevOrder GetTevOrder(int stage)
        {
            byte register = (byte)(0x28 + stage / 2);
            if (!TryGetBp(register, out uint value))
            {
                return default;
            }

            int shift = (stage & 1) * 12;
            return new TevOrder(
                TexMap: (int)((value >> shift) & 7),
                TexCoord: (int)((value >> (shift + 3)) & 7),
                TextureEnabled: ((value >> (shift + 6)) & 1) != 0,
                ColorChannel: (int)((value >> (shift + 7)) & 7));
        }

        private bool TryGetIndirectTevStage(int stage, out IndirectTevStage indirect)
        {
            indirect = default;
            if (stage is < 0 or >= 16 || !TryGetBp((byte)(0x10 + stage), out uint value) || value == 0 || IsDirectIndirectTevStage(value))
            {
                return false;
            }

            indirect = new IndirectTevStage(
                IndTexStage: (int)(value & 3),
                Format: (int)((value >> 2) & 3),
                Bias: (int)((value >> 4) & 7),
                Alpha: (int)((value >> 7) & 3),
                Matrix: (int)((value >> 9) & 0xF),
                WrapS: (int)((value >> 13) & 7),
                WrapT: (int)((value >> 16) & 7),
                UseTexCoordLod: ((value >> 19) & 1) != 0,
                AddPrevious: ((value >> 20) & 1) != 0);
            return true;
        }

        private bool TryGetIndirectOrder(int stage, out IndirectOrder order)
        {
            order = default;
            if (stage is < 0 or >= 4 || !TryGetBp(0x27, out uint value))
            {
                return false;
            }

            int shift = stage * 6;
            order = new IndirectOrder(
                TexMap: (int)((value >> shift) & 7),
                TexCoord: (int)((value >> (shift + 3)) & 7));
            return true;
        }

        private bool TryGetIndirectCoordScale(int stage, out int scaleS, out int scaleT)
        {
            scaleS = 0;
            scaleT = 0;
            if (stage is < 0 or >= 4)
            {
                return false;
            }

            byte register = (byte)(stage < 2 ? 0x25 : 0x26);
            if (!TryGetBp(register, out uint value))
            {
                return false;
            }

            int shift = (stage & 1) * 8;
            scaleS = (int)((value >> shift) & 0xF);
            scaleT = (int)((value >> (shift + 4)) & 0xF);
            return true;
        }

        private bool TryGetIndirectMatrix(int matrixIndex, out IndirectMatrix matrix)
        {
            matrix = default;
            if (matrixIndex is < 0 or > 2
                || !TryGetBp((byte)(0x06 + matrixIndex * 3), out uint row0)
                || !TryGetBp((byte)(0x07 + matrixIndex * 3), out uint row1)
                || !TryGetBp((byte)(0x08 + matrixIndex * 3), out uint row2))
            {
                return false;
            }

            int packedScale = DecodeIndirectMatrixPackedScale(row0, row1, row2);
            float scale = MathF.Pow(2, packedScale - 17);
            matrix = new IndirectMatrix(
                DecodeIndirectMatrixCoefficient(row0 & 0x7FF, scale),
                DecodeIndirectMatrixCoefficient(row1 & 0x7FF, scale),
                DecodeIndirectMatrixCoefficient(row2 & 0x7FF, scale),
                DecodeIndirectMatrixCoefficient((row0 >> 11) & 0x7FF, scale),
                DecodeIndirectMatrixCoefficient((row1 >> 11) & 0x7FF, scale),
                DecodeIndirectMatrixCoefficient((row2 >> 11) & 0x7FF, scale));
            return true;
        }

        private static int DecodeIndirectMatrixPackedScale(uint row0, uint row1, uint row2) =>
            (int)(((row0 >> 22) & 3) | (((row1 >> 22) & 3) << 2) | (((row2 >> 22) & 3) << 4));

        private static float DecodeIndirectMatrixCoefficient(uint raw, float scale)
        {
            int signed = (raw & 0x400) != 0 ? (int)raw - 0x800 : (int)raw;
            return signed / 1024f * scale;
        }

        private static int TevDestination(uint env) => (int)((env >> 22) & 3);

        private static int TevRasterSwapTable(uint alphaEnv) => (int)(alphaEnv & 3);

        private static int TevTextureSwapTable(uint alphaEnv) => (int)((alphaEnv >> 2) & 3);

        private void WriteTevColorRegister(byte register, uint value)
        {
            if (register is < 0xE0 or > 0xE7 || (value & 0x0080_0000) != 0)
            {
                return;
            }

            int index = (register - 0xE0) / 2;
            GxTevColor current = _tevRegisterColors[index];
            if ((register & 1) == 0)
            {
                _tevRegisterColors[index] = new GxTevColor((byte)(value & 0xFF), current.G, current.B, (byte)((value >> 12) & 0xFF));
            }
            else
            {
                _tevRegisterColors[index] = new GxTevColor(current.R, (byte)((value >> 12) & 0xFF), (byte)(value & 0xFF), current.A);
            }
        }

        private void WriteTevKColorRegister(byte register, uint value)
        {
            if (register is < 0xE0 or > 0xE7 || (value & 0x0080_0000) == 0)
            {
                return;
            }

            int index = (register - 0xE0) / 2;
            GxTevColor current = _tevKColors[index];
            if ((register & 1) == 0)
            {
                _tevKColors[index] = new GxTevColor((byte)(value & 0xFF), current.G, current.B, (byte)((value >> 12) & 0xFF));
            }
            else
            {
                _tevKColors[index] = new GxTevColor(current.R, (byte)((value >> 12) & 0xFF), (byte)(value & 0xFF), current.A);
            }
        }

        private GxTevColor GetKonstColor(int stage)
        {
            if (!TryGetKSel(stage, oddStageShift: 14, evenStageShift: 4, out int selector))
            {
                return GxTevColor.One;
            }

            return selector switch
            {
                0x00 => GxTevColor.One,
                0x01 => FractionColor(7, 8),
                0x02 => FractionColor(3, 4),
                0x03 => FractionColor(5, 8),
                0x04 => GxTevColor.Half,
                0x05 => FractionColor(3, 8),
                0x06 => FractionColor(1, 4),
                0x07 => FractionColor(1, 8),
                >= 0x0C and <= 0x0F => _tevKColors[selector - 0x0C],
                >= 0x10 and <= 0x13 => ReplicateColorComponent(_tevKColors[selector - 0x10].R),
                >= 0x14 and <= 0x17 => ReplicateColorComponent(_tevKColors[selector - 0x14].G),
                >= 0x18 and <= 0x1B => ReplicateColorComponent(_tevKColors[selector - 0x18].B),
                >= 0x1C and <= 0x1F => ReplicateColorComponent(_tevKColors[selector - 0x1C].A),
                _ => GxTevColor.Zero,
            };
        }

        private int GetKonstAlpha(int stage)
        {
            if (!TryGetKSel(stage, oddStageShift: 19, evenStageShift: 9, out int selector))
            {
                return 255;
            }

            return selector switch
            {
                0x00 => 255,
                0x01 => FractionByte(7, 8),
                0x02 => FractionByte(3, 4),
                0x03 => FractionByte(5, 8),
                0x04 => 128,
                0x05 => FractionByte(3, 8),
                0x06 => FractionByte(1, 4),
                0x07 => FractionByte(1, 8),
                >= 0x10 and <= 0x13 => _tevKColors[selector - 0x10].R,
                >= 0x14 and <= 0x17 => _tevKColors[selector - 0x14].G,
                >= 0x18 and <= 0x1B => _tevKColors[selector - 0x18].B,
                >= 0x1C and <= 0x1F => _tevKColors[selector - 0x1C].A,
                _ => 0,
            };
        }

        private bool TryGetKSel(int stage, int oddStageShift, int evenStageShift, out int selector)
        {
            selector = 0;
            if (stage is < 0 or >= 16 || !TryGetBp((byte)(0xF6 + stage / 2), out uint value))
            {
                return false;
            }

            int shift = (stage & 1) != 0 ? oddStageShift : evenStageShift;
            selector = (int)((value >> shift) & 0x1F);
            return true;
        }

        private GxTevColor ApplyTevSwap(GxTevColor input, int table)
        {
            TevSwapTable swap = GetTevSwapTable(table);
            return new GxTevColor(
                SwapComponent(input, swap.R),
                SwapComponent(input, swap.G),
                SwapComponent(input, swap.B),
                SwapComponent(input, swap.A));
        }

        private TevSwapTable GetTevSwapTable(int table)
        {
            table = Math.Clamp(table, 0, 3);
            TevSwapTable fallback = DefaultTevSwapTable(table);
            byte registerA = (byte)(0xF6 + table * 2);
            byte registerB = (byte)(registerA + 1);

            int r = fallback.R;
            int g = fallback.G;
            int b = fallback.B;
            int a = fallback.A;
            if (TryGetBp(registerA, out uint valueA))
            {
                r = (int)(valueA & 3);
                g = (int)((valueA >> 2) & 3);
            }

            if (TryGetBp(registerB, out uint valueB))
            {
                b = (int)(valueB & 3);
                a = (int)((valueB >> 2) & 3);
            }

            return new TevSwapTable(r, g, b, a);
        }

        private static TevSwapTable DefaultTevSwapTable(int table) =>
            table switch
            {
                1 => new TevSwapTable(0, 0, 0, 3),
                2 => new TevSwapTable(1, 1, 1, 3),
                3 => new TevSwapTable(2, 2, 2, 3),
                _ => new TevSwapTable(0, 1, 2, 3),
            };

        private static byte SwapComponent(GxTevColor input, int component) =>
            component switch
            {
                0 => input.R,
                1 => input.G,
                2 => input.B,
                _ => input.A,
            };

        private static GxTevColor FractionColor(int numerator, int denominator)
        {
            byte value = FractionByte(numerator, denominator);
            return new GxTevColor(value, value, value, value);
        }

        private static byte FractionByte(int numerator, int denominator) =>
            (byte)((255 * numerator + denominator / 2) / denominator);

        private static GxTevColor ReplicateColorComponent(byte value) => new(value, value, value, value);

        private static byte EvaluateTevColorChannel(uint env, int channel, GxTevColor previous, GxTevColor[] registers, GxTevColor texture, GxTevColor raster, GxTevColor konst)
        {
            GxTevColor a = ColorInput((int)((env >> 12) & 0xF), previous, registers, texture, raster, konst);
            GxTevColor b = ColorInput((int)((env >> 8) & 0xF), previous, registers, texture, raster, konst);
            GxTevColor c = ColorInput((int)((env >> 4) & 0xF), previous, registers, texture, raster, konst);
            GxTevColor d = ColorInput((int)(env & 0xF), previous, registers, texture, raster, konst);
            if (IsTevCompareOperation(env))
            {
                return ApplyTevColorCompare(env, channel, a, b, c, d);
            }

            return ApplyTevArithmetic(
                env,
                ColorComponent(a, channel),
                ColorComponent(b, channel),
                ColorComponent(c, channel),
                ColorComponent(d, channel));
        }

        private static byte EvaluateTevAlpha(uint env, GxTevColor previous, GxTevColor[] registers, GxTevColor texture, GxTevColor raster, int konst)
        {
            int a = AlphaInput((int)((env >> 13) & 7), previous, registers, texture, raster, konst);
            int b = AlphaInput((int)((env >> 10) & 7), previous, registers, texture, raster, konst);
            int c = AlphaInput((int)((env >> 7) & 7), previous, registers, texture, raster, konst);
            int d = AlphaInput((int)((env >> 4) & 7), previous, registers, texture, raster, konst);
            return IsTevCompareOperation(env)
                ? ApplyTevScalarCompare(env, a, b, c, d)
                : ApplyTevArithmetic(env, a, b, c, d);
        }

        private static GxTevColor ColorInput(int selector, GxTevColor previous, GxTevColor[] registers, GxTevColor texture, GxTevColor raster, GxTevColor konst) =>
            selector switch
            {
                0 => previous,
                1 => ReplicateAlpha(previous.A),
                2 => registers[1],
                3 => ReplicateAlpha(registers[1].A),
                4 => registers[2],
                5 => ReplicateAlpha(registers[2].A),
                6 => registers[3],
                7 => ReplicateAlpha(registers[3].A),
                8 => texture,
                9 => ReplicateAlpha(texture.A),
                10 => raster,
                11 => ReplicateAlpha(raster.A),
                12 => GxTevColor.One,
                13 => GxTevColor.Half,
                14 => konst,
                15 => GxTevColor.Zero,
                _ => GxTevColor.Zero,
            };

        private static int AlphaInput(int selector, GxTevColor previous, GxTevColor[] registers, GxTevColor texture, GxTevColor raster, int konst) =>
            selector switch
            {
                0 => previous.A,
                1 => registers[1].A,
                2 => registers[2].A,
                3 => registers[3].A,
                4 => texture.A,
                5 => raster.A,
                6 => konst,
                7 => 0,
                _ => 0,
            };

        private static GxTevColor ReplicateAlpha(byte alpha) => new(alpha, alpha, alpha, alpha);

        private static int ColorComponent(GxTevColor color, int channel) =>
            channel switch
            {
                0 => color.R,
                1 => color.G,
                2 => color.B,
                _ => color.A,
            };

        private static bool IsTevCompareOperation(uint env) => ((env >> 16) & 3) == 3;

        private static byte ApplyTevColorCompare(uint env, int channel, GxTevColor a, GxTevColor b, GxTevColor c, GxTevColor d)
        {
            int compareType = (int)((env >> 20) & 3);
            bool condition = compareType switch
            {
                0 => CompareTevValues(a.R, b.R, env),
                1 => CompareTevValues(PackTevCompare16(a), PackTevCompare16(b), env),
                2 => CompareTevValues(PackTevCompare24(a), PackTevCompare24(b), env),
                _ => CompareTevValues(ColorComponent(a, channel), ColorComponent(b, channel), env),
            };

            int value = ColorComponent(d, channel);
            if (condition)
            {
                value += ColorComponent(c, channel);
            }

            return ClampTevResult(env, value);
        }

        private static byte ApplyTevScalarCompare(uint env, int a, int b, int c, int d)
        {
            int value = d;
            if (CompareTevValues(a, b, env))
            {
                value += c;
            }

            return ClampTevResult(env, value);
        }

        private static bool CompareTevValues(int a, int b, uint env) =>
            ((env >> 18) & 1) != 0 ? a == b : a > b;

        private static int PackTevCompare16(GxTevColor color) =>
            (color.G << 8) | color.R;

        private static int PackTevCompare24(GxTevColor color) =>
            (color.B << 16) | (color.G << 8) | color.R;

        private static byte ApplyTevArithmetic(uint env, int a, int b, int c, int d)
        {
            int mixed = (a * (255 - c) + b * c + 127) / 255;
            int value = ((env >> 18) & 1) != 0
                ? d - mixed
                : d + mixed;

            value += ((env >> 16) & 3) switch
            {
                1 => 128,
                2 => -128,
                _ => 0,
            };

            value = ((env >> 20) & 3) switch
            {
                1 => value * 2,
                2 => value * 4,
                3 => value / 2,
                _ => value,
            };

            return ClampTevResult(env, value);
        }

        private static byte ClampTevResult(uint env, int value) =>
            (byte)Math.Clamp(((env >> 19) & 1) != 0 ? Math.Clamp(value, 0, 255) : value, 0, 255);

        private readonly record struct TevOrder(int TexMap, int TexCoord, bool TextureEnabled, int ColorChannel);

        private readonly record struct TevSwapTable(int R, int G, int B, int A);

        private readonly record struct IndirectOffsetState(float S, float T, int BumpAlphaBits);

        private readonly record struct IndirectTevStage(int IndTexStage, int Format, int Bias, int Alpha, int Matrix, int WrapS, int WrapT, bool UseTexCoordLod, bool AddPrevious)
        {
            public bool IsSimpleStOffset =>
                Format is >= 0 and <= 3
                && Bias is >= 0 and <= 7
                && (Matrix is >= 0 and <= 3 || Matrix is >= 5 and <= 7 || Matrix is >= 9 and <= 11)
                && WrapS is >= 0 and <= 6
                && WrapT is >= 0 and <= 6
                && !UseTexCoordLod;

            public bool IsRepeatPreviousOffset =>
                Format == 0
                && Bias == 0
                && Matrix == 0
                && WrapS == 6
                && WrapT == 6
                && !UseTexCoordLod
                && AddPrevious;
        }

        private readonly record struct IndirectOrder(int TexMap, int TexCoord);

        private readonly record struct IndirectMatrix(float M00, float M01, float M02, float M10, float M11, float M12);

        private readonly record struct SampleCoordinate(int Index0, int Index1, float Fraction);

        private static int TextureCoordinateToIndex(float coordinate, int size, int wrapMode)
        {
            if (size <= 1 || !float.IsFinite(coordinate))
            {
                return 0;
            }

            float normalized = wrapMode switch
            {
                1 => RepeatCoordinate(coordinate),
                2 => MirrorCoordinate(coordinate),
                _ => Math.Clamp(coordinate, 0f, 1f),
            };
            return Math.Clamp((int)MathF.Round(normalized * (size - 1)), 0, size - 1);
        }

        private static SampleCoordinate TextureCoordinateToLinearSample(float coordinate, int size, int wrapMode)
        {
            if (size <= 1 || !float.IsFinite(coordinate))
            {
                return new SampleCoordinate(0, 0, 0);
            }

            float normalized = wrapMode switch
            {
                1 => RepeatCoordinate(coordinate),
                2 => MirrorCoordinate(coordinate),
                _ => Math.Clamp(coordinate, 0f, 1f),
            };
            float texel = normalized * size - 0.5f;
            int index0 = (int)MathF.Floor(texel);
            float fraction = texel - index0;
            int index1 = index0 + 1;
            return new SampleCoordinate(WrapTextureIndex(index0, size, wrapMode), WrapTextureIndex(index1, size, wrapMode), fraction);
        }

        private static int WrapTextureIndex(int index, int size, int wrapMode)
        {
            if (size <= 1)
            {
                return 0;
            }

            return wrapMode switch
            {
                1 => PositiveModulo(index, size),
                2 => MirrorTextureIndex(index, size),
                _ => Math.Clamp(index, 0, size - 1),
            };
        }

        private static int PositiveModulo(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private static int MirrorTextureIndex(int index, int size)
        {
            int period = size * 2;
            int mirrored = PositiveModulo(index, period);
            return mirrored < size ? mirrored : period - mirrored - 1;
        }

        private static byte BilinearByte(byte v00, byte v10, byte v01, byte v11, float s, float t)
        {
            float top = Lerp(v00, v10, s);
            float bottom = Lerp(v01, v11, s);
            return (byte)Math.Clamp((int)MathF.Round(Lerp(top, bottom, t)), 0, 255);
        }

        private static float RepeatCoordinate(float coordinate) =>
            coordinate - MathF.Floor(coordinate);

        private static float MirrorCoordinate(float coordinate)
        {
            float repeated = coordinate % 2f;
            if (repeated < 0f)
            {
                repeated += 2f;
            }

            return repeated <= 1f ? repeated : 2f - repeated;
        }

        public int Stage0TextureMap
        {
            get
            {
                if (!TryGetBp(0x28, out uint tevOrder0))
                {
                    return -1;
                }

                int texMap = (int)(tevOrder0 & 7);
                bool textureEnabled = ((tevOrder0 >> 6) & 1) != 0;
                return textureEnabled ? texMap : -1;
            }
        }

        public int Stage0TexCoord
        {
            get
            {
                if (!TryGetBp(0x28, out uint tevOrder0))
                {
                    return -1;
                }

                int texCoord = (int)((tevOrder0 >> 3) & 7);
                bool textureEnabled = ((tevOrder0 >> 6) & 1) != 0;
                return textureEnabled ? texCoord : -1;
            }
        }

        public bool DepthUpdateEnabled =>
            TryGetBp(0x40, out uint zMode) && ((zMode >> 4) & 1) != 0;

        public bool ColorUpdateEnabled =>
            !TryGetBp(0x41, out uint blendMode) || ((blendMode >> 3) & 1) != 0;

        public bool AlphaUpdateEnabled =>
            TryGetBp(0x41, out uint blendMode) && ((blendMode >> 4) & 1) != 0;

        public bool DepthTestPasses(float incomingDepth, float storedDepth)
        {
            if (!TryGetBp(0x40, out uint zMode) || (zMode & 1) == 0 || !float.IsFinite(incomingDepth))
            {
                return true;
            }

            const float epsilon = 0.0001f;
            int compareMode = (int)((zMode >> 1) & 7);
            return compareMode switch
            {
                0 => false,
                1 => incomingDepth < storedDepth,
                2 => MathF.Abs(incomingDepth - storedDepth) <= epsilon,
                3 => incomingDepth <= storedDepth + epsilon,
                4 => incomingDepth > storedDepth,
                5 => MathF.Abs(incomingDepth - storedDepth) > epsilon,
                6 => incomingDepth >= storedDepth - epsilon,
                7 => true,
                _ => true,
            };
        }

        public bool AlphaTestPasses(byte alpha)
        {
            if (!TryGetBp(0xF3, out uint alphaCompare))
            {
                return true;
            }

            int ref0 = (int)(alphaCompare & 0xFF);
            int ref1 = (int)((alphaCompare >> 8) & 0xFF);
            int comp0 = (int)((alphaCompare >> 16) & 7);
            int comp1 = (int)((alphaCompare >> 19) & 7);
            int op = (int)((alphaCompare >> 22) & 3);
            bool first = CompareAlpha(alpha, ref0, comp0);
            bool second = CompareAlpha(alpha, ref1, comp1);
            return op switch
            {
                0 => first && second,
                1 => first || second,
                2 => first ^ second,
                3 => first == second,
                _ => true,
            };
        }

        public void ApplyBlend(byte[] rgb, byte[] alpha, int rgbIndex, int pixelIndex, ref byte r, ref byte g, ref byte b, ref byte a)
        {
            if (!TryGetBp(0x41, out uint blendMode))
            {
                return;
            }

            byte dstR = rgb[rgbIndex];
            byte dstG = rgb[rgbIndex + 1];
            byte dstB = rgb[rgbIndex + 2];
            byte dstA = alpha[pixelIndex];
            bool blendEnabled = (blendMode & 1) != 0;
            bool logicEnabled = ((blendMode >> 1) & 1) != 0;
            bool subtractEnabled = ((blendMode >> 11) & 1) != 0;
            if (subtractEnabled)
            {
                r = SubtractChannel(dstR, r);
                g = SubtractChannel(dstG, g);
                b = SubtractChannel(dstB, b);
                return;
            }

            if (logicEnabled)
            {
                int operation = (int)((blendMode >> 12) & 0xF);
                r = ApplyLogicOp(operation, r, dstR);
                g = ApplyLogicOp(operation, g, dstG);
                b = ApplyLogicOp(operation, b, dstB);
                return;
            }

            if (!blendEnabled)
            {
                return;
            }

            int dstFactor = (int)((blendMode >> 5) & 7);
            int srcFactor = (int)((blendMode >> 8) & 7);
            r = BlendChannel(r, dstR, a, dstA, srcFactor, dstFactor);
            g = BlendChannel(g, dstG, a, dstA, srcFactor, dstFactor);
            b = BlendChannel(b, dstB, a, dstA, srcFactor, dstFactor);
        }

        public GxTevStage0Mode Stage0Mode
        {
            get
            {
                if (!TryGetBp(0xC0, out uint colorEnv))
                {
                    return GxTevStage0Mode.Unknown;
                }

                int a = (int)((colorEnv >> 12) & 0xF);
                int b = (int)((colorEnv >> 8) & 0xF);
                int c = (int)((colorEnv >> 4) & 0xF);
                int d = (int)(colorEnv & 0xF);
                return (a, b, c, d) switch
                {
                    (15, 15, 15, 10) => GxTevStage0Mode.PassColor,
                    (15, 10, 12, 15) => GxTevStage0Mode.PassColor,
                    (15, 15, 15, 8) => GxTevStage0Mode.Replace,
                    (10, 8, 9, 15) => GxTevStage0Mode.Decal,
                    (10, 12, 8, 15) => GxTevStage0Mode.Blend,
                    (15, 8, 10, 15) => GxTevStage0Mode.Modulate,
                    _ => GxTevStage0Mode.Unknown,
                };
            }
        }

        private static bool CompareAlpha(byte alpha, int reference, int compareMode) =>
            compareMode switch
            {
                0 => false,
                1 => alpha < reference,
                2 => alpha == reference,
                3 => alpha <= reference,
                4 => alpha > reference,
                5 => alpha != reference,
                6 => alpha >= reference,
                7 => true,
                _ => true,
            };

        private static byte BlendChannel(byte source, byte destination, byte sourceAlpha, byte destinationAlpha, int sourceFactor, int destinationFactor)
        {
            int blended = source * BlendFactor(sourceFactor, source, sourceAlpha, destination, destinationAlpha, isSourceFactor: true)
                + destination * BlendFactor(destinationFactor, source, sourceAlpha, destination, destinationAlpha, isSourceFactor: false);
            return (byte)Math.Clamp((blended + 127) / 255, 0, 255);
        }

        private static int BlendFactor(int factor, byte source, byte sourceAlpha, byte destination, byte destinationAlpha, bool isSourceFactor) =>
            factor switch
            {
                0 => 0,
                1 => 255,
                2 => isSourceFactor ? source : destination,
                3 => 255 - (isSourceFactor ? source : destination),
                4 => sourceAlpha,
                5 => 255 - sourceAlpha,
                6 => destinationAlpha,
                7 => 255 - destinationAlpha,
                _ => 255,
            };

        private static byte SubtractChannel(byte destination, byte source) =>
            (byte)Math.Max(0, destination - source);

        private static byte ApplyLogicOp(int operation, byte source, byte destination) =>
            operation switch
            {
                0 => 0,
                1 => (byte)(source & destination),
                2 => (byte)(source & ~destination),
                3 => source,
                4 => (byte)(~source & destination),
                5 => destination,
                6 => (byte)(source ^ destination),
                7 => (byte)(source | destination),
                8 => (byte)~(source | destination),
                9 => (byte)~(source ^ destination),
                10 => (byte)~destination,
                11 => (byte)(source | ~destination),
                12 => (byte)~source,
                13 => (byte)(~source | destination),
                14 => (byte)~(source & destination),
                15 => 255,
                _ => source,
            };

        private bool TrySampleI4(GameCubeMemory memory, GxTextureState texture, int x, int y, out byte r, out byte g, out byte b, out byte a)
        {
            int blockColumns = (texture.Width + 7) / 8;
            int block = (y / 8) * blockColumns + x / 8;
            int offset = block * 32 + (y & 7) * 4 + (x & 7) / 2;
            byte packed = ReadTexture8(memory, texture.SourceAddress + (uint)offset);
            int nibble = (x & 1) == 0 ? packed >> 4 : packed & 0x0F;
            r = g = b = Expand4(nibble);
            a = 255;
            return true;
        }

        private bool TrySampleI8(GameCubeMemory memory, GxTextureState texture, int x, int y, out byte r, out byte g, out byte b, out byte a)
        {
            int blockColumns = (texture.Width + 7) / 8;
            int block = (y / 4) * blockColumns + x / 8;
            int offset = block * 32 + (y & 3) * 8 + (x & 7);
            r = g = b = ReadTexture8(memory, texture.SourceAddress + (uint)offset);
            a = 255;
            return true;
        }

        private bool TrySampleIA4(GameCubeMemory memory, GxTextureState texture, int x, int y, out byte r, out byte g, out byte b, out byte a)
        {
            int blockColumns = (texture.Width + 7) / 8;
            int block = (y / 4) * blockColumns + x / 8;
            int offset = block * 32 + (y & 3) * 8 + (x & 7);
            byte packed = ReadTexture8(memory, texture.SourceAddress + (uint)offset);
            r = g = b = Expand4(packed & 0x0F);
            a = Expand4(packed >> 4);
            return true;
        }

        private bool TrySampleIA8(GameCubeMemory memory, GxTextureState texture, int x, int y, out byte r, out byte g, out byte b, out byte a)
        {
            ushort value = ReadTiled16(memory, texture, x, y);
            a = (byte)(value >> 8);
            r = g = b = (byte)value;
            return true;
        }

        private bool TrySampleRgb565(GameCubeMemory memory, GxTextureState texture, int x, int y, out byte r, out byte g, out byte b, out byte a)
        {
            ushort value = ReadTiled16(memory, texture, x, y);
            r = Expand5((value >> 11) & 0x1F);
            g = Expand6((value >> 5) & 0x3F);
            b = Expand5(value & 0x1F);
            a = 255;
            return true;
        }

        private bool TrySampleRgb5A3(GameCubeMemory memory, GxTextureState texture, int x, int y, out byte r, out byte g, out byte b, out byte a)
        {
            ushort value = ReadTiled16(memory, texture, x, y);
            DecodeRgb5A3(value, out r, out g, out b, out a);
            return true;
        }

        private bool TrySampleRgba8(GameCubeMemory memory, GxTextureState texture, int x, int y, out byte r, out byte g, out byte b, out byte a)
        {
            int blockColumns = (texture.Width + 3) / 4;
            int block = (y / 4) * blockColumns + x / 4;
            int texel = (y & 3) * 4 + (x & 3);
            uint address = texture.SourceAddress + (uint)(block * 64 + texel * 2);
            a = ReadTexture8(memory, address);
            r = ReadTexture8(memory, address + 1);
            g = ReadTexture8(memory, address + 32);
            b = ReadTexture8(memory, address + 33);
            return true;
        }

        private bool TrySampleCI4(GameCubeMemory memory, GxTextureState texture, int x, int y, out byte r, out byte g, out byte b, out byte a)
        {
            int blockColumns = (texture.Width + 7) / 8;
            int block = (y / 8) * blockColumns + x / 8;
            int offset = block * 32 + (y & 7) * 4 + (x & 7) / 2;
            byte packed = ReadTexture8(memory, texture.SourceAddress + (uint)offset);
            int index = (x & 1) == 0 ? packed >> 4 : packed & 0x0F;
            return TrySamplePalette(memory, texture, index, out r, out g, out b, out a);
        }

        private bool TrySampleCI8(GameCubeMemory memory, GxTextureState texture, int x, int y, out byte r, out byte g, out byte b, out byte a)
        {
            int blockColumns = (texture.Width + 7) / 8;
            int block = (y / 4) * blockColumns + x / 8;
            int offset = block * 32 + (y & 3) * 8 + (x & 7);
            int index = ReadTexture8(memory, texture.SourceAddress + (uint)offset);
            return TrySamplePalette(memory, texture, index, out r, out g, out b, out a);
        }

        private bool TrySampleCmpr(GameCubeMemory memory, GxTextureState texture, int x, int y, out byte r, out byte g, out byte b, out byte a)
        {
            a = 255;
            int blockColumns = (texture.Width + 7) / 8;
            int block = (y / 8) * blockColumns + x / 8;
            int subBlock = ((y >> 2) & 1) * 2 + ((x >> 2) & 1);
            uint address = texture.SourceAddress + (uint)(block * 32 + subBlock * 8);
            ushort color0 = ReadTexture16(memory, address);
            ushort color1 = ReadTexture16(memory, address + 2);
            uint selectors = ReadTexture32(memory, address + 4);
            int selectorShift = 30 - 2 * (((y & 3) * 4) + (x & 3));
            int selector = (int)((selectors >> selectorShift) & 3);

            DecodeRgb565(color0, out byte r0, out byte g0, out byte b0);
            DecodeRgb565(color1, out byte r1, out byte g1, out byte b1);
            switch (selector)
            {
                case 0:
                    r = r0;
                    g = g0;
                    b = b0;
                    return true;
                case 1:
                    r = r1;
                    g = g1;
                    b = b1;
                    return true;
                case 2 when color0 > color1:
                    r = (byte)((2 * r0 + r1) / 3);
                    g = (byte)((2 * g0 + g1) / 3);
                    b = (byte)((2 * b0 + b1) / 3);
                    return true;
                case 3 when color0 > color1:
                    r = (byte)((r0 + 2 * r1) / 3);
                    g = (byte)((g0 + 2 * g1) / 3);
                    b = (byte)((b0 + 2 * b1) / 3);
                    return true;
                case 2:
                    r = (byte)((r0 + r1) / 2);
                    g = (byte)((g0 + g1) / 2);
                    b = (byte)((b0 + b1) / 2);
                    return true;
                default:
                    r = 0;
                    g = 0;
                    b = 0;
                    a = 0;
                    return true;
            }
        }

        private bool TrySamplePalette(GameCubeMemory memory, GxTextureState texture, int index, out byte r, out byte g, out byte b, out byte a)
        {
            r = 0;
            g = 0;
            b = 0;
            a = 255;
            if (!texture.HasTlut || !_tlutLoads.TryGetValue(texture.TlutBaseIndex, out GxTlutLoadState tlut))
            {
                return false;
            }

            ushort value = ReadTexture16(memory, tlut.SourceAddress + (uint)(index * sizeof(ushort)));
            switch (texture.TlutFormat)
            {
                case 0:
                    a = (byte)(value >> 8);
                    r = g = b = (byte)value;
                    return true;
                case 1:
                    DecodeRgb565(value, out r, out g, out b);
                    a = 255;
                    return true;
                case 2:
                    DecodeRgb5A3(value, out r, out g, out b, out a);
                    return true;
                default:
                    return false;
            }
        }

        private ushort ReadTiled16(GameCubeMemory memory, GxTextureState texture, int x, int y)
        {
            int blockColumns = (texture.Width + 3) / 4;
            int block = (y / 4) * blockColumns + x / 4;
            uint address = texture.SourceAddress + (uint)(block * 32 + ((y & 3) * 4 + (x & 3)) * 2);
            return ReadTexture16(memory, address);
        }

        private byte ReadTexture8(GameCubeMemory memory, uint address) =>
            MemorySnapshots is not null && MemorySnapshots.TryRead8(CurrentFifoOffset, address, out byte value)
                ? value
                : memory.Read8(address);

        private ushort ReadTexture16(GameCubeMemory memory, uint address) =>
            MemorySnapshots is not null && MemorySnapshots.TryRead16(CurrentFifoOffset, address, out ushort value)
                ? value
                : memory.Read16(address);

        private uint ReadTexture32(GameCubeMemory memory, uint address) =>
            MemorySnapshots is not null && MemorySnapshots.TryRead32(CurrentFifoOffset, address, out uint value)
                ? value
                : memory.Read32(address);

        private static void WriteI4Pixel(GameCubeMemory memory, uint baseAddress, int width, int x, int y, byte r, byte g, byte b)
        {
            int blockColumns = (width + 7) / 8;
            int block = (y / 8) * blockColumns + x / 8;
            uint address = baseAddress + (uint)(block * 32 + (y & 7) * 4 + (x & 7) / 2);
            int nibble = Luma(r, g, b) >> 4;
            byte packed = memory.Read8(address);
            packed = (x & 1) == 0
                ? (byte)((packed & 0x0F) | (nibble << 4))
                : (byte)((packed & 0xF0) | nibble);
            memory.Write8(address, packed);
        }

        private static void WriteI8Pixel(GameCubeMemory memory, uint baseAddress, int width, int x, int y, byte r, byte g, byte b)
        {
            int blockColumns = (width + 7) / 8;
            int block = (y / 4) * blockColumns + x / 8;
            uint address = baseAddress + (uint)(block * 32 + (y & 3) * 8 + (x & 7));
            memory.Write8(address, Luma(r, g, b));
        }

        private static void WriteIA4Pixel(GameCubeMemory memory, uint baseAddress, int width, int x, int y, byte r, byte g, byte b, byte a)
        {
            int blockColumns = (width + 7) / 8;
            int block = (y / 4) * blockColumns + x / 8;
            uint address = baseAddress + (uint)(block * 32 + (y & 3) * 8 + (x & 7));
            memory.Write8(address, (byte)((a & 0xF0) | (Luma(r, g, b) >> 4)));
        }

        private static void WriteIA8Pixel(GameCubeMemory memory, uint baseAddress, int width, int x, int y, byte r, byte g, byte b, byte a)
        {
            uint address = Tiled16Address(baseAddress, width, x, y);
            memory.Write16(address, (ushort)((a << 8) | Luma(r, g, b)));
        }

        private static void WriteRgb565Pixel(GameCubeMemory memory, uint baseAddress, int width, int x, int y, byte r, byte g, byte b)
        {
            uint address = Tiled16Address(baseAddress, width, x, y);
            memory.Write16(address, PackRgb565(r, g, b));
        }

        private static void WriteRgb5A3Pixel(GameCubeMemory memory, uint baseAddress, int width, int x, int y, byte r, byte g, byte b, byte a)
        {
            uint address = Tiled16Address(baseAddress, width, x, y);
            memory.Write16(address, PackRgb5A3(r, g, b, a));
        }

        private static void WriteRgba8Pixel(GameCubeMemory memory, uint baseAddress, int width, int x, int y, byte r, byte g, byte b, byte a)
        {
            int blockColumns = (width + 3) / 4;
            int block = (y / 4) * blockColumns + x / 4;
            int texel = (y & 3) * 4 + (x & 3);
            uint address = baseAddress + (uint)(block * 64 + texel * 2);
            memory.Write8(address, a);
            memory.Write8(address + 1, r);
            memory.Write8(address + 32, g);
            memory.Write8(address + 33, b);
        }

        private static uint Tiled16Address(uint baseAddress, int width, int x, int y)
        {
            int blockColumns = (width + 3) / 4;
            int block = (y / 4) * blockColumns + x / 4;
            return baseAddress + (uint)(block * 32 + ((y & 3) * 4 + (x & 3)) * 2);
        }

        private static byte Luma(byte r, byte g, byte b) =>
            (byte)((r * 299 + g * 587 + b * 114 + 500) / 1000);

        private static ushort PackRgb565(byte r, byte g, byte b) =>
            (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));

        private static ushort PackRgb5A3(byte r, byte g, byte b, byte a)
        {
            if (a >= 224)
            {
                return (ushort)(0x8000 | ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3));
            }

            return (ushort)(((a >> 5) << 12) | ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4));
        }

        private static byte Clamp8(int value) => (byte)Math.Clamp(value, 0, 255);

        private static void DecodeRgb565(ushort value, out byte r, out byte g, out byte b)
        {
            r = Expand5((value >> 11) & 0x1F);
            g = Expand6((value >> 5) & 0x3F);
            b = Expand5(value & 0x1F);
        }

        private static void DecodeRgb5A3(ushort value, out byte r, out byte g, out byte b)
        {
            DecodeRgb5A3(value, out r, out g, out b, out _);
        }

        private static void DecodeRgb5A3(ushort value, out byte r, out byte g, out byte b, out byte a)
        {
            if ((value & 0x8000) != 0)
            {
                r = Expand5((value >> 10) & 0x1F);
                g = Expand5((value >> 5) & 0x1F);
                b = Expand5(value & 0x1F);
                a = 255;
                return;
            }

            a = Expand3((value >> 12) & 0x07);
            r = Expand4((value >> 8) & 0x0F);
            g = Expand4((value >> 4) & 0x0F);
            b = Expand4(value & 0x0F);
        }

        private static byte Expand3(int value) => (byte)((value << 5) | (value << 2) | (value >> 1));

        private static byte Expand4(int value) => (byte)((value << 4) | value);

        private static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));

        private static byte Expand6(int value) => (byte)((value << 2) | (value >> 4));

        private void WriteTlutLoadRegister(byte register, uint value)
        {
            if (register == 0x64)
            {
                _pendingTlutSourceAddress = (value & 0x00FF_FFFF) << 5;
                return;
            }

            if (register != 0x65 || _pendingTlutSourceAddress is not { } sourceAddress)
            {
                return;
            }

            int baseIndex = (int)(value & 0x3FF);
            int entries = (int)((value >> 10) & 0x3FF) * 16;
            _tlutLoads[baseIndex] = new GxTlutLoadState(sourceAddress, entries);
            _pendingTlutSourceAddress = null;
        }

        private void WriteTextureRegister(byte register, uint value)
        {
            if (TryGetTextureSlot(register, TextureRegisterKind.Mode0, out int mode0Slot))
            {
                _textures[mode0Slot].Mode0 = value;
                _textures[mode0Slot].HasMode0 = true;
                return;
            }

            if (TryGetTextureSlot(register, TextureRegisterKind.Mode1, out int mode1Slot))
            {
                _textures[mode1Slot].Mode1 = value;
                _textures[mode1Slot].HasMode1 = true;
                return;
            }

            if (TryGetTextureSlot(register, TextureRegisterKind.Image0, out int image0Slot))
            {
                GxTextureState texture = _textures[image0Slot];
                texture.Image0 = value;
                texture.HasImage0 = true;
                texture.Width = (int)(value & 0x3FF) + 1;
                texture.Height = (int)((value >> 10) & 0x3FF) + 1;
                texture.Format = (int)((value >> 20) & 0xF);
                return;
            }

            if (TryGetTextureSlot(register, TextureRegisterKind.Image1, out int image1Slot))
            {
                GxTextureState texture = _textures[image1Slot];
                texture.Image1 = value;
                texture.HasImage1 = true;
                texture.TmemEven = value & 0x7FFF;
                return;
            }

            if (TryGetTextureSlot(register, TextureRegisterKind.Image2, out int image2Slot))
            {
                GxTextureState texture = _textures[image2Slot];
                texture.Image2 = value;
                texture.HasImage2 = true;
                texture.TmemOdd = value & 0x7FFF;
                return;
            }

            if (TryGetTextureSlot(register, TextureRegisterKind.Tlut, out int tlutSlot))
            {
                GxTextureState texture = _textures[tlutSlot];
                texture.Tlut = value;
                texture.HasTlut = true;
                texture.TlutBaseIndex = (int)(value & 0x3FF);
                texture.TlutFormat = (int)((value >> 10) & 3);
                return;
            }

            if (TryGetTextureSlot(register, TextureRegisterKind.Image3, out int image3Slot))
            {
                GxTextureState texture = _textures[image3Slot];
                texture.Image3 = value;
                texture.HasImage3 = true;
                texture.SourceAddress = (value & 0x00FF_FFFF) << 5;
            }
        }

        private static bool TryGetTextureSlot(byte register, TextureRegisterKind kind, out int slot)
        {
            ReadOnlySpan<byte> registers = kind switch
            {
                TextureRegisterKind.Mode0 => [0x80, 0x81, 0x82, 0x83, 0xA0, 0xA1, 0xA2, 0xA3],
                TextureRegisterKind.Mode1 => [0x84, 0x85, 0x86, 0x87, 0xA4, 0xA5, 0xA6, 0xA7],
                TextureRegisterKind.Image0 => [0x88, 0x89, 0x8A, 0x8B, 0xA8, 0xA9, 0xAA, 0xAB],
                TextureRegisterKind.Image1 => [0x8C, 0x8D, 0x8E, 0x8F, 0xAC, 0xAD, 0xAE, 0xAF],
                TextureRegisterKind.Image2 => [0x90, 0x91, 0x92, 0x93, 0xB0, 0xB1, 0xB2, 0xB3],
                TextureRegisterKind.Image3 => [0x94, 0x95, 0x96, 0x97, 0xB4, 0xB5, 0xB6, 0xB7],
                TextureRegisterKind.Tlut => [0x98, 0x99, 0x9A, 0x9B, 0xB8, 0xB9, 0xBA, 0xBB],
                _ => [],
            };

            slot = registers.IndexOf(register);
            return slot >= 0;
        }

        public static string TextureFormatName(int format) =>
            format switch
            {
                0 => "I4",
                1 => "I8",
                2 => "IA4",
                3 => "IA8",
                4 => "RGB565",
                5 => "RGB5A3",
                6 => "RGBA8",
                8 => "CI4",
                9 => "CI8",
                10 => "CI14",
                14 => "CMPR",
                _ => $"fmt{format}",
            };

        private static string TlutFormatName(int format) =>
            format switch
            {
                0 => "IA8",
                1 => "RGB565",
                2 => "RGB5A3",
                _ => $"tlut{format}",
            };

        private static string TextureWrapName(int wrap) =>
            wrap switch
            {
                0 => "clamp",
                1 => "repeat",
                2 => "mirror",
                _ => $"wrap{wrap}",
            };

        private static string TextureMagFilterName(int filter) =>
            filter == 1 ? "linear" : "nearest";

        private static string TextureMinFilterName(int filter) =>
            filter switch
            {
                0 => "nearest",
                1 => "nearest-mipmap-nearest",
                2 => "nearest-mipmap-linear",
                3 => "reserved",
                4 => "linear",
                5 => "linear-mipmap-nearest",
                6 => "linear-mipmap-linear",
                _ => $"min{filter}",
            };

        private enum TextureRegisterKind
        {
            Mode0,
            Mode1,
            Image0,
            Image1,
            Image2,
            Image3,
            Tlut,
        }

        private readonly record struct GxTlutLoadState(uint SourceAddress, int Entries);

        private sealed class GxTextureState
        {
            public bool HasMode0 { get; set; }
            public bool HasMode1 { get; set; }
            public bool HasImage0 { get; set; }
            public bool HasImage1 { get; set; }
            public bool HasImage2 { get; set; }
            public bool HasImage3 { get; set; }
            public bool HasTlut { get; set; }
            public uint Mode0 { get; set; }
            public uint Mode1 { get; set; }
            public uint Image0 { get; set; }
            public uint Image1 { get; set; }
            public uint Image2 { get; set; }
            public uint Image3 { get; set; }
            public uint Tlut { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int Format { get; set; }
            public uint SourceAddress { get; set; }
            public uint TmemEven { get; set; }
            public uint TmemOdd { get; set; }
            public int TlutBaseIndex { get; set; }
            public int TlutFormat { get; set; }
            public int WrapS => (int)(Mode0 & 3);
            public int WrapT => (int)((Mode0 >> 2) & 3);
            public int MagFilter => (int)((Mode0 >> 4) & 1);
            public int MinFilter => (int)((Mode0 >> 5) & 7);
            public bool DiagLodEnabled => ((Mode0 >> 8) & 1) != 0;
            public float LodBias
            {
                get
                {
                    int raw = (int)((Mode0 >> 9) & 0xFF);
                    if ((raw & 0x80) != 0)
                    {
                        raw -= 0x100;
                    }

                    return raw / 32f;
                }
            }

            public int MaxAnisotropy => (int)((Mode0 >> 19) & 3) switch
            {
                1 => 2,
                2 => 4,
                _ => 1,
            };

            public bool LodClampEnabled => ((Mode0 >> 21) & 1) != 0;
            public float MinLod => HasMode1 ? (Mode1 & 0xFF) / 16f : 0f;
            public float MaxLod => HasMode1 ? ((Mode1 >> 8) & 0xFF) / 16f : 10f;

            public bool HasAnyState => HasMode0 || HasMode1 || HasImage0 || HasImage1 || HasImage2 || HasImage3 || HasTlut;
        }

        private bool TryGetVertexStride(int format, out int stride, out string layout)
        {
            stride = 0;
            layout = string.Empty;
            uint vcdLow = _vcdLow[format];
            uint vcdHigh = _vcdHigh[format];
            uint vatA = _vatA[format];
            uint vatB = _vatB[format];
            uint vatC = _vatC[format];
            List<string> attributes = [];
            for (int bit = 0; bit < 9; bit++)
            {
                if (((vcdLow >> bit) & 1) != 0)
                {
                    stride++;
                    attributes.Add(bit == 0 ? "pnmtx:u8" : $"tex{bit - 1}mtx:u8");
                }
            }

            bool result = AddAttribute((vcdLow >> 9) & 3, ComponentBytes((vatA >> 1) & 7, (vatA & 1) == 0 ? 2 : 3), "pos", ref stride, attributes)
                && AddAttribute((vcdLow >> 11) & 3, ComponentBytes((vatA >> 10) & 7, ((vatA >> 9) & 1) == 0 ? 3 : 9), "nrm", ref stride, attributes)
                && AddAttribute((vcdLow >> 13) & 3, ColorBytes((vatA >> 14) & 7), "col0", ref stride, attributes)
                && AddAttribute((vcdLow >> 15) & 3, ColorBytes((vatA >> 18) & 7), "col1", ref stride, attributes)
                && AddTex((vcdHigh >> 0) & 3, vatA, 21, 22, "tex0", ref stride, attributes)
                && AddTex((vcdHigh >> 2) & 3, vatB, 0, 1, "tex1", ref stride, attributes)
                && AddTex((vcdHigh >> 4) & 3, vatB, 9, 10, "tex2", ref stride, attributes)
                && AddTex((vcdHigh >> 6) & 3, vatB, 18, 19, "tex3", ref stride, attributes)
                && AddTex((vcdHigh >> 8) & 3, vatB, 27, 28, "tex4", ref stride, attributes)
                && AddTex((vcdHigh >> 10) & 3, vatC, 5, 6, "tex5", ref stride, attributes)
                && AddTex((vcdHigh >> 12) & 3, vatC, 14, 15, "tex6", ref stride, attributes)
                && AddTex((vcdHigh >> 14) & 3, vatC, 23, 24, "tex7", ref stride, attributes);
            layout = attributes.Count == 0 ? "no vertex attributes" : string.Join(", ", attributes);
            return result;
        }

        private static bool AddTex(uint descriptor, uint vat, int countBit, int formatBit, string name, ref int stride, List<string> attributes) =>
            AddAttribute(descriptor, ComponentBytes((vat >> formatBit) & 7, ((vat >> countBit) & 1) == 0 ? 1 : 2), name, ref stride, attributes);

        private static bool AddAttribute(uint descriptor, int directBytes, string name, ref int stride, List<string> attributes)
        {
            switch (descriptor)
            {
                case 0:
                    return true;
                case 1 when directBytes > 0:
                    stride += directBytes;
                    attributes.Add($"{name}:direct/{directBytes}");
                    return true;
                case 2:
                    stride++;
                    attributes.Add($"{name}:idx8/1");
                    return true;
                case 3:
                    stride += 2;
                    attributes.Add($"{name}:idx16/2");
                    return true;
                default:
                    return false;
            }
        }

        private static int ComponentBytes(uint format, int components) =>
            format switch
            {
                0 or 1 => components,
                2 or 3 => components * 2,
                4 => components * 4,
                _ => 0,
            };

        private static int ColorBytes(uint format) =>
            format switch
            {
                0 => 2,
                1 => 3,
                2 => 4,
                3 => 2,
                4 => 3,
                5 => 4,
                _ => 0,
            };

        private bool TryReadPosition(byte[] bytes, ref int offset, uint descriptor, uint vatA, GameCubeMemory? memory, out float x, out float y, out float z)
        {
            x = 0;
            y = 0;
            z = 0;
            int components = (vatA & 1) == 0 ? 2 : 3;
            uint componentFormat = (vatA >> 1) & 7;
            int shift = (int)((vatA >> 4) & 0x1F);
            int componentBytes = PositionComponentBytes(componentFormat);
            if (components is not (2 or 3) || componentBytes == 0)
            {
                return false;
            }

            if (descriptor == 1)
            {
                int positionBytes = componentBytes * components;
                if (offset > bytes.Length - positionBytes
                    || !TryReadPositionComponent(bytes, offset, componentFormat, out x)
                    || !TryReadPositionComponent(bytes, offset + componentBytes, componentFormat, out y)
                    || (components == 3 && !TryReadPositionComponent(bytes, offset + componentBytes * 2, componentFormat, out z)))
                {
                    return false;
                }

                x = ScalePositionComponent(x, componentFormat, shift);
                y = ScalePositionComponent(y, componentFormat, shift);
                z = components == 3 ? ScalePositionComponent(z, componentFormat, shift) : 0;
                offset += positionBytes;
                return true;
            }

            return TryReadIndex(bytes, ref offset, descriptor, out int index)
                && TryReadArrayPositionComponent(memory, arrayIndex: 0, index, componentOffset: 0, componentFormat, shift, out x)
                && TryReadArrayPositionComponent(memory, arrayIndex: 0, index, componentOffset: componentBytes, componentFormat, shift, out y)
                && (components == 2 || TryReadArrayPositionComponent(memory, arrayIndex: 0, index, componentOffset: componentBytes * 2, componentFormat, shift, out z));
        }

        private bool TryReadColorOrSkip(byte[] bytes, ref int offset, uint descriptor, uint vatA, GameCubeMemory? memory, out byte r, out byte g, out byte b, out byte a)
        {
            r = 224;
            g = 224;
            b = 224;
            a = 255;
            uint colorFormat = (vatA >> 14) & 7;
            int colorBytes = ColorBytes(colorFormat);
            if (descriptor == 0)
            {
                return true;
            }

            if (colorBytes is not (3 or 4))
            {
                return false;
            }

            if (descriptor == 1)
            {
                if (offset > bytes.Length - colorBytes)
                {
                    return false;
                }

                r = bytes[offset];
                g = bytes[offset + 1];
                b = bytes[offset + 2];
                a = colorBytes == 4 ? bytes[offset + 3] : (byte)255;
                offset += colorBytes;
                return true;
            }

            return TryReadIndex(bytes, ref offset, descriptor, out int index)
                && TryReadArrayColor(memory, arrayIndex: 2, index, colorBytes, out r, out g, out b, out a);
        }

        private bool TryReadTexCoordOrSkip(
            byte[] bytes,
            ref int offset,
            uint descriptor,
            uint vat,
            int countBit,
            int formatBit,
            int arrayIndex,
            GameCubeMemory? memory,
            out bool hasTexCoord,
            out float s,
            out float t)
        {
            hasTexCoord = false;
            s = 0;
            t = 0;
            int components = ((vat >> countBit) & 1) == 0 ? 1 : 2;
            uint componentFormat = (vat >> formatBit) & 7;
            int shift = (int)((vat >> (formatBit + 3)) & 0x1F);
            int componentBytes = PositionComponentBytes(componentFormat);
            if (components is not (1 or 2) || componentBytes == 0)
            {
                return false;
            }

            if (descriptor == 0)
            {
                return true;
            }

            if (descriptor == 1)
            {
                int texBytes = componentBytes * components;
                if (offset > bytes.Length - texBytes
                    || !TryReadPositionComponent(bytes, offset, componentFormat, out s))
                {
                    return false;
                }

                if (components == 2 && !TryReadPositionComponent(bytes, offset + componentBytes, componentFormat, out t))
                {
                    return false;
                }

                s = ScalePositionComponent(s, componentFormat, shift);
                t = components == 2 ? ScalePositionComponent(t, componentFormat, shift) : 0;
                offset += texBytes;
                hasTexCoord = true;
                return true;
            }

            if (!TryReadIndex(bytes, ref offset, descriptor, out int index)
                || !TryReadArrayPositionComponent(memory, arrayIndex, index, componentOffset: 0, componentFormat, shift, out s))
            {
                return false;
            }

            if (components == 2 && !TryReadArrayPositionComponent(memory, arrayIndex, index, componentOffset: componentBytes, componentFormat, shift, out t))
            {
                return false;
            }

            hasTexCoord = true;
            return true;
        }

        private static bool SkipTexAttributes(byte[] bytes, ref int offset, uint vcdHigh, uint vatB, uint vatC)
        {
            return SkipAttribute(bytes, ref offset, (vcdHigh >> 2) & 3, ComponentBytes((vatB >> 1) & 7, (vatB & 1) == 0 ? 1 : 2))
                && SkipAttribute(bytes, ref offset, (vcdHigh >> 4) & 3, ComponentBytes((vatB >> 10) & 7, ((vatB >> 9) & 1) == 0 ? 1 : 2))
                && SkipAttribute(bytes, ref offset, (vcdHigh >> 6) & 3, ComponentBytes((vatB >> 19) & 7, ((vatB >> 18) & 1) == 0 ? 1 : 2))
                && SkipAttribute(bytes, ref offset, (vcdHigh >> 8) & 3, ComponentBytes((vatB >> 28) & 7, ((vatB >> 27) & 1) == 0 ? 1 : 2))
                && SkipAttribute(bytes, ref offset, (vcdHigh >> 10) & 3, ComponentBytes((vatC >> 6) & 7, ((vatC >> 5) & 1) == 0 ? 1 : 2))
                && SkipAttribute(bytes, ref offset, (vcdHigh >> 12) & 3, ComponentBytes((vatC >> 15) & 7, ((vatC >> 14) & 1) == 0 ? 1 : 2))
                && SkipAttribute(bytes, ref offset, (vcdHigh >> 14) & 3, ComponentBytes((vatC >> 24) & 7, ((vatC >> 23) & 1) == 0 ? 1 : 2));
        }

        private static bool SkipAttribute(byte[] bytes, ref int offset, uint descriptor, int directBytes)
        {
            return descriptor switch
            {
                0 => true,
                1 when directBytes > 0 => SkipBytes(bytes, ref offset, directBytes),
                2 => SkipBytes(bytes, ref offset, 1),
                3 => SkipBytes(bytes, ref offset, 2),
                _ => false,
            };
        }

        private static bool SkipBytes(byte[] bytes, ref int offset, int count)
        {
            if (count < 0 || offset > bytes.Length - count)
            {
                return false;
            }

            offset += count;
            return true;
        }

        private static bool TryReadIndex(byte[] bytes, ref int offset, uint descriptor, out int index)
        {
            index = 0;
            if (descriptor == 2)
            {
                if (offset > bytes.Length - 1)
                {
                    return false;
                }

                index = bytes[offset++];
                return true;
            }

            if (descriptor == 3)
            {
                if (offset > bytes.Length - 2)
                {
                    return false;
                }

                index = (bytes[offset] << 8) | bytes[offset + 1];
                offset += 2;
                return true;
            }

            return false;
        }

        private bool TryReadArrayPositionComponent(GameCubeMemory? memory, int arrayIndex, int index, int componentOffset, uint componentFormat, int shift, out float value)
        {
            value = 0;
            int componentBytes = PositionComponentBytes(componentFormat);
            if (memory is null || componentBytes == 0 || !TryResolveArrayAddress(arrayIndex, index, componentOffset, componentBytes, out uint address))
            {
                return false;
            }

            try
            {
                if (!TryReadPositionComponent(memory, address, componentFormat, out value))
                {
                    return false;
                }

                value = ScalePositionComponent(value, componentFormat, shift);
                return true;
            }
            catch (AddressTranslationException)
            {
                return false;
            }
        }

        private bool TryReadArrayColor(GameCubeMemory? memory, int arrayIndex, int index, int colorBytes, out byte r, out byte g, out byte b, out byte a)
        {
            r = 0;
            g = 0;
            b = 0;
            a = 255;
            if (memory is null || !TryResolveArrayAddress(arrayIndex, index, 0, colorBytes, out uint address))
            {
                return false;
            }

            try
            {
                r = memory.Read8(address);
                g = memory.Read8(address + 1);
                b = memory.Read8(address + 2);
                a = colorBytes == 4 ? memory.Read8(address + 3) : (byte)255;
                return true;
            }
            catch (AddressTranslationException)
            {
                return false;
            }
        }

        private bool TryResolveArrayAddress(int arrayIndex, int vertexIndex, int componentOffset, int size, out uint address)
        {
            address = 0;
            int stride = _arrayStrides[arrayIndex];
            if (stride <= 0)
            {
                return false;
            }

            ulong offset = (ulong)(uint)_arrayBases[arrayIndex] + (ulong)(uint)vertexIndex * (uint)stride + (uint)componentOffset;
            if (offset > uint.MaxValue || offset + (uint)size > GameCubeAddress.MainRamSize)
            {
                return false;
            }

            address = GameCubeAddress.MainRamCachedStart + (uint)offset;
            return true;
        }

        private static int PositionComponentBytes(uint format) =>
            format switch
            {
                0 or 1 => 1,
                2 or 3 => 2,
                4 => 4,
                _ => 0,
            };

        private static bool TryReadPositionComponent(byte[] bytes, int offset, uint format, out float value)
        {
            value = 0;
            return format switch
            {
                0 when offset <= bytes.Length - 1 => ReadUnsigned8(bytes, offset, out value),
                1 when offset <= bytes.Length - 1 => ReadSigned8(bytes, offset, out value),
                2 when offset <= bytes.Length - 2 => ReadUnsigned16(bytes, offset, out value),
                3 when offset <= bytes.Length - 2 => ReadSigned16(bytes, offset, out value),
                4 when offset <= bytes.Length - 4 => ReadFloat32(bytes, offset, out value),
                _ => false,
            };
        }

        private static bool TryReadPositionComponent(GameCubeMemory memory, uint address, uint format, out float value)
        {
            value = 0;
            switch (format)
            {
                case 0:
                    value = memory.Read8(address);
                    return true;
                case 1:
                    value = unchecked((sbyte)memory.Read8(address));
                    return true;
                case 2:
                    value = memory.Read16(address);
                    return true;
                case 3:
                    value = unchecked((short)memory.Read16(address));
                    return true;
                case 4:
                    value = BitConverter.Int32BitsToSingle((int)memory.Read32(address));
                    return true;
                default:
                    return false;
            }
        }

        private static bool ReadUnsigned8(byte[] bytes, int offset, out float value)
        {
            value = bytes[offset];
            return true;
        }

        private static bool ReadSigned8(byte[] bytes, int offset, out float value)
        {
            value = unchecked((sbyte)bytes[offset]);
            return true;
        }

        private static bool ReadUnsigned16(byte[] bytes, int offset, out float value)
        {
            value = (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
            return true;
        }

        private static bool ReadSigned16(byte[] bytes, int offset, out float value)
        {
            value = unchecked((short)((bytes[offset] << 8) | bytes[offset + 1]));
            return true;
        }

        private static bool ReadFloat32(byte[] bytes, int offset, out float value)
        {
            value = ReadSingleBigEndian(bytes, offset);
            return true;
        }

        private static float ScalePositionComponent(float value, uint format, int shift)
        {
            if (format is 0 or 1 or 2 or 3 && shift != 0)
            {
                return value / (1 << shift);
            }

            return value;
        }
    }
}
