using System.Buffers.Binary;
using System.Reflection;
using DSharpPlus;
using DSharpPlus.SlashCommands;
using VCConnectBot.Audio;
using VCConnectBot.Configuration;
using VCConnectBot.Discord;
using VCConnectBot.Relay;

namespace VCConnectBot.Tests;

public sealed class RelayCommandsTests
{
    [Theory]
    [InlineData("from")]
    [InlineData("to")]
    public void ConnectChannelOptionsOnlyAllowVoiceChannels(string parameterName)
    {
        var parameter = typeof(RelayCommands)
            .GetMethod(nameof(RelayCommands.ConnectAsync))!
            .GetParameters()
            .Single(parameter => parameter.Name == parameterName);

        var channelTypes = parameter.GetCustomAttribute<ChannelTypesAttribute>()?.ChannelTypes;

        Assert.Equal(new[] { ChannelType.Voice }, channelTypes);
    }
}

public sealed class RelayConnectionTests
{
    [Fact]
    public void SameChannel_IsRejected()
    {
        var connection = new RelayConnection("123", "123", RelayDirection.OneWay);

        Assert.Throws<ArgumentException>(() => connection.Validate());
    }

    [Theory]
    [InlineData("", "to")]
    [InlineData("from", "")]
    public void MissingChannel_IsRejected(string from, string to)
    {
        Assert.Throws<ArgumentException>(() => new RelayConnection(from, to, RelayDirection.OneWay).Validate());
    }

    [Fact]
    public void Session_RequiresBothBotsBeforeRunning()
    {
        var session = new RelaySession();
        session.Begin(new RelayConnection("from", "to", RelayDirection.TwoWay));
        session.SetBotAConnected(true);

        Assert.Throws<InvalidOperationException>(() => session.MarkRunning());
    }

    [Fact]
    public void Session_TransitionsToRunningWhenBothBotsAreConnected()
    {
        var session = new RelaySession();
        session.Begin(new RelayConnection("from", "to", RelayDirection.OneWay));
        session.SetBotAConnected(true);
        session.SetBotBConnected(true);
        session.MarkRunning();

        var status = session.GetStatus();
        Assert.Equal(RelaySessionState.Running, status.State);
        Assert.True(status.BotAConnected);
        Assert.True(status.BotBConnected);
    }

    [Fact]
    public void Session_CanBeStoppedOnlyOnce()
    {
        var session = CreateRunningSession(RelayDirection.OneWay);

        Assert.True(session.TryBeginStop());
        Assert.False(session.TryBeginStop());

        session.CompleteStop();
        Assert.Equal(RelaySessionState.Ended, session.GetStatus().State);
    }

    [Fact]
    public void EmptyAndOneWaySilenceTimeoutsAreDetected()
    {
        var started = Utc(0);
        var session = CreateRunningSession(RelayDirection.OneWay, started);
        session.SetHumanPresence(false, started);
        var evaluator = CreateEvaluator();

        Assert.Contains("無人", evaluator.GetDisconnectReason(session.GetStatus(), started.AddMinutes(3)));

        session.SetHumanPresence(true, started.AddMinutes(3));
        Assert.Null(evaluator.GetDisconnectReason(session.GetStatus(), started.AddMinutes(9)));
        Assert.Contains("無音", evaluator.GetDisconnectReason(session.GetStatus(), started.AddMinutes(10)));
    }

    [Fact]
    public void TwoWaySilenceRequiresBothDirections()
    {
        var started = Utc(0);
        var session = CreateRunningSession(RelayDirection.TwoWay, started);
        var evaluator = CreateEvaluator();

        session.RecordAudibleInput(RelayDirection.OneWay, started.AddMinutes(8));
        Assert.Null(evaluator.GetDisconnectReason(session.GetStatus(), started.AddMinutes(9)));
        Assert.Contains("無音", evaluator.GetDisconnectReason(session.GetStatus(), started.AddMinutes(18)));

        session.RecordAudibleInput(RelayDirection.TwoWay, started.AddMinutes(18));
        Assert.Null(evaluator.GetDisconnectReason(session.GetStatus(), started.AddMinutes(19)));
    }

    private static RelaySession CreateRunningSession(RelayDirection direction, DateTimeOffset? started = null)
    {
        var session = new RelaySession();
        session.Begin(new RelayConnection("from", "to", direction), started ?? Utc(0));
        session.SetBotAConnected(true);
        session.SetBotBConnected(true);
        session.MarkRunning();
        return session;
    }

