namespace VCConnectBot.Relay;

/// <summary>
/// 中継元・中継先と方向を表すDiscord非依存の接続設定です。
/// </summary>
public sealed record RelayConnection(string FromChannelId, string ToChannelId, RelayDirection Direction)
{
    /// <summary>
    /// 接続設定を検証します。
    /// </summary>
    /// <exception cref="ArgumentException">チャンネルIDが空、または同一の場合。</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(FromChannelId))
        {
            throw new ArgumentException("中継元チャンネルは必須です。", nameof(FromChannelId));
        }

        if (string.IsNullOrWhiteSpace(ToChannelId))
        {
            throw new ArgumentException("中継先チャンネルは必須です。", nameof(ToChannelId));
        }

        if (string.Equals(FromChannelId, ToChannelId, StringComparison.Ordinal))
        {
            throw new ArgumentException("中継元と中継先には異なるチャンネルを指定してください。");
        }
    }
}
