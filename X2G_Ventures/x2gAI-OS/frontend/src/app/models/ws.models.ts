/**
 * Wire contract for the daemon's WebSocket telemetry channel.
 * Envelope and payload shapes mirror Daemon.fs (Ws/Router/Discovery modules)
 * key-for-key; the daemon is the source of truth for these names.
 */

/** Section 1.4 menu node schema, emitted by the discovery pass. */
export interface MenuNode {
  id: string;
  label: string;
  modulePath: string;
  moduleType: 'domain' | 'webapp' | 'system';
  route: string;
  badgeCount: number;
}

export interface MenuSnapshotPayload {
  nodes: MenuNode[];
}

export interface MenuDeltaPayload {
  added: MenuNode[];
}

export interface TelemetryPayload {
  pipeline: string;
  state: string;
  [detail: string]: unknown;
}

export interface VaultEventPayload {
  vault: string;
  file: string;
}

export interface ScaffoldResultPayload {
  ok: boolean;
  node?: MenuNode;
  message?: string;
}

export interface DiscoveryResultPayload {
  moduleCount: number;
}

export interface InferenceResultPayload {
  agentId: string;
  provider: string;
  status: string;
  promptTokens: number;
  completionTokens: number;
  costUsd: number;
  response: string;
}

export interface ErrorPayload {
  message: string;
}

export interface ServerEnvelopeMap {
  'menu-snapshot': MenuSnapshotPayload;
  'menu-delta': MenuDeltaPayload;
  telemetry: TelemetryPayload;
  'vault-event': VaultEventPayload;
  'scaffold-result': ScaffoldResultPayload;
  'discovery-result': DiscoveryResultPayload;
  'inference-result': InferenceResultPayload;
  'terminal-result': { ok: boolean; message: string };
  error: ErrorPayload;
}

export type ServerMessageType = keyof ServerEnvelopeMap;

/** Discriminated union over message type so `switch (envelope.type)` narrows payload. */
export type ServerEnvelope = {
  [T in ServerMessageType]: { type: T; requestId?: string; payload: ServerEnvelopeMap[T] };
}[ServerMessageType];

export interface ScaffoldRequestPayload {
  moduleId: string;
  moduleLabel: string;
  templateType: string;
  moduleType?: string;
  parentPath?: string;
  overrides?: Record<string, unknown>;
}

export interface InferenceRequestPayload {
  agentId: string;
  project?: string;
  provider?: string;
  modelTarget?: string;
  estimatedTokens: number;
  monthlyBudgetUsd: number;
  prompt: string;
}

export interface ClientEnvelopeMap {
  'scaffold-request': ScaffoldRequestPayload;
  'discover-now': undefined;
  'inference-request': InferenceRequestPayload;
  'terminal-syscall': { command: string };
}

export type ClientMessageType = keyof ClientEnvelopeMap;

/** Reply type the daemon routes back for each request type. */
export const REPLY_TYPE = {
  'scaffold-request': 'scaffold-result',
  'discover-now': 'discovery-result',
  'inference-request': 'inference-result',
  'terminal-syscall': 'terminal-result',
} as const satisfies Record<ClientMessageType, ServerMessageType>;

export type ReplyPayload<T extends ClientMessageType> = ServerEnvelopeMap[(typeof REPLY_TYPE)[T]];
