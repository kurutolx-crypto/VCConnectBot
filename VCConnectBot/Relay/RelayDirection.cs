namespace VCConnectBot.Relay;

/// <summary>
/// 音声を中継する方向です。
/// </summary>
public enum RelayDirection
{
    /// <summary>送信元から送信先への一方向です。</summary>
    OneWay,

    /// <summary>送信元と送信先の双方向です。</summary>
    TwoWay
}
