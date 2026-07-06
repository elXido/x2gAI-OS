module X2G.AgenticOS.Daemon

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open Fable.Core
open Fable.Core.JsInterop
open X2G.AgenticOS.Domain

// ============================================================================
// Node.js runtime bindings.
// Hand-rolled minimal surface instead of a binding package: the daemon only
// touches fs/path/os/crypto/http plus chokidar and ws, and a full binding
// library would drag in far more than these ~60 lines.
// ============================================================================

module Node =

    type IDirent =
        abstract name: string
        abstract isDirectory: unit -> bool
        abstract isFile: unit -> bool

    type IStats =
        abstract isFile: unit -> bool

    type IFs =
        abstract existsSync: p: string -> bool
        abstract readFileSync: p: string * encoding: string -> string
        abstract writeFileSync: p: string * data: string * encoding: string -> unit
        abstract appendFileSync: p: string * data: string * encoding: string -> unit
        abstract renameSync: oldPath: string * newPath: string -> unit
        abstract mkdirSync: p: string * options: obj -> unit
        abstract readdirSync: p: string * options: obj -> IDirent[]
        abstract statSync: p: string -> IStats
        [<Emit("$0.readFileSync($1)")>]
        abstract readFileSyncRaw: p: string -> obj

    type IPath =
        abstract join: a: string * b: string -> string
        abstract resolve: p: string -> string
        abstract normalize: p: string -> string
        abstract basename: p: string -> string
        abstract dirname: p: string -> string

    type IOs =
        abstract homedir: unit -> string

    type IHash =
        abstract update: data: string -> IHash
        abstract digest: encoding: string -> string

    type ICrypto =
        abstract createHash: algorithm: string -> IHash

    type IFSWatcher =
        abstract on: event: string * handler: (string -> unit) -> IFSWatcher

    type IChokidar =
        abstract watch: path: string * options: obj -> IFSWatcher

    type IServerResponse =
        abstract writeHead: status: int * headers: obj -> unit
        abstract ``end``: data: obj -> unit

    type IIncomingMessage =
        abstract url: string

    type IHttpServer =
        abstract listen: port: int * callback: (unit -> unit) -> unit

    type IHttp =
        abstract createServer: handler: Action<IIncomingMessage, IServerResponse> -> IHttpServer

    type IFetchResponse =
        abstract ok: bool
        abstract status: int
        abstract text: unit -> JS.Promise<string>

    let fs: IFs = importAll "fs"
    let path: IPath = importAll "path"
    let os: IOs = importAll "os"
    let crypto: ICrypto = importAll "crypto"
    let chokidar: IChokidar = importAll "chokidar"
    let http: IHttp = importAll "http"

    [<Emit("process.cwd()")>]
    let processCwd () : string = jsNative

    [<Emit("process.exit($0)")>]
    let processExit (code: int) : unit = jsNative

    [<Emit("process.pid")>]
    let processPid () : int = jsNative

    [<Emit("setInterval($0, $1)")>]
    let setInterval (callback: unit -> unit) (ms: int) : obj = jsNative

    [<Emit("setTimeout($0, $1)")>]
    let setTimeout (callback: unit -> unit) (ms: int) : obj = jsNative

    [<Emit("clearTimeout($0)")>]
    let clearTimeout (handle: obj) : unit = jsNative

    [<Emit("$0[$1]")>]
    let getProp (target: obj) (key: string) : obj = jsNative

    [<Emit("Object.keys($0)")>]
    let objectKeys (target: obj) : string[] = jsNative

    [<Emit("Object.assign({}, $0, $1)")>]
    let mergeObjects (a: obj) (b: obj) : obj = jsNative

    [<Emit("$0 == null")>]
    let isNil (value: obj) : bool = jsNative

    [<Emit("String($0)")>]
    let jsString (value: obj) : string = jsNative

    [<Emit("fetch($0, $1)")>]
    let fetch (url: string) (init: obj) : JS.Promise<IFetchResponse> = jsNative

    let asStringOr (value: obj) (fallback: string) : string =
        if isNil value then fallback else jsString value

    let asFloatOr (value: obj) (fallback: float) : float =
        if isNil value then fallback else unbox<float> value

module Json =
    [<Emit("JSON.stringify($0)")>]
    let stringify (value: obj) : string = jsNative

    [<Emit("JSON.stringify($0, null, 2)")>]
    let stringifyPretty (value: obj) : string = jsNative

    [<Emit("JSON.parse($0)")>]
    let parse (text: string) : obj = jsNative

module Time =
    [<Emit("new Date().toISOString()")>]
    let nowIso () : string = jsNative

    [<Emit("Date.now()")>]
    let epochMs () : float = jsNative

// ============================================================================
// Canonical paths (Section 5). Resolved through the Node path module so
// Pop!_OS and Windows 11 normalize identically (2.1 STEP A).
// ============================================================================

module Paths =
    let repoRoot = Node.processCwd ()
    let join (a: string) (b: string) = Node.path.join (a, b)

    let claudeHome = join (Node.os.homedir ()) ".claude"
    let infra = join repoRoot "X2G_infrastructure"
    let ventures = join repoRoot "X2G_Ventures"

    let configDir = join infra "config"
    let pipelinesDir = join configDir "pipelines"
    let logsDir = join infra "logs"
    let templatesDir = join infra "templates"

    let globalRouting = join configDir "global_routing.json"
    let tokenLedger = join configDir "token_ledger.json"
    let discoveredTree = join configDir "discovered_tree.json"
    let scanIgnore = join configDir "scan_ignore.json"
    let auditTrail = join logsDir "audit_trail.json"
    let activeSession = join claudeHome "active_session.json"

    let webAppsRoot = join ventures "Web_Applications"
    let frontendDist = join (join (join ventures "x2gAI-OS") "frontend") "dist"

    let ensureDir (dir: string) =
        if not (Node.fs.existsSync dir) then
            Node.fs.mkdirSync (dir, box {| recursive = true |})

