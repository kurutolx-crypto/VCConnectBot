namespace VCConnectBot.Relay;

/// <summary>
/// 中継セッションの状態を一元管理します。
/// </summary>
public sealed class RelaySession
{
    private readonly object syncRoot = new();
    private RelaySessionState state = RelaySessionState.Idle;
    private RelayConnection? connection;
    private bool botAConnected;
    private bool botBConnected;
    private DateTimeOffset startedAt;
    private DateTimeOffset? lastFromToAudibleInput;
    private DateTimeOffset? lastToFromAudibleInput;
    private DateTimeOffset? emptyChannelsSince;

    /// <summary>中継を開始し、接続中状態へ遷移します。</summary>
    public void Begin(RelayConnection newConnection, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(newConnection);
        newConnection.Validate();

        lock (syncRoot)
        {
            if (state is not (RelaySessionState.Idle or RelaySessionState.Ended))
            {
                throw new InvalidOperationException("既に中継セッションが実行中です。");
            }

            connection = newConnection;
            state = RelaySessionState.Connecting;
            botAConnected = false;
            botBConnected = false;
            startedAt = now ?? DateTimeOffset.UtcNow;
            lastFromToAudibleInput = null;
            lastToFromAudibleInput = null;
            emptyChannelsSince = null;
        }
    }

    /// <summary>Bot Aの接続状態を更新します。</summary>
    public void SetBotAConnected(bool connected) => SetBotConnection(isBotA: true, connected);

    /// <summary>Bot Bの接続状態を更新します。</summary>
    public void SetBotBConnected(bool connected) => SetBotConnection(isBotA: false, connected);

    /// <summary>両Botの接続完了後に中継中へ遷移します。</summary>
    public void MarkRunning()
    {
        lock (syncRoot)
        {
            EnsureState(RelaySessionState.Connecting);
            if (!botAConnected || !botBConnected)
            {
                throw new InvalidOperationException("両Botが接続されるまで中継を開始できません。");
            }

            state = RelaySessionState.Running;
        }
    }

    /// <summary>人間ユーザーの在席状態を更新します。</summary>
    public void SetHumanPresence(bool anyHumanPresent, DateTimeOffset now)
    {
        lock (syncRoot)
        {
            if (state != RelaySessionState.Running)
            {
                return;
            }

            emptyChannelsSince = anyHumanPresent ? null : emptyChannelsSince ?? now;
        }
    }

    /// <summary>音量しきい値を超えた入力時刻を記録します。</summary>
    public void RecordAudibleInput(RelayDirection direction, DateTimeOffset now)
    {
        lock (syncRoot)
        {
            if (state == RelaySessionState.Running)
            {
                if (direction == RelayDirection.OneWay)
                {
                    lastFromToAudibleInput = now;
                }
                else
                {
                    lastToFromAudibleInput = now;
                }
            }
        }
    }

    /// <summary>終了処理を開始します。既に終了処理中ならfalseを返します。</summary>
    public bool TryBeginStop()
    {
        lock (syncRoot)
        {
            if (state is RelaySessionState.Idle or RelaySessionState.Ended or RelaySessionState.Stopping)
            {
                return false;
            }

            state = RelaySessionState.Stopping;
            return true;
        }
    }

    /// <summary>終了処理を完了し、セッションを終了状態にします。</summary>
    public void CompleteStop()
    {
        lock (syncRoot)
        {
            state = RelaySessionState.Ended;
            botAConnected = false;
            botBConnected = false;
        }
    }

    /// <summary>現在の状態をスナップショットとして取得します。</summary>
    public RelayStatus GetStatus()
    {
        lock (syncRoot)
        {
            return new RelayStatus(state, connection, botAConnected, botBConnected, startedAt, lastFromToAudibleInput, lastToFromAudibleInput, emptyChannelsSince);
        }
    }

    private void SetBotConnection(bool isBotA, bool connected)
    {
        lock (syncRoot)
        {
            EnsureState(RelaySessionState.Connecting, RelaySessionState.Running);
            if (isBotA)
            {
                botAConnected = connected;
            }
            else
            {
                botBConnected = connected;
            }
        }
    }

    private void EnsureState(params RelaySessionState[] allowedStates)
    {
        if (!allowedStates.Contains(state))
        {
            throw new InvalidOperationException($"状態 {state} ではこの操作を実行できません。");
        }
    }
}
