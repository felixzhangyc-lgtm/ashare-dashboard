#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/../../../.." && pwd)"
PROJECT_DIR="$ROOT_DIR/MarketCharts"
DLL_PATH="$PROJECT_DIR/bin/Debug/net11.0/MarketCharts.dll"

find_dotnet() {
  if command -v dotnet >/dev/null 2>&1; then
    command -v dotnet
    return 0
  fi
  for candidate in "/usr/local/share/dotnet/dotnet" "$HOME/.dotnet/dotnet" "/opt/homebrew/bin/dotnet"; do
    if [ -x "$candidate" ]; then
      echo "$candidate"
      return 0
    fi
  done
  echo "Error: dotnet SDK 11 not found in PATH or standard locations." >&2
  return 1
}

needs_build() {
  if [ ! -f "$DLL_PATH" ]; then
    return 0
  fi
  if [ -n "$(find "$PROJECT_DIR" -maxdepth 1 \( -name '*.cs' -o -name '*.csproj' -o -name '*.html' \) -newer "$DLL_PATH" 2>/dev/null)" ]; then
    return 0
  fi
  return 1
}

if [ $# -eq 0 ] || [ "$1" = "-h" ] || [ "$1" = "--help" ]; then
  cat <<'EOF'
Usage: run_charts.sh <regime|industry|tech|drawdown|turnover|chinext|all|everything|dashboard> [--from-csv] [--skip-update] [--output-dir PATH] [--rebuild]

Commands:
  regime        Compute ChiNext 4D Bull/Bear Regime Score Dashboard (ranked #1)
  industry      Update SW L1 industry margin cumulative chart and print daily report
  tech          Update Tech (STAR50+ChiNext ETF) vs Dividend Low-Vol ETF chart
  drawdown      Update Margin Drawdown Speed & ChiNext Index chart
  turnover      Update A-share Market Turnover Rate & Peak/Trough Thermometer chart
  chinext       Update ChiNext Turnover Rate & Peak/Trough Thermometer chart
  all           Run industry, tech, and drawdown (also refreshes dashboard.html)
  everything    Run all 6 report groups + refresh dashboard.html
  dashboard     Rebuild interactive outputs/dashboard.html from existing CSV files

Options:
  --from-csv        Redraw charts offline from existing CSV files
  --skip-update     (industry only) Print daily report from existing CSV without fetching
  --output-dir PATH Override output directory (default: <repo>/outputs)
  --rebuild         Force rebuilding MarketCharts before running
EOF
  exit 0
fi

FORCE_REBUILD=0
PASS_ARGS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --rebuild)
      FORCE_REBUILD=1
      shift
      ;;
    *)
      PASS_ARGS+=("$1")
      shift
      ;;
  esac
done

DOTNET="$(find_dotnet)"
cd "$ROOT_DIR"

export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp/dotnet-cli-home}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

if [ "$FORCE_REBUILD" -eq 1 ] || needs_build; then
  "$DOTNET" build "$PROJECT_DIR" --nologo >/dev/null
fi

"$DOTNET" "$DLL_PATH" "${PASS_ARGS[@]}"