// ============================================================================
// Append-only audit trail (4.4). Records are newline-delimited JSON: appending
// to a single JSON array would require rewriting prior bytes, which is exactly
// the in-place mutation the spec forbids.
// ============================================================================

module Audit =
    let append (kind: string) (payload: obj) =
        let record =
            Json.stringify (box {| timestamp = Time.nowIso (); kind = kind; payload = payload |})
        try
            Paths.ensureDir Paths.logsDir
            Node.fs.appendFileSync (Paths.auditTrail, record + "\n", "utf8")
        with _ ->
            eprintfn "[x2g HARD FAULT] audit trail is unwritable; halting per 4.4"
            Node.processExit 1

module Guardrails =
    /// Section 4.4: hard faults never continue. Generic return type lets this
    /// terminate any expression position; process.exit fires before failwith.
    let halt (context: string) (detail: string) : 'T =
        Audit.append "HARD_FAULT" (box {| context = context; detail = detail |})
        eprintfn "[x2g HARD FAULT] %s :: %s" context detail
        Node.processExit 1
        failwith detail

// ============================================================================
// Global routing config (Section 5 / 4.4 caps). Missing or anomalous config
// is a boot-blocking fault, never a silent default.
// ============================================================================

module Config =
    type GlobalRouting =
        { HttpPort: int
          WsPort: int
          DiscoveryHeartbeatMs: int
          GlobalMonthlyCapUsd: float
          ProviderRates: obj
          ObsidianVaults: string[]
          OllamaEndpoint: string
          OllamaModel: string }

    let load () : GlobalRouting =
        if not (Node.fs.existsSync Paths.globalRouting) then
            Guardrails.halt "config" (sprintf "missing %s; create it before boot (Section 5)" Paths.globalRouting)
        let raw = Node.fs.readFileSync (Paths.globalRouting, "utf8")
        if raw.Trim() = "" then
            Guardrails.halt "config" "global_routing.json is empty (4.4 anomaly)"
        let doc =
            try Json.parse raw
            with _ -> Guardrails.halt "config" "global_routing.json is not valid JSON"
        let require (key: string) : obj =
            let value = Node.getProp doc key
            if Node.isNil value then
                Guardrails.halt "config" (sprintf "global_routing.json missing required key '%s'" key)
            else value
        { HttpPort = int (unbox<float> (require "httpPort"))
          WsPort = int (unbox<float> (require "wsPort"))
          DiscoveryHeartbeatMs = int (unbox<float> (require "discoveryHeartbeatMs"))
          GlobalMonthlyCapUsd = unbox<float> (require "globalMonthlyCapUsd")
          ProviderRates = require "providerRatesPerMillionTokensUsd"
          ObsidianVaults =
            (let vaults = Node.getProp doc "obsidianVaults"
             if Node.isNil vaults then [||] else unbox<string[]> vaults)
          OllamaEndpoint = Node.jsString (require "ollamaEndpoint")
          OllamaModel = Node.jsString (require "ollamaDefaultModel") }

// ============================================================================
// Token ledger (4.2 STEP B / STEP F). Append-only JSONL, same rationale as the
// audit trail. Spend aggregation reads the ledger — the ledger, not the agent
// context, is the source of truth for budget decisions.
// ============================================================================

module Ledger =
    let append (row: obj) =
        Paths.ensureDir Paths.configDir
        Node.fs.appendFileSync (Paths.tokenLedger, Json.stringify row + "\n", "utf8")

    /// Returns (agentSpendUsd, globalSpendUsd) for the current calendar month.
    let monthlySpend (agentId: string) : float * float =
        if not (Node.fs.existsSync Paths.tokenLedger) then 0.0, 0.0
        else
            let monthPrefix = (Time.nowIso ()).Substring(0, 7)
            let mutable agentTotal = 0.0
            let mutable globalTotal = 0.0
            for line in (Node.fs.readFileSync (Paths.tokenLedger, "utf8")).Split('\n') do
                let trimmed = line.Trim()
                if trimmed <> "" then
                    let row =
                        try Json.parse trimmed
                        with _ -> Guardrails.halt "ledger" "corrupt row in append-only token_ledger.json (4.4)"
                    if (Node.asStringOr (Node.getProp row "timestamp") "").StartsWith monthPrefix then
                        let cost = Node.asFloatOr (Node.getProp row "costUsd") 0.0
                        globalTotal <- globalTotal + cost
                        if Node.asStringOr (Node.getProp row "agentId") "" = agentId then
                            agentTotal <- agentTotal + cost
            agentTotal, globalTotal

// ============================================================================
// WebSocket telemetry channel (1.2): the frontend never polls; every event the
// daemon produces is pushed through here.
// ============================================================================

module Ws =
    type IWsSocket =
        abstract send: data: string -> unit
        abstract on: event: string * handler: (obj -> unit) -> unit
        abstract readyState: int

    type private IWsServer =
        abstract on: event: string * handler: (IWsSocket -> unit) -> unit

    let private clients = ResizeArray<IWsSocket>()

    let broadcast (message: obj) =
        let text = Json.stringify message
        for client in clients do
            if client.readyState = 1 then client.send text

    let start (port: int) (onMessage: IWsSocket -> string -> unit) =
        let ctor: obj = import "WebSocketServer" "ws"
        let server: IWsServer = unbox (createNew ctor {| port = port |})
        server.on ("connection", fun socket ->
            clients.Add socket
            socket.on ("message", fun data -> onMessage socket (Node.jsString data))
            socket.on ("close", fun _ -> clients.Remove socket |> ignore))
        printfn "[x2g] ws: zero-polling telemetry channel on :%i" port

