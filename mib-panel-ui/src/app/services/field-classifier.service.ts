import { Injectable } from '@angular/core';
import { MibFieldSchema, MibPanelSchema } from '../models/mib-schema';

export interface DeviceIdentity {
  name?: string;
  model?: string;
  firmware?: string;
  serial?: string;
  mac?: string;
  uptime?: string;
  uptimeRaw?: number;
  contact?: string;
  location?: string;
  managementIp?: string;
  description?: string;
}

export interface SystemInfoItem {
  label: string;
  value: string;
  icon: string;       // SVG icon key
  category: 'identity' | 'network' | 'system';
}

export interface ClassifiedScalars {
  identity: MibFieldSchema[];   // device info fields shown in header card
  status: MibFieldSchema[];     // status/monitoring fields shown as indicators
  config: MibFieldSchema[];     // configurable fields shown as form
  counters: MibFieldSchema[];   // counter/gauge fields shown as stats
}

/** Patterns that identify device-identity fields (shown in header card) */
const IDENTITY_PATTERNS = [
  /devicename|sysname/i,
  /model/i,
  /firmware|fwver|swver/i,
  /serial/i,
  /macaddress|basemac|chassismac/i,
  /uptime|sysuptime/i,
  /contact|syscontact/i,
  /location|syslocation/i,
  /managementip|mgmtip/i,
  /sysdescr|description$/i,
];

/** Patterns that identify status/monitoring fields */
const STATUS_PATTERNS = [
  /status/i,
  /state$/i,
  /alarm/i,
  /health/i,
  /enabled|disabled/i,
  /operational/i,
  /ready/i,
  /active/i,
];

@Injectable({ providedIn: 'root' })
export class FieldClassifierService {

  extractIdentity(schema: MibPanelSchema): DeviceIdentity {
    const identity: DeviceIdentity = {};
    const allScalars = schema.modules.flatMap(m => m.scalars);

    for (const f of allScalars) {
      const name = f.name.toLowerCase();
      const val = f.currentValue;
      if (!val) continue;

      if (/devicename|sysname/i.test(name) && !identity.name) identity.name = val;
      else if (/model/i.test(name) && !identity.model) identity.model = val;
      else if (/firmware|fwver|swver/i.test(name) && !identity.firmware) identity.firmware = val;
      else if (/serial/i.test(name) && !identity.serial) identity.serial = val;
      else if (/macaddress|basemac|chassismac/i.test(name) && !identity.mac) identity.mac = val;
      else if (/uptime|sysuptime/i.test(name) && !identity.uptime) {
        identity.uptimeRaw = parseInt(val, 10);
        identity.uptime = this.formatUptime(identity.uptimeRaw);
      }
      else if (/contact|syscontact/i.test(name) && !identity.contact) identity.contact = val;
      else if (/location|syslocation/i.test(name) && !identity.location) identity.location = val;
      else if (/managementip|mgmtip/i.test(name) && !identity.managementIp) identity.managementIp = val;
      else if (/sysdescr|^sddescription$/i.test(name) && !identity.description) identity.description = val;
    }

    // Fallback name
    if (!identity.name) identity.name = schema.deviceName;
    if (!identity.managementIp) identity.managementIp = schema.deviceIp;

    return identity;
  }

  classifyScalars(scalars: MibFieldSchema[]): ClassifiedScalars {
    const result: ClassifiedScalars = {
      identity: [],
      status: [],
      config: [],
      counters: [],
    };

    for (const f of scalars) {
      if (this.matchesAny(f.name, IDENTITY_PATTERNS)) {
        result.identity.push(f);
      } else if (f.inputType === 'counter' || f.inputType === 'gauge') {
        result.counters.push(f);
      } else if (this.matchesAny(f.name, STATUS_PATTERNS) || (f.options?.length && !f.isWritable)) {
        result.status.push(f);
      } else if (f.isWritable) {
        result.config.push(f);
      } else {
        // Read-only non-counter, non-status → treat as status/info
        result.status.push(f);
      }
    }

    return result;
  }

  formatUptime(ticks: number): string {
    const secs = Math.floor(ticks / 100);
    const d = Math.floor(secs / 86400);
    const h = Math.floor((secs % 86400) / 3600);
    const m = Math.floor((secs % 3600) / 60);
    if (d > 0) return `${d}d ${h}h ${m}m`;
    if (h > 0) return `${h}h ${m}m`;
    return `${m}m`;
  }

