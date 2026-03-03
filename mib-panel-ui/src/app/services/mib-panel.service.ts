import { Injectable, signal, computed, inject, effect, untracked, DestroyRef } from '@angular/core';
import { MibPanelSchema, SetFeedback } from '../models/mib-schema';
import { SignalRService, OidChangedEvent } from './signalr.service';

/** Callback for name-based field watches. Receives the full change event. */
export type FieldWatchCallback = (event: OidChangedEvent) => void;

@Injectable({ providedIn: 'root' })
export class MibPanelService {
  private signalR = inject(SignalRService);
  private destroyRef = inject(DestroyRef);

  schema = signal<MibPanelSchema | null>(null);
  feedbacks = signal<SetFeedback[]>([]);
  isLoading = signal(false);
  currentDeviceId = signal<string | null>(null);

  /** True when SignalR is connected — use to enable/disable SET controls */
  isConnected = computed(() => this.signalR.connectionState() === 'connected');

  private feedbackId = 0;

  // OID change event — exposed for components to react to specific OID changes
  latestOidChanged = signal<OidChangedEvent | null>(null);

  // Name-based watch registry: fieldName (lowercase) → callbacks
  private nameWatches = new Map<string, FieldWatchCallback[]>();
  // Bidirectional name ↔ OID map (built from loaded schema)
  private nameToOid = new Map<string, string>();
  private oidToName = new Map<string, string>();

  constructor() {
    // Auto-update panel values when traffic events arrive with values
    // Only track latestTraffic — read schema with untracked to avoid loop
    // (setting schema would retrigger this effect otherwise)
    effect(() => {
      const traffic = this.signalR.latestTraffic();
      if (!traffic || !traffic.value) return;

      console.log('[MibPanel] Traffic update:', traffic.operation, traffic.oid, '=', traffic.value);

      untracked(() => {
        const current = this.schema();
        if (!current) return;

        // Ignore traffic from other devices
        if (traffic.deviceName && current.deviceName &&
            traffic.deviceName !== current.deviceName) return;

        const oid = traffic.oid;
        let updated = false;

        for (const module of current.modules) {
          // Match scalars
          for (const field of module.scalars) {
            const fieldOid = field.oid.endsWith('.0') ? field.oid : field.oid + '.0';
            if (fieldOid === oid || field.oid === oid) {
              field.currentValue = traffic.value;
              updated = true;
            }
          }

          // Match table cells: OID = column.oid + "." + row.index
          // Row values are keyed by column OID (not name)
          for (const table of module.tables) {
            for (const col of table.columns) {
              if (oid.startsWith(col.oid + '.')) {
                const rowIndex = oid.substring(col.oid.length + 1);
                const row = table.rows.find(r => r.index === rowIndex);
                if (row && row.values[col.oid]) {
                  row.values[col.oid].value = traffic.value;
                  updated = true;
                }
              }
            }
          }
        }

        if (updated) {
          this.emitSchema(current);
        }
      });
    });

    // Batch update — fired by BroadcastMibUpdate (e.g. after periodic WALK)
    effect(() => {
      const update = this.signalR.latestMibUpdate();
      if (!update || !update.values) return;

      console.log(`[MibPanel] MIB batch update: ${Object.keys(update.values).length} OIDs (device: ${update.deviceId})`);

      untracked(() => {
        const current = this.schema();
        if (!current) return;

        const values = update.values;
        let updated = false;

        for (const module of current.modules) {
          for (const field of module.scalars) {
            const fieldOid = field.oid.endsWith('.0') ? field.oid : field.oid + '.0';
            if (values[fieldOid] !== undefined) {
              field.currentValue = values[fieldOid];
              updated = true;
            } else if (values[field.oid] !== undefined) {
              field.currentValue = values[field.oid];
              updated = true;
            }
          }

          for (const table of module.tables) {
            for (const col of table.columns) {
              for (const row of table.rows) {
                const cellOid = col.oid + '.' + row.index;
                if (values[cellOid] !== undefined && row.values[col.oid]) {
                  row.values[col.oid].value = values[cellOid];
                  updated = true;
                }
              }
            }
          }
        }

        if (updated) {
          this.emitSchema(current);
        }
      });
    });

    // Forward OID change events + dispatch name-based watches
    effect(() => {
      const change = this.signalR.latestOidChanged();
      if (!change) return;

      console.log(`[MibPanel] OID changed: ${change.fieldName || change.oid} '${change.previousValue}' → '${change.newValue}' (device: ${change.deviceName})`);
      this.latestOidChanged.set(change);

      // Dispatch name-based watches
      untracked(() => this.dispatchNameWatches(change));
    });

    // Auto-refresh values when connection is restored after a drop
    effect(() => {
      const ts = this.signalR.reconnected();
      if (!ts) return;

      untracked(() => {
        const deviceId = this.currentDeviceId();
        if (deviceId && this.schema()) {
          console.log('[MibPanel] Connection restored — auto-refreshing values');
          this.refreshValues();
        }
      });
    });
  }

