namespace VCConnectBot.Relay;

/// <summary>
/// 中継状態の読み取り専用スナップショットです。
/// </summary>
public sealed record RelayStatus(
    RelaySessionState State,
    RelayConnection? Connection,
    bool BotAConnected,
    bool BotBConnected,
    DateTimeOffset StartedAt,
    DateTimeOffset? LastFromToAudibleInput,
    DateTimeOffset? LastToFromAudibleInput,
    DateTimeOffset? EmptyChannelsSince)
{
    /// <summary>中継が開始されているかを示します。</summary>
    public bool IsActive => State is RelaySessionState.Connecting or RelaySessionState.Running or RelaySessionState.Stopping;

    /// <summary>すべての方向のうち最後に音声入力があった時刻です。</summary>
    public DateTimeOffset? LastAudibleInput => new[] { LastFromToAudibleInput, LastToFromAudibleInput }.Max();
}
