using VCConnectBot.Configuration;

namespace VCConnectBot.Relay;

/// <summary>
/// 中継セッションの自動切断条件を判定します。
/// </summary>
public sealed class RelayTimeoutEvaluator
{
    private readonly RelayOptions options;

    /// <summary>設定を使用して判定器を生成します。</summary>
    public RelayTimeoutEvaluator(RelayOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>自動切断理由を返します。条件に該当しない場合はnullです。</summary>
    public string? GetDisconnectReason(RelayStatus status, DateTimeOffset now)
    {
        if (status.State != RelaySessionState.Running)
        {
            return null;
        }

        if (status.EmptyChannelsSince is { } emptySince && now - emptySince >= options.EmptyChannelTimeout)
        {
            return "両方のボイスチャンネルが無人の状態を3分間検出しました。";
        }

        var connection = status.Connection;
        var fromToLastAudio = status.LastFromToAudibleInput ?? status.StartedAt;
        if (connection is not null && now - fromToLastAudio >= options.SilenceTimeout &&
            (connection.Direction == RelayDirection.OneWay ||
             now - (status.LastToFromAudibleInput ?? status.StartedAt) >= options.SilenceTimeout))
        {
            return $"中継対象の全方向で無音の状態を{options.SilenceTimeout}継続しました。";
        }

        return null;
    }
}