// ============================================================================
// Section 2 — dynamic discovery: BFS tree walk, .claude.md rule extraction,
// atomic snapshot persistence, menu-delta emission.
// ============================================================================

module Discovery =
    type MenuNode =
        { id: string
          label: string
          modulePath: string
          moduleType: string
          route: string
          badgeCount: int }

    type ModuleDescriptor =
        { id: string
          moduleType: string
          absPath: string
          contextFile: string
          manifests: string[]
          localRules: obj
          discoveredAt: string }

    let private manifestNames =
        [ "project.json"; "configuration.json"; "processing_jobs.json"; "models_registry.json"; "protocol_rules.json" ]

    let private roots =
        [ "claude", Paths.claudeHome
          "infra", Paths.infra
          "ventures", Paths.ventures ]

    let private registry = Dictionary<string, ModuleDescriptor>()

    let private defaultIgnored = [ "node_modules"; ".git"; "dist"; "build"; ".angular" ]

    let private loadIgnored () : HashSet<string> =
        let ignored = HashSet<string>(defaultIgnored)
        if Node.fs.existsSync Paths.scanIgnore then
            let doc =
                try Json.parse (Node.fs.readFileSync (Paths.scanIgnore, "utf8"))
                with _ -> Guardrails.halt "discovery" "scan_ignore.json is not valid JSON (1.6: reject bad config)"
            for name in unbox<string[]> doc do ignored.Add name |> ignore
        ignored

    let private classify (absPath: string) : string =
        let normalized = absPath.Replace("\\", "/")
        if normalized.Contains "/Web_Applications" then "webapp"
        elif normalized.StartsWith (Paths.ventures.Replace("\\", "/")) then "domain"
        else "system"

    let private slugify (rootTag: string) (root: string) (dir: string) : string =
        let rel =
            if dir = root then Node.path.basename root
            else dir.Substring(root.Length).TrimStart([| '/'; '\\' |])
        let cleaned = Regex.Replace(rel.Replace("\\", "/").Replace("/", "-"), "[^A-Za-z0-9._-]", "-")
        (rootTag + "-" + cleaned).ToLowerInvariant()

    let private rulesRegex = Regex("```x2g-rules\\s*([\\s\\S]*?)```")

    // 2.2: .claude.md is human-authored text, never machine state. Only the
    // fenced x2g-rules block is machine rules; everything else is advisory.
    let private extractLocalRules (contextFile: string) : obj =
        if Node.isNil (box contextFile) || not (contextFile.EndsWith ".claude.md") then unbox null
        else
            let text =
                try Node.fs.readFileSync (contextFile, "utf8")
                with _ -> ""
            let m = rulesRegex.Match text
            if not m.Success then unbox null
            else
                try Json.parse m.Groups.[1].Value
                with _ ->
                    Audit.append "INVALID_X2G_RULES_BLOCK" (box {| file = contextFile |})
                    unbox null

    let private scanRoot (rootTag: string) (root: string) (ignored: HashSet<string>) (acc: ResizeArray<ModuleDescriptor>) =
        if Node.fs.existsSync root then
            let pending = ResizeArray<string>([ root ])
            let mutable index = 0
            while index < pending.Count do
                let dir = pending.[index]
                index <- index + 1
                let entries =
                    try Node.fs.readdirSync (dir, box {| withFileTypes = true |})
                    with _ -> [||]
                let mutable contextFile: string = unbox null
                let manifests = ResizeArray<string>()
                for entry in entries do
                    let full = Paths.join dir entry.name
                    if entry.isDirectory () then
                        if not (ignored.Contains entry.name) then pending.Add full
                    elif entry.isFile () then
                        if entry.name = ".claude.md" || entry.name = ".claude" then contextFile <- full
                        elif List.contains entry.name manifestNames then manifests.Add entry.name
                if manifests.Count > 0 || not (Node.isNil (box contextFile)) then
                    acc.Add
                        { id = slugify rootTag root dir
                          moduleType = classify dir
                          absPath = dir
                          contextFile = contextFile
                          manifests = manifests.ToArray()
                          localRules = extractLocalRules contextFile
                          discoveredAt = Time.nowIso () }

    // 2.1 STEP F: overwrite atomically via write-temp-then-rename.
    let private persist (modules: ModuleDescriptor list) =
        Paths.ensureDir Paths.configDir
        let tmp = Paths.discoveredTree + ".tmp"
        let snapshot = box {| generatedAt = Time.nowIso (); modules = Array.ofList modules |}
        Node.fs.writeFileSync (tmp, Json.stringifyPretty snapshot, "utf8")
        Node.fs.renameSync (tmp, Paths.discoveredTree)

    let menuNodeOf (descriptor: ModuleDescriptor) : MenuNode =
        { id = descriptor.id
          label = Node.path.basename descriptor.absPath
          modulePath = descriptor.absPath
          moduleType = descriptor.moduleType
          route = "/" + descriptor.moduleType + "/" + descriptor.id
          badgeCount = 0 }

    let run () : ModuleDescriptor list =
        let ignored = loadIgnored ()
        let acc = ResizeArray<ModuleDescriptor>()
        for (tag, root) in roots do
            scanRoot tag root ignored acc
        let modules = List.ofSeq acc
        persist modules
        let added = modules |> List.filter (fun m -> not (registry.ContainsKey m.id))
        registry.Clear()
        for m in modules do registry.[m.id] <- m
        if not (List.isEmpty added) then
            Ws.broadcast (box {| ``type`` = "menu-delta"; payload = {| added = added |> List.map menuNodeOf |> Array.ofList |} |})
        Ws.broadcast (box {| ``type`` = "menu-snapshot"; payload = {| nodes = modules |> List.map menuNodeOf |> Array.ofList |} |})
        modules

