# Session Handoff: 2026-04-25

Workspace: `/home/keegreil/Documents/GitHub/boot-protocol`

This note captures the current BOOT/DATUM stability work so a new session can resume without reconstructing the last several days of testing.

## Current Repo State

The repo is dirty. Important modified files:

- `boot_portal/Controllers/BootNetworkController.cs`
- `boot_portal/Models/BootProtocolModels.cs`
- `boot_portal/Program.cs`
- `boot_portal/Services/BootProtocolStateService.cs`
- `scripts/boot-g2-monitor.mjs`
- `scripts/boot-g2-soak.sh`
- `scripts/boot-soak-report.mjs`

New untracked docs:

- `docs/datum-session-churn-investigation.md`
- `docs/datum-upstream-pr-draft.md`

Do not revert unrelated dirty files. Some of these changes are deployed and under test.

## Recent Work

Main objective: stabilize DATUM/BOOT testing, improve soak tooling, and reduce false hashrate chart spikes.

Key code changes made:

- BOOT stale payout behavior changed so stale payout mismatches trigger template refresh warnings instead of forced DATUM disconnects by default.
- Added config around stale DATUM handling in `Program.cs`: `stale_datum_force_disconnect_enabled`, `stale_datum_refresh_interval_seconds`.
- Added DATUM session/protocol telemetry endpoints earlier: `/api/network/datum-sessions`, `/api/network/datum-protocol-events`, `/api/network/datum-share-responses`.
- Added monitor checkpointing:
  - `scripts/boot-g2-monitor.mjs` now writes partial JSON during runs and appends `.checkpoints.jsonl`.
  - It handles `SIGINT`/`SIGTERM` and writes a partial final report.
  - `scripts/boot-g2-soak.sh` now tries to emit a summary on shell exit.
  - `scripts/boot-soak-report.mjs` now tolerates `events`, `sessions`, or `items` payload shapes.
- Hashrate chart samples now skip persisted team estimates during the first `60s` after round rotation in `BootProtocolStateService.cs`, to suppress obvious lucky early-round spikes.

Verification already run before deploy:

- `node --check scripts/boot-g2-monitor.mjs`
- `node --check scripts/boot-soak-report.mjs`
- `bash -n scripts/boot-g2-soak.sh`
- `dotnet test boot.tests/boot.tests.csproj --no-restore`: `34/34` passed
- Short smoke test produced:
  - `logs/monitor-smoke.json`
  - `logs/monitor-smoke.checkpoints.jsonl`

## Deployment State

Main node:

- Deployed local publish and restarted `bootserverapp`.
- After restart, main was healthy on round `972`, tip `946588`, local DATUM around `1 TH/s`.

Laptop node:

- WSL runtime is under `/home/gridlabs/boot-wsl/current`.
- Runtime config should be symlinked to `/home/gridlabs/boot-wsl-data/boot_portal_config.json`.
- Deployment uses self-contained Linux publish copied as tarball to Windows host, then unpacked inside WSL.
- Laptop was restarted and checked healthy after deploy: round `972`, tip `946588`, local DATUM around `9 TH/s`.

Important laptop deploy details:

- SSH host `boot-laptop` lands in a Windows shell, not Linux. Use `wsl.exe bash -lc '...'`.
- `rsync` to laptop failed because the remote side is Windows. Use `scp` tarball to `C:\Users\keegr\...`, then unpack from `/mnt/c/Users/keegr/...` inside WSL.

## Latest Soak Attempt

A 1 hour soak was started:

- PID at start: `2021944`
- Expected finish: `2026-04-25 10:27:48 EDT`
- Runtime log: `logs/g2-monitor-20260425-1h-checkpointed.nohup.log`
- Expected monitor output: `logs/g2-monitor-20260425-1h-checkpointed.json`
- Expected checkpoints: `logs/g2-monitor-20260425-1h-checkpointed.checkpoints.jsonl`
- Expected summary: `logs/g2-monitor-20260425-1h-checkpointed-summary.json`

After a later check:

- No soak process was running.
- Runtime log only had the startup banner.
- No monitor/checkpoint/summary files were listed by `ls`.

Next session should debug why the background soak did not write after starting. Good first checks:

- Run `./scripts/boot-g2-soak.sh 2m logs/test.json` in foreground.
- Verify `node scripts/boot-g2-monitor.mjs ...` can fetch both URLs and write checkpoints.
- Inspect shell exit behavior in `scripts/boot-g2-soak.sh`.

## Technical Findings

### DATUM Churn

Laptop previously had repeat DATUM disconnects every `12s-29s`.

Root cause appears to be DATUM-side thread exit/reconnect race around file-scope globals, exposed by the `5s` post-exit linger in `datum_protocol.c`.

Minimal DATUM patch under test on laptop:

- remove post-exit `5s` linger before template notify
- snapshot timeout globals before evaluating timeout branches

This dramatically reduced rapid churn. Details are in:

- `docs/datum-session-churn-investigation.md`
- `docs/datum-upstream-pr-draft.md`

### Hashrate Spike

Main-only spike around `2026-04-25 00:10 EDT` was not likely real hashrate.

Raw series showed one main sample at `2026-04-25T04:10:36Z` with `945 TH/s`, only about `12s` after round rotation. Laptop did not record the same spike.

Most likely cause: early-round sampling artifact plus per-node sample timing, not a real external miner.

The new `60s` sample guard should prevent this artifact from being persisted.

### Summary API

`/api/network/summary` was not actually blank. Earlier null reads used wrong property names.

Correct fields include:

- `currentRoundNumber`
- `currentTipBlockHeight`
- `currentRoundObservedHashrateThs`
- `localDatumHashrateThs`
- `localDatumDiagnostics`

## Recommended Next Steps

1. Inspect the dirty diff and this handoff file.
2. Debug why the latest checkpointed soak produced only the startup banner.
3. Run a short foreground soak and confirm partial JSON plus `.checkpoints.jsonl` appear within the first minute.
4. Run a short background soak and confirm the same behavior.
5. Check live node summaries with the correct field names.
6. If tooling works, run a 1 hour soak and analyze the output.
7. Watch whether early-round hashrate spike artifacts are gone after the `60s` guard.

## Suggested Next-Session Prompt

```text
We are in /home/keegreil/Documents/GitHub/boot-protocol. Read docs/session-handoff-2026-04-25.md first and continue from there.

Main task: debug why the latest checkpointed 1 hour soak produced only the startup banner and no monitor/checkpoint/summary files. Start by inspecting the dirty diff and logs/g2-monitor-20260425-1h-checkpointed.nohup.log, then run a short foreground soak like ./scripts/boot-g2-soak.sh 2m logs/test.json. Confirm that scripts/boot-g2-monitor.mjs writes partial JSON and .checkpoints.jsonl within the first minute. Then verify a short background soak.

After the tooling works, check live node summaries using currentRoundNumber/currentTipBlockHeight/currentRoundObservedHashrateThs/localDatumHashrateThs, then run and analyze a 1 hour soak. Do not revert unrelated dirty files.
```
