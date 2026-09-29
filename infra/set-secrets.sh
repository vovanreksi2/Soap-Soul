#!/usr/bin/env bash
# Puts the app's secrets into the Key Vault created by main.bicep and restarts the app so it reads them.
# Run after the first deployment, and again to change a key. Needs Owner (or User Access Administrator)
# on the resource group, because it grants you "Key Vault Secrets Officer" on the vault.
#
#   infra/set-secrets.sh <resource-group>                 # sets missing secrets, keeps existing ones
#   infra/set-secrets.sh <resource-group> --anthropic     # replace the Anthropic API key
#   infra/set-secrets.sh <resource-group> --rotate-mcp    # generate a new MCP key
#
# The Anthropic key is read from ANTHROPIC_API_KEY or asked for; it is never passed on a command line.
set -euo pipefail
export MSYS_NO_PATHCONV=1 # Git Bash on Windows: keep /subscriptions/... as is

RG=${1:?usage: $0 <resource-group> [--anthropic] [--rotate-mcp]}
shift
SET_ANTHROPIC=false
ROTATE_MCP=false
for arg in "$@"; do
  case $arg in
    --anthropic) SET_ANTHROPIC=true ;;
    --rotate-mcp) ROTATE_MCP=true ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

KV=$(az keyvault list -g "$RG" --query "[0].name" -o tsv)
APP=$(az webapp list -g "$RG" --query "[?starts_with(name, 'app-')].name | [0]" -o tsv)
[ -n "$KV" ] || { echo "No Key Vault in $RG. Deploy infra/main.bicep first." >&2; exit 1; }

# Data-plane access for you (RBAC vault); the assignment can take a minute to apply.
az role assignment create --assignee-object-id "$(az ad signed-in-user show --query id -o tsv)" \
  --assignee-principal-type User --role "Key Vault Secrets Officer" \
  --scope "$(az keyvault show -n "$KV" --query id -o tsv)" -o none

TMP=$(mktemp)
trap 'rm -f "$TMP"' EXIT
chmod 600 "$TMP"

exists() { az keyvault secret show --vault-name "$KV" -n "$1" --query id -o tsv >/dev/null 2>&1; }

# Writes the value in $TMP to a secret, retrying while the role assignment propagates.
put() {
  for _ in $(seq 1 18); do
    az keyvault secret set --vault-name "$KV" -n "$1" --file "$TMP" --encoding utf-8 -o none 2>/dev/null && return 0
    sleep 10
  done
  echo "Could not write $1 to $KV (no access yet?)." >&2
  exit 1
}

# Wait until the vault answers for us (also covers reading existing secrets).
for _ in $(seq 1 18); do
  az keyvault secret list --vault-name "$KV" -o none 2>/dev/null && break
  sleep 10
done

if $SET_ANTHROPIC || ! exists Llm--ApiKey; then
  if [ -z "${ANTHROPIC_API_KEY:-}" ]; then
    read -rsp "Anthropic API key: " ANTHROPIC_API_KEY
    echo
  fi
  [ -n "$ANTHROPIC_API_KEY" ] || { echo "Empty key." >&2; exit 1; }
  printf '%s' "$ANTHROPIC_API_KEY" > "$TMP"
  put Llm--ApiKey
  echo "Llm--ApiKey set."
fi

if $ROTATE_MCP || ! exists Mcp--ApiKey; then
  openssl rand -base64 48 | tr -d '\n/+=' | cut -c1-48 > "$TMP"
  put Mcp--ApiKey
  echo "Mcp--ApiKey set (new key)."
fi

echo
if [ -n "$APP" ]; then
  az webapp restart -g "$RG" -n "$APP" -o none
  echo "Restarted $APP."
  echo "MCP endpoint: https://$(az webapp show -g "$RG" -n "$APP" --query defaultHostName -o tsv)/mcp"
fi
echo "MCP key:      az keyvault secret show --vault-name $KV -n Mcp--ApiKey --query value -o tsv"
