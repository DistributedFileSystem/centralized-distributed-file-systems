#!/bin/sh
set -eu

role="${NODE_ROLE:-}"

case "$role" in
  metadata|metadata-*)
    exec dotnet /app/metadata/Dfs.MetadataServer.dll "$@"
    ;;
  data)
    exec dotnet /app/data/Dfs.DataServer.dll "$@"
    ;;
  *)
    echo "Error: NODE_ROLE must be 'metadata' (or 'metadata-*') or 'data' (got: '${role}')." >&2
    exit 1
    ;;
esac
