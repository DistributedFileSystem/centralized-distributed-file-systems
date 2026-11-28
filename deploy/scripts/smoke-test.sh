#!/usr/bin/env bash
set -euo pipefail

DEFAULT_TARGETS="192.168.10.11:7000,192.168.10.11:7001,192.168.10.12:7001,192.168.10.13:7001,192.168.10.14:7001,192.168.10.15:7001"
TARGETS_CSV="$DEFAULT_TARGETS"
TIMEOUT_SEC=3

usage() {
  cat <<'EOF'
Usage: smoke-test.sh [OPTIONS]

Verify TCP reachability to DFS cluster endpoints and report latency.

Options:
  --targets <h1:p1,h2:p2,...>  Comma-separated host:port endpoints
                               (default: 6 Stage 1 LAN endpoints)
  --timeout <seconds>          Per-endpoint TCP timeout in seconds (default: 3)
  -h, --help                   Show this help message and exit
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --targets)
      if [[ $# -lt 2 || -z "$2" ]]; then
        echo "Error: --targets requires a non-empty comma-separated host:port list." >&2
        exit 1
      fi
      TARGETS_CSV="$2"
      shift 2
      ;;
    --targets=*)
      TARGETS_CSV="${1#*=}"
      if [[ -z "$TARGETS_CSV" ]]; then
        echo "Error: --targets requires a non-empty comma-separated host:port list." >&2
        exit 1
      fi
      shift
      ;;
    --timeout)
      if [[ $# -lt 2 || -z "$2" ]]; then
        echo "Error: --timeout requires a positive integer." >&2
        exit 1
      fi
      TIMEOUT_SEC="$2"
      shift 2
      ;;
    --timeout=*)
      TIMEOUT_SEC="${1#*=}"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Error: Unknown argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ ! "$TIMEOUT_SEC" =~ ^[1-9][0-9]*$ ]] || (( 10#$TIMEOUT_SEC < 1 )); then
  echo "Error: Invalid --timeout '$TIMEOUT_SEC' (must be a positive integer)." >&2
  exit 1
fi

IFS=',' read -r -a RAW_TARGETS <<< "$TARGETS_CSV"
if [[ ${#RAW_TARGETS[@]} -eq 0 ]]; then
  echo "Error: No targets specified." >&2
  exit 1
fi

HOSTS=()
PORTS=()
for raw_target in "${RAW_TARGETS[@]}"; do
  target="${raw_target//[[:space:]]/}"
  if [[ "$target" != *:* ]]; then
    echo "Error: Invalid target '$raw_target' (expected host:port)." >&2
    exit 1
  fi
  host="${target%:*}"
  port="${target##*:}"
  if [[ -z "$host" ]] || [[ ! "$port" =~ ^[1-9][0-9]*$ ]] || (( 10#$port < 1 || 10#$port > 65535 )); then
    echo "Error: Invalid target '$raw_target' (expected host:port with port 1-65535)." >&2
    exit 1
  fi
  HOSTS+=("$host")
  PORTS+=("$port")
done

now_ms() {
  local ms
  ms="$(date +%s%3N 2>/dev/null || true)"
  if [[ "$ms" =~ ^[1-9][0-9]*$ ]]; then
    echo "$ms"
  elif command -v python3 >/dev/null 2>&1; then
    python3 -c 'import time; print(int(time.time() * 1000))'
  else
    echo "$(( $(date +%s) * 1000 ))"
  fi
}

check_tcp() {
  local host="$1"
  local port="$2"
  local timeout_sec="$3"

  if command -v timeout >/dev/null 2>&1 && timeout --version >/dev/null 2>&1; then
    timeout "$timeout_sec" bash -ec 'exec 3<>/dev/tcp/"$1"/"$2" && exec 3>&-' _ "$host" "$port" >/dev/null 2>&1
    return $?
  elif command -v python3 >/dev/null 2>&1; then
    python3 -c 'import socket, sys; s = socket.create_connection((sys.argv[1], int(sys.argv[2])), timeout=float(sys.argv[3])); s.close()' "$host" "$port" "$timeout_sec" >/dev/null 2>&1
    return $?
  elif command -v nc >/dev/null 2>&1; then
    nc -z -w "$timeout_sec" "$host" "$port" >/dev/null 2>&1
    return $?
  else
    bash -ec 'exec 3<>/dev/tcp/"$1"/"$2" && exec 3>&-' _ "$host" "$port" >/dev/null 2>&1
    return $?
  fi
}

TOTAL=${#HOSTS[@]}
PASSED=0
FAILED=0

for i in "${!HOSTS[@]}"; do
  host="${HOSTS[$i]}"
  port="${PORTS[$i]}"
  start_ms="$(now_ms)"
  if check_tcp "$host" "$port" "$TIMEOUT_SEC"; then
    end_ms="$(now_ms)"
    elapsed_ms=$(( end_ms - start_ms ))
    echo "[PASS] ${host}:${port} (${elapsed_ms} ms)"
    PASSED=$(( PASSED + 1 ))
  else
    end_ms="$(now_ms)"
    elapsed_ms=$(( end_ms - start_ms ))
    echo "[FAIL] ${host}:${port} (${elapsed_ms} ms)"
    FAILED=$(( FAILED + 1 ))
  fi
done

echo "Summary: ${PASSED}/${TOTAL} endpoints reachable."
if [[ "$FAILED" -gt 0 ]]; then
  exit 1
fi
exit 0
