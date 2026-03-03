import { Injectable, signal, NgZone, inject } from '@angular/core';

// Type declarations for jQuery SignalR 2.x (loaded via script tag in index.html)
declare const $: any;

export interface TrafficEvent {
  deviceName: string;
  operation: string;
  oid: string;
  value: string;
  sourceIp: string;
  timestamp: string;
}

export interface DeviceStatusEvent {
  deviceId: string;
  deviceName: string;
  status: string;
  timestamp: string;
}

export interface MibUpdateEvent {
  deviceId: string;
  values: Record<string, string>;
  timestamp: string;
}

export interface OidChangedEvent {
  deviceId: string;
  deviceName: string;
  oid: string;
  fieldName: string;   // resolved field name (e.g., "sysName", "temperature") — for IDD, same as oid
  newValue: string;
  previousValue: string;
  source: string;
  timestamp: string;
}

export interface DeviceInfo {
  id: string;
  name: string;
  ipAddress: string;
  port: number;
  isSimulating: boolean;
  simulatorPort: number;
}

export interface SetResult {
  success: boolean;
  message: string;
}

export interface BulkSetItem {
  oid: string;
  value: string;
  valueType: string;
}

export interface BulkSetResult {
  total: number;
  succeeded: number;
  failed: number;
  results: BulkSetItemResult[];
}

export interface BulkSetItemResult {
  oid: string;
  success: boolean;
  message: string;
}

@Injectable({ providedIn: 'root' })
export class SignalRService {
  private zone = inject(NgZone);
  private connection: any = null;
  private hubProxy: any = null;
  private reconnectTimer: any = null;
  private reconnectAttempts = 0;
  private stopped = false; // true when user explicitly called disconnect()

  // Connection state
  connectionState = signal<'disconnected' | 'connecting' | 'connected' | 'error'>('disconnected');

  // Fires (with timestamp) each time connection is restored after a drop
  reconnected = signal<number>(0, { equal: () => false });

  // Reconnect attempt counter — visible to UI for status display
  reconnectAttempt = signal(0);

  // Event signals — always notify (never skip), since these are event streams
  latestTraffic = signal<TrafficEvent | null>(null, { equal: () => false });
  latestDeviceStatus = signal<DeviceStatusEvent | null>(null, { equal: () => false });
  latestMibUpdate = signal<MibUpdateEvent | null>(null, { equal: () => false });
  latestOidChanged = signal<OidChangedEvent | null>(null, { equal: () => false });

  private serverUrl = 'http://localhost:5050';

  connect(url?: string): void {
    if (url) this.serverUrl = url;
    this.stopped = false;

    // Ensure jQuery + SignalR are loaded
    if (typeof $ === 'undefined' || !$.hubConnection) {
      console.warn('jQuery or SignalR JS client not loaded — skipping connection');
      this.connectionState.set('error');
      return;
    }

    // Tear down any previous connection completely
    this.cleanup();

    this.connectionState.set('connecting');

    // Run outside Angular zone to prevent change detection on every SignalR poll
    this.zone.runOutsideAngular(() => {
      this.connection = $.hubConnection(this.serverUrl);

      // Disable SignalR's own reconnect — we handle it ourselves with fresh connections
      this.connection.disconnectTimeout = 3000;

      this.hubProxy = this.connection.createHubProxy('snmpHub');

      // Register client-side handlers BEFORE starting
      this.hubProxy.on('onTrafficReceived', (data: TrafficEvent) => {
        this.zone.run(() => this.latestTraffic.set(data));
      });

      this.hubProxy.on('onDeviceStatusChanged', (data: DeviceStatusEvent) => {
        this.zone.run(() => this.latestDeviceStatus.set(data));
      });

      this.hubProxy.on('onMibUpdated', (data: MibUpdateEvent) => {
        this.zone.run(() => this.latestMibUpdate.set(data));
      });

      this.hubProxy.on('onOidChanged', (data: OidChangedEvent) => {
        this.zone.run(() => this.latestOidChanged.set(data));
      });

      // Start connection
      this.connection
        .start()
        .done(() => {
          this.zone.run(() => {
            const wasReconnect = this.reconnectAttempts > 0;
            this.reconnectAttempts = 0;
            this.reconnectAttempt.set(0);
            this.connectionState.set('connected');
            console.log('[SignalR] Connected to', this.serverUrl);
            if (wasReconnect) {
              console.log('[SignalR] Reconnected — triggering refresh');
              this.reconnected.set(Date.now());
            }
          });
        })
        .fail((err: any) => {
          this.zone.run(() => {
            this.connectionState.set('error');
            console.warn('[SignalR] Connection failed:', err?.message || err);
            this.scheduleReconnect();
          });
        });

      // When connection drops, schedule a fresh reconnect (not SignalR's internal one)
      this.connection.disconnected(() => {
        this.zone.run(() => {
          if (this.stopped) return; // User called disconnect(), don't reconnect
          this.connectionState.set('disconnected');
          console.log('[SignalR] Disconnected');
          this.scheduleReconnect();
        });
      });
    });
  }

