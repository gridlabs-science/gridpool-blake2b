# Boot UI Modes Plan

## Goal
The current WebUI is excellent for protocol testing, debugging, and ops visibility, but it is too dense for a production-facing mining product. The public UI should support three distinct user mindsets:

- `Lottery Mode`: for small miners who care about odds, jackpots, recent luck, and whether they are "in the game".
- `Business Mode`: for operators who care about payout cadence, variance reduction, operational confidence, and expected revenue share.
- `Nerd Mode`: the current data-heavy dashboard for protocol debugging, testing, and advanced node operators.

## Branding Note
- Current experimental UI name: `Grid Anti-Pool`
- Do not fully propagate branding/copy changes through the product until the final name is chosen
- Once the name is finalized, do one deliberate propagation pass across:
  - page title / hero copy
  - connection instructions
  - docs and proposal materials
  - deployment/sample config naming where relevant

This document defines a UI architecture that can support all three without maintaining three separate pages.

## Product Principle
The data model should stay shared; presentation should change.

That means:
- one backend
- one set of APIs
- one page shell
- one mode toggle that changes layout, emphasis, labels, and visible modules

The mode system should not fork protocol logic or create separate code paths for the core state. It should only change what is shown and how it is explained.

## Top-Level Control
Add a persistent 3-way toggle near the top of the page:

- `LOTTERY`
- `BUSINESS`
- `NERD`

Behavior:
- store selection in `localStorage`
- allow a `?mode=` query param override
- default:
  - anonymous / public viewers: `LOTTERY`
  - local operators can switch freely
- preserve search state across mode changes

## Shared Page Shell
All modes should keep the same top-level structure:

1. Header / mode toggle
2. Search bar
3. Primary summary cards
4. Main narrative content for the selected mode
5. Optional advanced sections

Shared elements that should remain available in all modes:
- search by address
- current round number
- estimated team hashrate
- current Winners List / On Deck summary
- connection instructions

## Mode Definitions

### Lottery Mode
Primary user:
- solo miners
- Bitaxe / home miners
- hobbyists
- people thinking in terms of luck and odds, not variance math

Primary questions:
- Am I on the current Winners List?
- Am I on deck for the next one?
- How often might I get paid?
- How much would a payout be if my address makes the list?
- Is the team healthy and active?

Tone:
- simple
- intuitive
- low jargon
- visually exciting

Priority modules:
- `Am I In?` card
  - current winners: yes/no
  - on deck: yes/no
  - my slot count now
  - my slot count next
- `Your Estimated Odds`
  - recent rounds with address participation
  - rough payout frequency estimate if enough history exists
- `Current Round`
  - round number
  - age
  - estimated team hashrate
  - slot-fill progress
- `Recent Payouts`
  - last 5-10 completed rounds
  - highlight whether searched address got paid
- `Simple Round Timeline`
  - recent rounds with block height, time, and paid addresses

Hide or demote:
- peer table
- state IDs
- commitment preview
- deep charts
- detailed per-peer statuses
- long address and worker strings

Recommended wording changes:
- `Winners List` -> `Current Paid Team`
- `On Deck List` -> `Next Payout Queue`

### Business Mode
Primary user:
- mining businesses
- sovereign operators
- people deciding whether Boot is attractive versus solo or pool mining

Primary questions:
- What variance reduction am I actually getting?
- How much of the team am I contributing?
- How often are payouts happening?
- Is the node/network stable enough to rely on?
- How does my connected hashrate compare to team estimate?

Tone:
- sober
- operational
- ROI / planning oriented

Priority modules:
- `Operational Summary`
  - current round
  - team hashrate
  - local DATUM hashrate
  - peers online
  - latest trigger block
- `Revenue Share / Payout Trend`
  - recent rounds paid to searched address
  - percent of paid slots over time
  - payout BTC over recent windows
- `Variance / Cadence`
  - rounds per day
  - average round duration
  - estimated payout cadence for searched address
- `Team Composition`
  - paid share split across recent rounds
  - trend of local vs team contribution
- `Health`
  - condensed node diagnostics
  - recent accepted/rejected DATUM counters

Hide or demote:
- all-time best diff
- detailed peer status table
- long technical history fields
- full list tables by default

