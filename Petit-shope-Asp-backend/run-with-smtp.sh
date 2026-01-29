#!/usr/bin/env bash
set -euo pipefail

# Usage: ./run-with-smtp.sh
# Copies env from smtp.env (local, not committed) and starts the backend.

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_FILE="$DIR/smtp.env"

if [ ! -f "$ENV_FILE" ]; then
  echo "smtp.env not found in $DIR"
  echo "Copy smtp.env.example to smtp.env and fill values, then run this script."
  exit 1
fi

# Export variables from smtp.env (ignore comments and blank lines)
set -a
# shellcheck disable=SC2016
. <(grep -v '^\s*#' "$ENV_FILE" | sed '/^\s*$/d')
set +a

cd "$DIR"
exec dotnet run
