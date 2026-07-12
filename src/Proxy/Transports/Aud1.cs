using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace ComposeNowPlugins.Proxy.Transports;

public static class Aud1
{
    private const int HeaderSize = 40;
    private const double MinCompressionSavingsRatio = 0.08d;

    public sealed class RentedFrame : IDisposable
    {
        private byte[]? _buffer;

        internal RentedFrame(
            byte[] buffer,
            int length,
            bool compressed = false,
            int compressedCandidateLength = 0
        )
        {
            _buffer = buffer;
            Length = length;
            Compressed = compressed;
            CompressedCandidateLength = compressedCandidateLength;
        }

        public int Length { get; }
        public bool Compressed { get; private set; }
        public int CompressedCandidateLength { get; private set; }

        public ReadOnlyMemory<byte> Memory =>
            _buffer is null
                ? ReadOnlyMemory<byte>.Empty
                : _buffer.AsMemory(0, Length);

        public void Dispose()
        {
            byte[]? buffer = _buffer;
            if (buffer is null)
            {
                return;
            }

            _buffer = null;
            ArrayPool<byte>.Shared.Return(buffer);
        }

        internal void UpdateCompressionMetadata(
            bool compressed,
            int compressedCandidateLength
        )
        {
            Compressed = compressed;
            CompressedCandidateLength = compressedCandidateLength;
        }
    }

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

        int payloadBytes = samples * sizeof(float);
        byte[] buf = new byte[HeaderSize + payloadBytes];

        WriteHeader(buf, epoch, seq, ts, sampleRate, channels, frames);

        MemoryMarshal.Cast<float, byte>(interleaved.Span)
            .CopyTo(buf.AsSpan(HeaderSize));

        return buf;
    }

    public static RentedFrame RentPack(
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
        int payloadBytes = samples * sizeof(float);
        int length = HeaderSize + payloadBytes;
        byte[] buf = ArrayPool<byte>.Shared.Rent(length);

        WriteHeader(buf.AsSpan(0, HeaderSize), epoch, seq, ts, sampleRate, channels, frames);

        MemoryMarshal.Cast<float, byte>(interleaved.Span)
            .CopyTo(buf.AsSpan(HeaderSize, payloadBytes));

        return new RentedFrame(buf, length);
    }

    public static RentedFrame RentPackCompressed(
        ulong epoch,
        ulong seq,
        ulong ts,
        int sampleRate,
        int channels,
        ReadOnlyMemory<float> interleaved
    )
    {
        RentedFrame raw = RentPack(
            epoch,
            seq,
            ts,
            sampleRate,
            channels,
            interleaved
        );

        RentedFrame? compressed = TryCompress(
            raw.Memory.Span.Slice(HeaderSize),
            epoch,
            seq,
            ts,
            sampleRate,
            channels,
            interleaved.Length / channels,
            out int compressedCandidateLength
        );

        if (compressed is null)
        {
            raw.UpdateCompressionMetadata(compressed: false, compressedCandidateLength);
            return raw;
        }

        raw.Dispose();
        return compressed;
    }

    public static RentedFrame RentSilenceCompressed(
        ulong epoch,
        ulong seq,
        ulong ts,
        int sampleRate,
        int channels,
        int frames
    )
    {
        RentedFrame raw = RentSilence(
            epoch,
            seq,
            ts,
            sampleRate,
            channels,
            frames
        );

        RentedFrame? compressed = TryCompress(
            raw.Memory.Span.Slice(HeaderSize),
            epoch,
            seq,
            ts,
            sampleRate,
            channels,
            frames,
            out int compressedCandidateLength
        );

        if (compressed is null)
        {
            raw.UpdateCompressionMetadata(compressed: false, compressedCandidateLength);
            return raw;
        }

        raw.Dispose();
        return compressed;
    }

    public static RentedFrame RentSilence(
        ulong epoch,
        ulong seq,
        ulong ts,
        int sampleRate,
        int channels,
        int frames
    )
    {
        int samples = checked(frames * channels);
        int payloadBytes = checked(samples * sizeof(float));
        int length = HeaderSize + payloadBytes;
        byte[] buf = ArrayPool<byte>.Shared.Rent(length);

        WriteHeader(buf.AsSpan(0, HeaderSize), epoch, seq, ts, sampleRate, channels, frames);
        buf.AsSpan(HeaderSize, payloadBytes).Clear();

        return new RentedFrame(buf, length);
    }

    private static void WriteHeader(
        Span<byte> buf,
        ulong epoch,
        ulong seq,
        ulong ts,
        int sampleRate,
        int channels,
        int frames
    )
    {
        buf[0] = (byte)'A';
        buf[1] = (byte)'U';
        buf[2] = (byte)'D';
        buf[3] = (byte)'1';

        buf[4] = 1;
        buf[5] = (byte)channels;
        buf[6] = 0;
        buf[7] = 0;

        BinaryPrimitives.WriteUInt64LittleEndian(buf.Slice(8), epoch);
        BinaryPrimitives.WriteUInt64LittleEndian(buf.Slice(16), seq);
        BinaryPrimitives.WriteUInt64LittleEndian(buf.Slice(24), ts);
        BinaryPrimitives.WriteInt32LittleEndian(buf.Slice(32), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(buf.Slice(36), frames);
    }

    private static RentedFrame? TryCompress(
        ReadOnlySpan<byte> raw,
        ulong epoch,
        ulong seq,
        ulong ts,
        int sampleRate,
        int channels,
        int frames,
        out int compressedCandidateLength
    )
    {
        using MemoryStream compressedStream = new();
        using (ZLibStream deflate = new(
                   compressedStream,
                   CompressionLevel.Fastest,
                   leaveOpen: true
               ))
        {
            deflate.Write(raw);
        }

        compressedCandidateLength = compressedStream.Length > int.MaxValue - 48
            ? int.MaxValue
            : checked((int)compressedStream.Length + 48);

        if (compressedStream.Length <= 0 ||
            !HasEnoughSavings(compressedCandidateLength, raw.Length + HeaderSize))
        {
            return null;
        }

        int compressedBytes = checked((int)compressedStream.Length);
        int length = 48 + compressedBytes;
        byte[] buf = ArrayPool<byte>.Shared.Rent(length);
        Span<byte> header = buf.AsSpan(0, 48);

        header[0] = (byte)'A';
        header[1] = (byte)'O';
        header[2] = (byte)'Z';
        header[3] = (byte)'1';
        header[4] = 1;
        header[5] = (byte)channels;
        header[6] = 0;
        header[7] = 0;

        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(8), epoch);
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(16), seq);
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(24), ts);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(32), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(36), frames);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(40), raw.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(44), compressedBytes);

        compressedStream.ToArray().AsSpan(0, compressedBytes).CopyTo(buf.AsSpan(48, compressedBytes));

        return new RentedFrame(
            buf,
            length,
            compressed: true,
            compressedCandidateLength: length
        );
    }

    private static bool HasEnoughSavings(int compressedLength, int rawLength)
    {
        if (compressedLength <= 0 || rawLength <= 0)
        {
            return false;
        }

        return compressedLength <= rawLength * (1d - MinCompressionSavingsRatio);
    }

}
