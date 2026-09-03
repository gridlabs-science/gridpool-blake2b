#!/usr/bin/env bash
set -euo pipefail

source_cookie="${GRIDPOOL_BITCOIN_COOKIE_SOURCE:-/var/lib/gridpool-blake2b/mainnet/.cookie}"
target_directory="${GRIDPOOL_BITCOIN_COOKIE_DIRECTORY:-/opt/gridpool-blake2b/mainnet-private-soak/bitcoin-cookie}"
container_uid="${GRIDPOOL_CONTAINER_UID:-1000}"
container_gid="${GRIDPOOL_CONTAINER_GID:-1000}"

if [[ ! -s "$source_cookie" ]]; then
    printf 'Bitcoin RPC cookie is missing or empty: %s\n' "$source_cookie" >&2
    exit 1
fi

if ! grep -q '^__cookie__:' "$source_cookie"; then
    printf 'Bitcoin RPC cookie has an unexpected format: %s\n' "$source_cookie" >&2
    exit 1
fi

install -d -o "$container_uid" -g "$container_gid" -m 0700 "$target_directory"
install -o "$container_uid" -g "$container_gid" -m 0400 \
    "$source_cookie" "$target_directory/.cookie"
