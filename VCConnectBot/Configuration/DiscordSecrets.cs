namespace VCConnectBot.Configuration;

/// <summary>
/// Discord Botの認証情報です。値は環境変数からのみ取得します。
/// </summary>
public sealed record DiscordSecrets(string BotAToken, string BotBToken, ulong ApplicationId)
{
    /// <summary>
    /// 必須の環境変数からDiscord認証情報を読み込みます。
    /// </summary>
    /// <exception cref="InvalidOperationException">環境変数が未設定または不正な場合。</exception>
    public static DiscordSecrets FromEnvironment(IReadOnlyDictionary<string, string?> environment)
    {
        var botAToken = GetRequired(environment, "DISCORD_BOT_A_TOKEN");
        var botBToken = GetRequired(environment, "DISCORD_BOT_B_TOKEN");
        var applicationIdText = GetRequired(environment, "DISCORD_APPLICATION_ID");

        if (!ulong.TryParse(applicationIdText, out var applicationId) || applicationId == 0)
        {
            throw new InvalidOperationException("環境変数 DISCORD_APPLICATION_ID は正の整数で指定してください。");
        }

        return new DiscordSecrets(botAToken, botBToken, applicationId);
    }

    private static string GetRequired(IReadOnlyDictionary<string, string?> environment, string name)
    {
        if (!environment.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"必須の環境変数 {name} が設定されていません。");
        }

        return value;
    }
}
