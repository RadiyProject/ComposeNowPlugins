using ComposeNowPlugins.Proxy.Configurations;
using ComposeNowPlugins.Application.Services.Processing;
using ComposeNowPlugins.Domain.Models.Ids;
using ComposeNowPlugins.WorkerClient;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;

namespace ComposeNowPlugins.Proxy.Services.Processing;

public sealed class GrpcPluginBlockProcessor : IPluginBlockProcessor
{
    private readonly PluginWorker.PluginWorkerClient _client;

    public GrpcPluginBlockProcessor(IOptions<PluginWorkerOptions> options)
    {
        GrpcChannel channel = GrpcChannel.ForAddress(options.Value.Address);
        _client = new PluginWorker.PluginWorkerClient(channel);
    }

    public async Task<PluginBlockProcessResult> ProcessBlockAsync(
        PluginId pluginId,
        ulong seq,
        int frames,
        bool offline,
        ReadOnlyMemory<float>? inputAudio,
        CancellationToken cancellationToken
    )
    {
        ProcessBlockRequest request = new()
        {
            PluginId = pluginId.GetValue(),
            Seq = seq,
            Frames = frames,
            Offline = offline,
            HasInputAudio = inputAudio.HasValue
        };

        if (inputAudio.HasValue)
        {
            request.InputAudio.Add(inputAudio.Value.ToArray());
        }

        ProcessBlockReply reply = await _client.ProcessBlockAsync(
            request,
            cancellationToken: cancellationToken
        );

        return new PluginBlockProcessResult(
            reply.Audio.ToArray(),
            reply.ShouldSend
        );
    }
}
