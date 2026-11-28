#!/usr/bin/env bash
set -euo pipefail

PORTS_CSV="7000,7001"
REMOVE=0
DRY_RUN=0

usage() {
  cat <<'EOF'
Usage: open-firewall.sh [OPTIONS]

Open (or remove) TCP firewall rules for DFS nodes using ufw on Linux.

Options:
  --ports <p1,p2,...>  Comma-separated TCP ports to allow/remove (default: 7000,7001)
  --remove             Remove the firewall rules instead of adding them
  --dry-run            Print ufw commands without executing them
  -h, --help           Show this help message and exit
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --ports)
      if [[ $# -lt 2 || -z "$2" ]]; then
        echo "Error: --ports requires a non-empty comma-separated port list." >&2
        exit 1
      fi
      PORTS_CSV="$2"
      shift 2
      ;;
    --ports=*)
      PORTS_CSV="${1#*=}"
      if [[ -z "$PORTS_CSV" ]]; then
        echo "Error: --ports requires a non-empty comma-separated port list." >&2
        exit 1
      fi
      shift
      ;;
    --remove)
      REMOVE=1
      shift
      ;;
    --dry-run)
      DRY_RUN=1
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

IFS=',' read -r -a RAW_PORTS <<< "$PORTS_CSV"
if [[ ${#RAW_PORTS[@]} -eq 0 ]]; then
  echo "Error: No ports specified." >&2
  exit 1
fi

VALID_PORTS=()
for raw_port in "${RAW_PORTS[@]}"; do
  port="${raw_port//[[:space:]]/}"
  if [[ ! "$port" =~ ^[1-9][0-9]*$ ]] || (( 10#$port < 1 || 10#$port > 65535 )); then
    echo "Error: Invalid port '$raw_port' (must be an integer between 1 and 65535)." >&2
    exit 1
  fi
  VALID_PORTS+=("$port")
done

if [[ "$DRY_RUN" -eq 0 ]]; then
  UFW_BIN=""
  if command -v ufw >/dev/null 2>&1; then
    UFW_BIN="$(command -v ufw)"
  elif [[ -x /usr/sbin/ufw ]]; then
    UFW_BIN="/usr/sbin/ufw"
  elif [[ -x /sbin/ufw ]]; then
    UFW_BIN="/sbin/ufw"
  else
    echo "Error: 'ufw' is not installed. Install ufw or configure your firewall manually." >&2
    exit 1
  fi
  if [[ "$EUID" -ne 0 ]]; then
    if command -v sudo >/dev/null 2>&1; then
      UFW_CMD=(sudo "$UFW_BIN")
    else
      echo "Error: Root privileges are required to modify ufw rules." >&2
      exit 1
    fi
  else
    UFW_CMD=("$UFW_BIN")
  fi
fi

for port in "${VALID_PORTS[@]}"; do
  if [[ "$REMOVE" -eq 1 ]]; then
    if [[ "$DRY_RUN" -eq 1 ]]; then
      echo "[dry-run] ufw --force delete allow ${port}/tcp"
    else
      "${UFW_CMD[@]}" --force delete allow "${port}/tcp"
    fi
  else
    if [[ "$DRY_RUN" -eq 1 ]]; then
      echo "[dry-run] ufw allow ${port}/tcp comment 'DFS'"
    else
      "${UFW_CMD[@]}" allow "${port}/tcp" comment "DFS"
    fi
  fi
done
