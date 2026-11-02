using System.Buffers.Binary;

namespace VCConnectBot.Audio;

/// <summary>
/// 同じ形式の16-bit PCMフレームを飽和加算でミックスします。
/// </summary>
public sealed class PcmMixer
{
    /// <summary>
    /// PCMフレームをミックスします。入力がない場合は空配列を返します。
    /// </summary>
    /// <exception cref="ArgumentException">フレーム長が一致しない場合。</exception>
    public byte[] Mix(IReadOnlyList<ReadOnlyMemory<byte>> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            return [];
        }

        var length = frames[0].Length;
        if (length % 2 != 0 || frames.Any(frame => frame.Length != length || frame.Length % 2 != 0))
        {
            throw new ArgumentException("PCMフレームは同じ長さの16-bitサンプルである必要があります。", nameof(frames));
        }

        var mixed = new byte[length];
        for (var offset = 0; offset < length; offset += 2)
        {
            var sum = 0;
            foreach (var frame in frames)
            {
                sum += BinaryPrimitives.ReadInt16LittleEndian(frame.Span[offset..(offset + 2)]);
            }

            var sample = Math.Clamp(sum, short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(mixed.AsSpan(offset, 2), (short)sample);
        }

        return mixed;
    }
}
