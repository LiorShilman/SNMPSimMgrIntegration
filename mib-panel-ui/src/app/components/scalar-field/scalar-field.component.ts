import { Component, Input, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MibFieldSchema } from '../../models/mib-schema';
import { MibPanelService } from '../../services/mib-panel.service';

@Component({
  selector: 'app-scalar-field',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './scalar-field.component.html',
  styleUrl: './scalar-field.component.scss',
})
export class ScalarFieldComponent {
  @Input({ required: true }) field!: MibFieldSchema;

  panelService = inject(MibPanelService);
  editValue = '';
  isEditing = false;

  // Toggle helpers: derive on/off values and labels from enum options
  get toggleOnValue(): string {
    if (this.field.options?.length === 2) {
      const sorted = [...this.field.options].sort((a, b) => a.value - b.value);
      return String(sorted[1].value); // higher value = ON
    }
    return '1';
  }
  get toggleOffValue(): string {
    if (this.field.options?.length === 2) {
      const sorted = [...this.field.options].sort((a, b) => a.value - b.value);
      return String(sorted[0].value); // lower value = OFF
    }
    return '0';
  }
  get toggleOnLabel(): string {
    if (this.field.options?.length === 2) {
      const sorted = [...this.field.options].sort((a, b) => a.value - b.value);
      return sorted[1].label;
    }
    return 'ON';
  }
  get toggleOffLabel(): string {
    if (this.field.options?.length === 2) {
      const sorted = [...this.field.options].sort((a, b) => a.value - b.value);
      return sorted[0].label;
    }
    return 'OFF';
  }

  get displayValue(): string {
    if (!this.field.currentValue) return '—';

    // Resolve enum label (also for toggle and status-led)
    if (this.field.options?.length) {
      const num = parseInt(this.field.currentValue, 10);
      const opt = this.field.options.find(o => o.value === num);
      if (opt) return `${opt.label} (${num})`;
    }

    // TimeTicks → human readable
    if (this.field.inputType === 'timeticks') {
      const ticks = parseInt(this.field.currentValue, 10);
      if (!isNaN(ticks)) {
        const secs = Math.floor(ticks / 100);
        const d = Math.floor(secs / 86400);
        const h = Math.floor((secs % 86400) / 3600);
        const m = Math.floor((secs % 3600) / 60);
        const s = secs % 60;
        return d > 0 ? `${d}d ${h}h ${m}m ${s}s` : `${h}h ${m}m ${s}s`;
      }
    }

    const val = this.field.currentValue;
    if (this.field.units) return `${val} ${this.field.units}`;
    return val;
  }

  get typeIcon(): string {
    switch (this.field.inputType) {
      case 'counter': return '⟳';
      case 'gauge':   return '◔';
      case 'timeticks': return '⏱';
      case 'ip':      return '⌘';
      case 'enum':    return '☰';
      case 'toggle':  return '⊘';
      case 'number':  return '#';
      case 'text':    return 'T';
      case 'oid':     return '⎆';
      case 'bits':    return '⊞';
      case 'status-led': return '◉';
      default:        return '·';
    }
  }

  // Status LED color class based on current enum label
  get statusClass(): string {
    if (!this.field.options?.length || !this.field.currentValue) return 'off';
    const num = parseInt(this.field.currentValue, 10);
    const label = (this.field.options.find(o => o.value === num)?.label || '').toLowerCase();
    if (['ok', 'normal', 'on', 'up', 'active', 'enabled'].includes(label)) return 'ok';
    if (['low', 'high', 'warning'].includes(label)) return 'warn';
    if (['fail', 'fault', 'alarm', 'error', 'critical'].includes(label) || label.includes('failed')) return 'error';
    return 'off';
  }

  get accessBadge(): string {
    if (this.field.access.includes('create')) return 'RC';
    if (this.field.isWritable) return 'RW';
    return 'RO';
  }

  get canSet(): boolean {
    return this.field.isWritable && this.panelService.isConnected();
  }

  startEdit(): void {
    if (!this.field.isWritable) return;
    this.editValue = this.field.currentValue || this.field.defaultValue || '';
    this.isEditing = true;
  }

  cancelEdit(): void {
    this.isEditing = false;
  }

  sendSet(): void {
    const valueType = this.panelService.resolveValueType(this.field.inputType, this.field.baseType);
    this.panelService.sendSet(
      this.field.oid + '.0',
      this.field.name,
      this.editValue,
      valueType
    );
    this.field.currentValue = this.editValue;
    this.isEditing = false;
  }
}
