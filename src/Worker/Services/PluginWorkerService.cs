using ComposeNowPlugins.Domain.Models.Ids;
using ComposeNowPlugins.Application.Services.Processing;
using ComposeNowPlugins.Worker;
using Grpc.Core;

namespace ComposeNowPlugins.Worker.Services;

public sealed class PluginWorkerService(
    IPluginBlockProcessor pluginBlockProcessor
) : PluginWorker.PluginWorkerBase
{
    private readonly IPluginBlockProcessor _pluginBlockProcessor = pluginBlockProcessor;

    public override async Task<ProcessBlockReply> ProcessBlock(
        ProcessBlockRequest request,
        ServerCallContext context
    )
    {
        ReadOnlyMemory<float>? inputAudio = request.HasInputAudio
            ? request.InputAudio.ToArray()
            : null;

        PluginBlockProcessResult result = await _pluginBlockProcessor.ProcessBlockAsync(
            new PluginId(request.PluginId),
            request.Seq,
            request.Frames,
            request.Offline,
            inputAudio,
            context.CancellationToken
        );

        ProcessBlockReply reply = new()
        {
            ShouldSend = result.ShouldSend
        };

        reply.Audio.Add(result.Audio.ToArray());

        result.Dispose();

        return reply;
    }
}
