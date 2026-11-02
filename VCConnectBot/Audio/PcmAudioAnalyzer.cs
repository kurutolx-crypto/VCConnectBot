using System.Buffers.Binary;

namespace VCConnectBot.Audio;

/// <summary>
/// 16-bit little-endian PCMの音量を判定します。
/// </summary>
public sealed class PcmAudioAnalyzer
{
    /// <summary>
    /// PCMフレームのRMS振幅を0から1の範囲で返します。
    /// </summary>
    public double CalculateRms(ReadOnlySpan<byte> pcmData)
    {
        if (pcmData.Length < 2)
        {
            return 0;
        }

        var sampleCount = pcmData.Length / 2;
        double sumSquares = 0;
        for (var index = 0; index + 1 < pcmData.Length; index += 2)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(pcmData[index..(index + 2)]);
            var normalized = sample / (double)short.MaxValue;
            sumSquares += normalized * normalized;
        }

        return Math.Sqrt(sumSquares / sampleCount);
    }

    /// <summary>RMS振幅が指定したしきい値以上かを判定します。</summary>
    public bool IsAudible(ReadOnlySpan<byte> pcmData, double threshold) => CalculateRms(pcmData) >= threshold;
}
