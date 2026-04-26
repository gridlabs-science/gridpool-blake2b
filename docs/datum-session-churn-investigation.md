# DATUM Session Churn Investigation

Date: 2026-04-23

## Problem

Laptop DATUM was reconnecting to BOOT every roughly 12s to 29s while the main StartOS DATUM remained stable.

Observed BOOT symptom:

- DATUM sessions repeatedly ended as `client-disconnected-no-data`
- BOOT was still accepting shares and serving coinbaser fetch responses immediately before close
- BOOT did not appear to be initiating the disconnect

Observed DATUM symptom:

- Repeated warning: `No data received from server in over 600 seconds. Exiting protocol thread to retry.`
- The warning cadence was about 12s to 29s, not 600s

## Key Findings

### 1. BOOT is not going silent before the disconnect

Using BOOT-side protocol event telemetry:

- accepted share responses and coinbaser responses were still being sent
- the terminal BOOT sequence for churned sessions was typically:
  - `send share-response-accepted`
  - sometimes `send coinbaser-fetch-response`
  - `recv-header-eof`
  - `session-close client-disconnected-no-data`

This argues against BOOT sending an incomplete message and then stalling.

### 2. The same laptop DATUM client can show the same behavior against different BOOT servers

Tests run:

- laptop DATUM -> laptop BOOT over loopback
- laptop DATUM -> main BOOT over LAN

Result:

- both paths showed the same basic close pattern from BOOT's perspective: the client closed the socket

This weakens the theory that the bug is caused by the laptop BOOT runtime itself.

### 3. The DATUM timeout log is internally contradictory

Instrumentation added to `datum_protocol.c` showed timeout warnings like:

- `loop_ts=1776997791918`
- `latest_msg_ts=1776997792813`
- `delta_ms=0`
- `latest_msg_owner_session=7`

That means the same DATUM session claimed it had timed out while also recording a later server-message timestamp for that same session.

This is strong evidence that the timeout check is reading inconsistent shared state.

### 4. Gateway reconnect logic is not launching a replacement thread before the protocol thread marks itself inactive

Instrumentation added around:

- `datum_protocol_client_active`
- `datum_protocol_start_connector()`
- the reconnect checks in `datum_gateway.c`

showed that the gateway starts a new connector only after the old protocol thread transitions `active` from `3` to `0`.

This does not fully eliminate overlap during cleanup, but it weakens the theory that the main gateway loop is blindly double-starting the connector while it is still marked active.

### 5. A narrow timeout-check change materially improved behavior

The current laptop DATUM test build snapshots timeout-related globals into locals before the timeout branch is evaluated and logged.

After that change, churn improved sharply:

- pre-change: repeated 12s to 29s reconnects
- current run: one BOOT session stayed open for about 245s, then the next has remained open well beyond the old failure window

This suggests the timeout branch was being tripped by inconsistent multi-read access to shared globals, not by real server silence.

## Current Hypothesis

Most likely root cause: DATUM protocol timeout logic is using file-scope shared state in a way that is not safe across reconnect boundaries and/or detached thread cleanup windows.

The highest-risk shared fields are:

- `datum_protocol_mainloop_tsms`
- `latest_server_msg_tsms`
- `datum_last_accepted_share_tsms`
- `datum_last_accepted_share_local_tsms`
- `datum_state`
- `protocol_state`

The current evidence points more toward a DATUM-side race / shared-state bug than a BOOT protocol bug.

More specific theory:

- the detached protocol thread can linger in exit cleanup after it has already marked `datum_protocol_client_active = 0`
- the gateway then starts a replacement connector quickly, which is desirable for fast recovery
- both the old exiting thread and the new live thread share file-scope timeout state in `datum_protocol.c`
- the old code also evaluated timeout branches by re-reading those globals multiple times in the same branch
- that makes timing-sensitive false timeout decisions possible even while real server traffic is still flowing

## Minimal Candidate Fix

After the wider debug run isolated the behavior, the DATUM change was reduced to a minimal patch in `src/datum_protocol.c`:

- snapshot timeout-related globals into locals before evaluating the server-silence timeout
- snapshot timeout-related globals into locals before evaluating the `>30s` share-accept timeout
- remove the old 5 second post-exit linger before `datum_blocktemplates_notify_othercause()`

The removed linger came from upstream commit `f9e03d7`:

- message: `Bugfix: If DATUM connection is lost, switch to a new job ASAP`

That linger appears to have been intended to give the replacement connector time to come up before forcing a new job. The intent is reasonable, but combined with file-scope protocol state it appears to widen a race window during reconnect.

## Reduced-Patch Validation

The laptop DATUM was rebuilt with only the minimal patch above.

Spot-check validation on 2026-04-24:

- BOOT protocol telemetry showed a single laptop session id, `datum-615`, for the entire 3 minute validation window
- zero `session-close` events were observed in the BOOT protocol-event stream during that window
- the last BOOT events remained normal mining traffic such as `share-response-accepted` and `coinbaser-fetch-response`
- the DATUM log no longer showed the old repeating `No data received from server in over 600 seconds` churn during that validation window

This is enough to rule out the old 12s to 29s reconnect loop under the reduced patch.

## Candidate Upstream Fix Direction

Minimum-risk fix candidate:

- snapshot timeout-related globals into locals before evaluating timeout branches
- remove the post-exit linger that allows an old detached protocol thread to keep running after the connector is considered inactive

Stronger architectural fix:

- move protocol thread state out of file-scope globals and into per-thread/per-session state
- or otherwise guarantee no cross-session reuse without synchronization

## Overnight Soak

Started:

- monitor pid: `3943528`
- monitor output: `logs/g2-monitor-20260424-023839.json`
- summary output: `logs/g2-monitor-20260424-023839-summary.json`
- runtime log: `logs/g2-monitor-20260424-023839.nohup.log`
- expected finish: `2026-04-24 05:30:00 EDT`

The soak is intended to answer:

- whether the timeout-snapshot change actually suppresses the churn under normal mining load
- whether any new BOOT divergence or reject-pattern regression appears while the laptop DATUM is stable
