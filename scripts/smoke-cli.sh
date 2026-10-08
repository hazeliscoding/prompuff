#!/usr/bin/env bash
# Smoke-tests a published prompuff in a scratch library: quick-save from stdin, render with --var, and an MCP
# session before and after Settings allows it. Used by CI and the release workflow on every platform.
#
#   scripts/smoke-cli.sh <path to prompuff> [command to run it under, such as arch -x86_64]
set -euo pipefail

cli="$1"
shift
run=("$@")

data="$(mktemp -d)"
trap 'rm -rf "$data"' EXIT
export PROMPUFF_DATA_DIR="$data"
if command -v cygpath > /dev/null; then export PROMPUFF_DATA_DIR="$(cygpath -w "$data")"; fi

# The ${run[@]+...} form keeps macOS's bash 3.2 from calling an empty array unbound.
prompuff() { ${run[@]+"${run[@]}"} "$cli" "$@"; }
fail() { echo "::error::$1"; for log in "$data"/logs/*.log; do [ -f "$log" ] && cat "$log"; done; exit 1; }

echo "== version"
prompuff --version

echo "== quick-save"
printf 'You are a senior Angular engineer planning an upgrade of {{repo_name}} to Angular {{target_version}}.\n' |
  prompuff quick-save --title "Angular Upgrade Planner" --tag angular | tee "$data/saved.txt"
grep -q "Saved" "$data/saved.txt" || fail "quick-save didn't save"

echo "== render"
prompuff render "Angular Upgrade Planner" --var repo_name=acme > "$data/rendered.txt" 2> "$data/missing.txt"
cat "$data/rendered.txt" "$data/missing.txt"
grep -q "upgrade of acme to Angular {{target_version}}" "$data/rendered.txt" || fail "render didn't fill repo_name"
grep -q "target_version" "$data/missing.txt" || fail "render didn't list the unfilled variable"

# Sends the requests, then keeps stdin open until the last answer arrives, since the server stops at end of input.
mcp() {
  local out="$1"
  : > "$out"
  {
    printf '%s\n' \
      '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}}' \
      '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
      '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"search_prompts","arguments":{"query":"angular"}}}'
    for _ in $(seq 60); do grep -q '"id":2' "$out" && break; sleep 1; done
  } | prompuff mcp > "$out"
  cat "$out"
}

echo "== mcp, turned off"
mcp "$data/mcp-off.txt"
grep -q "MCP access is turned off" "$data/mcp-off.txt" || fail "MCP answered while it was turned off"

echo "== mcp, turned on"
printf '{ "allowMcp": true }\n' > "$data/settings.json"
mcp "$data/mcp-on.txt"
grep -q "Angular Upgrade Planner" "$data/mcp-on.txt" || fail "MCP search didn't find the prompt"

test ! -e "$HOME/.net/prompuff" || fail "prompuff unpacked files into ~/.net"
echo "prompuff works."