  disconnect(): void {
    this.stopped = true;
    clearTimeout(this.reconnectTimer);
    this.reconnectTimer = null;
    this.reconnectAttempts = 0;
    this.cleanup();
    this.connectionState.set('disconnected');
  }

  /** Tear down existing connection without triggering reconnect */
  private cleanup(): void {
    clearTimeout(this.reconnectTimer);
    this.reconnectTimer = null;
    if (this.connection) {
      // Temporarily set stopped=true to prevent disconnected handler from firing reconnect
      const wasStopped = this.stopped;
      this.stopped = true;
      try { this.connection.stop(); } catch (_) {}
      this.stopped = wasStopped;
      this.connection = null;
      this.hubProxy = null;
    }
  }

  private scheduleReconnect(): void {
    if (this.stopped) return;
    if (this.reconnectTimer) return; // Already scheduled

    // Exponential backoff: 3s, 6s, 12s, 24s, max 30s
    const delay = Math.min(3000 * Math.pow(2, this.reconnectAttempts), 30000);
    this.reconnectAttempts++;

    this.reconnectAttempt.set(this.reconnectAttempts);
    console.log(`[SignalR] Reconnect in ${delay / 1000}s (attempt ${this.reconnectAttempts})`);

    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      if (!this.stopped && this.connectionState() !== 'connected') {
        this.connect();
      }
    }, delay);
  }

  // ── Server method invocations ──

  async sendSet(deviceId: string, oid: string, value: string, valueType: string): Promise<SetResult> {
    this.ensureConnected();
    return this.hubProxy.invoke('SendSet', deviceId, oid, value, valueType);
  }

  async sendBulkSet(deviceId: string, items: BulkSetItem[]): Promise<BulkSetResult> {
    this.ensureConnected();
    return this.hubProxy.invoke('SendBulkSet', deviceId, items);
  }

  async sendIddSet(deviceId: string, fieldId: string, value: string): Promise<SetResult> {
    this.ensureConnected();
    return this.hubProxy.invoke('SendIddSet', deviceId, fieldId, value);
  }

  async requestRefresh(deviceId: string): Promise<Record<string, string>> {
    this.ensureConnected();
    return this.hubProxy.invoke('RequestRefresh', deviceId);
  }

  async requestSchema(deviceId: string): Promise<any> {
    this.ensureConnected();
    console.log('[SignalR] Invoking RequestSchema for:', deviceId);
    const result = await this.hubProxy.invoke('RequestSchema', deviceId);
    console.log('[SignalR] RequestSchema result:', result);
    return result;
  }

  async getDevices(): Promise<DeviceInfo[]> {
    this.ensureConnected();
    return this.hubProxy.invoke('GetDevices');
  }

  private ensureConnected(): void {
    if (!this.hubProxy || this.connectionState() !== 'connected') {
      throw new Error('Not connected to WPF server');
    }
  }
}