  formatCounter(value: string): string {
    const num = parseInt(value, 10);
    if (isNaN(num)) return value;
    if (num >= 1_000_000_000) return (num / 1_000_000_000).toFixed(1) + 'G';
    if (num >= 1_000_000) return (num / 1_000_000).toFixed(1) + 'M';
    if (num >= 1_000) return (num / 1_000).toFixed(1) + 'K';
    return num.toLocaleString();
  }

  resolveEnumLabel(field: MibFieldSchema): string | null {
    if (!field.options?.length || !field.currentValue) return null;
    const num = parseInt(field.currentValue, 10);
    return field.options.find(o => o.value === num)?.label ?? null;
  }

  extractSystemInfo(schema: MibPanelSchema): SystemInfoItem[] {
    const items: SystemInfoItem[] = [];
    const allScalars = schema.modules.flatMap(m => m.scalars);
    const seen = new Set<string>();

    const mappings: Array<{ pattern: RegExp; label: string; icon: string; category: SystemInfoItem['category'] }> = [
      { pattern: /^(sysname|sddevicename)$/i,            label: 'Device Name',    icon: 'device',   category: 'identity' },
      { pattern: /^(sysdescr|sddescription)$/i,          label: 'Description',    icon: 'info',     category: 'identity' },
      { pattern: /^(sysObjectID|sdmodel)$/i,             label: 'Model / OID',    icon: 'chip',     category: 'identity' },
      { pattern: /^(sd)?serial(number)?$/i,              label: 'Serial Number',  icon: 'barcode',  category: 'identity' },
      { pattern: /^(sd)?(firmware|fwver|swver)/i,        label: 'Firmware',       icon: 'code',     category: 'system' },
      { pattern: /^(sd)?hardwarever/i,                   label: 'Hardware Ver.',   icon: 'board',    category: 'system' },
      { pattern: /^(sysuptime|sduptime)/i,               label: 'Uptime',         icon: 'clock',    category: 'system' },
      { pattern: /^(syscontact|sdcontact)$/i,            label: 'Contact',        icon: 'user',     category: 'system' },
      { pattern: /^(syslocation|sdlocation)$/i,          label: 'Location',       icon: 'pin',      category: 'system' },
      { pattern: /^(sd)?(managementip|mgmtip)/i,        label: 'Management IP',  icon: 'network',  category: 'network' },
      { pattern: /^(sd)?(macaddress|basemac|chassismac)/i, label: 'MAC Address', icon: 'mac',      category: 'network' },
      { pattern: /^(sd)?gateway/i,                       label: 'Gateway',        icon: 'gateway',  category: 'network' },
      { pattern: /^(sd)?subnet/i,                        label: 'Subnet Mask',    icon: 'subnet',   category: 'network' },
      { pattern: /^(sd)?dns/i,                           label: 'DNS Server',     icon: 'dns',      category: 'network' },
      { pattern: /^(sd)?domain/i,                        label: 'Domain',         icon: 'globe',    category: 'network' },
      { pattern: /^(sd)?slot(number|id)?$/i,             label: 'Slot',           icon: 'slot',     category: 'identity' },
      { pattern: /^(sd)?chassis(id)?$/i,                 label: 'Chassis',        icon: 'chassis',  category: 'identity' },
    ];

    for (const f of allScalars) {
      for (const m of mappings) {
        if (m.pattern.test(f.name) && !seen.has(m.label)) {
          let val = f.currentValue || '';
          if (!val) continue;

          // Format uptime from ticks
          if (/uptime/i.test(m.label)) {
            const ticks = parseInt(val, 10);
            if (!isNaN(ticks)) val = this.formatUptime(ticks);
          }

          items.push({ label: m.label, value: val, icon: m.icon, category: m.category });
          seen.add(m.label);
          break;
        }
      }
    }

    // Add connection info from schema root
    if (schema.deviceIp && !seen.has('Management IP')) {
      items.push({ label: 'Management IP', value: schema.deviceIp, icon: 'network', category: 'network' });
    }
    if (schema.devicePort) {
      items.push({ label: 'SNMP Port', value: String(schema.devicePort), icon: 'port', category: 'network' });
    }
    if (schema.community) {
      items.push({ label: 'Community', value: schema.community, icon: 'key', category: 'network' });
    }
    if (schema.snmpVersion) {
      items.push({ label: 'SNMP Version', value: schema.snmpVersion, icon: 'protocol', category: 'network' });
    }

    return items;
  }

  private matchesAny(name: string, patterns: RegExp[]): boolean {
    return patterns.some(p => p.test(name));
  }
}
