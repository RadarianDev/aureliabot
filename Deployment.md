# Deploying AureliaBot

## On a Windows machine that stays on

The simple way: copy the repo, build it, and run the build.

```powershell
dotnet build -c Release
.\bin\Release\net10.0\AureliaBot.exe
```

- **Settings:** put the token in `appsettings.Local.json` next to `AureliaBot.csproj` (it is copied into the
  build). The bot finds the repo's `config/` and `content/` by walking up from the build folder, so edits there
  apply live. The database (`aurelia.db`) and `snapshots/` are created in the folder you start it from.
- **To update:** stop the bot (Ctrl+C waits for a resolution in progress), copy or pull the new repo, build, start.
- **Keep it awake:** turn off sleep in Windows power settings, and set active hours so Windows Update doesn't
  restart the VM at resolution time. A resolution missed while the bot was off runs once when it starts again.
- **One running copy per token:** don't run the bot on your PC with the same token while the VM runs it, or every
  resolution runs twice. Use a second bot application for testing.
- **Backups:** copy `aurelia.db` somewhere safe now and then, while the bot is stopped.

The rest of this file covers a Linux server with a service, for if you ever move it.

> **Before moving to Linux:** add SkiaSharp's Linux native library back, matching the `SkiaSharp`
> version in the csproj: `dotnet add package SkiaSharp.NativeAssets.Linux.NoDependencies --version <same>`.
> Without it the seat charts, vote charts and province map fail to render on Linux.

## On a Linux server

### What you need

- **A Linux server that stays on.** A 1 GB VPS is plenty (Ubuntu 24.04 or Debian 12; Hetzner, OVH, DigitalOcean
  and the like cost a few euros a month). Your own PC works too, but only while it is on and online.
- **SSH access** from this machine (`ssh root@your-server` works). Windows 10/11 has `ssh`, `scp` and `tar` built in.
- **No .NET on the server.** `deploy.ps1` publishes a self-contained build.
- **`sqlite3`** on the server, only for backups: `apt install sqlite3`.

### Discord settings

- In the [developer portal](https://discord.com/developers/applications), under *Bot*, turn on the
  **Server Members** and **Message Content** intents.
- **One bot token, one running copy.** If your PC and the server run on the same token, both run every
  resolution and post everything twice. Make a second application for testing, and keep the real token on the
  server only.
- `Discord:GuildId` in `appsettings.json` registers commands to your server instantly. Leave it set.

### First install

On the server, as root:

```sh
useradd --system --home /var/lib/aureliabot --create-home --shell /usr/sbin/nologin aureliabot
mkdir -p /opt/aureliabot
printf 'Discord__Token=YOUR_BOT_TOKEN\n' > /etc/aureliabot.env
chmod 600 /etc/aureliabot.env
```

Copy the service file and enable it:

```sh
scp deploy/aureliabot.service root@your-server:/etc/systemd/system/aureliabot.service
ssh root@your-server "systemctl daemon-reload && systemctl enable aureliabot"
```

Then deploy, from the repo on this machine:

```powershell
./deploy/deploy.ps1 -Server your-server
```

It builds, uploads, replaces `/opt/aureliabot`, and starts the service. Run it again after every change.

### Where things live

| Path | What |
| --- | --- |
| `/opt/aureliabot` | The bot, with `config/`, `content/` and `assets/`. **Replaced on every deploy**: edit these files in the repo, not on the server. |
| `/var/lib/aureliabot/aurelia.db` | The game. Never replaced by a deploy. |
| `/var/lib/aureliabot/snapshots/` | The last 30 pre-resolution snapshots, for `/gm undo`. |
| `/var/lib/aureliabot/backups/` | Daily backups, if you set them up (below). |
| `/etc/aureliabot.env` | The token. Readable only by root. |

Settings come from `appsettings.json`, then environment variables (`Discord__Token`, `Discord__GuildId`,
`Database__Path`, `Game__Root`), which the service file and `/etc/aureliabot.env` set.

### Everyday use

```sh
systemctl status aureliabot          # is it running?
journalctl -u aureliabot -f          # watch the log
journalctl -u aureliabot --since today
systemctl restart aureliabot
```

- **Crashes and reboots:** systemd restarts the bot within 10 seconds, and starts it on boot.
- **Stopping:** the bot waits for a resolution in progress (up to two minutes), then disconnects cleanly.
- **Downtime:** a resolution missed while the bot was down runs once when it comes back, and the GM is told
  in #gm.
- **Deploying mid-game is safe:** the database is kept, and the service stops cleanly. Avoid deploying in the
  minute a resolution is due.

### Backups

Snapshots live on the same disk as the game, so also take a daily copy, ideally off the server.

```sh
scp deploy/backup.sh root@your-server:/usr/local/bin/aureliabot-backup
ssh root@your-server "chmod +x /usr/local/bin/aureliabot-backup && (crontab -l 2>/dev/null; echo '30 4 * * * /usr/local/bin/aureliabot-backup') | crontab -"
```

It keeps the last 30 daily copies in `/var/lib/aureliabot/backups/`. Pull them to your PC now and then:

```powershell
scp -r root@your-server:/var/lib/aureliabot/backups ./backups
```

To restore: `systemctl stop aureliabot`, copy a backup over `/var/lib/aureliabot/aurelia.db`,
`chown aureliabot:aureliabot` it, and start the service.

### Moving your test game to the server

To keep the game you have been running on your PC, stop the bot there and copy the database across before the
first start on the server:

```powershell
scp aurelia.db root@your-server:/var/lib/aureliabot/aurelia.db
ssh root@your-server "chown aureliabot:aureliabot /var/lib/aureliabot/aurelia.db"
```

To start fresh instead, don't copy it: the bot creates an empty database, and `/gm setup`, `/gm cleanup` and
`/gm start` take it from there.