  async loadFromFile(file: File): Promise<void> {
    this.isLoading.set(true);
    this.currentDeviceId.set(null);
    try {
      const text = await file.text();
      const data = JSON.parse(text) as MibPanelSchema;
      this.buildNameMap(data);
      this.schema.set(data);
    } finally {
      this.isLoading.set(false);
    }
  }

  loadFromJson(json: MibPanelSchema): void {
    this.currentDeviceId.set(null);
    this.buildNameMap(json);
    this.schema.set(json);
  }

  /** Load schema from SignalR for a specific device */
  async loadFromDevice(deviceId: string): Promise<void> {
    console.log('[MibPanel] loadFromDevice called with:', deviceId);
    this.isLoading.set(true);
    this.currentDeviceId.set(deviceId);
    try {
      const schema = await this.signalR.requestSchema(deviceId);
      console.log('[MibPanel] requestSchema returned:', schema);
      if (schema) {
        this.buildNameMap(schema as MibPanelSchema);
        this.schema.set(schema as MibPanelSchema);
        // Auto-refresh to get live values from the running simulator
        await this.refreshValues();
      } else {
        console.warn('[MibPanel] Schema was null/undefined');
      }
    } catch (err) {
      console.error('[MibPanel] loadFromDevice error:', err);
    } finally {
      this.isLoading.set(false);
    }
  }

  /** Refresh all values (scalars + table cells) from a live simulator via SignalR */
  async refreshValues(): Promise<void> {
    const deviceId = this.currentDeviceId();
    if (!deviceId || this.signalR.connectionState() !== 'connected') return;

    this.isLoading.set(true);
    try {
      const values = await this.signalR.requestRefresh(deviceId);
      const current = this.schema();
      if (current && values) {
        for (const module of current.modules) {
          // Update scalars
          for (const field of module.scalars) {
            const oid = field.oid.endsWith('.0') ? field.oid : field.oid + '.0';
            if (values[oid] !== undefined) {
              field.currentValue = values[oid];
            }
          }
          // Update table cells
          for (const table of module.tables) {
            for (const row of table.rows) {
              for (const col of table.columns) {
                const cellOid = col.oid + '.' + row.index;
                if (values[cellOid] !== undefined && row.values[col.oid]) {
                  row.values[col.oid].value = values[cellOid];
                }
              }
            }
          }
        }
        this.emitSchema(current);
      }
    } finally {
      this.isLoading.set(false);
    }
  }

  /** Send an SNMP/IDD SET — real via SignalR if connected, mock fallback otherwise */
  sendSet(oid: string, name: string, value: string, valueType: string): void {
    const feedback: SetFeedback = {
      id: ++this.feedbackId,
      oid,
      name,
      value,
      valueType,
      timestamp: new Date(),
      status: 'pending',
    };

    this.feedbacks.update(list => [feedback, ...list]);

    const deviceId = this.currentDeviceId();
    const isConnected = this.signalR.connectionState() === 'connected';
    const isIdd = oid.length > 0 && !/^\d/.test(oid);

    if (isConnected && (deviceId || isIdd)) {
      // Real SignalR call — IDD uses field ID even without a deviceId
      const effectiveDeviceId = deviceId || 'idd-local';
      const setPromise = isIdd
        ? this.signalR.sendIddSet(effectiveDeviceId, oid, value)
        : this.signalR.sendSet(effectiveDeviceId, oid, value, valueType);

      setPromise
        .then(result => {
          if (result.success) this.updateFieldValue(oid, value);
          this.feedbacks.update(list =>
            list.map(f =>
              f.id === feedback.id
                ? { ...f, status: (result.success ? 'success' : 'error') as any, message: result.message }
                : f
            )
          );
        })
        .catch(err => {
          this.feedbacks.update(list =>
            list.map(f =>
              f.id === feedback.id
                ? { ...f, status: 'error' as const, message: err?.message || 'SET failed' }
                : f
            )
          );
        });
    } else {
      // Not connected — report error (no simulation in production)
      this.feedbacks.update(list =>
        list.map(f =>
          f.id === feedback.id
            ? { ...f, status: 'error' as const, message: 'Not connected to server' }
            : f
        )
      );
    }

    // Auto-remove after 8 seconds
    setTimeout(() => {
      this.feedbacks.update(list => list.filter(f => f.id !== feedback.id));
    }, 8000);
  }

