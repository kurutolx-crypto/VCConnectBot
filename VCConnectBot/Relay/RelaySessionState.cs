namespace VCConnectBot.Relay;

/// <summary>
/// 中継セッションの状態です。
/// </summary>
public enum RelaySessionState
{
    /// <summary>セッションがありません。</summary>
    Idle,

    /// <summary>両Botの接続を確立中です。</summary>
    Connecting,

    /// <summary>中継中です。</summary>
    Running,

    /// <summary>終了処理中です。</summary>
    Stopping,

    /// <summary>終了済みです。</summary>
    Ended
}
