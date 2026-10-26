#!/usr/bin/env bash
# Deploy the Weather collector: deploy/weather/deploy.sh [--self-contained] [user@host]. See deploy/deploy.sh.
exec "$(dirname "$0")/../deploy.sh" weather "$@"
