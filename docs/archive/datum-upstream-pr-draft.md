# DATUM PR Draft: tighten timeout reads and remove post-exit linger race window

## Proposed Title

`datum_protocol: snapshot timeout state and remove post-exit linger after reconnect`

## Problem Summary

In a GridPool-backed mining setup, one DATUM client repeatedly disconnected and reconnected every roughly 12s to 29s while another DATUM deployment remained stable.

The disconnect warning on the affected client was:

- `No data received from server in over 600 seconds. Exiting protocol thread to retry.`

That warning was inconsistent with observed reality:

- the GridPool server was still sending accepted-share responses and coinbaser responses immediately before the client closed
- the DATUM client logs showed impossible timeout-state combinations where the same session claimed timeout while also recording a newer server-message timestamp

## Why This Looks Like a DATUM-Side Bug

The affected code in `src/datum_protocol.c` uses file-scope protocol state such as:

- `datum_protocol_mainloop_tsms`
- `latest_server_msg_tsms`
- `datum_last_accepted_share_tsms`
- `datum_last_accepted_share_local_tsms`

The protocol thread exit path historically:

- closed the socket
- set `datum_protocol_client_active = 0`
- freed the POW queue
- waited up to 5 seconds for another protocol thread to reconnect
- then forced a new job with `datum_blocktemplates_notify_othercause()`

That 5 second linger was introduced by upstream commit `f9e03d7` with message:

- `Bugfix: If DATUM connection is lost, switch to a new job ASAP`

The intended behavior makes sense, but the linger leaves a detached exiting thread alive after the connector has already become eligible for replacement. Because timeout state is file-scope global, that widens the chance of cross-thread/shared-state interference during reconnect.

## Minimal Fix

1. Snapshot timeout-related globals into locals before evaluating timeout branches.
2. Remove the post-exit linger before `datum_blocktemplates_notify_othercause()`.

This keeps the urgent job refresh behavior, but avoids leaving the old detached protocol thread around for several more seconds after it is logically inactive.

## Validation Summary

Before patch:

- repeated reconnects every roughly 12s to 29s
- repeated false `No data received ... over 600 seconds` warnings

After wider debug instrumentation:

- behavior improved sharply, indicating the bug was in DATUM-side timeout/reconnect handling rather than GridPool messaging

After reduction to the minimal patch:

- the affected client stayed on one DATUM session for a clean 3 minute validation window
- GridPool protocol telemetry showed zero `session-close` events in that window
- accepted share traffic continued normally

## Risk / Tradeoff

The main behavioral change is removing the old 5 second linger. If that linger was intentionally masking another reconnect edge case, a follow-up upstream review should re-evaluate whether a safer replacement is needed.

A stronger long-term fix would move protocol-thread state out of file-scope globals entirely, but the minimal patch is much smaller and easier to validate.
