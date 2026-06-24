#!/usr/bin/env bash
set -euo pipefail

services=(
  docker.service
  cloudflared.service
  bootserverapp.service
  hydrapool-gridpool.service
)

echo "== systemd =="
for service in "${services[@]}"; do
  active="$(systemctl is-active "$service" 2>/dev/null || true)"
  enabled="$(systemctl is-enabled "$service" 2>/dev/null || true)"
  printf '%-30s active=%-12s enabled=%s\n' "$service" "$active" "$enabled"
done

echo
echo "== listening ports =="
ss -ltnp | grep -E ':(5000|3008|3333|46884|8332)\b' || true

echo
echo "== docker bitcoin =="
docker inspect bitcoin_bitcoind_1 \
  --format 'bitcoin_bitcoind_1 running={{.State.Running}} started={{.State.StartedAt}} restart={{.HostConfig.RestartPolicy.Name}}' \
  2>/dev/null || echo "bitcoin_bitcoind_1 not found"

echo
echo "== http checks =="
if curl -fsS http://127.0.0.1:5000/api/mining/share-advice >/dev/null; then
  echo "GridPool share-advice API: ok"
else
  echo "GridPool share-advice API: FAIL"
fi

if curl -fsS http://127.0.0.1:5000/api/mining/payouts >/dev/null; then
  echo "GridPool payout API: ok"
else
  echo "GridPool payout API: FAIL"
fi

if curl -fsS -u hydrapool:hydrapool http://127.0.0.1:46884/health >/dev/null; then
  echo "Hydrapool API health: ok"
else
  echo "Hydrapool API health: FAIL"
fi