    private static RelayTimeoutEvaluator CreateEvaluator() => new(new RelayOptions
    {
        EmptyChannelTimeout = TimeSpan.FromMinutes(3),
        SilenceTimeout = TimeSpan.FromMinutes(10)
    });

    private static DateTimeOffset Utc(int minutes) => new(2026, 1, 1, 0, minutes, 0, TimeSpan.Zero);
}

public sealed class PcmProcessingTests
{
    [Fact]
    public void SilenceHasZeroRms()
    {
        var analyzer = new PcmAudioAnalyzer();

        Assert.Equal(0, analyzer.CalculateRms(new byte[8]));
        Assert.False(analyzer.IsAudible(new byte[8], 0.01));
    }

    [Fact]
    public void LoudPcmIsAudible()
    {
        var pcm = new byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(0, 2), short.MaxValue);
        BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(2, 2), short.MaxValue);

        Assert.True(new PcmAudioAnalyzer().IsAudible(pcm, 0.9));
    }

    [Fact]
    public void OddLengthPcmIgnoresIncompleteSample()
    {
        var pcm = new byte[3];
        BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(0, 2), short.MaxValue);

        Assert.Equal(1, new PcmAudioAnalyzer().CalculateRms(pcm), precision: 5);
    }

    [Fact]
    public void MixerSaturatesInsteadOfWrapping()
    {
        var first = new byte[2];
        var second = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(first, 30_000);
        BinaryPrimitives.WriteInt16LittleEndian(second, 30_000);

        var mixed = new PcmMixer().Mix([first, second]);

        Assert.Equal(short.MaxValue, BinaryPrimitives.ReadInt16LittleEndian(mixed));
    }

    [Fact]
    public void MixerRejectsDifferentFrameLengths()
    {
        Assert.Throws<ArgumentException>(() => new PcmMixer().Mix([new byte[2], new byte[4]]));
    }

    [Fact]
    public void MixerReturnsSilenceForNoFrames()
    {
        Assert.Empty(new PcmMixer().Mix([]));
    }

    [Fact]
    public async Task QueueHasBoundedCapacity()
    {
        using var queue = new PcmFrameQueue(1);
        Assert.True(queue.TryWrite(new byte[] { 1 }));
        Assert.True(queue.TryWrite(new byte[] { 2 }));

        Assert.Equal(new byte[] { 2 }, await queue.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public void QueueRejectsNonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcmFrameQueue(0));
    }
}

public sealed class DiscordSecretsTests
{
    [Fact]
    public void EnvironmentValuesAreLoaded()
    {
        var secrets = DiscordSecrets.FromEnvironment(new Dictionary<string, string?>
        {
            ["DISCORD_BOT_A_TOKEN"] = "token-a",
            ["DISCORD_BOT_B_TOKEN"] = "token-b",
            ["DISCORD_APPLICATION_ID"] = "123"
        });

        Assert.Equal("token-a", secrets.BotAToken);
        Assert.Equal("token-b", secrets.BotBToken);
        Assert.Equal((ulong)123, secrets.ApplicationId);
    }

    [Theory]
    [InlineData("DISCORD_BOT_A_TOKEN")]
    [InlineData("DISCORD_BOT_B_TOKEN")]
    [InlineData("DISCORD_APPLICATION_ID")]
    public void MissingRequiredValueIsRejected(string missingName)
    {
        var environment = new Dictionary<string, string?>
        {
            ["DISCORD_BOT_A_TOKEN"] = "token-a",
            ["DISCORD_BOT_B_TOKEN"] = "token-b",
            ["DISCORD_APPLICATION_ID"] = "123"
        };
        environment.Remove(missingName);

        Assert.Throws<InvalidOperationException>(() => DiscordSecrets.FromEnvironment(environment));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("0")]
    public void InvalidApplicationIdIsRejected(string applicationId)
    {
        var environment = new Dictionary<string, string?>
        {
            ["DISCORD_BOT_A_TOKEN"] = "token-a",
            ["DISCORD_BOT_B_TOKEN"] = "token-b",
            ["DISCORD_APPLICATION_ID"] = applicationId
        };

        Assert.Throws<InvalidOperationException>(() => DiscordSecrets.FromEnvironment(environment));
    }
}
