GridPool: Decentralized Reward Sharing for Sovereign Bitcoin Mining
  https://github.com/gridlabs-science/boot-protocol/tree/main

  GridPool is an open-source decentralized reward-sharing protocol for sovereign Bitcoin miners using DATUM and, pending upstream acceptance, Hydrapool. Its purpose is to reduce the payout variance of solo-style mining without requiring centralized pool custody or centralized block construction.

  Conceptually, GridPool sits between pure solo mining and traditional pooled mining. Each miner continues constructing and hashing their own block templates. Instead of sending all rewards to a pool wallet and distributing them later, miners cooperatively build templates that pay the current GridPool payout snapshot, which is derived from a bounded unpaid Work Set of high-difficulty contributors. Slot 0 remains reserved for the block finder’s own address and receives transaction fees, preserving a direct incentive to publish valid blocks immediately. The result is a much simpler decentralized reward-sharing design than historical P2Pool-style systems, with attractive
  tradeoffs in operational simplicity, privacy, and resistance to block withholding.

  This project aligns closely with The 256 Foundation’s mission because it expands the practical reach of sovereign mining. Small operators and businesses often cannot tolerate the payout variance of pure solo mining, even when they prefer it philosophically. GridPool is designed to reduce that variance by up to 300x at full configuration while preserving miner control over block templates. That makes sovereign mining more accessible to smaller operators who need regular cash flow for power bills and equipment economics, and it reduces dependence on centralized pool payout infrastructure.

  The core prototype already exists and is running today on two test nodes.  You can see the user interface (and connect your DATUM client) to one of them at https://gridpool.net/. The codebase includes DATUM integration, peer-to-peer share relay between GridPool nodes, state synchronization, WebUI and operator tooling, Docker packaging, basic abuse controls, and active multi-node testing. In short, this is no longer just a concept. The remaining work is launch hardening: broader testing, operational cleanup, documentation, seed-node deployment, privacy improvements, and final interoperability work around Hydrapool and production launch support.

  Project Scope and Deliverables
  The proposed funded scope is to take GridPool from an advanced prototype to a stable public launch suitable for real sovereign miners.

  Primary deliverables:

  - Public beta release of GridPool protocol for DATUM-based sovereign miners
  - Finalized decentralized peer bootstrap and state sync behavior
  - Hardened installation paths via Docker and standard Linux service deployment
  - Operator documentation, install guide, and troubleshooting guide
  - Public network status page and launch documentation
  - Deployment and operation of several globally distributed public seed nodes
  - Launch support for the Foundation’s node so it can participate in the public beta network

  Secondary deliverables, if time and budget permit:

  - Hydrapool integration support once upstream review is complete
  - Start9 and Umbrel packaging
  - Additional interoperability targets such as Stratum V2 or other pool software
  - Longer-term performance rewrite work in Rust or C

  The launch model has been revised to avoid a special high-fee genesis block. In protocol V2, every ordinary Bitcoin block creates a fresh payout snapshot from the current unpaid Work Set, while only a valid GridPool block pays the active snapshot. This lets early miners earn their way into the first paid snapshot through submitted work rather than relying on a hardcoded donation-only genesis Winners List. By default, one canonical 1/300 support slot may be included for Grid Labs development support, but this is far smaller than the earlier genesis-donation model and can be disabled by node operators.

  Timeline
  Target launch would ideally align with the next Telehash, subject to software stability.

  A realistic execution plan is:

  1. Launch hardening and bug fixing
  2. Expanded multi-node testing and operational validation
  3. Documentation, packaging, and seed-node deployment
  4. Public beta launch and monitoring

  Given current progress, this should be achievable on a part-time basis over roughly 8-12 weeks, assuming focused support and light operational funding.

  Necessary Materials

  - Several cloud VPS instances for globally distributed seed nodes
  - Domain or subdomain for public network status and bootstrap endpoints
  - Development tooling and AI-assisted coding support
  - Test infrastructure for multiple active GridPool and DATUM nodes
  - Modest launch communications support


  Both contributors have full-time jobs and families, so the project is being executed part-time. The value proposition of funding is not that the project is impossible otherwise, but that support would significantly accelerate stabilization, deployment, and launch.

  Budget and Funding Structure
  Estimated total project budget: approximately $27,000-$30,000, including:

  - Developer time
  - VPS and seed-node infrastructure
  - Development tooling and launch support
  - Optional public status hosting and operational costs

  However, I do not want upfront cash requirements to be a blocker if the Foundation currently has limited liquidity. I am open to a deferred, success-based structure.

  A practical structure could be:

  - I front modest hard costs such as VPS and tooling if necessary
  - The Foundation incurs no immediate labor obligation
  - Compensation is deferred until launch milestones are achieved, ideally tied to the first GridPool-winning mainnet block and/or subsequent Foundation fundraising capacity
  - Hard costs advanced personally can be reimbursed later as a priority once funds are available

  I would also welcome discussion of a deferred recognition payment for prior development work. Over the past year I have already contributed several hundred hours of open-source design and implementation because I believe this project matters for the Bitcoin network. I understand that past work may not always be fundable, but if the Foundation sees value in the progress already made, I would be open to structuring part of the eventual compensation as recognition of that prior in-kind contribution.

  Risk and Mitigation
  This is not a zero-engineering-risk project, but the core protocol risk is already substantially reduced because a working prototype exists and is running. The remaining risks are primarily around stability, testing depth, interoperability, and launch operations rather than basic feasibility.

  That is exactly the kind of gap this proposal is intended to close: take a real, functional protocol and finish the last mile required to put it into public use.

  Close
  GridPool offers a credible path to making sovereign mining more practical for smaller operators without re-centralizing block construction or reward custody. With modest support from The 256 Foundation, I believe it can make Hydrapool more effective and more flexible, and be launched as a meaningful piece of open Bitcoin infrastructure. 


Also generated some attachments here:
https://github.com/gridlabs-science/boot-protocol/blob/main/docs/256-foundation-architecture.md
https://github.com/gridlabs-science/boot-protocol/blob/main/docs/256-foundation-status.md
  
