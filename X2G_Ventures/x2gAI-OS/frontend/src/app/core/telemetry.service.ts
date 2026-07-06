import { Injectable, InjectionToken, computed, inject, signal } from '@angular/core';
import {
  ClientEnvelopeMap,
  ClientMessageType,
  MenuNode,
  REPLY_TYPE,
  ReplyPayload,
  ServerEnvelope,
  TelemetryPayload,
  VaultEventPayload,
} from '../models/ws.models';

/**
 * The daemon serves the SPA on httpPort and telemetry on wsPort (4287 in
 * global_routing.json), so the WS host is the page's host on a different port.
 */
export const X2G_WS_URL = new InjectionToken<string>('X2G_WS_URL', {
  providedIn: 'root',
  factory: () => `ws://${location.hostname}:4287`,
});

export type ConnectionState = 'connecting' | 'open' | 'closed';

interface PendingRequest {
  expectedType: string;
  resolve: (payload: unknown) => void;
  reject: (reason: Error) => void;
  timeoutHandle: ReturnType<typeof setTimeout>;
}

const REQUEST_TIMEOUT_MS = 15_000;
const MAX_BACKOFF_MS = 15_000;

@Injectable({ providedIn: 'root' })
export class TelemetryService {
  private readonly wsUrl = inject(X2G_WS_URL);

  private socket: WebSocket | null = null;
  private reconnectAttempt = 0;
  private requestCounter = 0;
  private readonly pending = new Map<string, PendingRequest>();

  private readonly connectionStateSignal = signal<ConnectionState>('closed');
  private readonly menuNodesSignal = signal<MenuNode[]>([]);
  private readonly lastTelemetrySignal = signal<TelemetryPayload | null>(null);
  private readonly lastVaultEventSignal = signal<VaultEventPayload | null>(null);

  readonly connectionState = this.connectionStateSignal.asReadonly();
  readonly menuNodes = this.menuNodesSignal.asReadonly();
  readonly lastTelemetry = this.lastTelemetrySignal.asReadonly();
  readonly lastVaultEvent = this.lastVaultEventSignal.asReadonly();
  readonly isConnected = computed(() => this.connectionStateSignal() === 'open');

  constructor() {
    this.connect();
  }

  /** Sends a request envelope and resolves with the daemon's correlated reply. */
  request<T extends ClientMessageType>(type: T, payload: ClientEnvelopeMap[T]): Promise<ReplyPayload<T>> {
    const socket = this.socket;
    if (!socket || socket.readyState !== WebSocket.OPEN) {
      return Promise.reject(new Error('telemetry channel is not connected'));
    }
    const requestId = `req-${Date.now()}-${++this.requestCounter}`;
    return new Promise<ReplyPayload<T>>((resolve, reject) => {
      const timeoutHandle = setTimeout(() => {
        this.pending.delete(requestId);
        reject(new Error(`request '${type}' timed out after ${REQUEST_TIMEOUT_MS}ms`));
      }, REQUEST_TIMEOUT_MS);
      this.pending.set(requestId, {
        expectedType: REPLY_TYPE[type],
        resolve: (value) => resolve(value as ReplyPayload<T>),
        reject,
        timeoutHandle,
      });
      socket.send(JSON.stringify({ type, requestId, payload }));
    });
  }

  private connect(): void {
    this.connectionStateSignal.set('connecting');
    const socket = new WebSocket(this.wsUrl);
    this.socket = socket;

    socket.onopen = () => {
      this.reconnectAttempt = 0;
      this.connectionStateSignal.set('open');
    };

    socket.onmessage = (event: MessageEvent<string>) => {
      let envelope: ServerEnvelope;
      try {
        envelope = JSON.parse(event.data) as ServerEnvelope;
      } catch {
        return;
      }
      this.dispatch(envelope);
    };

    socket.onclose = () => {
      this.connectionStateSignal.set('closed');
      this.rejectAllPending(new Error('telemetry channel closed'));
      this.scheduleReconnect();
    };

    socket.onerror = () => socket.close();
  }

  private dispatch(envelope: ServerEnvelope): void {
    if (envelope.requestId && this.settleRequest(envelope)) {
      return;
    }
    switch (envelope.type) {
      case 'menu-snapshot':
        this.menuNodesSignal.set(envelope.payload.nodes);
        break;
      case 'menu-delta': {
        const added = envelope.payload.added;
        this.menuNodesSignal.update((nodes) => {
          const known = new Set(nodes.map((node) => node.id));
          return [...nodes, ...added.filter((node) => !known.has(node.id))];
        });
        break;
      }
      case 'telemetry':
        this.lastTelemetrySignal.set(envelope.payload);
        break;
      case 'vault-event':
        this.lastVaultEventSignal.set(envelope.payload);
        break;
      default:
        break;
    }
  }

  private settleRequest(envelope: ServerEnvelope): boolean {
    const entry = this.pending.get(envelope.requestId!);
    if (!entry) {
      return false;
    }
    this.pending.delete(envelope.requestId!);
    clearTimeout(entry.timeoutHandle);
    if (envelope.type === 'error') {
      entry.reject(new Error(envelope.payload.message));
    } else if (envelope.type === entry.expectedType) {
      entry.resolve(envelope.payload);
    } else {
      entry.reject(new Error(`unexpected reply type '${envelope.type}'`));
    }
    return true;
  }

  private rejectAllPending(reason: Error): void {
    for (const entry of this.pending.values()) {
      clearTimeout(entry.timeoutHandle);
      entry.reject(reason);
    }
    this.pending.clear();
  }

  private scheduleReconnect(): void {
    const backoffMs = Math.min(1000 * 2 ** this.reconnectAttempt, MAX_BACKOFF_MS);
    this.reconnectAttempt += 1;
    setTimeout(() => this.connect(), backoffMs);
  }
}
