#!/usr/bin/env bash
# Deploy Pay & Pensions: deploy/pay-and-pensions/deploy.sh [--self-contained] [user@host]. See deploy/deploy.sh.
exec "$(dirname "$0")/../deploy.sh" pay-and-pensions "$@"
