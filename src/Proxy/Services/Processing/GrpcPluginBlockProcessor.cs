using System.Collections.Concurrent;
using System.Buffers.Binary;
using ComposeNowPlugins.Application.Services.Processing;
using ComposeNowPlugins.Domain.Models.Ids;
using ComposeNowPlugins.WorkerClient;
using Grpc.Net.Client;
using Grpc.Core;
using Google.Protobuf;

namespace ComposeNowPlugins.Proxy.Services.Processing;

public sealed class GrpcPluginBlockProcessor(
    IPluginWorkerRouter workerRouter
) : IPluginBlockProcessor, IDisposable
{
    private static readonly TimeSpan WorkerCallTimeout = TimeSpan.FromSeconds(30);

    private readonly IPluginWorkerRouter _workerRouter = workerRouter;
    private readonly ConcurrentDictionary<string, GrpcChannel> _channels = new();

    public async Task<PluginBlockProcessResult> ProcessBlockAsync(
        PluginId pluginId,
        ulong epoch,
        ulong seq,
        int frames,
        bool offline,
        ReadOnlyMemory<float>? inputAudio,
        CancellationToken cancellationToken
    )
    {
        ReadOnlyMemory<float> audio = inputAudio.GetValueOrDefault();
        bool hasInputAudio = !audio.IsEmpty;
        ProcessBlockRequest request = new()
        {
            PluginId = pluginId.GetValue(),
            Epoch = epoch,
            Seq = seq,
            Frames = frames,
            Offline = offline,
            HasInputAudio = hasInputAudio
        };

        if (hasInputAudio)
        {
            request.InputAudio = EncodeAudio(audio.Span);
        }

        HashSet<string> excludedWorkers = [];
        for (int attempt = 0; attempt < 2; attempt++)
        {
            PluginWorkerEndpoint endpoint = await _workerRouter.ResolveAsync(pluginId, excludedWorkers);
            PluginWorker.PluginWorkerClient client = GetClient(endpoint.Address);

            try
            {
                ProcessBlockReply reply = await client.ProcessBlockAsync(
                    request,
                    deadline: DateTime.UtcNow + WorkerCallTimeout,
                    cancellationToken: cancellationToken
                );

                return new PluginBlockProcessResult(
                    DecodeAudio(reply.Audio.Span),
                    reply.ShouldSend
                );
            }
            catch (RpcException exception) when (
                (exception.StatusCode is StatusCode.Unavailable or
                    StatusCode.DeadlineExceeded or
                    StatusCode.Aborted) &&
                attempt == 0 &&
                !cancellationToken.IsCancellationRequested
            )
            {
                RemoveChannel(endpoint.Address);
                excludedWorkers.Add(endpoint.WorkerId);
                await _workerRouter.InvalidateOwnershipAsync(pluginId, endpoint.WorkerId);
            }
            catch (RpcException exception) when (
                exception.StatusCode == StatusCode.Cancelled &&
                cancellationToken.IsCancellationRequested
            )
            {
                throw new OperationCanceledException(
                    "Plugin block processing was cancelled.",
                    exception,
                    cancellationToken
                );
            }
        }

        throw new InvalidOperationException("Plugin block processing retry was exhausted.");
    }

    public void Dispose()
    {
        foreach (GrpcChannel channel in _channels.Values)
        {
            channel.Dispose();
        }

        _channels.Clear();
    }

    private PluginWorker.PluginWorkerClient GetClient(Uri address)
    {
        GrpcChannel channel = _channels.GetOrAdd(
            address.AbsoluteUri,
            _ => GrpcChannel.ForAddress(address)
        );

        return new PluginWorker.PluginWorkerClient(channel);
    }

    private void RemoveChannel(Uri address)
    {
        if (_channels.TryRemove(address.AbsoluteUri, out GrpcChannel? channel))
        {
            channel.Dispose();
        }
    }

    private static ByteString EncodeAudio(ReadOnlySpan<float> audio)
    {
        byte[] payload = new byte[checked(audio.Length * sizeof(float))];
        for (int i = 0; i < audio.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(i * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(audio[i])
            );
        }

        return UnsafeByteOperations.UnsafeWrap(payload);
    }

    private static float[] DecodeAudio(ReadOnlySpan<byte> payload)
    {
        if (payload.Length % sizeof(float) != 0)
        {
            throw new InvalidDataException("Worker audio payload is malformed.");
        }

        float[] audio = new float[payload.Length / sizeof(float)];
        for (int i = 0; i < audio.Length; i++)
        {
            audio[i] = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.Slice(i * sizeof(float), sizeof(float))
                )
            );
        }

        return audio;
    }
}
