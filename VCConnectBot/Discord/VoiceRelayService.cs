using DSharpPlus.Entities;
using DSharpPlus.VoiceNext;
using DSharpPlus.VoiceNext.EventArgs;
using Microsoft.Extensions.Options;
using VCConnectBot.Audio;
using VCConnectBot.Configuration;
using VCConnectBot.Relay;

namespace VCConnectBot.Discord;

/// <summary>
/// DSharpPlus VoiceNextとDiscord非依存の中継ドメインを橋渡しします。
/// </summary>
public sealed class VoiceRelayService : IAsyncDisposable
{
    private readonly DiscordBotHost botHost;
    private readonly RelaySession session;
    private readonly RelayTimeoutEvaluator timeoutEvaluator;
    private readonly PcmAudioAnalyzer audioAnalyzer = new();
    private readonly AudioOptions audioOptions;
    private readonly RelayOptions relayOptions;
    private readonly ILogger<VoiceRelayService> logger;
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private CancellationTokenSource? relayCancellation;
    private Task? monitorTask;
    private VoiceNextConnection? fromConnection;
    private VoiceNextConnection? toConnection;
    private PcmFrameQueue? fromToQueue;
    private PcmFrameQueue? toFromQueue;
    private DiscordChannel? fromChannel;
    private DiscordChannel? toChannel;

    /// <summary>中継サービスを生成します。</summary>
    public VoiceRelayService(
        DiscordBotHost botHost,
        RelaySession session,
        IOptions<RelayOptions> relayOptions,
        IOptions<AudioOptions> audioOptions,
        ILogger<VoiceRelayService> logger)
    {
        this.botHost = botHost;
        this.session = session;
        this.relayOptions = relayOptions.Value;
        this.audioOptions = audioOptions.Value;
        timeoutEvaluator = new RelayTimeoutEvaluator(this.relayOptions);
        this.logger = logger;
        botHost.ConnectionLost += OnDiscordConnectionLost;
    }

    /// <summary>指定されたVCにBot A/Bを接続し、中継ループを開始します。</summary>
    public async Task StartAsync(DiscordChannel from, DiscordChannel to, RelayDirection direction, CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            if (from.Guild is null || to.Guild is null || from.Guild.Id != to.Guild.Id)
            {
                throw new InvalidOperationException("中継元と中継先は同じDiscordサーバー内で指定してください。");
            }

            var connection = new RelayConnection(from.Id.ToString(), to.Id.ToString(), direction);
            session.Begin(connection);
            relayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            fromChannel = from;
            toChannel = to;
            fromToQueue = new PcmFrameQueue(audioOptions.MaxBufferedFrames);
            toFromQueue = direction == RelayDirection.TwoWay ? new PcmFrameQueue(audioOptions.MaxBufferedFrames) : null;

            try
            {
                if (botHost.BotA is null || botHost.BotB is null)
                {
                    throw new InvalidOperationException("Bot A/Bが接続されていません。");
                }

                fromConnection = await botHost.BotA.GetVoiceNext().ConnectAsync(from);
                session.SetBotAConnected(true);
                toConnection = await botHost.BotB.GetVoiceNext().ConnectAsync(to);
                session.SetBotBConnected(true);
                session.MarkRunning();
                AttachReceiveHandler(fromConnection, fromToQueue, isFromSide: true);
                if (toFromQueue is not null)
                {
                    AttachReceiveHandler(toConnection, toFromQueue, isFromSide: false);
                }

                var token = relayCancellation.Token;
                var tasks = new List<Task> { ForwardAsync(fromToQueue, toConnection, token) };
                if (toFromQueue is not null)
                {
                    tasks.Add(ForwardAsync(toFromQueue, fromConnection, token));
                }

                monitorTask = MonitorAsync(token);
                _ = ObserveBackgroundTasksAsync(tasks, token);
            }
            catch
            {
                await StopCoreAsync("VC接続に失敗したため中継をロールバックしました。", CancellationToken.None);
                throw;
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    /// <summary>実行中の中継を終了します。</summary>
    public async Task StopAsync(string reason, CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync(reason, cancellationToken);
        }
        finally
        {
            operationLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        botHost.ConnectionLost -= OnDiscordConnectionLost;
        await StopAsync("アプリケーション終了", CancellationToken.None);
        operationLock.Dispose();
    }

    private void OnDiscordConnectionLost() => _ = StopAsync("Discord接続が切断されたため中継を終了しました。", CancellationToken.None);

    private void AttachReceiveHandler(VoiceNextConnection connection, PcmFrameQueue queue, bool isFromSide)
    {
        connection.VoiceReceived += (_, args) =>
        {
            if (args.User is null || args.User.IsBot || !audioAnalyzer.IsAudible(args.PcmData.Span, relayOptions.SilenceThreshold))
            {
                return Task.CompletedTask;
            }

            session.RecordAudibleInput(isFromSide ? RelayDirection.OneWay : RelayDirection.TwoWay, DateTimeOffset.UtcNow);
            queue.TryWrite(args.PcmData);
            return Task.CompletedTask;
        };
    }

    private async Task ForwardAsync(PcmFrameQueue queue, VoiceNextConnection target, CancellationToken cancellationToken)
    {
        var sink = target.GetTransmitSink(audioOptions.FrameDurationMilliseconds);
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await queue.ReadAsync(cancellationToken);
            await sink.WriteAsync(frame, cancellationToken);
        }
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var anyHuman = (fromChannel?.Users ?? []).Any(user => !user.IsBot) ||
                           (toChannel?.Users ?? []).Any(user => !user.IsBot);
            session.SetHumanPresence(anyHuman, DateTimeOffset.UtcNow);
            var reason = timeoutEvaluator.GetDisconnectReason(session.GetStatus(), DateTimeOffset.UtcNow);
            if (reason is not null)
            {
                await StopAsync(reason, cancellationToken);
                return;
            }
        }
    }

    private async Task ObserveBackgroundTasksAsync(IEnumerable<Task> tasks, CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(tasks.Append(monitorTask!));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "音声中継のバックグラウンド処理が異常終了しました。");
            await StopAsync("音声処理が異常終了したため中継を終了しました。", CancellationToken.None);
        }
    }

    private async Task StopCoreAsync(string reason, CancellationToken cancellationToken)
    {
        if (!session.TryBeginStop())
        {
            return;
        }

        relayCancellation?.Cancel();
        fromToQueue?.Dispose();
        toFromQueue?.Dispose();
        fromConnection?.Disconnect();
        toConnection?.Disconnect();
        fromConnection = null;
        toConnection = null;
        relayCancellation?.Dispose();
        relayCancellation = null;
        monitorTask = null;
        fromToQueue = null;
        toFromQueue = null;
        session.CompleteStop();
        await NotifyDisconnectAsync(reason);
        logger.LogInformation("音声中継を終了しました。理由: {Reason}", reason);
        await Task.CompletedTask;
    }

    private async Task NotifyDisconnectAsync(string reason)
    {
        if (toChannel is null)
        {
            return;
        }

        try
        {
            await toChannel.SendMessageAsync($"音声中継を終了しました。理由: {reason}");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "中継終了通知をDiscordへ送信できませんでした。");
        }
    }
}
