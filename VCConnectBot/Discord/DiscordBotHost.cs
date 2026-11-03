using DSharpPlus;
using DSharpPlus.SlashCommands;
using DSharpPlus.VoiceNext;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VCConnectBot.Configuration;
using VCConnectBot.Relay;

namespace VCConnectBot.Discord;

/// <summary>
/// Bot AとBot Bを独立したDiscordClientとして管理します。
/// </summary>
public sealed class DiscordBotHost : IHostedService, IAsyncDisposable
{
    private readonly DiscordSecrets secrets;
    private readonly IServiceProvider services;
    private readonly ILogger<DiscordBotHost> logger;
    private readonly AudioOptions audioOptions;
    private DiscordClient? botA;
    private DiscordClient? botB;

    /// <summary>Bot AのDiscordクライアントです。</summary>
    public DiscordClient? BotA => botA;

    /// <summary>Bot BのDiscordクライアントです。</summary>
    public DiscordClient? BotB => botB;

    /// <summary>Discordソケットが予期せず閉じたときに発生します。</summary>
    public event Action? ConnectionLost;

    /// <summary>Botホストを生成します。</summary>
    public DiscordBotHost(DiscordSecrets secrets, IServiceProvider services, IOptions<AudioOptions> audioOptions, ILogger<DiscordBotHost> logger)
    {
        this.secrets = secrets;
        this.services = services;
        this.logger = logger;
        this.audioOptions = audioOptions.Value;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        botA = CreateClient(secrets.BotAToken);
        botB = CreateClient(secrets.BotBToken);
        var slashCommands = botA.UseSlashCommands(new SlashCommandsConfiguration { Services = services });
        slashCommands.RegisterCommands<RelayCommands>();

        try
        {
            await Task.WhenAll(botA.ConnectAsync(), botB.ConnectAsync()).WaitAsync(cancellationToken);
            logger.LogInformation("Bot A/BのDiscord接続が完了しました。");
        }
        catch
        {
            await StopAsync(CancellationToken.None);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var clients = new[] { botA, botB }.Where(client => client is not null).Cast<DiscordClient>();
        foreach (var client in clients)
        {
            try
            {
                await client.DisconnectAsync();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Discordクライアントの切断に失敗しました。");
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        botA?.Dispose();
        botB?.Dispose();
    }

    private DiscordClient CreateClient(string token)
    {
        var client = new DiscordClient(new DiscordConfiguration
        {
            Token = token,
            TokenType = TokenType.Bot,
            Intents = DiscordIntents.AllUnprivileged | DiscordIntents.GuildVoiceStates,
            MinimumLogLevel = Microsoft.Extensions.Logging.LogLevel.Debug
        });
        client.UseVoiceNext(new VoiceNextConfiguration
        {
            EnableIncoming = true,
            AudioFormat = new AudioFormat(audioOptions.SampleRate, audioOptions.Channels, VoiceApplication.Voice),
            PacketQueueSize = audioOptions.MaxBufferedFrames
        });
        client.SocketClosed += (_, eventArgs) =>
        {
            logger.LogWarning("Discord接続が切断されました。Code={Code}", eventArgs.CloseCode);
            ConnectionLost?.Invoke();
            return Task.CompletedTask;
        };
        return client;
    }
}
