import { Component, Input, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MibFieldSchema } from '../../models/mib-schema';
import { MibPanelService } from '../../services/mib-panel.service';

@Component({
  selector: 'app-config-group',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './config-group.component.html',
  styleUrl: './config-group.component.scss',
})
export class ConfigGroupComponent {
  @Input({ required: true }) fields: MibFieldSchema[] = [];

  panelService = inject(MibPanelService);

  // Track which field is being edited
  editingOid: string | null = null;
  editValue = '';

  startEdit(field: MibFieldSchema): void {
    this.editingOid = field.oid;
    this.editValue = field.currentValue || field.defaultValue || '';
  }

  cancelEdit(): void {
    this.editingOid = null;
  }

  sendSet(field: MibFieldSchema): void {
    const valueType = this.panelService.resolveValueType(field.inputType, field.baseType);
    // IDD fields (non-numeric OID) don't need .0 suffix; SNMP scalars do
    const setOid = /^\d/.test(field.oid) ? field.oid + '.0' : field.oid;
    this.panelService.sendSet(setOid, field.name, this.editValue, valueType);
    field.currentValue = this.editValue;
    this.editingOid = null;
  }

  resolveEnum(field: MibFieldSchema): string | null {
    if (!field.options?.length || !field.currentValue) return null;
    const num = parseInt(field.currentValue, 10);
    return field.options.find(o => o.value === num)?.label ?? null;
  }

  friendlyName(name: string): string {
    return name
      .replace(/^sd|^sys/i, '')
      .replace(/([a-z])([A-Z])/g, '$1 $2')
      .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2');
  }

  // Toggle helpers: derive on/off values and labels from enum options
  getToggleOnValue(field: MibFieldSchema): string {
    if (field.options?.length === 2) {
      const sorted = [...field.options].sort((a, b) => a.value - b.value);
      return String(sorted[1].value);
    }
    return '1';
  }
  getToggleOffValue(field: MibFieldSchema): string {
    if (field.options?.length === 2) {
      const sorted = [...field.options].sort((a, b) => a.value - b.value);
      return String(sorted[0].value);
    }
    return '0';
  }
  getToggleLabel(field: MibFieldSchema, isOn: boolean): string {
    if (field.options?.length === 2) {
      const sorted = [...field.options].sort((a, b) => a.value - b.value);
      return isOn ? sorted[1].label : sorted[0].label;
    }
    return isOn ? 'ON' : 'OFF';
  }

  // Status LED color class
  getStatusClass(field: MibFieldSchema): string {
    if (!field.options?.length || !field.currentValue) return 'off';
    const num = parseInt(field.currentValue, 10);
    const label = (field.options.find(o => o.value === num)?.label || '').toLowerCase();
    if (['ok', 'normal', 'on', 'up', 'active', 'enabled'].includes(label)) return 'ok';
    if (['low', 'high', 'warning'].includes(label)) return 'warn';
    if (['fail', 'fault', 'alarm', 'error', 'critical'].includes(label) || label.includes('failed')) return 'error';
    return 'off';
  }

  displayValue(field: MibFieldSchema): string {
    if (!field.currentValue) return field.defaultValue || '—';

    const enumLabel = this.resolveEnum(field);
    if (enumLabel) return enumLabel;

    if (field.inputType === 'toggle') {
      return field.currentValue === this.getToggleOnValue(field)
        ? this.getToggleLabel(field, true) : this.getToggleLabel(field, false);
    }

    return field.currentValue;
  }
}
