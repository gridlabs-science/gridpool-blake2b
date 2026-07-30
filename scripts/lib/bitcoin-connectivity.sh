#!/usr/bin/env bash

gridpool_endpoint_host_port() {
    local endpoint="${1#*://}"
    endpoint="${endpoint%%/*}"
    [[ "$endpoint" == *:* ]] || return 1
    printf '%s %s\n' "${endpoint%:*}" "${endpoint##*:}"
}

gridpool_probe_tcp_endpoint() {
    local endpoint="$1"
    local timeout_seconds="${2:-3}"
    local host port
    read -r host port < <(gridpool_endpoint_host_port "$endpoint") || return 1
    [[ -n "$host" && "$port" =~ ^[0-9]+$ ]] || return 1

    timeout "$timeout_seconds" bash -c \
        'exec 3<>"/dev/tcp/$1/$2"' _ "$host" "$port" >/dev/null 2>&1
}

gridpool_rpc_credentials() {
    local username="${1:-}"
    local password_file="${2:-}"
    local cookie_file="${3:-}"

    if [[ -n "$cookie_file" ]]; then
        [[ -r "$cookie_file" ]] || return 1
        tr -d '\r\n' <"$cookie_file"
        return
    fi

    [[ -n "$username" && -n "$password_file" && -r "$password_file" ]] || return 1
    printf '%s:' "$username"
    tr -d '\r\n' <"$password_file"
}

gridpool_rpc_call() {
    local rpc_url="$1"
    local username="${2:-}"
    local password_file="${3:-}"
    local cookie_file="${4:-}"
    local method="$5"
    local params="${6:-[]}"
    local timeout_seconds="${7:-3}"
    local credentials curl_config

    credentials="$(gridpool_rpc_credentials "$username" "$password_file" "$cookie_file")" || return 1
    curl_config="$(mktemp)"
    chmod 0600 "$curl_config"
    trap 'rm -f "$curl_config"' RETURN
    {
        printf 'silent\n'
        printf 'show-error\n'
        printf 'fail-with-body\n'
        printf 'max-time = %q\n' "$timeout_seconds"
        printf 'user = %q\n' "$credentials"
        printf 'header = %q\n' 'content-type: application/json'
    } >"$curl_config"

    curl --config "$curl_config" \
        --data-binary "{\"jsonrpc\":\"1.0\",\"id\":\"gridpool-preflight\",\"method\":\"${method}\",\"params\":${params}}" \
        "$rpc_url"
}

gridpool_check_bitcoin_connectivity() {
    local rpc_url="$1"
    local username="${2:-}"
    local password_file="${3:-}"
    local cookie_file="${4:-}"
    local hashblock_endpoint="${5:-}"
    local rawblock_endpoint="${6:-}"
    local timeout_seconds="${7:-3}"
    local info notifications

    info="$(gridpool_rpc_call \
        "$rpc_url" "$username" "$password_file" "$cookie_file" \
        getblockchaininfo '[]' "$timeout_seconds")" || {
        printf 'Bitcoin RPC authentication or connectivity failed at %s.\n' "$rpc_url" >&2
        return 1
    }

    if ! jq -e '.error == null and .result.blocks >= 0 and .result.headers >= 0' \
        >/dev/null <<<"$info"; then
        printf 'Bitcoin RPC returned an invalid getblockchaininfo response.\n' >&2
        return 1
    fi

    local blocks headers ibd
    blocks="$(jq -r '.result.blocks' <<<"$info")"
    headers="$(jq -r '.result.headers' <<<"$info")"
    ibd="$(jq -r '.result.initialblockdownload // false' <<<"$info")"
    if [[ "$ibd" == "true" || "$blocks" -lt "$headers" ]]; then
        printf 'Bitcoin RPC is reachable but not synchronized (blocks=%s headers=%s ibd=%s).\n' \
            "$blocks" "$headers" "$ibd" >&2
        return 2
    fi

    notifications="$(gridpool_rpc_call \
        "$rpc_url" "$username" "$password_file" "$cookie_file" \
        getzmqnotifications '[]' "$timeout_seconds")" || {
        printf 'Bitcoin RPC getzmqnotifications failed.\n' >&2
        return 1
    }

    if [[ -n "$hashblock_endpoint" ]]; then
        jq -e '.result[]? | select(.type == "pubhashblock")' >/dev/null <<<"$notifications" || {
            printf 'Bitcoin Core does not advertise a pubhashblock publisher.\n' >&2
            return 1
        }
        gridpool_probe_tcp_endpoint "$hashblock_endpoint" "$timeout_seconds" || {
            printf 'Bitcoin hashblock ZMQ endpoint is unreachable: %s\n' "$hashblock_endpoint" >&2
            return 1
        }
    fi

    if [[ -n "$rawblock_endpoint" ]]; then
        jq -e '.result[]? | select(.type == "pubrawblock")' >/dev/null <<<"$notifications" || {
            printf 'Bitcoin Core does not advertise a pubrawblock publisher.\n' >&2
            return 1
        }
        gridpool_probe_tcp_endpoint "$rawblock_endpoint" "$timeout_seconds" || {
            printf 'Bitcoin rawblock ZMQ endpoint is unreachable: %s\n' "$rawblock_endpoint" >&2
            return 1
        }
    fi

    printf 'Bitcoin attached-node preflight passed (height=%s, hashblock=%s, rawblock=%s).\n' \
        "$blocks" "${hashblock_endpoint:-disabled}" "${rawblock_endpoint:-disabled}"
}
