// Transports/Aud0.cs
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ComposeNowPlugins.Transports;

public static class Aud0
{
    public static byte[] Pack(ulong seq, ulong ts, int sampleRate, int channels, ReadOnlyMemory<float> interleaved)
    {
        // предполагаем, что длина кратна channels
        int samples = interleaved.Length; 
        int frames = samples / channels;

        const int HeaderSize = 32; // = 4 + 1 + 1 + 2 + 8 + 8 + 4 + 4
        var payloadBytes = samples * sizeof(float);
        var buf = new byte[HeaderSize + payloadBytes];

        // magic
        buf[0] = (byte)'A'; buf[1] = (byte)'U'; buf[2] = (byte)'D'; buf[3] = (byte)'0';
        buf[4] = 1;                         // version
        buf[5] = (byte)channels;            // channels
        // [6..7] reserved = 0

        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(8),  seq);
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(16), ts);
        BinaryPrimitives.WriteInt32LittleEndian (buf.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian (buf.AsSpan(28), frames);

        // payload float32 LE
        MemoryMarshal.Cast<float, byte>(interleaved.Span).CopyTo(buf.AsSpan(HeaderSize));
        return buf;
    }
}
