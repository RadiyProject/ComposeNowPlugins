using System.Diagnostics;

namespace ComposeNowPlugins.Application.Services.Processing;

public sealed class AudioSessionCoordinator(
    IPluginBlockProcessor pluginBlockProcessor
) : IAudioSessionCoordinator
{
    private readonly IPluginBlockProcessor _pluginBlockProcessor = pluginBlockProcessor;

    public async Task RunAsync(
        AudioSessionConfiguration configuration,
        IAudioSessionPort port,
        CancellationToken cancellationToken
    )
    {
        await port.StartAsync(cancellationToken);

        TimeSpan period = TimeSpan.FromSeconds(
            (double)configuration.BlockSize / configuration.SampleRate
        );
        long nextDeadline = Stopwatch.GetTimestamp();
        long periodTicks = (long)(period.TotalSeconds * Stopwatch.Frequency);

        ulong sequence = 0;
        ulong timestamp = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            await port.WaitForPermitAsync(cancellationToken);

            int frames = port.TakeFrames(sequence, configuration.BlockSize);
            AudioSessionInput? input = await port.TakeInputAsync(
                sequence,
                configuration.RequiresInputAudio,
                cancellationToken
            );

            if (input is not null)
            {
                frames = input.Frames;
            }

            using PluginBlockProcessResult result = await _pluginBlockProcessor.ProcessBlockAsync(
                configuration.PluginId,
                configuration.Epoch,
                sequence,
                frames,
                configuration.Offline,
                input?.Audio,
                cancellationToken
            );

            if (result.ShouldSend)
            {
                await port.WriteOutputAsync(
                    sequence,
                    timestamp,
                    frames,
                    result.Audio,
                    cancellationToken
                );
            }

            port.CompleteBlock(sequence);
            sequence++;
            timestamp += (ulong)frames;

            if (!configuration.Offline && !configuration.RequiresInputAudio)
            {
                nextDeadline += periodTicks;
                long now = Stopwatch.GetTimestamp();
                if (now - nextDeadline > periodTicks * 2)
                {
                    nextDeadline = now + periodTicks;
                }

                TimeSpan delay = Stopwatch.GetElapsedTime(
                    Stopwatch.GetTimestamp(),
                    nextDeadline
                );
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }
    }
}
