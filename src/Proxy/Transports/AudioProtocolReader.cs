using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace ComposeNowPlugins.Proxy.Transports;

public static class AudioProtocolReader
{
    public static string ReadText(ReadOnlySpan<byte> payload)
    {
        return Encoding.UTF8.GetString(payload).Trim();
    }

    public static bool TryParseHello(
        string text,
        out HelloOptions? hello
    )
    {
        hello = null;

        if (!text.StartsWith("hello ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = text.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries
        );

        if (parts.Length < 5)
        {
            return true;
        }

        if (!int.TryParse(parts[1], out int sampleRate) ||
            !int.TryParse(parts[2], out int blockSize) ||
            !int.TryParse(parts[3], out int channels))
        {
            return true;
        }

        hello = new HelloOptions(
            sampleRate,
            blockSize,
            channels,
            parts[4]
        );

        return true;
    }

    public static bool TryHandleCreditMessage(
        ReadOnlySpan<byte> span,
        Action<int> addCredits,
        SemaphoreSlim creditSignal
    )
    {
        if (span.Length != 8 ||
            span[0] != 'C' ||
            span[1] != 'R' ||
            span[2] != 'D' ||
            span[3] != '0')
        {
            return false;
        }

        uint blocks = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);

        if (blocks == 0)
        {
            return true;
        }

        int creditsToAdd = blocks > int.MaxValue
            ? int.MaxValue
            : (int)blocks;

        addCredits(creditsToAdd);

        for (int i = 0; i < creditsToAdd; i++)
        {
            creditSignal.Release();
        }

        return true;
    }

    public static bool TryParseEvt1Message(
        ReadOnlySpan<byte> span,
        out ulong seq,
        out int blockFrames,
        out List<PluginEventInput> events
    )
    {
        seq = 0;
        blockFrames = 0;
        events = [];

        if (span.Length < 4 ||
            span[0] != 'E' ||
            span[1] != 'V' ||
            span[2] != 'T' ||
            span[3] != '1')
        {
            return false;
        }

        return TryParseEvt1(
            span,
            out seq,
            out blockFrames,
            out events
        );
    }

    public static bool TryParseAudioInputMessage(
        ReadOnlySpan<byte> span,
        out ulong seq,
        out AudioInputBlock? inputBlock
    )
    {
        if (TryParseAin1Message(
                span,
                out seq,
                out inputBlock
            ))
        {
            return true;
        }

        return TryParseAiz1Message(
            span,
            out seq,
            out inputBlock
        );
    }

    private static bool TryParseAiz1Message(
        ReadOnlySpan<byte> span,
        out ulong seq,
        out AudioInputBlock? inputBlock
    )
    {
        seq = 0;
        inputBlock = null;

        if (span.Length < 28 ||
            span[0] != 'A' ||
            span[1] != 'I' ||
            span[2] != 'Z' ||
            span[3] != '1')
        {
            return false;
        }

        seq = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(4, 8));
        int uncompressedBytes = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(20, 4));
        int compressedBytes = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(24, 4));

        if (uncompressedBytes <= 0 ||
            compressedBytes <= 0 ||
            span.Length < 28 + compressedBytes)
        {
            return false;
        }

        byte[] uncompressed = new byte[uncompressedBytes];

        try
        {
            using MemoryStream source = new(span.Slice(28, compressedBytes).ToArray());
            using ZLibStream deflate = new(source, CompressionMode.Decompress);
            int totalRead = 0;
            while (totalRead < uncompressedBytes)
            {
                int read = deflate.Read(
                    uncompressed,
                    totalRead,
                    uncompressedBytes - totalRead
                );

                if (read <= 0)
                {
                    break;
                }

                totalRead += read;
            }

            if (totalRead != uncompressedBytes)
            {
                return false;
            }
        }
        catch (InvalidDataException)
        {
            return false;
        }

        return TryParseAin1Message(
            uncompressed,
            out seq,
            out inputBlock
        );
    }

    private static bool TryParseAin1Message(
        ReadOnlySpan<byte> span,
        out ulong seq,
        out AudioInputBlock? inputBlock
    )
    {
        seq = 0;
        inputBlock = null;

        if (span.Length < 20 ||
            span[0] != 'A' ||
            span[1] != 'I' ||
            span[2] != 'N' ||
            span[3] != '1')
        {
            return false;
        }

        seq = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(4, 8));
        int frames = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(12, 4));
        int channels = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(16, 4));

        if (frames <= 0 || channels <= 0)
        {
            return false;
        }

        int samples;
        try
        {
            samples = checked(frames * channels);
        }
        catch (OverflowException)
        {
            return false;
        }

        int bytes;
        try
        {
            bytes = checked(samples * sizeof(float));
        }
        catch (OverflowException)
        {
            return false;
        }

        if (span.Length < 20 + bytes)
        {
            return false;
        }

        float[] audio = new float[samples];
        ReadOnlySpan<byte> payload = span.Slice(20, bytes);

        for (int i = 0; i < samples; ++i)
        {
            audio[i] = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(i * sizeof(float), sizeof(float)))
            );
        }

        inputBlock = new AudioInputBlock(frames, channels, audio);
        return true;
    }

    private static bool TryParseEvt1(
        ReadOnlySpan<byte> span,
        out ulong seq,
        out int blockFrames,
        out List<PluginEventInput> events
    )
    {
        events = [];
        seq = 0;
        blockFrames = 0;

        if (span.Length < 20)
        {
            return false;
        }

        if (!(span[0] == 'E' && span[1] == 'V' && span[2] == 'T' && span[3] == '1'))
        {
            return false;
        }

        seq = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(4, 8));
        blockFrames = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(12, 4));
        uint cnt = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16, 4));

        if (blockFrames <= 0)
        {
            return false;
        }

        int pos = 20;
        const int entrySize = 12;

        if (cnt > int.MaxValue / entrySize)
        {
            return false;
        }

        if (span.Length < pos + entrySize * (int)cnt)
        {
            return false;
        }

        for (uint i = 0; i < cnt; i++, pos += entrySize)
        {
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(pos + 0, 2));
            ushort pitch = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(pos + 2, 2));

            float velocity = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(span.Slice(pos + 4, 4))
            );

            ushort offset = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(pos + 8, 2));

            events.Add(new PluginEventInput(type, pitch, velocity, offset));
        }

        events.Sort((left, right) =>
            left.Offset != right.Offset
                ? left.Offset.CompareTo(right.Offset)
                : left.Type.CompareTo(right.Type)
        );

        return true;
    }
}
