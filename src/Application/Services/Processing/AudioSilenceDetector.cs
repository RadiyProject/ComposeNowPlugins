namespace ComposeNowPlugins.Application.Services.Processing;

public sealed class AudioSilenceDetector : IAudioSilenceDetector
{
    public bool IsSilent(
        ReadOnlyMemory<float> audio,
        float threshold
    )
    {
        ReadOnlySpan<float> span = audio.Span;

        for (int i = 0; i < span.Length; i++)
        {
            if (Math.Abs(span[i]) > threshold)
            {
                return false;
            }
        }

        return true;
    }
}