  /** Update a field value in the schema after a successful SET */
  private updateFieldValue(oid: string, value: string): void {
    const current = this.schema();
    if (!current) return;

    let updated = false;
    for (const module of current.modules) {
      for (const field of module.scalars) {
        const fieldOid = field.oid.endsWith('.0') ? field.oid : field.oid + '.0';
        if (fieldOid === oid || field.oid === oid) {
          field.currentValue = value;
          updated = true;
        }
      }
    }
    if (updated) {
      this.emitSchema(current);
    }
  }

  /** Create a new schema object with new references at all levels so Angular detects changes */
  private emitSchema(s: MibPanelSchema): void {
    this.schema.set({
      ...s,
      modules: s.modules.map(m => ({
        ...m,
        scalars: m.scalars.map(f => ({ ...f })),
        tables: m.tables.map(t => ({
          ...t,
          columns: t.columns.map(c => ({ ...c })),
          rows: t.rows.map(r => ({ ...r, values: { ...r.values } }))
        }))
      }))
    });
  }

  // ── Name-based Watch API ──

  /**
   * Register a callback that fires when a specific field changes, by name.
   * Works for both SNMP fields (e.g., "sysName") and IDD fields (e.g., "temperature").
   * Case-insensitive. Returns an unsubscribe function.
   *
   * Usage:
   *   const unsub = panelService.watchByName('temperature', (event) => {
   *     if (parseInt(event.newValue) > 80) console.warn('Overheating!');
   *   });
   *   // later: unsub();
   */
  watchByName(fieldName: string, callback: FieldWatchCallback): () => void {
    const key = fieldName.toLowerCase();
    const list = this.nameWatches.get(key) || [];
    list.push(callback);
    this.nameWatches.set(key, list);

    return () => {
      const current = this.nameWatches.get(key);
      if (current) {
        const idx = current.indexOf(callback);
        if (idx >= 0) current.splice(idx, 1);
        if (current.length === 0) this.nameWatches.delete(key);
      }
    };
  }

  /** Remove all watches for a field name. */
  unwatchByName(fieldName: string): void {
    this.nameWatches.delete(fieldName.toLowerCase());
  }

  /** Dispatch name-based watches for a change event. */
  private dispatchNameWatches(event: OidChangedEvent): void {
    const serverName = event.fieldName?.toLowerCase();
    if (serverName) {
      const cbs = this.nameWatches.get(serverName);
      if (cbs) cbs.forEach(cb => this.safeCallback(cb, event));
    }

    const localName = this.oidToName.get(event.oid)?.toLowerCase();
    if (localName && localName !== serverName) {
      const cbs = this.nameWatches.get(localName);
      if (cbs) cbs.forEach(cb => this.safeCallback(cb, event));
    }

    const oidAsName = event.oid.toLowerCase();
    if (oidAsName !== serverName && oidAsName !== localName) {
      const cbs = this.nameWatches.get(oidAsName);
      if (cbs) cbs.forEach(cb => this.safeCallback(cb, event));
    }
  }

  private safeCallback(cb: FieldWatchCallback, event: OidChangedEvent): void {
    try { cb(event); }
    catch (err) { console.error('[MibPanel] Watch callback error:', err); }
  }

  /** Build name ↔ OID maps from current schema. Called after schema load. */
  private buildNameMap(schema: MibPanelSchema): void {
    this.nameToOid.clear();
    this.oidToName.clear();
    for (const mod of schema.modules) {
      for (const field of mod.scalars) {
        if (field.name && field.oid) {
          this.nameToOid.set(field.name.toLowerCase(), field.oid);
          this.oidToName.set(field.oid, field.name);
        }
      }
      for (const table of mod.tables) {
        for (const col of table.columns) {
          if (col.name && col.oid) {
            this.nameToOid.set(col.name.toLowerCase(), col.oid);
            this.oidToName.set(col.oid, col.name);
          }
        }
      }
    }
    console.log(`[MibPanel] Built name map: ${this.nameToOid.size} fields`);
  }

  /** Map inputType to SNMP value type for SET */
  resolveValueType(inputType: string, baseType: string): string {
    switch (inputType) {
      case 'number':
      case 'enum':
      case 'toggle':
        return 'Integer32';
      case 'counter':
        return baseType.includes('64') ? 'Counter64' : 'Counter32';
      case 'gauge':
        return 'Gauge32';
      case 'timeticks':
        return 'TimeTicks';
      case 'ip':
        return 'IpAddress';
      case 'oid':
        return 'ObjectIdentifier';
      default:
        return 'OctetString';
    }
  }
}