// ============================================================================
// Boot-time pipeline verification: the *.pipeline.json files are the runtime
// source of truth, PipelineGraphs (Domain.fs) is the compile-time contract.
// Drift between them is a hard fault, as is any config edit that weakens the
// Section 3.1 terminology law or the Section 3.2 MRN policy.
// ============================================================================

module PipelineLoader =
    let private xtmsWire (state: XtmsState) =
        match state with
        | XtmsState.IngestAlert -> "INGEST_ALERT"
        | XtmsState.ParseRatecon -> "PARSE_RATECON"
        | XtmsState.ValidateFreight -> "VALIDATE_FREIGHT"
        | XtmsState.DispatchSms -> "DISPATCH_SMS"
        | XtmsState.Archive -> "ARCHIVE"

    let private trialLensWire (state: TrialLensState) =
        match state with
        | TrialLensState.IntakeDataset -> "INTAKE_DATASET"
        | TrialLensState.NormalizeRecords -> "NORMALIZE_RECORDS"
        | TrialLensState.MapProtocol -> "MAP_PROTOCOL"
        | TrialLensState.IndexMemory -> "INDEX_MEMORY"
        | TrialLensState.Report -> "REPORT"

    let private pathologyWire (state: PathologyState) =
        match state with
        | PathologyState.RegisterWsi -> "REGISTER_WSI"
        | PathologyState.ExtractMetadata -> "EXTRACT_METADATA"
        | PathologyState.QueueAnalysis -> "QUEUE_ANALYSIS"
        | PathologyState.RunJob -> "RUN_JOB"
        | PathologyState.PublishResult -> "PUBLISH_RESULT"

    let private mlWire (state: MlRunState) =
        match state with
        | MlRunState.RegisterRun -> "REGISTER_RUN"
        | MlRunState.Monitor -> "MONITOR"
        | MlRunState.CollectMetrics -> "COLLECT_METRICS"
        | MlRunState.EmitCharts -> "EMIT_CHARTS"

    let private webAppsWire (state: WebAppsState) =
        match state with
        | WebAppsState.Monitor -> "MONITOR"
        | WebAppsState.ScaffoldModule -> "SCAFFOLD_MODULE"
        | WebAppsState.RegisterModule -> "REGISTER_MODULE"

    let private graphTransitions (graph: StateGraph<'S>) (toWire: 'S -> string) : string list =
        graph.Transitions
        |> List.map (fun (from, dest, guard) -> sprintf "%s->%s:%s" (toWire from) (toWire dest) guard)
        |> List.sort

    let private docTransitions (doc: obj) : string list =
        Node.getProp doc "transitions"
        |> unbox<obj[]>
        |> Array.toList
        |> List.map (fun t ->
            sprintf "%s->%s:%s"
                (Node.jsString (Node.getProp t "from"))
                (Node.jsString (Node.getProp t "to"))
                (Node.jsString (Node.getProp t "guard")))
        |> List.sort

    let private loadDoc (fileName: string) : obj =
        let fullPath = Paths.join Paths.pipelinesDir fileName
        if not (Node.fs.existsSync fullPath) then
            Guardrails.halt "pipeline-loader" (sprintf "missing pipeline definition %s" fullPath)
        let raw = Node.fs.readFileSync (fullPath, "utf8")
        if raw.Trim() = "" then
            Guardrails.halt "pipeline-loader" (sprintf "%s is empty (4.4 anomaly)" fileName)
        try Json.parse raw
        with _ -> Guardrails.halt "pipeline-loader" (sprintf "%s is not valid JSON" fileName)

    let loadAndVerify () =
        let xtmsDoc = loadDoc "xtms.pipeline.json"
        if graphTransitions PipelineGraphs.xtms xtmsWire <> docTransitions xtmsDoc then
            Guardrails.halt "pipeline-loader" "xtms.pipeline.json transitions drifted from Domain.PipelineGraphs.xtms"

        // 3.1 override: terminology law applies to the config itself.
        let schemas = Node.getProp xtmsDoc "recordSchemas"
        if not (Node.isNil schemas) then
            for schemaName in Node.objectKeys schemas do
                match XtmsSchemaIntegrity.validateDocumentFields (Node.objectKeys (Node.getProp schemas schemaName)) with
                | Ok () -> ()
                | Error errors ->
                    Guardrails.halt "pipeline-loader" (sprintf "xtms recordSchemas.%s: %s" schemaName (String.concat "; " errors))

        let trialLensDoc = loadDoc "triallens.pipeline.json"
        if graphTransitions PipelineGraphs.trialLens trialLensWire <> docTransitions trialLensDoc then
            Guardrails.halt "pipeline-loader" "triallens.pipeline.json transitions drifted from Domain.PipelineGraphs.trialLens"

        // 3.2 override: a config edit must not be able to weaken the MRN policy.
        let policy = Node.getProp trialLensDoc "identifierPolicy"
        if Node.isNil policy
           || Node.asStringOr (Node.getProp policy "mrnType") "" <> "string"
           || Node.asStringOr (Node.getProp policy "numericCoercion") "" <> "hard_fault" then
            Guardrails.halt "pipeline-loader" "triallens identifierPolicy weakened: mrnType must be 'string' and numericCoercion 'hard_fault' (Section 3.2 override)"

        let pathologyDoc = loadDoc "pathology.pipeline.json"
        if graphTransitions PipelineGraphs.pathology pathologyWire <> docTransitions pathologyDoc then
            Guardrails.halt "pipeline-loader" "pathology.pipeline.json transitions drifted from Domain.PipelineGraphs.pathology"

        let mlDoc = loadDoc "mlprojects.pipeline.json"
        if graphTransitions PipelineGraphs.mlProjects mlWire <> docTransitions mlDoc then
            Guardrails.halt "pipeline-loader" "mlprojects.pipeline.json transitions drifted from Domain.PipelineGraphs.mlProjects"

        let webAppsDoc = loadDoc "webapps.pipeline.json"
        if graphTransitions PipelineGraphs.webApps webAppsWire <> docTransitions webAppsDoc then
            Guardrails.halt "pipeline-loader" "webapps.pipeline.json transitions drifted from Domain.PipelineGraphs.webApps"

        printfn "[x2g] pipelines verified against Domain.PipelineGraphs: xtms, triallens, pathology, mlprojects, webapps"

