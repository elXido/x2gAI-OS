# x2g-AI-OS_research: The Obsidian-Powered Deterministic Agentic Operating System

This research document outlines the architectural requirements, foundational principles, and development roadmap for building the **x2gAI-OS**—a local, deterministic agentic operating system tailored for **X2G Ventures**. This system is designed to orchestrate AI-driven workflows using **Fable 5** and **Node.js**, treating an **Obsidian Vault** as its primary user interface and long-term memory.

---

## 1. Executive Summary: The Paradigm Shift
The computing landscape is transitioning from **deterministic engineering** (static logic and GUIs) to **probabilistic systems** driven by human intent. The x2gAI-OS moves AI from an application layer to the **kernel layer**, treating the LLM as the CPU and human intent as a core system primitive.

---

## 2. Core Architectural Pillars

### 2.1 The Agentic Kernel (Control Plane)
The x2gAI-OS kernel must decouple **semantic reasoning** ("what to do") from **system execution** ("how to do it"). 
*   **Syscall Interface:** All agent requests must be decomposed into sub-execution units or "syscalls" (e.g., LLM inference, file operations, tool usage) to enable parallelism.
*   **Deterministic Execution:** Unlike legacy OS kernels that are purely passive, this kernel must be proactive, using a **Model Context Protocol (MCP)** as a standardized "semantic API" to interact with the file system and hardware.
*   **Scheduler:** Implement a **pre-emptive scheduler** that assigns time-slices and priorities to concurrent agents. For high efficiency, use **KV Cache IPC passing**, allowing agents to transfer internal attention states directly instead of raw text, reducing handoff latency by up to 3x.

### 2.2 Hierarchical Memory & "Second Brain"
To overcome the fixed context window of LLMs, the x2gAI-OS implements **Virtual Context Management**.
*   **Main Context (RAM):** The active session history and immediate system instructions.
*   **External Context (Disk):** Uses an **Obsidian Vault** as a "Second Brain".
*   **Context Paging:** When the context window reaches capacity (e.g., 80%), the system should move semantically less-relevant blocks to the Obsidian Vault or a vector database via an **LRU-K eviction policy**.
*   **LLM Wiki Structure:** Follow the "Karpathy LLM Wiki" model—organizing unstructured data into a structured hierarchy within Obsidian using `/raw`, `/wiki`, and `/log` directories to maintain high token efficiency.

### 2.3 The Single Port (Natural User Interface)
Replace the traditional desktop metaphor with a **Single Natural Language Port**.
*   **Primary Interface:** An Obsidian-based gateway utilizing **Markdown frontmatter** for configurations and **Obsidian Canvas** for visual dashboards.
*   **Voice/Text Portal:** A persistent multimodal interface that accepts voice and text contextual signals, generating GUIs only when necessary (e.g., for charts or maps).

---

## 3. Implementation Strategy: The Four C’s

| Pillar | Strategy for x2gAI-OS |
| :--- | :--- |
| **Context** | Build the "Brain" by indexing the user's business data, voice logs, and historical preferences into the Obsidian Personal Knowledge Graph (PKG). |
| **Connections** | Utilize MCP and API endpoints to wire the OS to core tools (e.g., ClickUp, Slack, Google Workspace, Stripe). |
| **Capabilities** | Build "Skills-as-Modules"—reusable markdown recipes that turn SOPs into automated agent workflows. |
| **Cadence** | Use **Routines** and **Loop Engineering** to allow agents to execute tasks (e.g., daily reports, pulse checks) while the user is away. |

---

## 4. Safety and Governance: The Semantic Firewall
Giving AI agents system-level access requires a **defense-in-depth** strategy.
*   **Agent Workspace:** Every autonomous agent must operate within a virtualized, low-privilege sandbox.
*   **Semantic Firewall:** A text-mining layer that monitors information flow to detect **indirect prompt injections** or malicious intent before they reach the kernel.
*   **State Rollback:** Maintain file-level snapshots (e.g., via `ws-ckpt`) to allow one-click rollback if an agent misinterprets a command and causes data loss.
*   **Token Interceptor:** Every inference call must pass through a Fable-driven budget controller that suspends loops if spending thresholds are breached.

---

## 5. Development Roadmap: Phase 1 to Completion

### Phase 1: Foundation (The Skeleton)
*   **Tooling:** Initialize the Fable 5 project targeting Node.js.
*   **UI:** Set up the primary Obsidian Vault with the necessary folder architecture (`/Skills`, `/Projects`, `/Context`, `/References`).
*   **Watcher:** Implement the `chokidar` file-system watcher to enable real-time state updates between Obsidian and the Node.js daemon.

### Phase 2: Memory & Connections (The Nervous System)
*   **Knowledge Ingestion:** Implement the LLM Wiki ingestor to populate the Obsidian `/wiki` from raw files.
*   **Inference Integration:** Connect the **Ollama** local REST API and commercial cloud APIs (Anthropic/OpenAI) using F# type-safe routing.
*   **MCP Servers:** Deploy MCP servers for core X2G business tools to bridge data silos.

### Phase 3: Capabilities & Skills (The Muscles)
*   **Skill Creator:** Develop a "Skill Builder" meta-agent that interviews the user to turn manual workflows into codified `.md` skill files.
*   **Automation:** Set up a headless `claude-p` or similar routine engine to trigger skills on a schedule.

### Phase 4: Optimization & Governance (The Immune System)
*   **Audit Skill:** Create an audit agent to score the OS on its context, connections, and capability leverage.
*   **Guardrails:** Implement the **Tokenless** context compression pipeline and **AgentSecCore** pre-execution boundary to prevent malicious code execution.

### Phase 5: Distribution (The Ecosystem)
*   **Team Deployment:** Transition the personal AIOS into a shared repository, allowing team members to access standardized OS Skills through a unified dashboard.