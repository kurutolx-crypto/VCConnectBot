using System.Threading.Channels;

namespace VCConnectBot.Audio;

/// <summary>
/// 遅延が無制限に蓄積しないPCMフレームキューです。
/// </summary>
public sealed class PcmFrameQueue : IDisposable
{
    private readonly Channel<byte[]> channel;

    /// <summary>指定した最大フレーム数のキューを生成します。</summary>
    public PcmFrameQueue(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>フレームを追加します。満杯の場合は最古のフレームを破棄します。</summary>
    public bool TryWrite(ReadOnlyMemory<byte> frame) => channel.Writer.TryWrite(frame.ToArray());

    /// <summary>次のフレームを非同期に取得します。</summary>
    public ValueTask<byte[]> ReadAsync(CancellationToken cancellationToken) => channel.Reader.ReadAsync(cancellationToken);

    /// <summary>待機せず、現在キューにあるフレームを取得します。</summary>
    public bool TryRead(out byte[]? frame) => channel.Reader.TryRead(out frame);

    /// <summary>キューを完了し、保持中のバッファを破棄します。</summary>
    public void Dispose()
    {
        channel.Writer.TryComplete();
        while (channel.Reader.TryRead(out _))
        {
        }
    }
}
