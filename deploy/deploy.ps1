param(
    [Parameter(Mandatory)][string]$Server,
    [string]$User = "root"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "out\linux"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $root "AureliaBot.csproj") -c Release -r linux-x64 --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
Remove-Item (Join-Path $out "appsettings.Local.json") -ErrorAction SilentlyContinue

$target = "$User@$Server"
tar -C $out -czf (Join-Path $root "out\aureliabot.tar.gz") .
scp (Join-Path $root "out\aureliabot.tar.gz") "${target}:/tmp/aureliabot.tar.gz"
ssh $target "systemctl stop aureliabot; rm -rf /opt/aureliabot && mkdir -p /opt/aureliabot && tar -C /opt/aureliabot -xzf /tmp/aureliabot.tar.gz && chmod +x /opt/aureliabot/AureliaBot && chown -R aureliabot:aureliabot /opt/aureliabot && systemctl start aureliabot && systemctl --no-pager status aureliabot | head -5"
