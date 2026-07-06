# FINAL INSTRUCTIONS

META
DOCUMENT_ID: x2g-ai-os-final-instructions
DERIVED_FROM: ./docs/x2g-ai-os-rsch2.txt (Step 1 Research Specification)
TARGET_ENGINE: Fable 5 (F# to JS/ES6 compilation)
TARGET_RUNTIME: Headless Node.js Execution Daemon
HOST_RIG: Pop!_OS workstation, NVIDIA RTX 5070 Ti, 32GB RAM
PARITY_TARGET: Pop!_OS (primary) and Windows 11 (secondary)
CONFIG_LAW: All state machines, pipelines, routing rules, and configs are JSON. YAML and markdown frontmatter are FORBIDDEN for any machine-parsed state.
PARSE_MODE: Read this file top to bottom. Each numbered directive is atomic and idempotent. Execute in order.

================================================================================
SECTION 1 - CORE SYSTEM TOPOLOGY AND DUAL-PLANE RUNTIME
================================================================================

1.1 RUNTIME PLANES
The system runs two planes. Plane A is the PRIMARY operational deck: a containerized Angular + DevExtreme web dashboard (the Command Center). Plane B is the SECONDARY reactive memory layer: the existing Obsidian multi-vault environment, read and written through asynchronous file-system watchers. Plane A is authoritative for control and execution. Plane B is authoritative for long-term semantic memory and human-editable notes. The Fable daemon is the single writer that reconciles both planes.

1.2 CONTAINERIZED ANGULAR FRONTEND
Directory: X2G_Ventures/x2gAI-OS/frontend
Framework: Angular (standalone components, strict TypeScript, signals-based reactivity).
UI Library: DevExtreme Angular (@devextreme/devextreme-angular).
Build inside Docker via multi-stage Dockerfile. Stage 1 builds the Angular SPA to static assets. Stage 2 runs the Node.js Fable daemon and serves the SPA plus a WebSocket telemetry channel.
The frontend NEVER polls. It opens one WebSocket to the daemon for zero-polling telemetry, log streaming, and live file-tree discovery events.

1.3 DEVEXTREME UI SURFACE
Implement these DevExtreme controls on the Command Center:
- dxDataGrid: high-performance virtual-scroll grids for tickets, model runs, audit rows, and token-ledger entries. Enable remoteOperations, virtual scrolling, and column chooser.
- dxTabPanel / dxMultiView: multi-tab layout so each active domain (X-TMS, TrialLens, Digital Pathology, ML Projects, Web Applications) loads in its own tab without a full reload.
- dxTreeView: left side-navigation menu matrix, populated dynamically from the discovered file tree (Section 2). Each discovered module appends one node.
- dxChart / dxPieChart: analytical charts fed by ML run statistics and telemetry.
- dxToolbar + custom terminal panel: the Command Center terminal component for direct text syscalls, routine injections, and manual overrides.

1.4 SIDE-NAVIGATION MENU MATRIX
The dxTreeView menu is generated, never hardcoded. Its data source is a JSON tree produced by the daemon discovery pass (Section 2). Menu node schema:
{ "id": string, "label": string, "modulePath": string, "moduleType": "domain|webapp|system", "route": string, "badgeCount": number }
When the daemon discovers or scaffolds a module it emits a menu-delta WebSocket event; the frontend patches the tree in place.

1.5 OBSIDIAN MULTI-VAULT INTEGRATION BOUNDARIES
The daemon treats each Obsidian vault as a read/write cache, monitored with Chokidar (awaitWriteFinish enabled to avoid partial-write reads). Boundaries:
- The daemon may WRITE only to files under a vault subfolder named _x2g/ inside each vault. Human-authored notes outside _x2g/ are READ-ONLY to the daemon.
- All machine state written into vaults is JSON files (for example _x2g/state.json, _x2g/ledger_mirror.json). The daemon MUST NOT write YAML frontmatter or parse frontmatter for state.
- Link graphs and markdown histories outside _x2g/ must never be rewritten by the daemon.
- Debounce every Chokidar event by 250ms and hash file contents before acting to suppress echo writes.

1.6 STRICT JSON CONFIGURATION LAW
Every state machine, pipeline definition, routing rule, agent profile, skill definition, and system setting is a JSON document validated against a JSON Schema stored in X2G_infrastructure/schemas/. No YAML. No markdown frontmatter for machine state. The Step 1 sample YAML agent profiles are hereby MIGRATED to JSON. Canonical agent profile JSON:
{ "id": "arch-01", "name": "Systems Architect", "model_provider": "ollama", "model_target": "llama3:8b-instruct-q8_0", "temperature": 0.2, "max_tokens_per_call": 4096, "budget_cap_monthly": 0.0, "current_spend_monthly": 0.0, "status": "idle", "assigned_project": "X-TMS", "skills": ["read_file", "write_file", "execute_command"], "system_prompt": "You are a Senior Systems Architect..." }
Validate on load; reject and log any config that fails schema validation. Fail loudly.

================================================================================
SECTION 2 - DYNAMIC SYSTEM DISCOVERY AND SCAFFOLDING
================================================================================

2.1 RECURSIVE TREE SCAN ALGORITHM
On daemon boot and on a heartbeat interval, run discoverTree() over these roots: ~/.claude, X2G_infrastructure, X2G_Ventures. Algorithm:
STEP A. Resolve each root with Node path module so Pop!_OS forward-slash and Windows backslash paths normalize identically.
STEP B. Walk each root recursively, breadth-first. Skip node_modules, .git, dist, and any directory listed in X2G_infrastructure/config/scan_ignore.json.
STEP C. For every directory, test for a context file named .claude.md or .claude (Section 2.2).
STEP D. For every directory containing a *.json manifest (project.json, configuration.json, processing_jobs.json, models_registry.json, protocol_rules.json), register it as a discovered module.
STEP E. Emit a normalized module descriptor to the in-memory registry and to the WebSocket menu-delta channel.
STEP F. Persist the full snapshot to X2G_infrastructure/config/discovered_tree.json (overwrite atomically via write-temp-then-rename).

2.2 LOCALIZED RULE DISCOVERY (.claude.md PARSING)
Inside every subdirectory the daemon looks for .claude.md. These files are human-authored rule documents, NOT machine state, so parse them as plain text and extract only a bounded rules block:
STEP A. Read the file as UTF-8 text.
STEP B. Locate a fenced block delimited by ```x2g-rules ... ```. If present, parse its contents as JSON and treat it as the module's authoritative machine rules.
STEP C. If no fenced x2g-rules block exists, treat the whole file as descriptive scope context only (advisory, never executed).
STEP D. Merge discovered rules into the module descriptor under a "localRules" key. Local rules may tighten but NEVER loosen global guardrails (token ceilings, determinism, audit).

2.3 MODULE DESCRIPTOR SCHEMA
{ "id": string, "moduleType": "domain|webapp|system", "absPath": string, "contextFile": string|null, "manifests": string[], "localRules": object|null, "discoveredAt": ISO8601 }

2.4 DYNAMIC SCAFFOLDING FROM THE COMMAND CENTER
The DevExtreme Command Center exposes a scaffold form. On submit it sends a scaffold-request over WebSocket. The daemon executes scaffoldModule(request) deterministically:
STEP A. Validate the request against X2G_infrastructure/schemas/scaffold_request.schema.json.
STEP B. Resolve target directory under the requested parent (default X2G_Ventures/Web_Applications for webapp type).
STEP C. Refuse if the directory already exists (no overwrite; fail loudly).
STEP D. Create the directory tree from the template matching request.templateType.
STEP E. Write the JSON manifest (project.json) populated from template defaults + request overrides.
STEP F. Write a fresh localized .claude.md containing a ```x2g-rules``` JSON block seeded with default scope and inherited global guardrails.
STEP G. Append a new node to the side-navigation menu matrix via menu-delta event.
STEP H. Append a scaffold audit record to X2G_infrastructure/logs/audit_trail.json (append-only).

2.5 STRUCTURAL TEMPLATE LOGIC
Templates live in X2G_infrastructure/templates/<templateType>/ as a manifest.json plus a files array describing relative paths and content stubs. Supported templateType values at minimum: "web-app", "micro-app", "domain-pipeline", "localized-config". A template is applied by copying its files array, substituting ${moduleId}, ${moduleLabel}, ${createdAt} tokens. Templates are data, not code, so new module types are added by dropping a new template folder with a JSON manifest, no daemon recompilation required.

================================================================================
SECTION 3 - MULTI-PROJECT ALIGNMENT MANDATES (DEDICATED PIPELINES)
================================================================================
Each pipeline is a JSON-declared deterministic state machine under X2G_infrastructure/config/pipelines/<name>.pipeline.json, executed by the Fable state engine (Section 4). Each pipeline declares: states[], transitions[], entryState, terminalStates[], and per-state skill bindings.

3.1 X-TMS LOGISTICS PIPELINE
Root: X2G_Ventures/X-TMS
Function: process inbound freight alerts, parse rate confirmation (ratecon) sheets, orchestrate Twilio 10DLC SMS notifications.
States: INGEST_ALERT -> PARSE_RATECON -> VALIDATE_LOAD -> DISPATCH_SMS -> ARCHIVE.
Directives:
- INGEST_ALERT: watch X-TMS/inbound/ via Chokidar; each new alert file becomes a JSON ticket in X-TMS/tickets/ (schema ticket.schema.json, e.g. XTMS-101).
- PARSE_RATECON: extract origin, destination, rate, weight, pickup/delivery windows into structured JSON. Prefer local Ollama vision/text parse; escalate to cloud only per Section 4 fallback rules.
- DISPATCH_SMS: send only via approved Twilio 10DLC A2P routes defined in X-TMS/configuration.json. Wrap the Twilio REST call in a type-safe F# wrapper. No SMS may send unless the load passed VALIDATE_LOAD.
- Every dispatch writes an append-only audit record.

3.2 TRIALLENS CLINICAL RESEARCH PIPELINE
Root: X2G_Ventures/TrialLens
Function: manage complex medical informatics datasets with alphanumeric-safe medical records parsing parameters.
States: INTAKE_DATASET -> NORMALIZE_RECORDS -> MAP_PROTOCOL -> INDEX_MEMORY -> REPORT.
Directives:
- Enforce alphanumeric-safe parsing: strip or escape any non [A-Za-z0-9._-] token in record identifiers before processing; never emit raw identifiers into logs.
- Mapping vectors and parse patterns come from TrialLens/protocol_rules.json.
- Per the global research guardrail, deep literature and long clinical documents are routed through the notebooklm skill for indexing; the pipeline queries high-density answers rather than ingesting full texts into model context.
- INDEX_MEMORY writes structured summaries only into the Obsidian vault _x2g/ subfolder.

3.3 DIGITAL PATHOLOGY PIPELINE
Root: X2G_Ventures/Digital_Pathology
Function: manage pipeline transitions for whole slide image (WSI) metadata analysis and processing jobs.
States: REGISTER_WSI -> EXTRACT_METADATA -> QUEUE_ANALYSIS -> RUN_JOB -> PUBLISH_RESULT.
Directives:
- Operate on metadata only in the daemon; large WSI binaries are referenced by path, never loaded into model context.
- Job configs come from Digital_Pathology/processing_jobs.json.
- Each state transition is deterministic and recorded; a job cannot re-enter RUN_JOB without an explicit transition event.

3.4 ML PROJECTS PIPELINE
Root: X2G_Ventures/ML_Projects
Function: register, monitor, and output local model execution statistics to frontend analytical charts.
States: REGISTER_RUN -> MONITOR -> COLLECT_METRICS -> EMIT_CHARTS.
Directives:
- Runs are registered in ML_Projects/models_registry.json (path, hyperparameters, checkpoint refs, evaluations).
- MONITOR streams live metrics over WebSocket to DevExtreme dxChart/dxDataGrid.
- COLLECT_METRICS aggregates loss/accuracy/latency per checkpoint; EMIT_CHARTS pushes series data to the Command Center analytics tab.

3.5 WEB APPLICATION SCAFFOLD-ON-DEMAND (supporting pipeline)
Root: X2G_Ventures/Web_Applications
Function: monitor existing web codebases and generate new ones via the Section 2.4 scaffolding flow. Each generated app gets its own project.json, localized .claude.md, and side-nav node.

================================================================================
SECTION 4 - PAPERCLIP-STYLE GOVERNANCE AND RISK INTERCEPTORS
================================================================================

4.1 DETERMINISTIC AGENT TRANSITIONS (F# DISCRIMINATED UNIONS + RECORD TYPES)
All agent state lives in F# Discriminated Unions and Record Types compiled by Fable. Dynamic loops, self-modifying routes, and loose prompt-driven routing are FORBIDDEN. Canonical domain model in X2G_Ventures/x2gAI-OS/src/Domain.fs:

type AgentStatus =
    | Idle
    | Running
    | Suspended of reason: string
    | Aborted of reason: string
    | Completed

type ModelProvider =
    | OllamaLocal
    | AnthropicClaude
    | OpenAI
    | Gemini

type PipelineState =
    | Ingest
    | Parse
    | Validate
    | Dispatch
    | Archive
    | Terminal

type Transition =
    { From: PipelineState
      To: PipelineState
      Guard: string }

type TokenLedgerEntry =
    { Timestamp: string
      AgentId: string
      Provider: ModelProvider
      PromptTokens: int
      CompletionTokens: int
      CostUsd: decimal }

type AgentContext =
    { Id: string
      Status: AgentStatus
      Provider: ModelProvider
      Project: string
      MonthlyBudgetUsd: decimal
      SpendMonthUsd: decimal }

Transitions are only legal if a matching Transition record exists in the pipeline's declared transition set. Any attempt to move to a state with no declared Transition returns Aborted and writes an audit record. The compiler-checked DU makes illegal states unrepresentable.

4.2 INLINE INTERCEPTOR PIPELINE
Every inference call passes through interceptAndExecute(request) before hitting any provider. Ordered steps:
STEP A. AUDIT PRE: append the intended call (agent, provider, estimated tokens) to X2G_infrastructure/logs/audit_trail.json (append-only, never mutated).
STEP B. LEDGER READ: read X2G_infrastructure/config/token_ledger.json, an append-only ledger. Aggregate current-period spend for the agent and the global budget.
STEP C. BUDGET EVALUATION: compute projected cost = estimatedTokens * providerRate. If provider is a paid cloud endpoint AND (agent.SpendMonthUsd + projectedCost > agent.MonthlyBudgetUsd OR global cap exceeded), the paid parameters are VIOLATED.
STEP D. FALLBACK ON VIOLATION: roll back the transaction, set AgentStatus to Suspended, and re-route the call to the local Ollama endpoint at http://localhost:11434 on the Pop!_OS RTX 5070 Ti host. Transfer context flags to the local model. Never silently proceed on a paid endpoint after a violation.
STEP E. EXECUTE: perform the call against the resolved provider.
STEP F. LEDGER APPEND: append a TokenLedgerEntry with actual prompt/completion tokens and computed cost. Append-only; no edits or deletes.
STEP G. AUDIT POST: append the response envelope (tokens, provider, status) to the audit trail.

4.3 LOCAL-FIRST EXECUTION AND FALLBACK POLICY
Default provider for every agent is OllamaLocal (quantized weights: llama3, mistral, phi3) via http://localhost:11434. Escalate to a paid cloud provider ONLY when: required context length exceeds local VRAM capacity, OR verified local latency bottleneck, AND the budget check in 4.2 passes. If a paid call would breach budget, the system falls back DOWN to Ollama, never up to paid. This is the inverse-safe default: cheap and local unless explicitly and affordably justified.

4.4 IMMUTABLE AUDIT AND HARD CEILINGS
- token_ledger.json and audit_trail.json are append-only. The daemon opens them in append mode; any code path that attempts truncation or in-place edit is a hard fault and must halt execution.
- Per-request and cumulative daily/monthly caps are read from X2G_infrastructure/config/global_routing.json. Breach triggers instant rollback, a failure flag, and reassignment to local Ollama context.
- Fail loudly: exit code 0 with empty or anomalous payloads must halt, intercept, and log state rather than continue.

================================================================================
SECTION 5 - CANONICAL DIRECTORY AND FILE TARGETS
================================================================================
~/.claude/config.json                                  global endpoint + defaults
~/.claude/active_session.json                          live PID/agent state
X2G_infrastructure/docker/docker-compose.yml           Angular + Node daemon orchestration
X2G_infrastructure/docker/Dockerfile                   multi-stage Angular + Node build
X2G_infrastructure/config/global_routing.json          global state paths + budget caps
X2G_infrastructure/config/token_ledger.json            append-only budget ledger
X2G_infrastructure/config/discovered_tree.json         latest discovery snapshot
X2G_infrastructure/config/scan_ignore.json             directories excluded from scan
X2G_infrastructure/config/pipelines/*.pipeline.json    per-domain state machines
X2G_infrastructure/schemas/*.schema.json               JSON Schemas for all configs
X2G_infrastructure/templates/<type>/manifest.json      scaffold templates
X2G_infrastructure/logs/audit_trail.json               immutable audit history
X2G_Ventures/x2gAI-OS/frontend/                         Angular + DevExtreme SPA
X2G_Ventures/x2gAI-OS/src/Domain.fs                     F# DUs, records, JSON maps
X2G_Ventures/x2gAI-OS/src/Daemon.fs                     Chokidar watchers, tree walkers, WS routers
X2G_Ventures/x2gAI-OS/src/App.fsproj                    Fable project manifest
X2G_Ventures/x2gAI-OS/package.json                      Node deps (chokidar), build scripts

================================================================================
SECTION 6 - BUILD AND RUN COMMANDS (EXPLICIT, DO NOT GUESS SYNTAX)
================================================================================
Fable build (F# to JS):       dotnet fable X2G_Ventures/x2gAI-OS/src/App.fsproj --outDir X2G_Ventures/x2gAI-OS/build
Frontend install:             if pnpm-lock.yaml exists run pnpm install, else npm ci
Frontend dev:                 pnpm start (else npm run dev)
Frontend lint:                pnpm lint (else npm run lint)
Frontend test:                pnpm test (else npm test)
Container cluster:            docker compose up --build
Daemon start (headless):      node X2G_Ventures/x2gAI-OS/build/Daemon.js

================================================================================
SECTION 7 - ACCEPTANCE CRITERIA (DAEMON MUST SATISFY BEFORE MARKED READY)
================================================================================
AC1. Boot performs discoverTree() over all three roots and writes discovered_tree.json.
AC2. Side-navigation menu matrix renders from discovered modules with zero hardcoded nodes.
AC3. A scaffold request from the Command Center creates a directory, project.json, localized .claude.md with an x2g-rules JSON block, a menu node, and an audit record.
AC4. All four domain pipelines load from their *.pipeline.json and reject undeclared transitions.
AC5. Every inference call appends exactly one pre-audit, one ledger entry, and one post-audit record; none mutate prior rows.
AC6. A simulated budget breach on a paid provider rolls back and re-routes to http://localhost:11434, with AgentStatus = Suspended.
AC7. No YAML or markdown frontmatter is parsed for any machine state anywhere in the runtime.
AC8. Chokidar writes to Obsidian vaults occur only under _x2g/ and never rewrite human notes.

END OF FINAL INSTRUCTIONS
