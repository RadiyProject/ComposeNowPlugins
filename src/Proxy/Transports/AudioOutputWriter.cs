namespace ComposeNowPlugins.Proxy.Transports;

public sealed class AudioOutputWriter
{
    private const int OutputCompressionRawStreakToSkip = 64;
    private const int OutputCompressionSkipBlocks = 256;
    private const float NearlySilentSampleThreshold = 1e-5f;

    private int _audioOutRawCompressionStreak;
    private int _audioOutCompressionSkipRemaining;

    public async Task WriteAsync(
        IRuntimeChannel channel,
        ulong epoch,
        ulong seq,
        ulong timestamp,
        int sampleRate,
        int channels,
        int framesForBlock,
        ReadOnlyMemory<float> audio,
        CancellationToken cancellationToken
    )
    {
        using Aud1.RentedFrame frame = RentOutputFrame(
            epoch,
            seq,
            timestamp,
            sampleRate,
            channels,
            framesForBlock,
            audio
        );

        UpdateOutputCompressionBackoff(frame);

        await channel.SendAsync(
            frame.Memory,
            "application/octet-stream",
            endOfMessage: true,
            cancellationToken
        );
    }

    private Aud1.RentedFrame RentOutputFrame(
        ulong epoch,
        ulong seq,
        ulong timestamp,
        int sampleRate,
        int channels,
        int framesForBlock,
        ReadOnlyMemory<float> audio
    )
    {
        if (audio.IsEmpty)
        {
            return Aud1.RentSilenceCompressed(
                epoch,
                seq,
                timestamp,
                sampleRate,
                channels,
                framesForBlock
            );
        }

        if (ShouldTryOutputCompression(audio))
        {
            return Aud1.RentPackCompressed(
                epoch,
                seq,
                timestamp,
                sampleRate,
                channels,
                audio
            );
        }

        return Aud1.RentPack(
            epoch,
            seq,
            timestamp,
            sampleRate,
            channels,
            audio
        );
    }

    private bool ShouldTryOutputCompression(ReadOnlyMemory<float> audio)
    {
        if (_audioOutCompressionSkipRemaining <= 0)
        {
            return true;
        }

        if (IsNearlySilent(audio.Span))
        {
            _audioOutCompressionSkipRemaining = 0;
            return true;
        }

        _audioOutCompressionSkipRemaining--;
        return false;
    }

    private static bool IsNearlySilent(ReadOnlySpan<float> audio)
    {
        foreach (float sample in audio)
        {
            if (Math.Abs(sample) > NearlySilentSampleThreshold)
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateOutputCompressionBackoff(Aud1.RentedFrame frame)
    {
        if (frame.Compressed)
        {
            _audioOutRawCompressionStreak = 0;
            return;
        }

        if (frame.CompressedCandidateLength <= 0)
        {
            return;
        }

        _audioOutRawCompressionStreak++;
        if (_audioOutRawCompressionStreak >= OutputCompressionRawStreakToSkip)
        {
            _audioOutCompressionSkipRemaining = OutputCompressionSkipBlocks;
            _audioOutRawCompressionStreak = 0;
        }
    }
}
