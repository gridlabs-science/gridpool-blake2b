# GridPool Project Architecture Map

Status: working target layout for the public beta era.

This repo is the GridPool reference node implementation. It should stay focused
on code that is needed to run a GridPool node, operate the beta network, and
support adapter modules that reuse the same consensus and networking core.

## Repository Responsibilities

### `boot-protocol`

Current repo. Reference implementation and operator tooling.

Owns:

- GridPool consensus implementation
- state bundle import/export
- peer discovery, sync, relay, and node health surfaces
- DATUM-facing server implementation
- HTTP share submission API
- WebUI bundled with a node
- Docker image, sample configs, installers, and service scripts
- operator runbooks and implementation-specific docs
- adapter modules that directly reuse this node process and consensus core

Does not own long-term:

- marketing website content
- simulation notebooks and long-running research data
- canonical protocol spec test vectors once `gridpool-spec` is populated
- large historical soak logs or machine-specific test artifacts

### `gridpool-spec`

Separate protocol/specification repo. Created locally and intended to become
the canonical home for implementation-independent protocol material.

Should own:

- normative consensus rules
- internode protocol specification
- state bundle schema specification
- share proof validation rules
- wire-format test vectors
- cross-implementation compatibility tests
- versioning policy for consensus and peer protocol changes

Migration rule:

- Keep implementation docs here until the spec repo has equivalent coverage.
- Once moved, this repo should link to `gridpool-spec` rather than carrying a
  second divergent copy of the canonical protocol.

### `gridpool-web`

Separate public website repo.

Owns:

- landing page
- public FAQ
- public beta connection guide
- educational diagrams/video embeds
- miner-facing support and compatibility summaries

The website may summarize protocol rules, but should link to `gridpool-spec`
for normative details and this repo for the reference node.

### `gridpool-simulations`

Separate modeling and simulation repo.

Owns:

- Monte Carlo models
- bandwidth/latency models
- pool-hopping and stale-branch attack models
- generated reports, CSVs, and charts
- publishable research summaries

This repo may keep short summaries of current findings when they are needed for
operator decisions, but large simulation outputs should not live here.

## Adapter Policy

Adapters that reuse the existing GridPool node codebase can live in this repo
while the API surface is still moving. Examples:

- DATUM support
- Hydrapool HTTP submission support
- future Stratum V2 gateway module
- future Public Pool / CKPool-style adapter module if it links directly against
  the reference node internals

Adapters should move to separate repos when they become independently useful
software with their own release lifecycle, dependency stack, or upstream
community.

Recommended split rule:

- In-repo adapter: thin integration layer around the reference node.
- Separate adapter repo: full gateway, firmware fork, alternate pool backend, or
  project that can run without the reference node process.

## Documentation Classes

Active docs in `docs/` should be one of:

- launch-gating checklist
- operator runbook
- architecture or protocol explanation
- compatibility guidance
- current research summary
- implementation note required by contributors

Historical notes belong in `docs/archive/`.

Examples:

- one-off soak notes
- old session handoffs
- obsolete V1 plans
- completed investigations whose result has been merged into active docs

Do not delete historical material that may contain useful forensic detail unless
it contains secrets or machine-specific private data. Archive it instead.

## Public Naming

Use `GridPool` in public docs, website text, UI labels, and release notes.

Legacy `Boot` names may remain in:

- repository name during beta
- internal class names
- config keys
- scripts
- API headers
- Docker image names

Do not introduce new public-facing `Boot Protocol` language unless explaining
the old name.

## Near-Term Cleanup Targets

- Move canonical protocol language to `gridpool-spec` once the initial spec repo
  has a README, draft protocol document, and first state/share test vectors.
- Keep this repo's README short enough that new operators see install,
  current-doc links, and safety warnings before historical design discussion.
- Archive stale V1-era planning docs after their useful claims are copied into
  current V2.1 docs or explicitly rejected.
- Keep generated logs, state files, private keys, local deploy scripts, and live
  machine configs out of git.
