# x2gAI-OS_research

## Project Goal: To build a Local website for my agentic OS

## Description
The **x2gAI-OS** is a local, deterministic agentic operating system designed to orchestrate, execute, and monitor AI-driven workflows across multiple projects. Built using **Fable 5** targeting a **Node.js** execution daemon, this platform treats an **Obsidian Vault** as its primary user interface, state machine, and long-term memory configuration ("2nd Brain"). It runs cross-platform with full parity between **Linux** and **Windows**, ensuring high-performance local inference while providing drop-in compatibility with commercial cloud APIs.

---

## Architectural Breakdown (Fable 5 Engine)

To achieve strict multi-platform support without the heavy overhead of complex GUI development, the architecture splits into a clean **Model-View-Controller** structure powered by the local filesystem:


+----------------------------------------+
           |          OBSIDIAN VAULT (UI)           |
           |  - Markdown Frontmatter (.md configs)   |
           |  - Canvas Dashboard (Visual Layout)    |
           +----------------------------------------+
                                ▲
                                │ File Watcher (Chokidar via Fable)
                                ▼
           +----------------------------------------+
           |         FABLE 5 / NODE.JS DAEMON       |
           |  - F# Type-Safe State Machines         |
           |  - Token / Cost Governance Interceptor  |
           +----------------------------------------+
                                │
              ┌─────────────────┴─────────────────┐
              ▼                                   ▼
  +------------------------+          +------------------------+
  |      LOCAL INFERENCE   |          |       PAID CLOUD       |
  | - Ollama (Local REST)  |          | - Anthropic / OpenAI   |
  | - llama3, mistral, etc |          | - Gemini API           |
  +------------------------+          +------------------------+

  ### 1. Cross-Platform Runtime Strategy
