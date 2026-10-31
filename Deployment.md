# Deploying AureliaBot

How to run the bot around the clock on a small Linux server, and keep it updated from this repo.

## What you need

- **A Linux server that stays on.** A 1 GB VPS is plenty (Ubuntu 24.04 or Debian 12; Hetzner, OVH, DigitalOcean
  and the like cost a few euros a month). Your own PC works too, but only while it is on and online.
- **SSH access** from this machine (`ssh root@your-server` works). Windows 10/11 has `ssh`, `scp` and `tar` built in.
- **No .NET on the server.** `deploy.ps1` publishes a self-contained build.
- **`sqlite3`** on the server, only for backups: `apt install sqlite3`.

## Discord settings

- In the [developer portal](https://discord.com/developers/applications), under *Bot*, turn on the
  **Server Members** and **Message Content** intents.
- **One bot token, one running copy.** If your PC and the server run on the same token, both run every
  resolution and post everything twice. Make a second application for testing, and keep the real token on the
  server only.
- `Discord:GuildId` in `appsettings.json` registers commands to your server instantly. Leave it set.

## First install

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

## Where things live

| Path | What |
| --- | --- |
| `/opt/aureliabot` | The bot, with `config/`, `content/` and `assets/`. **Replaced on every deploy**: edit these files in the repo, not on the server. |
| `/var/lib/aureliabot/aurelia.db` | The game. Never replaced by a deploy. |
| `/var/lib/aureliabot/snapshots/` | The last 30 pre-resolution snapshots, for `/gm undo`. |
| `/var/lib/aureliabot/backups/` | Daily backups, if you set them up (below). |
| `/etc/aureliabot.env` | The token. Readable only by root. |

Settings come from `appsettings.json`, then environment variables (`Discord__Token`, `Discord__GuildId`,
`Database__Path`, `Game__Root`), which the service file and `/etc/aureliabot.env` set.

## Everyday use

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

## Backups

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

## Moving your test game to the server

To keep the game you have been running on your PC, stop the bot there and copy the database across before the
first start on the server:

```powershell
scp aurelia.db root@your-server:/var/lib/aureliabot/aurelia.db
ssh root@your-server "chown aureliabot:aureliabot /var/lib/aureliabot/aurelia.db"
```

To start fresh instead, don't copy it: the bot creates an empty database, and `/gm setup`, `/gm cleanup` and
`/gm start` take it from there.
