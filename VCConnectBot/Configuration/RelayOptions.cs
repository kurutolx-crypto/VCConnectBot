namespace VCConnectBot.Configuration;

/// <summary>
/// 中継セッションの一般設定です。
/// </summary>
public sealed class RelayOptions
{
    /// <summary>無人状態を理由に切断するまでの時間です。</summary>
    public TimeSpan EmptyChannelTimeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>無音状態を理由に切断するまでの時間です。</summary>
    public TimeSpan SilenceTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>音量判定に使用する正規化PCM振幅のしきい値です。</summary>
    public double SilenceThreshold { get; set; } = 0.01;
}