Recommended wording changes:
- `On Deck` -> `Pending Winners`
- `Current Winners` remains acceptable
- emphasize `estimated payout share`, `payout cadence`, `node health`

### Nerd Mode
Primary user:
- protocol devs
- testers
- advanced operators

Primary questions:
- Are nodes converged?
- Are shares relaying?
- Are rejections happening?
- Are rounds rotating correctly?
- Are peer statuses / triggers / edge cases behaving?

Tone:
- raw
- explicit
- dense

Priority modules:
- current dashboard stays mostly intact
- peer status table
- diagnostics sections
- chart scale toggle
- detailed round history
- local DATUM diagnostics
- state IDs / trigger metadata / commitment preview

This mode should preserve the current spirit of the dashboard, with polish but minimal information loss.

## Suggested Information Architecture

### Header
- logo
- mode toggle
- search
- compact connection status

### Summary Cards by Mode
Lottery:
- `Am I In?`
- `Current Round`
- `Next Payout Estimate`

Business:
- `Operations`
- `Payout Cadence`
- `Local vs Team Hashrate`

Nerd:
- current 3-card dashboard, expanded diagnostics

### Main Content by Mode
Lottery:
- recent payouts timeline
- next payout queue summary
- searched address view

Business:
- payout trend chart
- hashrate trend chart
- recent rounds table with payout splits

Nerd:
- round history
- full list tables
- peer/diagnostic sections

## Module Visibility Matrix

Always visible:
- search bar
- mode toggle
- current round number
- estimated team hashrate

Lottery only:
- "Am I In?"
- simplified recent payout timeline
- address-focused payout summary

Business only:
- payout cadence summary
- recent share-of-team trend
- local vs team hashrate comparison

Nerd only:
- peer status table
- state IDs
- commitment preview
- test tools
- full diagnostics

All modes, but collapsed differently:
- Winners / On Deck tables
- round history
- hashrate chart

## Data Features Worth Keeping Across Modes
The current dashboard already has several features that should remain, but be reframed:

- Round history
- Estimated team hashrate
- Local DATUM hashrate
- Search by address
- Recent payout splits

These are too useful to discard; the right move is mode-specific presentation.

## UI Simplification Rules

1. Default to compact labels:
- address: last 6 chars
- worker: first 6 chars

2. Prefer human identifiers over hashes:
- block height over block hash
- round number over state ID

3. Move protocol internals behind disclosure:
- `Show Technical Details`

4. Use stronger visual hierarchy:
- searched address should be highlighted everywhere
- active/current round should be obvious
- orphaned rounds should be visually distinct and collapsed by default

## Round Timeline Direction
Keep the existing user preference:
- `OLD LEFT`
- `NEW LEFT`

This should apply in all modes.

## Charts Roadmap

### Keep
- difficulty distribution
- hashrate through time
- payout share through time

### Reframe by Mode
Lottery:
- `Recent Luck`
- `Did your address get paid?`

Business:
- `Payout Share`
- `Team Hashrate`
- `Node Contribution`

Nerd:
- current charts with scale toggles and raw tooltips

## Suggested Implementation Phases

### Phase 1: Mode Framework
- add mode toggle
- add `localStorage` persistence
- create mode-aware section wrappers
- do not redesign content yet

### Phase 2: Lottery Mode
- add `Am I In?`
- simplify tables and round history
- emphasize searched address participation

### Phase 3: Business Mode
- add payout cadence and share trends
- add condensed health/readiness section
- compare local DATUM hashrate vs team estimate

### Phase 4: Nerd Cleanup
- keep all current tooling
- move lowest-value raw details behind disclosure panels

## Nice-to-Have Later
- mobile-first alternate layout for Lottery Mode
- operator-only sections gated by a small `Advanced` toggle
- exportable CSV of recent rounds
- per-address payout performance card
- live notification when searched address enters On Deck or Winners

## Non-Goals
- three separate apps
- separate APIs per mode
- role/permission system for modes
- mode-specific protocol behavior

## Immediate Stash Notes
When this work starts, the first code push should do only the framework:
- mode toggle
- section visibility rules
- no major redesign yet

That keeps the current dashboard intact while creating the skeleton for the simplified public UI.
