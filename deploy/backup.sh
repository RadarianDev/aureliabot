#!/bin/sh
set -eu

DATA=/var/lib/aureliabot
DEST="$DATA/backups"
KEEP=30

mkdir -p "$DEST"
sqlite3 "$DATA/aurelia.db" ".backup '$DEST/aurelia-$(date +%Y-%m-%d-%H%M).db'"
ls -1t "$DEST"/aurelia-*.db | tail -n +$((KEEP + 1)) | xargs -r rm --
