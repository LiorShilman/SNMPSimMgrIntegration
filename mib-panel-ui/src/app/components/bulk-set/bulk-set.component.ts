import { Component, inject, signal, output } from '@angular/core';
import { SignalRService, BulkSetItem, BulkSetResult, BulkSetItemResult } from '../../services/signalr.service';
import { MibPanelService } from '../../services/mib-panel.service';

@Component({
  selector: 'app-bulk-set',
  standalone: true,
  template: `
    <div class="bulkset-backdrop" (click)="close.emit()"></div>
    <div class="bulkset-dialog">
      <div class="dialog-header">
        <span class="dialog-icon">&#9776;</span>
        <span class="dialog-title">Bulk SET / Config Import</span>
        <button class="btn-close" (click)="close.emit()">&times;</button>
      </div>

      <div class="dialog-body">
        @if (!result()) {
          <!-- Import controls -->
          <div class="import-row">
            <label class="btn-file">
              <input type="file" accept=".csv,.json" (change)="onFileSelected($event)" hidden />
              Choose CSV / JSON
            </label>
            @if (fileName()) {
              <span class="file-name">{{ fileName() }}</span>
            }
            <span class="format-hint">CSV: OID,Value,Type &middot; JSON: [{{ '{' }}"oid","value","valueType"{{ '}' }}]</span>
          </div>

          <!-- Preview table -->
          @if (items().length > 0) {
            <div class="preview-section">
              <div class="preview-header">
                <span class="preview-title">Preview ({{ items().length }} items)</span>
                <button class="btn-clear" (click)="clearItems()">Clear</button>
              </div>
              <div class="preview-table">
                <div class="preview-row header-row">
                  <span>OID</span>
                  <span>Value</span>
                  <span>Type</span>
                </div>
                @for (item of items(); track $index) {
                  <div class="preview-row">
                    <span class="cell-oid">{{ item.oid }}</span>
                    <span class="cell-val">{{ item.value }}</span>
                    <span class="cell-type">{{ item.valueType }}</span>
                  </div>
                }
              </div>
            </div>
          }

          @if (parseError()) {
            <div class="parse-error">{{ parseError() }}</div>
          }
        } @else {
          <!-- Results -->
          <div class="results-section">
            <div class="results-summary">
              <span class="summary-total">{{ result()!.total }} total</span>
              <span class="summary-ok">{{ result()!.succeeded }} succeeded</span>
              @if (result()!.failed > 0) {
                <span class="summary-fail">{{ result()!.failed }} failed</span>
              }
            </div>

            <div class="results-table">
              <div class="result-row header-row">
                <span></span>
                <span>OID</span>
                <span>Message</span>
              </div>
              @for (r of result()!.results; track $index) {
                <div class="result-row" [class.failed]="!r.success">
                  <span class="result-icon">{{ r.success ? '\u2713' : '\u2717' }}</span>
                  <span class="result-oid">{{ r.oid }}</span>
                  <span class="result-msg">{{ r.message }}</span>
                </div>
              }
            </div>

            <button class="btn-back" (click)="result.set(null)">&#8592; Back</button>
          </div>
        }
      </div>

      @if (!result()) {
        <div class="dialog-footer">
          <!-- Progress bar -->
          @if (sending()) {
            <div class="progress-bar">
              <div class="progress-fill" [style.width.%]="progress()"></div>
            </div>
            <span class="progress-text">{{ progressText() }}</span>
          }
          <button class="btn-send" [disabled]="items().length === 0 || sending()" (click)="send()">
            @if (sending()) {
              Sending...
            } @else {
              Send All ({{ items().length }})
            }
          </button>
        </div>
      }
    </div>
  `,
  styles: [`
    .bulkset-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.5);
      z-index: 900;
    }

    .bulkset-dialog {
      position: fixed;
      top: 50%;
      left: 50%;
      transform: translate(-50%, -50%);
      width: min(680px, 92vw);
      max-height: 85vh;
      background: #1a1e2e;
      border: 1px solid #2a3050;
      border-radius: 14px;
      z-index: 950;
      display: flex;
      flex-direction: column;
      box-shadow: 0 16px 48px rgba(0, 0, 0, 0.5);
      animation: bulkset-in 0.2s ease-out;
    }

    @keyframes bulkset-in {
      from { opacity: 0; transform: translate(-50%, -50%) scale(0.95); }
      to { opacity: 1; transform: translate(-50%, -50%) scale(1); }
    }

    .dialog-header {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 16px 20px;
      border-bottom: 1px solid #252d42;
      background: linear-gradient(135deg, #1c2133 0%, #1e2740 100%);
      border-radius: 14px 14px 0 0;
    }

    .dialog-icon { font-size: 18px; color: #4C9AFF; }

    .dialog-title {
      font-weight: 700;
      font-size: 15px;
      color: #E8EAED;
      flex: 1;
    }

    .btn-close {
      background: none;
      border: none;
      color: #8C95A6;
      font-size: 22px;
      cursor: pointer;
      padding: 0 4px;
      line-height: 1;
      transition: color 0.15s;
    }
    .btn-close:hover { color: #FF5252; }

    .dialog-body {
      flex: 1;
      overflow-y: auto;
      padding: 16px 20px;
      display: flex;
      flex-direction: column;
      gap: 14px;
    }

    // Import row
    .import-row {
      display: flex;
      align-items: center;
      gap: 10px;
      flex-wrap: wrap;
    }

    .btn-file {
      background: rgba(76, 154, 255, 0.1);
      border: 1px solid rgba(76, 154, 255, 0.3);
      color: #4C9AFF;
      padding: 6px 14px;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 600;
      cursor: pointer;
      transition: background 0.15s;
    }
    .btn-file:hover { background: rgba(76, 154, 255, 0.2); }

    .file-name {
      font-size: 12px;
      font-weight: 600;
      color: #FFAB00;
      font-family: 'Consolas', monospace;
    }

    .format-hint {
      font-size: 11px;
      color: #5A6888;
      margin-left: auto;
    }

    // Preview
    .preview-section {
      border: 1px solid #252d42;
      border-radius: 10px;
      overflow: hidden;
    }

    .preview-header {
      display: flex;
      align-items: center;
      padding: 8px 12px;
      background: rgba(255, 255, 255, 0.03);
      border-bottom: 1px solid #252d42;
    }

    .preview-title {
      font-size: 11px;
      font-weight: 700;
      color: #5A6888;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      flex: 1;
    }

    .btn-clear {
      background: none;
      border: 1px solid #3D4663;
      color: #8C95A6;
      padding: 2px 10px;
      border-radius: 4px;
      font-size: 11px;
      cursor: pointer;
    }
    .btn-clear:hover { color: #FF5252; border-color: #FF5252; }

    .preview-table {
      max-height: 300px;
      overflow-y: auto;
    }

    .preview-row, .result-row {
      display: grid;
      grid-template-columns: 1fr 1fr 100px;
      gap: 8px;
      padding: 5px 12px;
      font-size: 12px;
      align-items: center;
    }

    .preview-row.header-row, .result-row.header-row {
      font-size: 10px;
      font-weight: 700;
      color: #5A6888;
      text-transform: uppercase;
      border-bottom: 1px solid #1e2236;
      background: rgba(255, 255, 255, 0.02);
    }

    .preview-row:hover:not(.header-row) { background: rgba(255, 255, 255, 0.02); }

    .cell-oid { font-family: 'Consolas', monospace; color: #E8EAED; }
    .cell-val { color: #CDD1D8; }
    .cell-type { color: #5A6888; font-size: 11px; }

    .parse-error {
      padding: 8px 12px;
      background: rgba(255, 82, 82, 0.1);
      border: 1px solid rgba(255, 82, 82, 0.2);
      border-radius: 6px;
      color: #FF5252;
      font-size: 12px;
    }

    // Results
    .results-summary {
      display: flex;
      gap: 12px;
      padding: 10px 14px;
      background: rgba(255, 255, 255, 0.03);
      border: 1px solid #252d42;
      border-radius: 10px;
    }

    .summary-total {
      font-size: 13px;
      font-weight: 700;
      color: #E8EAED;
      margin-right: auto;
    }

    .summary-ok {
      font-size: 12px;
      font-weight: 600;
      color: #57D9A3;
      background: rgba(87, 217, 163, 0.1);
      padding: 3px 10px;
      border-radius: 12px;
    }

    .summary-fail {
      font-size: 12px;
      font-weight: 600;
      color: #FF5252;
      background: rgba(255, 82, 82, 0.1);
      padding: 3px 10px;
      border-radius: 12px;
    }

    .results-table {
      border: 1px solid #252d42;
      border-radius: 10px;
      overflow: hidden;
      max-height: 350px;
      overflow-y: auto;
    }

    .result-row {
      grid-template-columns: 24px 1fr 1fr;
    }

    .result-row.failed { background: rgba(255, 82, 82, 0.04); }

    .result-icon { font-size: 14px; text-align: center; }
    .result-row:not(.failed) .result-icon { color: #57D9A3; }
    .result-row.failed .result-icon { color: #FF5252; }

    .result-oid { font-family: 'Consolas', monospace; color: #E8EAED; font-size: 12px; }
    .result-msg { color: #8C95A6; font-size: 11px; }

    .btn-back {
      background: rgba(255, 255, 255, 0.04);
      border: 1px solid #252d42;
      color: #8C95A6;
      padding: 6px 14px;
      border-radius: 6px;
      font-size: 12px;
      cursor: pointer;
      align-self: flex-start;
      transition: all 0.15s;
    }
    .btn-back:hover { background: rgba(255, 255, 255, 0.08); color: #E8EAED; }

    // Footer
    .dialog-footer {
      padding: 12px 20px;
      border-top: 1px solid #252d42;
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .progress-bar {
      flex: 1;
      height: 6px;
      background: #252d42;
      border-radius: 3px;
      overflow: hidden;
    }

    .progress-fill {
      height: 100%;
      background: #4C9AFF;
      border-radius: 3px;
      transition: width 0.3s;
    }

    .progress-text {
      font-size: 11px;
      color: #8C95A6;
      white-space: nowrap;
    }

    .btn-send {
      background: #4C9AFF;
      border: none;
      color: #fff;
      padding: 8px 24px;
      border-radius: 6px;
      font-size: 13px;
      font-weight: 700;
      cursor: pointer;
      margin-left: auto;
      transition: background 0.15s;
    }
    .btn-send:hover { background: #79B8FF; }
    .btn-send:disabled { opacity: 0.5; cursor: not-allowed; }
  `]
})
export class BulkSetComponent {
  private signalR = inject(SignalRService);
  private panelService = inject(MibPanelService);

