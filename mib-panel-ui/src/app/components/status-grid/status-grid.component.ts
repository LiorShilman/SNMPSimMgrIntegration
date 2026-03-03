import { Component, Input, inject } from '@angular/core';
import { MibFieldSchema } from '../../models/mib-schema';
import { FieldClassifierService } from '../../services/field-classifier.service';

@Component({
  selector: 'app-status-grid',
  standalone: true,
  template: `
    <div class="status-grid">
      @for (field of fields; track field.oid) {
        <div class="status-card" [class]="cardClass(field)">
          <div class="card-top">
            <span class="status-label">{{ friendlyName(field.name) }}</span>
            @if (resolveEnum(field); as label) {
              <span class="enum-pill" [class]="pillClass(label)">{{ label }}</span>
            }
          </div>
          <div class="card-value">
            @if (field.inputType === 'counter' || field.inputType === 'gauge') {
              <span class="big-number">{{ formatCounter(field.currentValue) }}</span>
              @if (field.units) {
                <span class="unit">{{ field.units }}</span>
              }
            } @else if (field.inputType === 'timeticks') {
              <span class="big-number time">{{ formatUptime(field.currentValue) }}</span>
            } @else if (resolveEnum(field)) {
              <!-- enum value shown in pill above -->
            } @else {
              <span class="text-value">{{ field.currentValue || '—' }}</span>
            }
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .status-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(165px, 1fr));
      gap: 10px;
    }

    .status-card {
      background: #1c2133;
      border: 1px solid #252d42;
      border-radius: 12px;
      padding: 14px 16px;
      transition: all 0.2s;
      border-left: 3px solid #252d42;

      &:hover {
        background: #1f2538;
        transform: translateY(-1px);
        box-shadow: 0 4px 12px rgba(0,0,0,0.15);
      }

      &.card-positive { border-left-color: #57D9A3; }
      &.card-negative { border-left-color: #FF6B6B; }
      &.card-warning  { border-left-color: #FFB347; }
      &.card-counter  { border-left-color: #6C9FFF; }
    }

    .card-top {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 6px;
      margin-bottom: 8px;
    }

    .status-label {
      font-size: 11px;
      font-weight: 600;
      color: #7A849A;
      text-transform: capitalize;
      letter-spacing: 0.3px;
    }

    .enum-pill {
      font-size: 10px;
      font-weight: 700;
      padding: 2px 8px;
      border-radius: 6px;
      text-transform: uppercase;
      letter-spacing: 0.5px;

      &.pill-positive {
        color: #57D9A3;
        background: rgba(87, 217, 163, 0.12);
      }
      &.pill-negative {
        color: #FF6B6B;
        background: rgba(255, 107, 107, 0.12);
      }
      &.pill-warning {
        color: #FFB347;
        background: rgba(255, 179, 71, 0.12);
      }
      &.pill-neutral {
        color: #7A849A;
        background: rgba(122, 132, 154, 0.12);
      }
    }

    .card-value {
      display: flex;
      align-items: baseline;
      gap: 4px;
    }

    .big-number {
      font-size: 22px;
      font-weight: 700;
      color: #F0F2F5;
      font-family: 'Consolas', monospace;
      line-height: 1;

      &.time {
        font-size: 17px;
        color: #8BB8D0;
      }
    }

    .unit {
      font-size: 12px;
      color: #5A6888;
      font-weight: 500;
    }

    .text-value {
      font-size: 14px;
      color: #CDD1D8;
      font-family: 'Consolas', monospace;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  `]
})
export class StatusGridComponent {
  @Input({ required: true }) fields: MibFieldSchema[] = [];

  private classifier = inject(FieldClassifierService);

  resolveEnum(field: MibFieldSchema): string | null {
    return this.classifier.resolveEnumLabel(field);
  }

  formatCounter(value?: string): string {
    return value ? this.classifier.formatCounter(value) : '—';
  }

  formatUptime(value?: string): string {
    if (!value) return '—';
    const ticks = parseInt(value, 10);
    return isNaN(ticks) ? value : this.classifier.formatUptime(ticks);
  }

  friendlyName(name: string): string {
    // "sdCpuTemperature" → "Cpu Temperature"
    return name
      .replace(/^sd|^sys/i, '')
      .replace(/([a-z])([A-Z])/g, '$1 $2')
      .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2');
  }

  cardClass(field: MibFieldSchema): string {
    if (field.inputType === 'counter' || field.inputType === 'gauge') return 'card-counter';
    const label = this.resolveEnum(field);
    if (!label) return '';
    return 'card-' + this.classifyLabel(label);
  }

  pillClass(label: string): string {
    return 'pill-' + this.classifyLabel(label);
  }

  private classifyLabel(label: string): string {
    const lower = label.toLowerCase();
    if (/up|active|ok|normal|enabled|true|running|online|ready/.test(lower)) return 'positive';
    if (/down|error|fail|critical|disabled|false|offline/.test(lower)) return 'negative';
    if (/warning|degraded|standby|testing|suspended/.test(lower)) return 'warning';
    return 'neutral';
  }
}
