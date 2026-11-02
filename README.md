# VCConnectBot

DSharpPlus 5.0.0とVoiceNextを使用して、同一Discordサーバー内の2つのボイスチャンネル間でPCM音声を中継する.NET 10アプリケーションです。

## 構成

- Bot A: スラッシュコマンド受付と中継元VCへの接続・受信
- Bot B: 中継先VCへの接続・送信
- `one-way`: fromからtoへの一方向中継
- `two-way`: fromとtoの双方向中継
- PCMは16-bit little-endianとして扱い、複数フレームは飽和加算します。
- 録音・永続保存は行いません。

## 必要環境

- .NET SDK 10
- Discord Botアカウント2個
- Botに `View Channel`、`Connect`、`Speak`、`Use Voice Activity` 権限
- Bot Aにはアプリケーションコマンドの登録権限
- Gateway Intent: `Guilds`、`Guild Voice States`
- LinuxではARM64版の`libopus`と`libsodium`

Discord Developer Portalで両Botを作成し、各Botを対象サーバーへ招待してください。トークンはソースコードやJSONへ保存しません。

## 環境変数

必須:

- `DISCORD_BOT_A_TOKEN`
- `DISCORD_BOT_B_TOKEN`
- `DISCORD_APPLICATION_ID`

PowerShell:

```powershell
$env:DISCORD_BOT_A_TOKEN = "Bot Aのトークン"
$env:DISCORD_BOT_B_TOKEN = "Bot Bのトークン"
$env:DISCORD_APPLICATION_ID = "アプリケーションID"
 dotnet run --project VCConnectBot/VCConnectBot.csproj
```

Linux:

```bash
export DISCORD_BOT_A_TOKEN='Bot Aのトークン'
export DISCORD_BOT_B_TOKEN='Bot Bのトークン'
export DISCORD_APPLICATION_ID='アプリケーションID'
```

未設定または不正な環境変数がある場合、アプリケーションは起動時に明確なエラーを出して終了します。トークン値はログに出力しません。

`.env.example`は変数名だけを含みます。実際の`.env`はGit管理対象外です。

## 設定

`VCConnectBot/appsettings.json`で変更できます。

- `Relay:EmptyChannelTimeout`: 両VCが無人の場合の切断時間。既定3分
- `Relay:SilenceTimeout`: 全対象方向が無音の場合の切断時間。既定10分
- `Relay:SilenceThreshold`: PCM RMS振幅の無音しきい値。0から1
- `Audio:SampleRate`: 既定48000Hz
- `Audio:Channels`: 1または2。既定2
- `Audio:FrameDurationMilliseconds`: 10、20、40、60。既定20
- `Audio:MaxBufferedFrames`: 方向ごとのキュー上限。既定25

## コマンド

- `/connect from:<VC> to:<VC> direction:<one-way|two-way>`
- `/disconnect`
- `/relay-status`

`from`と`to`が同一VCの場合、異なるサーバーのVCの場合、中継中に二重開始した場合は拒否します。接続途中で片方が失敗した場合、成功側も切断してロールバックします。自動再接続は行わず、再接続には`/connect`を再実行します。

## ビルドと実行

```bash
dotnet restore VCConnectBot.slnx
dotnet build VCConnectBot.slnx
```

Linux ARM64向けFramework-dependent発行:

```bash
dotnet publish VCConnectBot/VCConnectBot.csproj -c Release -r linux-arm64 --self-contained false -o publish/linux-arm64
```

Linux ARM64では、ディストリビューションのパッケージマネージャーでネイティブライブラリを導入します。Debian/Ubuntuの例:

```bash
sudo apt-get update
sudo apt-get install -y libopus0 libsodium23
```

`DSharpPlus.VoiceNext.Natives` 1.0.0はWindows x86/x64用DLLのみを含みます。Linux ARM64へWindows DLLをコピーせず、OSのARM64共有ライブラリを使用してください。ディストリビューションによってパッケージ名が異なる場合は、`libopus.so`と`libsodium.so`を提供するARM64パッケージを選択してください。

## systemd

`systemd/vcconnectbot.service`を配置し、`/etc/vcconnectbot/vcconnectbot.env`に秘密情報を設定します。

```bash
sudo install -d -m 0750 /etc/vcconnectbot
sudo install -m 0600 vcconnectbot.env /etc/vcconnectbot/vcconnectbot.env
sudo install -m 0644 systemd/vcconnectbot.service /etc/systemd/system/vcconnectbot.service
sudo systemctl daemon-reload
sudo systemctl enable --now vcconnectbot
sudo journalctl -u vcconnectbot -f
```

## Docker

Docker Composeはルートの`.env`からBotトークンを読み込みます。初回だけ`.env.example`をコピーして値を設定してください。

```bash
cp .env.example .env
# .envにDISCORD_BOT_A_TOKEN、DISCORD_BOT_B_TOKEN、DISCORD_APPLICATION_IDを設定
docker compose up -d --build
```

停止・ログ確認:

```bash
docker compose logs -f
docker compose down
```

DockerfileはLinuxランタイムへ`libopus0`と`libsodium23`を導入します。ARM64で明示的にビルドする場合:

```bash
docker compose build --no-cache
docker compose up -d
```

Docker ComposeはホストのCPUアーキテクチャを使用します。ARM64ホストではARM64イメージがビルドされます。x64環境からARM64イメージを作成する場合は、`docker buildx`の設定でARM64プラットフォームを指定してください。

## トラブルシューティング

- 起動時に環境変数エラー: 必須3変数を設定し、Application IDが整数であることを確認します。
- コマンドが表示されない: Bot Aがアプリケーションコマンドを使用できる招待URLで追加されていることを確認します。グローバルコマンド反映には時間がかかる場合があります。
- VC接続失敗: 両Botの`Connect`、`Speak`、`Use Voice Activity`権限とGateway Intentを確認します。
- LinuxでOpus読み込み失敗: `ldconfig -p | grep -E 'opus|sodium'`でARM64共有ライブラリを確認します。
- 音声が中継されない: VoiceNextの受信が有効であること、Bot自身ではなく人間ユーザーが発話していること、PCMしきい値が高すぎないことを確認します。

## 制約

Discord実接続、Linux ARM64実機、systemd、ネイティブ共有ライブラリの読み込みは、認証情報と対象環境が必要です。ソースコードのビルドだけではこれらの実環境動作を保証できません。
