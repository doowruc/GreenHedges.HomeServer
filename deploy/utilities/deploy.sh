#!/usr/bin/env bash
# Deploy Utilities: deploy/utilities/deploy.sh [--self-contained] [user@host]. See deploy/deploy.sh.
exec "$(dirname "$0")/../deploy.sh" utilities "$@"
