using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ComposeNowPlugins.Transports;

public static class Aud1
{
    public static byte[] Pack(
        ulong epoch,
        ulong seq,
        ulong ts,
        int sampleRate,
        int channels,
        ReadOnlyMemory<float> interleaved
    )
    {
        int samples = interleaved.Length;
        int frames = samples / channels;

        const int HeaderSize = 40;
        int payloadBytes = samples * sizeof(float);
        byte[] buf = new byte[HeaderSize + payloadBytes];

        buf[0] = (byte)'A';
        buf[1] = (byte)'U';
        buf[2] = (byte)'D';
        buf[3] = (byte)'1';

        buf[4] = 1;
        buf[5] = (byte)channels;

        // flags/reserved [6..7]
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(8), epoch);
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(16), seq);
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(24), ts);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(32), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(36), frames);

        MemoryMarshal.Cast<float, byte>(interleaved.Span)
            .CopyTo(buf.AsSpan(HeaderSize));

        return buf;
    }
}