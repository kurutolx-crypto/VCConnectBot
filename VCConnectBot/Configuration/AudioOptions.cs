namespace VCConnectBot.Configuration;

/// <summary>
/// Discord音声処理の形式とバッファ上限を表します。
/// </summary>
public sealed class AudioOptions
{
    /// <summary>音声サンプルレートです。</summary>
    public int SampleRate { get; set; } = 48_000;

    /// <summary>PCMチャンネル数です。</summary>
    public int Channels { get; set; } = 2;

    /// <summary>1音声フレームの長さ（ミリ秒）です。</summary>
    public int FrameDurationMilliseconds { get; set; } = 20;

    /// <summary>中継キューに保持できる最大フレーム数です。</summary>
    public int MaxBufferedFrames { get; set; } = 25;
}