// ============================================================================
// Obsidian vault boundary (1.5): single write path that refuses anything
// outside _x2g/, Chokidar with awaitWriteFinish, 250ms debounce, and
// content-hash echo suppression.
// ============================================================================

module VaultBoundary =
    let private lastHashes = Dictionary<string, string>()
    let private debouncers = Dictionary<string, obj>()

    let private sha256 (text: string) =
        Node.crypto.createHash("sha256").update(text).digest("hex")

    let writeVaultState (vaultRoot: string) (relPath: string) (value: obj) =
        let normalized = relPath.Replace("\\", "/")
        if not (normalized.StartsWith "_x2g/") then
            Guardrails.halt "vault-boundary" (sprintf "attempted vault write outside _x2g/: %s" relPath)
        let fullPath = Paths.join vaultRoot relPath
        Paths.ensureDir (Node.path.dirname fullPath)
        let text = Json.stringifyPretty value
        // Remember our own write so the watcher recognizes the echo and skips it.
        lastHashes.[fullPath] <- sha256 text
        Node.fs.writeFileSync (fullPath, text, "utf8")

    let private handleFsEvent (vaultRoot: string) (filePath: string) =
        if Node.fs.existsSync filePath then
            let text =
                try Node.fs.readFileSync (filePath, "utf8")
                with _ -> ""
            let hash = sha256 text
            let known =
                match lastHashes.TryGetValue filePath with
                | true, value -> value
                | _ -> ""
            if hash <> known then
                lastHashes.[filePath] <- hash
                Ws.broadcast (box {| ``type`` = "vault-event"; payload = {| vault = vaultRoot; file = filePath |} |})

    let private debounced (key: string) (ms: int) (action: unit -> unit) =
        match debouncers.TryGetValue key with
        | true, handle -> Node.clearTimeout handle
        | _ -> ()
        debouncers.[key] <- Node.setTimeout (fun () ->
            debouncers.Remove key |> ignore
            action ()) ms

    let watch (vaults: string[]) =
        for vault in vaults do
            if Node.fs.existsSync vault then
                let watcher =
                    Node.chokidar.watch (
                        vault,
                        box {| ignoreInitial = true
                               awaitWriteFinish = {| stabilityThreshold = 250; pollInterval = 50 |} |}
                    )
                let onEvent (filePath: string) =
                    debounced filePath 250 (fun () -> handleFsEvent vault filePath)
                watcher.on("add", onEvent).on ("change", onEvent) |> ignore
                printfn "[x2g] vault: watching %s (writes restricted to _x2g/)" vault
            else
                Audit.append "VAULT_MISSING" (box {| vault = vault |})

// ============================================================================
// X-TMS INGEST_ALERT watcher (3.1): inbound alert files become JSON tickets.
// External JSON alerts must pass the terminology validator before a ticket is
// cut — the override's rejection path for 'load'/'rate_confirmation' feeds.
// ============================================================================

module XtmsIngest =
    let private xtmsRoot = Paths.join Paths.ventures "X-TMS"
    let private inboundDir = Paths.join xtmsRoot "inbound"
    let private ticketsDir = Paths.join xtmsRoot "tickets"
    let mutable private counter = 0

    let private onAlert (sourcePath: string) =
        counter <- counter + 1
        let fileName = Node.path.basename sourcePath
        let schemaOk =
            if fileName.EndsWith ".json" then
                let raw =
                    try Node.fs.readFileSync (sourcePath, "utf8")
                    with _ -> ""
                let doc =
                    try Json.parse raw
                    with _ -> unbox null
                if Node.isNil doc then
                    Audit.append "XTMS_ALERT_REJECTED" (box {| file = fileName; reason = "not valid JSON" |})
                    false
                else
                    match XtmsSchemaIntegrity.validateDocumentFields (Node.objectKeys doc) with
                    | Ok () -> true
                    | Error errors ->
                        Audit.append "XTMS_SCHEMA_VIOLATION" (box {| file = fileName; errors = Array.ofList errors |})
                        false
            else true
        if schemaOk then
            let ticketId = sprintf "XTMS-%.0f-%i" (Time.epochMs ()) counter
            let ticket =
                box {| ticket_id = ticketId
                       freight_id = "FRT-" + Regex.Replace(fileName, "[^A-Za-z0-9._-]", "-")
                       ratecon = unbox<obj> null
                       pipeline_state = "INGEST_ALERT"
                       sms_dispatched = false
                       audit_ref = Time.nowIso () |}
            Paths.ensureDir ticketsDir
            let ticketPath = Paths.join ticketsDir (ticketId + ".json")
            Node.fs.writeFileSync (ticketPath, Json.stringifyPretty ticket, "utf8")
            Audit.append "XTMS_TICKET_CREATED" (box {| ticket = ticketPath; source = fileName |})
            Ws.broadcast (box {| ``type`` = "telemetry"; payload = {| pipeline = "xtms"; state = "INGEST_ALERT"; ticketPath = ticketPath |} |})

    let start () =
        if Node.fs.existsSync inboundDir then
            let watcher =
                Node.chokidar.watch (
                    inboundDir,
                    box {| ignoreInitial = true
                           awaitWriteFinish = {| stabilityThreshold = 250; pollInterval = 50 |} |}
                )
            watcher.on ("add", onAlert) |> ignore
            printfn "[x2g] xtms: watching %s" inboundDir
        else
            printfn "[x2g] xtms: inbound directory not present (%s); watcher idle" inboundDir