*   **Engine:** Fable 5 compiling F# source down to clean JavaScript/ES6 running on **Node.js**. 
*   **OS Portability:** Native Node.js standard modules (`path`, `fs`) manage differences between Windows backslashes (`\`) and Linux forward slashes (`/`).
*   **File-System Watcher:** Uses Fable bindings for `chokidar` to capture immediate modifications to files within your Obsidian vault across both operating systems without high CPU polling spikes.

### 2. State & Token Governance (Paperclip-style Architecture)
*   **Type-Safe Routing:** Modeled entirely via F# Discriminated Unions to prevent invalid states during execution.
*   **The Interceptor Pipeline:** Every inference call passes through a strict budget controller. It reads a centralized ledger, aggregates prompt/completion tokens, computes cost weights, and instantly suspends an agent loop if spending thresholds are breached.

---

## Key Functional Modules

### 1. Skills Directory (`/Skills`)
*   **Definition:** Each skill is a functional capability exposed to the OS daemon (e.g., executing shell scripts, searching local files, hitting a custom endpoint).
*   **Obsidian Integration:** Managed as individual `.md` files detailing descriptions and schema definitions within the frontmatter.

### 2. Project Run-Down & Status (`/Projects`)
*   **Definition:** Central tracking system mapping out operational health, milestones, and active logs for your production applications.
*   **Obsidian Integration:** Built using markdown-native task tracking or Kanban plugins. The Fable daemon reads task states (`status: Todo`, `status: In-Progress`, `status: Done`) and triggers corresponding backend cycles.

### 3. Unified Dashboards & Obsidian Vaults
*   **Definition:** The operational control deck aggregating execution pipelines, performance charts, and audit histories.
*   **Obsidian Integration:** Driven entirely by Obsidian Canvas JSON files and frontmatter properties. The background daemon updates active properties inside your daily notes or central canvas cards in real time.

### 4. News Run-Down
*   **Definition:** A deterministic pipeline module that handles batch aggregation, thematic cleaning, and semantic filtering of incoming developer news or system events.
*   **Obsidian Integration:** Commits structured summaries directly to an internal note (`Daily_Run_Down.md`) every morning using predefined local model pipelines.

---

## Sample UI Options & Frontmatter Schemas

### Agent Profile Configuration File (`/Agents/Architect-Agent.md`)
```yaml
---
id: "arch-01"
name: "Systems Architect"
model_provider: "ollama"
model_target: "llama3:8b-instruct-q8_0"
temperature: 0.2
max_tokens_per_call: 4096
budget_cap_monthly: 0.00
current_spend_monthly: 0.00
status: "idle"
assigned_project: "X-TMS"
skills:
  - "read_file"
  - "write_file"
  - "execute_command"
---
# System Prompt
You are a Senior Systems Architect and AI Engineer specializing in Fable 5, .NET, and microservices architecture. Your objective is to review code modifications, optimize algorithms, and enforce design patterns deterministically.

### 1. Cross-Platform Runtime Strategy
*   **Engine:** Fable 5 compiling F# source down to clean JavaScript/ES6 running on **Node.js**. 
*   **OS Portability:** Native Node.js standard modules (`path`, `fs`) manage differences between Windows backslashes (`\`) and Linux forward slashes (`/`).
*   **File-System Watcher:** Uses Fable bindings for `chokidar` to capture immediate modifications to files within your Obsidian vault across both operating systems without high CPU polling spikes.

### 2. State & Token Governance (Paperclip-style Architecture)
*   **Type-Safe Routing:** Modeled entirely via F# Discriminated Unions to prevent invalid states during execution.
*   **The Interceptor Pipeline:** Every inference call passes through a strict budget controller. It reads a centralized ledger, aggregates prompt/completion tokens, computes cost weights, and instantly suspends an agent loop if spending thresholds are breached.

---

## Key Functional Modules

### 1. Skills Directory (`/Skills`)
*   **Definition:** Each skill is a functional capability exposed to the OS daemon (e.g., executing shell scripts, searching local files, hitting a custom endpoint).
*   **Obsidian Integration:** Managed as individual `.md` files detailing descriptions and schema definitions within the frontmatter.

### 2. Project Run-Down & Status (`/Projects`)
*   **Definition:** Central tracking system mapping out operational health, milestones, and active logs for your production applications.
*   **Obsidian Integration:** Built using markdown-native task tracking or Kanban plugins. The Fable daemon reads task states (`status: Todo`, `status: In-Progress`, `status: Done`) and triggers corresponding backend cycles.

### 3. Unified Dashboards & Obsidian Vaults
*   **Definition:** The operational control deck aggregating execution pipelines, performance charts, and audit histories.
*   **Obsidian Integration:** Driven entirely by Obsidian Canvas JSON files and frontmatter properties. The background daemon updates active properties inside your daily notes or central canvas cards in real time.

### 4. News Run-Down
*   **Definition:** A deterministic pipeline module that handles batch aggregation, thematic cleaning, and semantic filtering of incoming developer news or system events.
*   **Obsidian Integration:** Commits structured summaries directly to an internal note (`Daily_Run_Down.md`) every morning using predefined local model pipelines.

---

## Sample UI Options & Frontmatter Schemas

### Agent Profile Configuration File (`/Agents/Architect-Agent.md`)
```yaml
---
id: "arch-01"
name: "Systems Architect"
model_provider: "ollama"
model_target: "llama3:8b-instruct-q8_0"
temperature: 0.2
max_tokens_per_call: 4096
budget_cap_monthly: 0.00
current_spend_monthly: 0.00
status: "idle"
assigned_project: "X-TMS"
skills:
  - "read_file"
  - "write_file"
  - "execute_command"
---
# System Prompt
You are a Senior Systems Architect and AI Engineer specializing in Fable 5, .NET, and microservices architecture. Your objective is to review code modifications, optimize algorithms, and enforce design patterns deterministically.

---
ticket_id: "XTMS-101"
title: "Implement Twilio SMS Gateway Driver"
project: "X-TMS"
status: "todo"
assigned_to: "arch-01"
priority: "high"
created_date: 2026-07-03
logs: []
---
## Task Objectives
- Create a type-safe F# wrapper for the Twilio REST endpoint.
- Support fallback mechanisms if the primary local router encounters latency spikes.


Guardrails (System Policies)
No Dynamic Workflows: Agent execution routes are entirely deterministic. Self-modifying code, unconstrained autonomous loops, and loose prompt-driven routing structures are strictly forbidden. Transitions between agents must use an explicit state model mapped inside an F# configuration module or clear Obsidian Canvas link connections.

Immutable Audit Logs: Every inference payload, system prompt, and intermediate tool choice writes directly to an append-only transaction file or SQLite ledger. No modifications or deletions are permitted on historical traces.

Hard Budget Ceilings: If any paid commercial model execution exceeds its configured per-request or cumulative daily cap, the Fable loop instantly rolls back the transactions, triggers a failure flag, and shifts the task back to a local Ollama model context.

References & Infrastructure Requirements
References
Paperclip Architecture Blueprint — Organization modeling, budget enforcement controls, and persistent heartbeat intervals.

Paperclip Open Source Core — Open-source reference for tracking multiple isolated task execution layers via a localized ticket paradigm.


Must-Haves
Local Ollama Deployment: Primary operations execute on local infrastructure (such as an NVIDIA RTX 5070 Ti) utilizing quantized local weights (llama3, mistral, phi3) exposed via http://localhost:11434.

Paid API Fallbacks: Native runtime configurations for commercial endpoints (Anthropic Claude, OpenAI API, Gemini API) wrapped in standard authorization headers.

Local Filesystem Isolation: Zero external data persistence requirements. The entire system configuration is portable, running straight out of your designated Obsidian Vault folder structures.

***

### Recommended Next Steps
To begin programming the core foundation of this document using Fable 5, we should implement the file watcher system that reads the agent configuration templates. 

For a solid reference on how the structural foundation of a multi-agent system operates in practice, check out this guide: [Building an Autonomous AI Team with Paperclip](https://www.youtube.com/watch?v=gPbDxMS_x9s). This video provides an overview of setting up organizational agent models, controlling spending budgets, and monitoring task logs, matching the core objectives we laid out for your background daemon.