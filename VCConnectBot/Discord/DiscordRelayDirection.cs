using DSharpPlus.SlashCommands;

namespace VCConnectBot.Discord;

/// <summary>Discord上で表示する中継方向の選択肢です。</summary>
public enum DiscordRelayDirection
{
    /// <summary>fromからtoへの一方向です。</summary>
    [ChoiceName("one-way")]
    OneWay,

    /// <summary>fromとtoの双方向です。</summary>
    [ChoiceName("two-way")]
    TwoWay
}