// ============================================================================
// Section 4.2 — inline interceptor. Every inference call runs the ordered
// STEP A..G sequence. The core daemon ships no cloud client, so paid
// providers always resolve to local Ollama (4.3 local-first) — a cloud
// executor added later still cannot bypass the budget gate.
// ============================================================================

module Interceptor =
    type InferenceRequest =
        { Agent: AgentContext
          RequestedProvider: ModelProvider
          ModelTarget: string
          EstimatedTokens: int
          Prompt: string }

    let providerName (provider: ModelProvider) =
        match provider with
        | OllamaLocal -> "OllamaLocal"
        | AnthropicClaude -> "AnthropicClaude"
        | OpenAI -> "OpenAI"
        | Gemini -> "Gemini"

    let parseProvider (name: string) : ModelProvider option =
        match name with
        | "OllamaLocal" -> Some OllamaLocal
        | "AnthropicClaude" -> Some AnthropicClaude
        | "OpenAI" -> Some OpenAI
        | "Gemini" -> Some Gemini
        | _ -> None

    let private rateFor (cfg: Config.GlobalRouting) (provider: ModelProvider) : float =
        let rate = Node.getProp cfg.ProviderRates (providerName provider)
        if Node.isNil rate then
            Guardrails.halt "interceptor" (sprintf "no rate configured for provider %s in global_routing.json" (providerName provider))
        else unbox<float> rate

    let private executeOllama (cfg: Config.GlobalRouting) (model: string) (promptText: string) : JS.Promise<float * float * string> =
        promise {
            let init =
                box {| ``method`` = "POST"
                       headers = {| ``Content-Type`` = "application/json" |}
                       body = Json.stringify (box {| model = model; prompt = promptText; stream = false |}) |}
            let! response = Node.fetch (cfg.OllamaEndpoint + "/api/generate") init
            if not response.ok then
                return Guardrails.halt "interceptor.ollama" (sprintf "HTTP %i from %s" response.status cfg.OllamaEndpoint)
            else
                let! bodyText = response.text ()
                if bodyText.Trim() = "" then
                    return Guardrails.halt "interceptor.ollama" "empty response payload (4.4 anomaly)"
                else
                    let body = Json.parse bodyText
                    let responseText = Node.asStringOr (Node.getProp body "response") ""
                    if responseText = "" then
                        return Guardrails.halt "interceptor.ollama" "response field empty or missing (4.4 anomaly)"
                    else
                        let promptTokens = Node.asFloatOr (Node.getProp body "prompt_eval_count") 0.0
                        let completionTokens = Node.asFloatOr (Node.getProp body "eval_count") 0.0
                        return (promptTokens, completionTokens, responseText)
        }

    let interceptAndExecute (cfg: Config.GlobalRouting) (request: InferenceRequest) : JS.Promise<obj> =
        promise {
            let agentId = request.Agent.Id
            // STEP A — AUDIT PRE
            Audit.append "INFERENCE_PRE" (box {| agentId = agentId
                                                 provider = providerName request.RequestedProvider
                                                 estimatedTokens = request.EstimatedTokens |})
            // STEP B — LEDGER READ (ledger is authoritative, not the agent context)
            let agentSpend, globalSpend = Ledger.monthlySpend agentId
            // STEP C — BUDGET EVALUATION
            let projectedCost = float request.EstimatedTokens / 1000000.0 * rateFor cfg request.RequestedProvider
            let isPaid = request.RequestedProvider <> OllamaLocal
            let violated =
                isPaid
                && (agentSpend + projectedCost > float request.Agent.MonthlyBudgetUsd
                    || globalSpend + projectedCost > cfg.GlobalMonthlyCapUsd)
            // STEP D — FALLBACK ON VIOLATION: suspend and re-route to local Ollama
            if violated then
                Audit.append "BUDGET_VIOLATION_REROUTE" (box {| agentId = agentId
                                                                requestedProvider = providerName request.RequestedProvider
                                                                projectedCostUsd = projectedCost
                                                                agentSpendUsd = agentSpend
                                                                globalSpendUsd = globalSpend |})
            elif isPaid then
                Audit.append "LOCAL_FIRST_REROUTE" (box {| agentId = agentId
                                                           requestedProvider = providerName request.RequestedProvider
                                                           reason = "no cloud executor registered; 4.3 local-first" |})
            let resolvedProvider = OllamaLocal
            let status =
                if violated then Suspended "budget cap violated; rerouted to local Ollama (4.2 STEP D)"
                else Running
            let model = if request.ModelTarget = "" then cfg.OllamaModel else request.ModelTarget
            // STEP E — EXECUTE
            let! (promptTokens, completionTokens, responseText) = executeOllama cfg model request.Prompt
            // STEP F — LEDGER APPEND (actual tokens, computed cost)
            let actualCost = (promptTokens + completionTokens) / 1000000.0 * rateFor cfg resolvedProvider
            Ledger.append (box {| timestamp = Time.nowIso ()
                                  agentId = agentId
                                  provider = providerName resolvedProvider
                                  promptTokens = promptTokens
                                  completionTokens = completionTokens
                                  costUsd = actualCost |})
            // STEP G — AUDIT POST
            let statusLabel =
                match status with
                | Suspended reason -> "Suspended: " + reason
                | _ -> "Running"
            Audit.append "INFERENCE_POST" (box {| agentId = agentId
                                                  provider = providerName resolvedProvider
                                                  promptTokens = promptTokens
                                                  completionTokens = completionTokens
                                                  status = statusLabel |})
            return box {| agentId = agentId
                          provider = providerName resolvedProvider
                          status = statusLabel
                          promptTokens = promptTokens
                          completionTokens = completionTokens
                          costUsd = actualCost
                          response = responseText |}
        }