  close = output();

  items = signal<BulkSetItem[]>([]);
  fileName = signal<string | null>(null);
  parseError = signal<string | null>(null);
  result = signal<BulkSetResult | null>(null);
  sending = signal(false);
  progress = signal(0);
  progressText = signal('');

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.fileName.set(file.name);
    this.parseError.set(null);

    const reader = new FileReader();
    reader.onload = () => {
      try {
        const text = reader.result as string;
        if (file.name.endsWith('.json')) {
          this.parseJson(text);
        } else {
          this.parseCsv(text);
        }
      } catch (err: any) {
        this.parseError.set(err?.message || 'Failed to parse file');
        this.items.set([]);
      }
    };
    reader.readAsText(file);
    input.value = '';
  }

  private parseJson(text: string): void {
    const data = JSON.parse(text);
    if (!Array.isArray(data)) throw new Error('JSON must be an array of objects');

    const parsed: BulkSetItem[] = data.map((item: any) => ({
      oid: String(item.oid || item.OID || ''),
      value: String(item.value || item.Value || ''),
      valueType: String(item.valueType || item.ValueType || item.type || 'OctetString'),
    })).filter((i: BulkSetItem) => i.oid.trim());

    if (parsed.length === 0) throw new Error('No valid items found in JSON');
    this.items.set(parsed);
  }

  private parseCsv(text: string): void {
    const lines = text.split(/\r?\n/).map(l => l.trim()).filter(l => l);
    const parsed: BulkSetItem[] = [];

    for (const line of lines) {
      // Skip header row
      if (/^oid\b/i.test(line)) continue;
      // Skip comments
      if (line.startsWith('#')) continue;

      const parts = line.split(',').map(p => p.trim());
      if (parts.length < 2) continue;

      parsed.push({
        oid: parts[0],
        value: parts[1],
        valueType: parts[2] || 'OctetString',
      });
    }

    if (parsed.length === 0) throw new Error('No valid rows found in CSV');
    this.items.set(parsed);
  }

  clearItems(): void {
    this.items.set([]);
    this.fileName.set(null);
    this.parseError.set(null);
  }

  async send(): Promise<void> {
    const deviceId = this.panelService.currentDeviceId();
    if (!deviceId) {
      this.parseError.set('No device loaded. Select a device first.');
      return;
    }

    this.sending.set(true);
    this.progress.set(0);
    this.progressText.set(`0 / ${this.items().length}`);

    try {
      const res = await this.signalR.sendBulkSet(deviceId, this.items());
      this.result.set(res);
      this.progress.set(100);
      this.progressText.set(`${res.total} / ${res.total}`);
    } catch (err: any) {
      this.parseError.set(err?.message || 'Bulk SET failed');
    } finally {
      this.sending.set(false);
    }
  }
}
