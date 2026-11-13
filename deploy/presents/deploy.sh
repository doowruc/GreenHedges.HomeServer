#!/usr/bin/env bash
# Deploy Presents: deploy/presents/deploy.sh [--self-contained] [user@host]. See deploy/deploy.sh.
exec "$(dirname "$0")/../deploy.sh" presents "$@"