// ============================================================================
// Section 2.4 — deterministic scaffolding from the Command Center.
// ============================================================================

module Scaffolder =
    let private idPattern = Regex("^[A-Za-z0-9._-]{1,64}$")

    let private substitute (moduleId: string) (moduleLabel: string) (createdAt: string) (content: string) =
        content.Replace("${moduleId}", moduleId).Replace("${moduleLabel}", moduleLabel).Replace("${createdAt}", createdAt)

    // 2.4 STEP F: seeded x2g-rules may tighten but never loosen global guardrails.
    let private claudeMdSeed (moduleId: string) (moduleLabel: string) (moduleType: string) (createdAt: string) =
        let rules =
            Json.stringifyPretty (box {| scope = moduleId
                                         moduleType = moduleType
                                         createdAt = createdAt
                                         guardrails = {| tokenCeilings = "inherit-global"
                                                         determinism = "required"
                                                         audit = "append-only"
                                                         loosening = "forbidden" |} |})
        sprintf "# %s\n\nLocalized scope context for module `%s`. Human-authored notes go here.\n\n```x2g-rules\n%s\n```\n" moduleLabel moduleId rules

    let execute (payload: obj) : Result<Discovery.MenuNode, string> =
        if Node.isNil payload then Error "scaffold-request payload missing"
        else
            let moduleId = Node.asStringOr (Node.getProp payload "moduleId") ""
            let moduleLabel = Node.asStringOr (Node.getProp payload "moduleLabel") ""
            let templateType = Node.asStringOr (Node.getProp payload "templateType") ""
            let moduleType = Node.asStringOr (Node.getProp payload "moduleType") "webapp"
            if not (idPattern.IsMatch moduleId) then
                Error "invalid moduleId: must match ^[A-Za-z0-9._-]{1,64}$"
            elif moduleLabel = "" then
                Error "moduleLabel is required"
            elif templateType = "" then
                Error "templateType is required"
            else
                let parent =
                    let requested = Node.asStringOr (Node.getProp payload "parentPath") ""
                    if requested = "" then Paths.webAppsRoot
                    else Paths.join Paths.repoRoot requested
                let target = Paths.join parent moduleId
                if Node.fs.existsSync target then
                    Error (sprintf "refused: %s already exists — no overwrite (2.4 STEP C)" target)
                else
                    let manifestPath = Paths.join (Paths.join Paths.templatesDir templateType) "manifest.json"
                    if not (Node.fs.existsSync manifestPath) then
                        Error (sprintf "unknown templateType '%s': no template manifest at %s" templateType manifestPath)
                    else
                        let manifestRaw = Node.fs.readFileSync (manifestPath, "utf8")
                        let manifest =
                            try Json.parse manifestRaw
                            with _ -> unbox null
                        if Node.isNil manifest then Error "template manifest is not valid JSON"
                        else
                            let createdAt = Time.nowIso ()
                            Paths.ensureDir target
                            let files =
                                let f = Node.getProp manifest "files"
                                if Node.isNil f then [||] else unbox<obj[]> f
                            let mutable wroteProjectJson = false
                            for file in files do
                                let rel = Node.jsString (Node.getProp file "path")
                                let content = substitute moduleId moduleLabel createdAt (Node.asStringOr (Node.getProp file "content") "")
                                let dest = Paths.join target rel
                                Paths.ensureDir (Node.path.dirname dest)
                                Node.fs.writeFileSync (dest, content, "utf8")
                                if rel = "project.json" then wroteProjectJson <- true
                            if not wroteProjectJson then
                                let defaults =
                                    let d = Node.getProp manifest "projectDefaults"
                                    if Node.isNil d then createObj [] else d
                                let overrides =
                                    let o = Node.getProp payload "overrides"
                                    if Node.isNil o then createObj [] else o
                                // Identity fields are stamped last so request overrides cannot forge them.
                                let project =
                                    Node.mergeObjects
                                        (Node.mergeObjects defaults overrides)
                                        (box {| id = moduleId; label = moduleLabel; moduleType = moduleType; createdAt = createdAt |})
                                Node.fs.writeFileSync (Paths.join target "project.json", Json.stringifyPretty project, "utf8")
                            Node.fs.writeFileSync (Paths.join target ".claude.md", claudeMdSeed moduleId moduleLabel moduleType createdAt, "utf8")
                            Audit.append "SCAFFOLD_CREATED" (box {| moduleId = moduleId; templateType = templateType; target = target |})
                            Ok { id = moduleId
                                 label = moduleLabel
                                 modulePath = target
                                 moduleType = moduleType
                                 route = "/" + moduleType + "/" + moduleId
                                 badgeCount = 0 }

// ============================================================================
// WebSocket message router: {type, payload, requestId?} envelopes in,
// {type, payload, requestId} envelopes out.
// ============================================================================

