# GridPool Hashrate Estimation

## Purpose

GridPool needs a usable estimate of team hashrate even when very few shares are visible.

That is a different problem from a conventional pool:

- A normal pool sees a high volume of low-threshold shares and can estimate hashrate from share counts over time.
- GridPool intentionally only keeps the top `N` shares in the current `On Deck` list.
- That means the estimator has to work from sparse, high-value order statistics rather than a flood of uniform-difficulty share counts.

The current estimator works surprisingly well for this reason:

- it does **not** rely on total share count alone
- it uses the **ranked difficulties** of the best shares seen so far
- it converts each ranked share into an implied hashrate estimate
- it then takes a **median** across those per-rank estimates for robustness

This document explains the current estimator, the mathematics behind it, why it behaves better than a naive "best share only" approach, and what the likely next improvements are.

## Where It Is Implemented

Backend:
- [BootProtocolStateService.cs](boot_portal/Services/BootProtocolStateService.cs#L1452)
- [BootProtocolStateService.cs](boot_portal/Services/BootProtocolStateService.cs#L1535)
- [BootProtocolStateService.cs](boot_portal/Services/BootProtocolStateService.cs#L1733)
- [BootProtocolStateService.cs](boot_portal/Services/BootProtocolStateService.cs#L1876)

Browser mirror for the live current-round estimate:
- [Index.cshtml](boot_portal/Pages/Index.cshtml#L1105)

Sampling config:
- [Program.cs](boot_portal/Program.cs#L137)

## The Current Team Estimator

For the current round:

1. Take the current `On Deck` share set.
2. Extract only positive share difficulties.
3. Sort them descending:

`d_1 >= d_2 >= d_3 >= ... >= d_m`

where:
- `d_1` is the best share difficulty
- `d_k` is the `k`th best share difficulty
- `m` is the number of shares currently on deck

4. Let `t` be the elapsed time since the current round started.
5. For each rank `k`, compute an implied hashrate estimate:

`H_k = k * d_k * 2^32 / t`

6. Convert to TH/s.
7. Take the median of all `H_k`.

That is the current team estimate.

The exact code currently does:

```csharp
double hashesPerSecond = (index + 1) * rankedDifficulties[index] * 4294967296d / elapsedSeconds.Value;
perShareEstimatesThs.Add(hashesPerSecond / 1_000_000_000_000d);
```

then sorts those estimates and returns the median.

## Why `2^32` Appears

Bitcoin difficulty is conventionally normalized so that:

- a difficulty-1 share is expected once every `2^32` hashes

So if a share has difficulty `d`, then the expected number of hashes needed is approximately:

`E[hashes to hit difficulty d] = d * 2^32`

That means a machine with hashrate `H` hashes/sec should produce difficulty-`d` shares at an average rate:

`lambda(d) = H / (d * 2^32)`

Over a window of `t` seconds:

`E[count of shares with difficulty >= d] = H * t / (d * 2^32)`

This is the key relationship the estimator exploits.

## Poisson View

At a fixed threshold difficulty `d`, the number of shares with difficulty at least `d` over time `t` is well modeled as a Poisson random variable:

`N_d ~ Poisson(H * t / (d * 2^32))`

This is the same rare-event logic used by ordinary pool share counting, except here the threshold is not fixed at one pool share difficulty. Instead, the threshold is inferred from the ranked top shares.

If the current `k`th best share has difficulty `d_k`, then by definition there are about `k` shares at or above that threshold.

So a natural back-of-the-envelope estimator is:

`k ~= H * t / (d_k * 2^32)`

which rearranges to:

`H ~= k * d_k * 2^32 / t`

That is exactly the current estimator at each rank.

## Why This Works Better Than "Best Share Only"

A best-share-only estimator would use:

`H ~= d_1 * 2^32 / t`

That is extremely noisy.

The reason is statistical:

- The best share is an extreme-value statistic.
- Extreme values are luck-dominated.
- In Poisson/order-statistic language, the top share corresponds to the first arrival of a Poisson process in transformed space.

If we define:

`z_k = H * t / (d_k * 2^32)`

then under the idealized model:

`z_k ~ Gamma(shape = k, rate = 1)`

Equivalently:
- `z_1` is exponential
- `z_2` is Erlang/Gamma with shape 2
- larger ranks get progressively tighter

This matters because:

- `k = 1` is wildly unstable
- larger `k` values are much less luck-sensitive
- the middle ranks are usually the most informative in a finite sample

So the GridPool estimator gets robustness by:

- building one implied estimate from every rank
- then taking the median instead of trusting the best share

That is why it can work reasonably well using only dozens of visible shares instead of needing a huge stream of low-difficulty pool shares.

## Why Median Instead Of Mean

The per-rank estimates:

`H_1, H_2, ..., H_m`

are not symmetrically distributed.

The top ranks are especially noisy and can overshoot badly during lucky rounds. A mean is too sensitive to that tail behavior. A median is much more robust:

- one absurdly lucky top share does not dominate the estimate
- the lower half of the list still contributes information
- the estimate remains stable enough for live UI use

This was an explicit design goal: make the estimator informative during sparse-data rounds without needing heavy smoothing.

## Relation To Conventional Pool Estimation

Standard pool estimation usually looks like:

`H_hat = N * d_min * 2^32 / t`

where:
- `N` is the count of shares above a fixed minimum difficulty
- `d_min` is that minimum share difficulty

This works well when `N` is large. By the central limit theorem, large Poisson counts begin to look approximately normal, and relative error scales like:

`1 / sqrt(N)`

The problem is that GridPool does not expose a large fixed-threshold share count to the public UI. It only exposes the top of the share distribution.

So GridPool is effectively using a different sufficient statistic:

- not "how many fixed-difficulty shares arrived?"
- but "how far out into the tail did the top `k` shares reach?"

That is why Poisson/order-statistic reasoning is more natural here than a plain normal approximation.

## Current Live UI Behavior

There are now three related but distinct hashrate displays:

### 1. Current Team Estimate

This uses only the current `On Deck` list and the time since the round started.

Backend:
- [BootProtocolStateService.cs](boot_portal/Services/BootProtocolStateService.cs#L1452)

Browser mirror:
- [Index.cshtml](boot_portal/Pages/Index.cshtml#L1105)

The browser recomputes this every second from the current visible `On Deck` difficulties so the estimate decays and updates smoothly during the round.

### 2. Completed Round Estimate

For archived rounds, the same rank-adjusted median estimator is run on the locked winning shares of that round and the round duration.

Implementation:
- [BootProtocolStateService.cs](boot_portal/Services/BootProtocolStateService.cs#L1535)

### 3. Local DATUM Estimate

This uses accepted DATUM shares over a rolling local window, not just On Deck winners.

Implementation:
- [BootProtocolStateService.cs](boot_portal/Services/BootProtocolStateService.cs#L1749)

Defaults:
- sample interval: `60s`
- local window: `1800s`

from:
- [Program.cs](boot_portal/Program.cs#L137)

This local estimate usually has more data and is therefore useful for comparing:

- `my node's directly connected DATUM flow`
- versus
- `the team-wide estimate inferred from the On Deck distribution`

## Known Behavior And Caveats

### 1. Short rounds are noisy

If a round rotates quickly and only a few shares make it onto On Deck, the estimate is much less stable.

This is expected.

The estimator is strongest when:
- the round has had enough time to accumulate a meaningful top-share distribution
- the On Deck list is moderately filled

### 2. Top-share luck still matters

The median helps a lot, but the entire estimator is still built from order statistics, so luck remains visible.

This is unavoidable; the estimator is not measuring raw work directly.

### 3. The current per-rank factor likely has some bias

The current formula uses:

`k * d_k`

for rank `k`.

Under the Gamma/order-statistic framing, this is intuitive, but it is not obviously the lowest-bias choice. In fact:

- for the `k`th transformed arrival, the mode is around `k - 1`
- the mean-based correction and the mode-based correction are not the same
- the top rank is especially pathological

So the current estimator should be thought of as:

- practical
- robust
- empirically good
- but not mathematically final

### 4. It is a team estimate, not a fairness proof

This estimator is useful for:
- trend detection
- sanity checks
- comparing team growth over time

It is not by itself a formal proof that a payout split is fair.

## Why The Current Method Feels Good In Practice

Empirically, this method has been promising because it uses the **shape** of the top-share distribution rather than just one extreme point.

That gives it three useful properties:

1. It works with sparse data.
2. It updates naturally as the On Deck list grows.
3. It stays informative even in a decentralized setting where nodes may not want to expose all low-difficulty share traffic.

This makes it unusually well suited to GridPool specifically.

## Planned Next Improvements

These are the most promising next estimator variants to try.

### 1. Bias-corrected rank factors

Current:

`H_k = k * d_k * 2^32 / t`

Candidates to test:

- `(k - 1) * d_k * 2^32 / t`
- `(k - 1/2) * d_k * 2^32 / t`
- quantile-based corrections derived from Gamma medians

Reason:
- the current `k` factor is simple but probably slightly biased
- a bias-corrected factor may improve absolute calibration

### 2. Trimmed median

Current:
- median over all ranks

Candidate:
- drop the top few and bottom few ranks, then take the median

Reason:
- top ranks are very luck-sensitive
- bottom ranks can be unstable when the list is not yet full or is dominated by replacement dynamics

### 3. Weighted median or weighted fit

Candidate:
- weight middle ranks more heavily
- downweight extremes

Reason:
- not all ranks carry the same variance

### 4. Full likelihood fit

Instead of reducing each rank to a standalone estimate, fit hashrate directly from the joint likelihood of the observed order statistics.

This is the mathematically clean version.

Potential forms:
- Gamma/order-statistic likelihood
- censored Poisson process likelihood for the top `N` shares

Reason:
- use all information coherently
- get a point estimate plus confidence interval

### 5. Confidence bands / uncertainty display

Current UI shows only a point estimate.

Future improvement:
- a range or confidence band
- for example `4.8 TH/s +/- 1.1 TH/s`

Reason:
- short rounds can otherwise look deceptively precise

### 6. Per-address estimate line

Planned UI feature:
- show an estimated hashrate line for the searched address

Likely implementation:
- bucket accepted winning-share telemetry by address over time
- apply the same rank-based estimator to that address-specific subset when enough data exists

### 7. Smoothing / decay refinements

Current live estimate updates each second in-browser from the current On Deck list.

Future options:
- exponential smoothing
- change-point aware smoothing around rotations
- blending current-round estimate with recent sampled history

### 8. Revisit if round-reset semantics change

If GridPool changes the way new rounds begin, such as carrying forward prior On Deck state in some form, the estimator should be re-evaluated. A carry-forward rule changes the statistical meaning of early-round order statistics.

## Suggested Experiment Plan

When revisiting the estimator, test at least these variants side by side:

1. Current median rank-adjusted estimator
2. Bias-corrected `(k - 1)` version
3. `(k - 1/2)` version
4. Trimmed-median version
5. Full-likelihood fit if practical

Evaluate them against:
- known test hashrate
- stability during short rounds
- stability during long rounds
- response to sudden hashrate changes
- sensitivity to one extremely lucky top share

## Bottom Line

The current GridPool hashrate estimator works because it turns a sparse top-share list into many implicit Poisson threshold observations:

- the `k`th best share tells you what hashrate would be needed to expect about `k` shares at that threshold in time `t`
- repeating that over many ranks gives many noisy estimates
- taking the median makes the result robust

It is not mathematically final, but it is a strong and unusually useful estimator for a decentralized system that intentionally does **not** expose a huge stream of fixed-threshold share counts.
