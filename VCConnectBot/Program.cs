using VCConnectBot.Configuration;
using VCConnectBot.Discord;
using VCConnectBot.Relay;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

builder.Services
    .AddOptions<RelayOptions>()
    .Bind(builder.Configuration.GetSection("Relay"))
    .Validate(options => options.EmptyChannelTimeout > TimeSpan.Zero, "Relay:EmptyChannelTimeout must be positive.")
    .Validate(options => options.SilenceTimeout > TimeSpan.Zero, "Relay:SilenceTimeout must be positive.")
    .Validate(options => options.SilenceThreshold is >= 0 and <= 1, "Relay:SilenceThreshold must be between 0 and 1.")
    .ValidateOnStart();

builder.Services
    .AddOptions<AudioOptions>()
    .Bind(builder.Configuration.GetSection("Audio"))
    .Validate(options => options.SampleRate > 0, "Audio:SampleRate must be positive.")
    .Validate(options => options.Channels is 1 or 2, "Audio:Channels must be 1 or 2.")
    .Validate(options => options.FrameDurationMilliseconds is 10 or 20 or 40 or 60, "Audio:FrameDurationMilliseconds must be 10, 20, 40, or 60.")
    .Validate(options => options.MaxBufferedFrames > 0, "Audio:MaxBufferedFrames must be positive.")
    .ValidateOnStart();

var secrets = DiscordSecrets.FromEnvironment(
    Environment.GetEnvironmentVariables()
        .Cast<System.Collections.DictionaryEntry>()
        .ToDictionary(entry => (string)entry.Key, entry => entry.Value?.ToString(), StringComparer.Ordinal));
builder.Services.AddSingleton(secrets);
builder.Services.AddSingleton<RelaySession>();
builder.Services.AddSingleton<DiscordBotHost>();
builder.Services.AddSingleton<VoiceRelayService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<DiscordBotHost>());

var app = builder.Build();

app.MapGet("/", () => "VCConnectBot is running");

app.Run();
