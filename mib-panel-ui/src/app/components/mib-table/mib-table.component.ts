import { Component, Input, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MibTableSchema, MibFieldSchema } from '../../models/mib-schema';
import { MibPanelService } from '../../services/mib-panel.service';

@Component({
  selector: 'app-mib-table',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './mib-table.component.html',
  styleUrl: './mib-table.component.scss',
})
export class MibTableComponent {
  @Input({ required: true }) table!: MibTableSchema;

  panelService = inject(MibPanelService);
  isExpanded = false;
  editingCell: { rowIndex: string; colOid: string } | null = null;
  editValue = '';

  get writableColumns(): MibFieldSchema[] {
    return this.table.columns.filter(c => c.isWritable);
  }

  getCellValue(rowIndex: string, colOid: string): string {
    const row = this.table.rows.find(r => r.index === rowIndex);
    if (!row) return '—';
    const cell = row.values[colOid];
    if (!cell) return '—';

    // Check for enum label
    if (cell.enumLabel) return `${cell.enumLabel} (${cell.value})`;
    return cell.value || '—';
  }

  getCellType(rowIndex: string, colOid: string): string {
    const row = this.table.rows.find(r => r.index === rowIndex);
    return row?.values[colOid]?.type || '';
  }

  isWritable(colOid: string): boolean {
    return this.table.columns.find(c => c.oid === colOid)?.isWritable || false;
  }

  getColumn(colOid: string): MibFieldSchema | undefined {
    return this.table.columns.find(c => c.oid === colOid);
  }

  startCellEdit(rowIndex: string, colOid: string): void {
    if (!this.isWritable(colOid)) return;
    const row = this.table.rows.find(r => r.index === rowIndex);
    this.editValue = row?.values[colOid]?.value || '';
    this.editingCell = { rowIndex, colOid };
  }

  cancelCellEdit(): void {
    this.editingCell = null;
  }

  sendCellSet(): void {
    if (!this.editingCell) return;
    const { rowIndex, colOid } = this.editingCell;
    const col = this.getColumn(colOid);
    if (!col) return;

    const fullOid = `${colOid}.${rowIndex}`;
    const valueType = this.panelService.resolveValueType(col.inputType, col.baseType);
    this.panelService.sendSet(fullOid, col.name, this.editValue, valueType);

    // Update local cell value
    const row = this.table.rows.find(r => r.index === rowIndex);
    if (row && row.values[colOid]) {
      row.values[colOid].value = this.editValue;
    }
    this.editingCell = null;
  }

  isEditingCell(rowIndex: string, colOid: string): boolean {
    return this.editingCell?.rowIndex === rowIndex && this.editingCell?.colOid === colOid;
  }

  getCellStatus(rowIndex: string, col: MibFieldSchema): string {
    const row = this.table.rows.find(r => r.index === rowIndex);
    if (!row || !col.options?.length) return 'off';
    const cell = row.values[col.oid];
    if (!cell) return 'off';
    const num = parseInt(cell.value, 10);
    const label = (col.options.find(o => o.value === num)?.label || '').toLowerCase();
    if (['ok', 'normal', 'on', 'up', 'active', 'enabled'].includes(label)) return 'ok';
    if (['low', 'high', 'warning'].includes(label)) return 'warn';
    if (['fail', 'fault', 'alarm', 'error', 'critical'].includes(label) || label.includes('failed')) return 'error';
    return 'off';
  }

  friendlyName(name: string): string {
    return name
      .replace(/^sd|^sys/i, '')
      .replace(/([a-z])([A-Z])/g, '$1 $2')
      .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2');
  }
}
