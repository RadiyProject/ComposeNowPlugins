using System.Buffers.Binary;
using ComposeNowPlugins.Application.Exceptions;
using ComposeNowPlugins.Domain.Models.Ids;
using ComposeNowPlugins.Application.Services.Processing;
using ComposeNowPlugins.Worker;
using Google.Protobuf;
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
        if (string.IsNullOrWhiteSpace(request.PluginId) ||
            request.PluginId.Length > PluginId.MaxLength ||
            request.Frames is <= 0 or > AudioProcessingLimits.MaxFramesPerBlock ||
            request.InputAudio.Length > AudioProcessingLimits.MaxSamplesPerBlock * sizeof(float))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Plugin block request exceeds processing limits."));
        }

        ReadOnlyMemory<float>? inputAudio = request.InputAudio.IsEmpty
            ? null
            : DecodeAudio(request.InputAudio.Span);

        PluginBlockProcessResult result;
        try
        {
            result = await _pluginBlockProcessor.ProcessBlockAsync(
                new PluginId(request.PluginId),
                request.Epoch,
                request.Seq,
                request.Frames,
                request.Offline,
                inputAudio,
                context.CancellationToken
            );
        }
        catch (PluginOwnershipLostException exception)
        {
            throw new RpcException(new Status(StatusCode.Aborted, exception.Message));
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }

        using (result)
        {
            return new ProcessBlockReply
            {
                ShouldSend = result.ShouldSend,
                Audio = EncodeAudio(result.Audio.Span)
            };
        }
    }

    private static float[] DecodeAudio(ReadOnlySpan<byte> payload)
    {
        if (payload.Length % sizeof(float) != 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Input audio payload is malformed."));
        }

        float[] audio = new float[payload.Length / sizeof(float)];
        for (int i = 0; i < audio.Length; i++)
        {
            float sample = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.Slice(i * sizeof(float), sizeof(float))
                )
            );
            if (!float.IsFinite(sample))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Input audio contains a non-finite sample."));
            }

            audio[i] = sample;
        }

        return audio;
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
}
