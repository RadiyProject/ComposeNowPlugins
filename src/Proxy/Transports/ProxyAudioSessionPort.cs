using System.Text;
using ComposeNowPlugins.Application.Services.Processing;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Proxy.Transports;

public sealed class ProxyAudioSessionPort(
    PluginId pluginId,
    AudioSessionConfiguration configuration,
    IRuntimeChannel channel,
    AudioOutputWriter outputWriter,
    SequencedBuffer<int> frameBuffer,
    SequencedBuffer<AudioInputBlock> inputBuffer,
    TaskCompletionSource<ulong> ready,
    SemaphoreSlim creditSignal,
    Action<ulong> markProcessed,
    ILogger logger
) : IAudioSessionPort
{
    private const int RenderInitialPrefillBlocks = 0;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string beginMessage = configuration.Offline
            ? $"render begin {configuration.Epoch} 0 {RenderInitialPrefillBlocks}"
            : $"realtime begin {configuration.Epoch} 0";

        await channel.SendAsync(
            Encoding.UTF8.GetBytes(beginMessage),
            "text/plain",
            true,
            cancellationToken
        );

        TimeSpan timeout = configuration.Offline
            ? TimeSpan.FromSeconds(2)
            : TimeSpan.FromMilliseconds(100);

        try
        {
            await ready.Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            if (configuration.Offline)
            {
                logger.LogWarning(
                    "Offline audio ready confirmation timeout. PluginId={PluginId}, Epoch={Epoch}",
                    pluginId,
                    configuration.Epoch
                );
            }
        }
    }

    public async Task WaitForPermitAsync(CancellationToken cancellationToken)
    {
        if (!configuration.Offline)
        {
            return;
        }

        await creditSignal.WaitAsync(cancellationToken);
    }

    public int TakeFrames(ulong sequence, int fallback)
    {
        if (!configuration.Offline)
        {
            return fallback;
        }

        if (frameBuffer.TryTake(sequence, out int frames) && frames > 0)
        {
            return frames;
        }

        logger.LogWarning(
            "Offline block size marker was not found. PluginId={PluginId}, Seq={Seq}",
            pluginId,
            sequence
        );
        return fallback;
    }

    public async Task<AudioSessionInput?> TakeInputAsync(
        ulong sequence,
        bool required,
        CancellationToken cancellationToken
    )
    {
        AudioInputBlock? block;
        if (required)
        {
            TimeSpan timeout = configuration.Offline
                ? TimeSpan.FromSeconds(10)
                : TimeSpan.FromMilliseconds(250);
            block = await inputBuffer.WaitAsync(sequence, timeout, cancellationToken);
        }
        else
        {
            inputBuffer.TryTake(sequence, out block);
        }

        return block is null
            ? null
            : new AudioSessionInput(block.Frames, block.Audio);
    }

    public ValueTask WriteOutputAsync(
        ulong sequence,
        ulong timestamp,
        int frames,
        ReadOnlyMemory<float> audio,
        CancellationToken cancellationToken
    )
    {
        return new ValueTask(outputWriter.WriteAsync(
            channel,
            configuration.Epoch,
            sequence,
            timestamp,
            configuration.SampleRate,
            configuration.Channels,
            frames,
            audio,
            cancellationToken
        ));
    }

    public void CompleteBlock(ulong sequence)
    {
        markProcessed(sequence);
    }
}