module Router =
    let handle (cfg: Config.GlobalRouting) (ws: Ws.IWsSocket) (raw: string) =
        let envelope =
            try Json.parse raw
            with _ -> unbox null
        let requestId = if Node.isNil envelope then unbox<obj> null else Node.getProp envelope "requestId"
        let reply (msgType: string) (payload: obj) =
            ws.send (Json.stringify (box {| ``type`` = msgType; requestId = requestId; payload = payload |}))
        if Node.isNil envelope then
            reply "error" (box {| message = "malformed JSON envelope" |})
        else
            match Node.asStringOr (Node.getProp envelope "type") "" with
            | "scaffold-request" ->
                match Scaffolder.execute (Node.getProp envelope "payload") with
                | Ok node ->
                    Ws.broadcast (box {| ``type`` = "menu-delta"; payload = {| added = [| node |] |} |})
                    reply "scaffold-result" (box {| ok = true; node = node |})
                | Error message ->
                    Audit.append "SCAFFOLD_REJECTED" (box {| reason = message |})
                    reply "scaffold-result" (box {| ok = false; message = message |})
            | "discover-now" ->
                let modules = Discovery.run ()
                reply "discovery-result" (box {| moduleCount = List.length modules |})
            | "inference-request" ->
                let payload = Node.getProp envelope "payload"
                let providerText = Node.asStringOr (Node.getProp payload "provider") "OllamaLocal"
                match Interceptor.parseProvider providerText with
                | None -> reply "error" (box {| message = "unknown provider: " + providerText |})
                | Some provider ->
                    let request: Interceptor.InferenceRequest =
                        { Agent =
                            { Id = Node.asStringOr (Node.getProp payload "agentId") "anonymous"
                              Status = Idle
                              Provider = provider
                              Project = Node.asStringOr (Node.getProp payload "project") ""
                              MonthlyBudgetUsd = decimal (Node.asFloatOr (Node.getProp payload "monthlyBudgetUsd") 0.0)
                              SpendMonthUsd = 0M }
                          RequestedProvider = provider
                          ModelTarget = Node.asStringOr (Node.getProp payload "modelTarget") ""
                          EstimatedTokens = int (Node.asFloatOr (Node.getProp payload "estimatedTokens") 0.0)
                          Prompt = Node.asStringOr (Node.getProp payload "prompt") "" }
                    Interceptor.interceptAndExecute cfg request
                    |> Promise.map (fun outcome -> reply "inference-result" outcome)
                    |> Promise.catch (fun err -> reply "error" (box {| message = err.Message |}))
                    |> ignore
            | "terminal-syscall" ->
                Audit.append "TERMINAL_SYSCALL" (box {| payload = Node.getProp envelope "payload" |})
                reply "terminal-result" (box {| ok = false; message = "terminal syscalls are audited but not executed by the core daemon" |})
            | other ->
                reply "error" (box {| message = "unknown message type: " + other |})

// ============================================================================
// Static SPA server (1.2 stage 2): serves the built Angular Command Center.
// ============================================================================

module StaticServer =
    let private contentType (filePath: string) =
        let ext =
            let idx = filePath.LastIndexOf '.'
            if idx >= 0 then filePath.Substring(idx) else ""
        match ext with
        | ".html" -> "text/html; charset=utf-8"
        | ".js" | ".mjs" -> "text/javascript"
        | ".css" -> "text/css"
        | ".json" -> "application/json"
        | ".svg" -> "image/svg+xml"
        | ".ico" -> "image/x-icon"
        | ".png" -> "image/png"
        | ".woff2" -> "font/woff2"
        | _ -> "application/octet-stream"

    let start (port: int) =
        let dist = Node.path.resolve Paths.frontendDist
        let indexHtml = Paths.join dist "index.html"
        let server =
            Node.http.createServer (Action<_, _>(fun request response ->
                let urlPath = (request.url.Split '?').[0]
                let relative = if urlPath = "/" then "index.html" else urlPath.TrimStart '/'
                let requested = Node.path.resolve (Paths.join dist relative)
                if not (requested.StartsWith dist) then
                    response.writeHead (403, box {| ``Content-Type`` = "text/plain" |})
                    response.``end`` (box "forbidden")
                elif Node.fs.existsSync requested && (Node.fs.statSync requested).isFile () then
                    response.writeHead (200, box {| ``Content-Type`` = contentType requested |})
                    response.``end`` (Node.fs.readFileSyncRaw requested)
                elif Node.fs.existsSync indexHtml then
                    // SPA fallback: deep links resolve client-side in Angular's router
                    response.writeHead (200, box {| ``Content-Type`` = "text/html; charset=utf-8" |})
                    response.``end`` (Node.fs.readFileSyncRaw indexHtml)
                else
                    response.writeHead (503, box {| ``Content-Type`` = "text/plain" |})
                    response.``end`` (box "frontend build not found; build the Angular SPA first")))
        server.listen (port, fun () -> printfn "[x2g] http: Command Center on :%i" port)

// ============================================================================
// Boot sequence (Section 7 acceptance order: config → pipeline verification →
// discovery → watchers → channels).
// ============================================================================

module Boot =
    let private writeActiveSession (cfg: Config.GlobalRouting) =
        Paths.ensureDir Paths.claudeHome
        let tmp = Paths.activeSession + ".tmp"
        let session =
            box {| pid = Node.processPid ()
                   startedAt = Time.nowIso ()
                   httpPort = cfg.HttpPort
                   wsPort = cfg.WsPort
                   status = "running" |}
        Node.fs.writeFileSync (tmp, Json.stringifyPretty session, "utf8")
        Node.fs.renameSync (tmp, Paths.activeSession)

    let start () =
        printfn "[x2g] daemon booting (Fable/Node, repo root: %s)" Paths.repoRoot
        let cfg = Config.load ()
        PipelineLoader.loadAndVerify ()
        writeActiveSession cfg
        Discovery.run () |> ignore
        Node.setInterval (fun () -> Discovery.run () |> ignore) cfg.DiscoveryHeartbeatMs |> ignore
        VaultBoundary.watch cfg.ObsidianVaults
        XtmsIngest.start ()
        Ws.start cfg.WsPort (fun socket raw -> Router.handle cfg socket raw)
        StaticServer.start cfg.HttpPort
        Audit.append "DAEMON_BOOT" (box {| httpPort = cfg.HttpPort; wsPort = cfg.WsPort |})

Boot.start ()
