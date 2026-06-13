namespace NgcSharp.App;

public static class SegaPrsDecoder
{
    public const int DefaultMaxOutputBytes = 16 * 1024 * 1024;

    public static bool TryDecode(
        ReadOnlyMemory<byte> source,
        out SegaPrsDecodeResult result,
        out string? failure,
        int maxOutputBytes = DefaultMaxOutputBytes)
    {
        result = default;
        failure = null;
        string? failureMessage = null;
        if (source.IsEmpty)
        {
            failure = "source is empty";
            return false;
        }

        List<byte> decoded = [];
        int input = 1;
        byte flags = source.Span[0];
        int remainingFlagBits = 8;

        bool TryReadByte(out byte value)
        {
            value = 0;
            if (input > maxOutputBytes || input >= source.Length)
            {
                failureMessage = $"input read out of range at source offset 0x{input:X} after 0x{decoded.Count:X} decoded byte(s)";
                return false;
            }

            value = source.Span[input];
            input++;
            return true;
        }

        bool TryReadBit(out int bit)
        {
            bit = 0;
            if (remainingFlagBits == 0)
            {
                if (!TryReadByte(out flags))
                {
                    return false;
                }

                remainingFlagBits = 8;
            }

            bit = flags & 1;
            flags >>= 1;
            remainingFlagBits--;
            return true;
        }

        bool TryAppendCopy(int sourceIndex, int count)
        {
            if (sourceIndex < 0 || count < 0 || decoded.Count + count > maxOutputBytes)
            {
                failureMessage = $"copy out of range sourceIndex=0x{sourceIndex:X} count=0x{count:X} output=0x{decoded.Count:X}";
                return false;
            }

            for (int index = 0; index < count; index++)
            {
                int readIndex = sourceIndex + index;
                if ((uint)readIndex >= (uint)decoded.Count)
                {
                    failureMessage = $"copy read out of range readIndex=0x{readIndex:X} count=0x{count:X} output=0x{decoded.Count:X}";
                    return false;
                }

                decoded.Add(decoded[readIndex]);
            }

            return true;
        }

        while (decoded.Count <= maxOutputBytes)
        {
            if (!TryReadBit(out int commandBit))
            {
                failure = failureMessage ?? "could not read command bit";
                return false;
            }

            if (commandBit == 1)
            {
                if (!TryReadByte(out byte literal))
                {
                    failure = failureMessage ?? "could not read literal byte";
                    return false;
                }

                decoded.Add(literal);
                continue;
            }

            if (!TryReadBit(out int longCopyBit))
            {
                failure = failureMessage ?? "could not read copy mode bit";
                return false;
            }

            if (longCopyBit == 1)
            {
                if (!TryReadByte(out byte tokenLow) || !TryReadByte(out byte tokenHigh))
                {
                    failure = failureMessage ?? "could not read long-copy token";
                    return false;
                }

                int token = tokenLow | (tokenHigh << 8);
                if (token == 0)
                {
                    result = new SegaPrsDecodeResult(decoded.ToArray(), input, flags, remainingFlagBits);
                    return true;
                }

                int offset = unchecked((int)(0xFFFF_E000u | ((uint)token >> 3)));
                int count = token & 7;
                if (count == 0)
                {
                    if (!TryReadByte(out byte extendedCount))
                    {
                        failure = failureMessage ?? "could not read extended long-copy count";
                        return false;
                    }

                    count = extendedCount + 1;
                }
                else
                {
                    count += 2;
                }

                if (!TryAppendCopy(decoded.Count + offset, count))
                {
                    failure = failureMessage ?? "long-copy append failed";
                    return false;
                }

                continue;
            }

            if (!TryReadBit(out int firstLengthBit) || !TryReadBit(out int secondLengthBit) || !TryReadByte(out byte shortOffsetByte))
            {
                failure = failureMessage ?? "could not read short-copy token";
                return false;
            }

            int shortCountCode = (firstLengthBit << 1) | secondLengthBit;
            int shortOffset = unchecked((int)(0xFFFF_FF00u | shortOffsetByte));
            if (!TryAppendCopy(decoded.Count + shortOffset, shortCountCode + 2))
            {
                failure = failureMessage ?? "short-copy append failed";
                return false;
            }
        }

        failure = $"decoded output exceeded 0x{maxOutputBytes:X} byte limit";
        return false;
    }

    public static uint EstimateInstructionCount(int sourceBytesConsumed, int outputLength)
    {
        ulong estimate = 32ul + (uint)Math.Max(0, sourceBytesConsumed) * 24ul + (uint)Math.Max(0, outputLength) * 12ul;
        return checked((uint)Math.Min(estimate, int.MaxValue));
    }
}

public readonly record struct SegaPrsDecodeResult(
    byte[] Output,
    int SourceBytesConsumed,
    byte LastFlagByte,
    int BitsRemaining);
