import { Component, inject, computed, signal, effect } from '@angular/core';
import { MibPanelService } from './services/mib-panel.service';
import { FieldClassifierService } from './services/field-classifier.service';
import { SignalRService, DeviceInfo } from './services/signalr.service';
import { SidePanelComponent } from './components/side-panel/side-panel.component';
import { ModuleSectionComponent } from './components/module-section/module-section.component';
import { SetFeedbackComponent } from './components/set-feedback/set-feedback.component';
import { SystemInfoComponent } from './components/system-info/system-info.component';
import { BulkSetComponent } from './components/bulk-set/bulk-set.component';

@Component({
  selector: 'app-root',
  imports: [SidePanelComponent, ModuleSectionComponent, SetFeedbackComponent, SystemInfoComponent, BulkSetComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private panelService = inject(MibPanelService);
  private classifier = inject(FieldClassifierService);
  private signalR = inject(SignalRService);

  schema = this.panelService.schema;
  identity = computed(() => {
    const s = this.schema();
    return s ? this.classifier.extractIdentity(s) : null;
  });

  systemInfo = computed(() => {
    const s = this.schema();
    return s ? this.classifier.extractSystemInfo(s) : [];
  });

  /** SignalR connection state */
  signalRState = computed(() => this.signalR.connectionState());

  /** True when not connected — shows banner */
  isDisconnected = computed(() => {
    const s = this.signalR.connectionState();
    return s === 'disconnected' || s === 'error';
  });

  /** Reconnect attempt count for banner display */
  reconnectAttempt = computed(() => this.signalR.reconnectAttempt());

  /** Quick stats for panel header */
  panelStats = computed(() => {
    const s = this.schema();
    if (!s) return { fields: 0, tables: 0, modules: 0 };
    let tables = 0;
    for (const m of s.modules) tables += m.tables.length;
    return { fields: s.totalFields, tables, modules: s.modules.length };
  });

  /** Aggregate health summary across all modules */
  healthSummary = computed(() => {
    const s = this.schema();
    if (!s) return { ok: 0, warning: 0, alarm: 0, info: 0 };
    let ok = 0, warning = 0, alarm = 0, info = 0;
    for (const module of s.modules) {
      const classified = this.classifier.classifyScalars(module.scalars);
      for (const field of classified.status) {
        if (!field.options?.length || !field.currentValue) { info++; continue; }
        const num = parseInt(field.currentValue, 10);
        const label = (field.options.find((o: any) => o.value === num)?.label || '').toLowerCase();
        if (/up|active|ok|normal|enabled|true|running|online|ready/.test(label)) ok++;
        else if (/warning|degraded|standby|testing|suspended/.test(label)) warning++;
        else if (/down|error|fail|critical|disabled|false|offline|alarm|fault/.test(label)) alarm++;
        else info++;
      }
      info += classified.counters.length;
    }
    return { ok, warning, alarm, info };
  });

  /** Module tabs for navigation */
  moduleTabs = computed(() => {
    const s = this.schema();
    if (!s) return [];
    return s.modules.map((m, i) => ({
      name: m.moduleName,
      count: m.scalarCount + m.tableCount,
      index: i,
    }));
  });

  activeModuleIndex = 0;
  isPanelOpen = false;
  isBulkSetOpen = false;

  /** Currently selected device ID (for card highlight) */
  selectedDeviceId = signal<string | null>(null);

  /** Available devices fetched from WPF */
  devices = signal<DeviceInfo[]>([]);
  isLoadingDevices = signal(false);

  constructor() {
    // Auto-connect to WPF SignalR server
    this.signalR.connect();

    // Fetch device list when connected
    effect(() => {
      const state = this.signalR.connectionState();
      if (state === 'connected') {
        this.fetchDevices();
      }
    });

    // ── Watch by Name examples ──
    // Register automation callbacks that fire when specific fields change.
    // These work for both SNMP fields (e.g., "sysName") and IDD fields (e.g., "temperature").

    // Example 1: Log when sysName changes
    this.panelService.watchByName('sysName', (event) => {
      console.log(`[Automation] sysName changed: '${event.previousValue}' → '${event.newValue}'`);
    });

    // Example 2: Alert when temperature exceeds threshold → auto-SET alarm
    this.panelService.watchByName('temperature', (event) => {
      const temp = parseInt(event.newValue, 10);
      if (temp > 80) {
        console.warn(`[Automation] Temperature ${temp}°C exceeds threshold — sending alarm SET`);
        this.signalR.sendIddSet(event.deviceId, 'alarm-indicator', 'ON')
          .then(r => console.log('[Automation] Alarm SET result:', r.message))
          .catch(err => console.error('[Automation] Alarm SET failed:', err));
      }
    });

    // Example 3: Watch interface status changes
    this.panelService.watchByName('ifOperStatus', (event) => {
      const status = event.newValue === '1' ? 'UP' : 'DOWN';
      console.log(`[Automation] Interface status: ${status} (device: ${event.deviceName})`);
    });
  }

  async fetchDevices(): Promise<void> {
    this.isLoadingDevices.set(true);
    try {
      const list = await this.signalR.getDevices();
      this.devices.set(list);
    } catch (err) {
      console.warn('[App] Failed to fetch devices:', err);
    } finally {
      this.isLoadingDevices.set(false);
    }
  }

  openDevice(dev: DeviceInfo): void {
    this.selectedDeviceId.set(dev.id);
    this.panelService.loadFromDevice(dev.id);
    this.isPanelOpen = true;
  }

  closePanel(): void {
    this.isPanelOpen = false;
  }

  scrollToModule(index: number): void {
    this.activeModuleIndex = index;
    const el = document.getElementById('panel-module-' + index);
    el?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }
}
