namespace X2G.AgenticOS.Domain

open System.Text.RegularExpressions

// ============================================================================
// SECTION 4.1 — Deterministic agent state.
// Compiler-checked DUs make illegal states unrepresentable; transitions are
// legal only when declared in the pipeline's transition set.
// ============================================================================

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

// ============================================================================
// Generic strongly-typed state graph.
// Mirrors the *.pipeline.json declarations; the JSON is the runtime source of
// truth, these graphs are the compile-time contract the daemon validates
// against on load. An undeclared transition is a rejection, never a no-op.
// ============================================================================

type StateGraph<'S when 'S: equality> =
    { EntryState: 'S
      TerminalStates: 'S list
      Transitions: ('S * 'S * string) list }

[<RequireQualifiedAccess>]
type TransitionResult<'S> =
    | Transitioned of state: 'S * guard: string
    | Rejected of reason: string

module StateGraph =
    let tryTransition (graph: StateGraph<'S>) (current: 'S) (target: 'S) : TransitionResult<'S> =
        match graph.Transitions |> List.tryFind (fun (from, dest, _) -> from = current && dest = target) with
        | Some (_, _, guard) -> TransitionResult.Transitioned(target, guard)
        | None ->
            TransitionResult.Rejected(
                sprintf "Undeclared transition %A -> %A: agent must be set to Aborted and an audit record appended" current target
            )

    let isTerminal (graph: StateGraph<'S>) (state: 'S) =
        graph.TerminalStates |> List.contains state

// ============================================================================
// SECTION 3.1 OVERRIDE — X-TMS logistics schema integrity.
// Canonical terminology is 'freight' and 'ratecon' across every data-binding
// attribute, JSON field name, and variable definition. The generic synonyms
// 'load', 'rate_confirmation', and 'carrier_doc' silently fork the schema
// across integrations, so they are rejected at validation time.
// ============================================================================

[<RequireQualifiedAccess>]
type XtmsState =
    | IngestAlert
    | ParseRatecon
    | ValidateFreight
    | DispatchSms
    | Archive

// JSON mapping records: field names ARE the wire field names. Fable compiles
// records structurally, so these bind 1:1 to the pipeline schema keys and to
// the DevExtreme dxDataGrid dataFields on the frontend.

type FreightAlertJson =
    { freight_id: string
      freight_source: string
      freight_received_at: string
      ratecon_document_path: string }

type RateconJson =
    { ratecon_id: string
      freight_id: string
      ratecon_origin: string
      ratecon_destination: string
      ratecon_rate_usd: decimal
      freight_weight_lbs: decimal
      ratecon_pickup_window: string
      ratecon_delivery_window: string
      ratecon_carrier_mc: string }

type FreightTicketJson =
    { ticket_id: string
      freight_id: string
      ratecon: RateconJson
      pipeline_state: string
      sms_dispatched: bool
      audit_ref: string }

module XtmsSchemaIntegrity =
    let RequiredTerms = [ "freight"; "ratecon" ]

    // 'load' is banned as a whole token (not substring, so 'payload' passes);
    // the multi-word synonyms are banned as phrases.
    let private bannedTokens = [ "load"; "loads" ]
    let private bannedPhrases = [ "rate_confirmation"; "carrier_doc" ]

    let private fieldTokens (fieldName: string) =
        fieldName.Split([| '_'; '-'; '.' |])
        |> Array.map (fun token -> token.ToLowerInvariant())

    let validateFieldName (fieldName: string) : Result<string, string> =
        let lower = fieldName.ToLowerInvariant()
        let phraseHit = bannedPhrases |> List.exists lower.Contains
        let tokenHit =
            let tokens = fieldTokens fieldName
            bannedTokens |> List.exists (fun banned -> Array.contains banned tokens)

        if phraseHit || tokenHit then
            Error(
                sprintf
                    "X-TMS schema violation: field '%s' uses forbidden generic terminology; canonical terms are 'freight' and 'ratecon'"
                    fieldName
            )
        else
            Ok fieldName

    /// Validates every key of an inbound/outbound JSON document before it is
    /// bound to the pipeline. Any violation fails the whole document loudly.
    let validateDocumentFields (fieldNames: string seq) : Result<unit, string list> =
        let errors =
            fieldNames
            |> Seq.choose (fun field ->
                match validateFieldName field with
                | Error message -> Some message
                | Ok _ -> None)
            |> List.ofSeq

        if List.isEmpty errors then Ok() else Error errors

// ============================================================================
// SECTION 3.2 OVERRIDE — TrialLens Medical Record Number capture.
// MRNs are opaque external identifiers: they carry leading zeros, alpha
// prefixes, and separators. Casting to int/int32/number truncates leading
// zeros and drops alpha characters, corrupting patient linkage — so numeric
// coercion is a hard fault, not a conversion.
// ============================================================================

[<RequireQualifiedAccess>]
type TrialLensState =
    | IntakeDataset
    | NormalizeRecords
    | MapProtocol
    | IndexMemory
    | Report

/// Single-case DU with a private constructor: the only way to obtain a value
/// is through MedicalRecordNumber.create, which enforces the string guard.
type MedicalRecordNumber = private Mrn of string

module MedicalRecordNumber =
    [<Literal>]
    let ValidationPattern = "^[A-Za-z0-9._-]{1,64}$"

    /// Takes obj deliberately: a mis-declared upstream feed can deliver the
    /// MRN field as a JS number after JSON.parse. Anything that is not
    /// already a string is rejected outright — never coerced or stringified,
    /// because the damage (dropped leading zeros) happened at parse time.
    let create (raw: obj) : Result<MedicalRecordNumber, string> =
        match raw with
        | :? string as candidate when Regex.IsMatch(candidate, ValidationPattern) -> Ok(Mrn candidate)
        | :? string ->
            Error "MRN rejected: contains characters outside [A-Za-z0-9._-] or exceeds 64 characters"
        | _ ->
            Error "MRN rejected: value is not a string; numeric coercion of MRNs is forbidden (Section 3.2 override)"

    let value (Mrn mrn) = mrn

    /// Raw identifiers must never reach logs (spec 3.2); this is the only
    /// representation permitted in log lines and error payloads.
    let redacted (Mrn mrn) =
        if mrn.Length <= 4 then "****"
        else sprintf "****%s" (mrn.Substring(mrn.Length - 4))

type ClinicalRecordJson =
    { mrn: string
      dataset_id: string
      protocol_id: string
      record_hash: string
      intake_timestamp: string
      pipeline_state: string }

// ============================================================================
// SECTION 3.3 — Digital Pathology WSI pipeline. The daemon operates on
// metadata only; WSI binaries are referenced by path, never loaded.
// ============================================================================

[<RequireQualifiedAccess>]
type PathologyState =
    | RegisterWsi
    | ExtractMetadata
    | QueueAnalysis
    | RunJob
    | PublishResult

// ============================================================================
// SECTION 3.4 — ML Projects run monitoring pipeline.
// ============================================================================

[<RequireQualifiedAccess>]
type MlRunState =
    | RegisterRun
    | Monitor
    | CollectMetrics
    | EmitCharts

// ============================================================================
// SECTION 3.5 — Web Applications scaffold-on-demand (supporting pipeline).
// A perpetual loop, so it declares no terminal states.
// ============================================================================

[<RequireQualifiedAccess>]
type WebAppsState =
    | Monitor
    | ScaffoldModule
    | RegisterModule

// ============================================================================
// Declared pipeline graphs — the compile-time mirror of the JSON definitions
// in X2G_infrastructure/config/pipelines/. Guard names must match the JSON
// exactly; the daemon cross-checks both on boot and fails loudly on drift.
// ============================================================================

module PipelineGraphs =
    let xtms: StateGraph<XtmsState> =
        { EntryState = XtmsState.IngestAlert
          TerminalStates = [ XtmsState.Archive ]
          Transitions =
            [ XtmsState.IngestAlert, XtmsState.ParseRatecon, "alert_ticket_created"
              XtmsState.ParseRatecon, XtmsState.ValidateFreight, "ratecon_fields_extracted"
              XtmsState.ValidateFreight, XtmsState.DispatchSms, "freight_validated"
              XtmsState.ValidateFreight, XtmsState.Archive, "freight_rejected"
              XtmsState.DispatchSms, XtmsState.Archive, "sms_dispatch_audited" ] }

    let trialLens: StateGraph<TrialLensState> =
        { EntryState = TrialLensState.IntakeDataset
          TerminalStates = [ TrialLensState.Report ]
          Transitions =
            [ TrialLensState.IntakeDataset, TrialLensState.NormalizeRecords, "dataset_manifest_valid"
              TrialLensState.NormalizeRecords, TrialLensState.MapProtocol, "all_mrns_string_validated"
              TrialLensState.MapProtocol, TrialLensState.IndexMemory, "protocol_rules_applied"
              TrialLensState.IndexMemory, TrialLensState.Report, "summaries_written_to_x2g_vault" ] }

    // 3.3: RUN_JOB is only re-enterable through the declared requeue transition —
    // the "explicit transition event" the spec demands, made structural.
    let pathology: StateGraph<PathologyState> =
        { EntryState = PathologyState.RegisterWsi
          TerminalStates = [ PathologyState.PublishResult ]
          Transitions =
            [ PathologyState.RegisterWsi, PathologyState.ExtractMetadata, "wsi_reference_registered"
              PathologyState.ExtractMetadata, PathologyState.QueueAnalysis, "metadata_extracted"
              PathologyState.QueueAnalysis, PathologyState.RunJob, "job_dispatch_event"
              PathologyState.RunJob, PathologyState.PublishResult, "job_completed"
              PathologyState.RunJob, PathologyState.QueueAnalysis, "job_failed_requeue_event" ] }

    let mlProjects: StateGraph<MlRunState> =
        { EntryState = MlRunState.RegisterRun
          TerminalStates = [ MlRunState.EmitCharts ]
          Transitions =
            [ MlRunState.RegisterRun, MlRunState.Monitor, "run_registered_in_models_registry"
              MlRunState.Monitor, MlRunState.CollectMetrics, "run_completed"
              MlRunState.CollectMetrics, MlRunState.EmitCharts, "metrics_aggregated" ] }

    let webApps: StateGraph<WebAppsState> =
        { EntryState = WebAppsState.Monitor
          TerminalStates = []
          Transitions =
            [ WebAppsState.Monitor, WebAppsState.ScaffoldModule, "scaffold_request_validated"
              WebAppsState.ScaffoldModule, WebAppsState.RegisterModule, "module_created"
              WebAppsState.RegisterModule, WebAppsState.Monitor, "menu_delta_emitted" ] }
