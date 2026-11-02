using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using VCConnectBot.Relay;

namespace VCConnectBot.Discord;

/// <summary>
/// Bot Aだけが登録する中継制御スラッシュコマンドです。
/// </summary>
public sealed class RelayCommands : ApplicationCommandModule
{
    private readonly RelaySession session;
    private readonly VoiceRelayService voiceRelayService;
    private readonly ILogger<RelayCommands> logger;

    /// <summary>中継セッションを受け取ります。</summary>
    public RelayCommands(RelaySession session, VoiceRelayService voiceRelayService, ILogger<RelayCommands> logger)
    {
        this.session = session;
        this.voiceRelayService = voiceRelayService;
        this.logger = logger;
    }

    /// <summary>中継セッションを開始します。実際のVC接続はVoiceNextアダプターが担当します。</summary>
    [SlashCommand("connect", "2つのボイスチャンネル間の中継を開始します。")]
    public async Task ConnectAsync(
        InteractionContext context,
        [Option("from", "中継元ボイスチャンネル")] DiscordChannel from,
        [Option("to", "中継先ボイスチャンネル")] DiscordChannel to,
        [Option("direction", "中継方向")] DiscordRelayDirection direction = DiscordRelayDirection.OneWay)
    {
        if (from.Type != ChannelType.Voice || to.Type != ChannelType.Voice)
        {
            await RespondAsync(context, "from と to にはボイスチャンネルを指定してください。");
            return;
        }

        var deferred = false;
        try
        {
            await context.Interaction.DeferAsync();
            deferred = true;
            await voiceRelayService.StartAsync(from, to, (RelayDirection)direction, CancellationToken.None);
            await context.Interaction.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().WithContent($"中継を開始しました: {from.Name} → {to.Name} ({direction})"));
        }
        catch (ArgumentException exception)
        {
            await EditOrRespondAsync(context, exception.Message, deferred);
        }
        catch (InvalidOperationException exception)
        {
            await EditOrRespondAsync(context, exception.Message, deferred);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "/connectの実行に失敗しました。");
            await EditOrRespondAsync(context, "中継の開始に失敗しました。ログを確認してください。", deferred);
        }
    }

    /// <summary>実行中の中継を終了します。</summary>
    [SlashCommand("disconnect", "実行中の中継を終了します。")]
    public async Task DisconnectAsync(InteractionContext context)
    {
        if (!session.GetStatus().IsActive)
        {
            await RespondAsync(context, "実行中の中継セッションはありません。");
            return;
        }

        await voiceRelayService.StopAsync("/disconnect が実行されました。", CancellationToken.None);
        await RespondAsync(context, "中継を終了しました。");
    }

    /// <summary>現在の中継状態を表示します。</summary>
    [SlashCommand("relay-status", "中継状態を表示します。")]
    public async Task StatusAsync(InteractionContext context)
    {
        var status = session.GetStatus();
        var connection = status.Connection;
        var now = DateTimeOffset.UtcNow;
        var silenceSince = status.LastAudibleInput ?? status.StartedAt;
        var emptyDuration = status.EmptyChannelsSince is { } emptySince ? now - emptySince : TimeSpan.Zero;
        var message = connection is null
            ? $"状態: {status.State}"
            : $"状態: {status.State}\n元: {connection.FromChannelId}\n先: {connection.ToChannelId}\n方向: {connection.Direction}\nBot A: {status.BotAConnected}\nBot B: {status.BotBConnected}\n無音時間: {now - silenceSince}\n無人時間: {emptyDuration}";
        await RespondAsync(context, message);
    }

    private static Task RespondAsync(InteractionContext context, string message) =>
        context.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().WithContent(message));

    private static Task EditOrRespondAsync(InteractionContext context, string message, bool deferred) => deferred
        ? context.Interaction.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent(message))
        : RespondAsync(context, message);
}
