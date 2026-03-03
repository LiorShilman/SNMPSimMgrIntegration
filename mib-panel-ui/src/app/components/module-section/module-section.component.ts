import { Component, Input, inject, OnChanges } from '@angular/core';
import { MibModuleSchema, MibFieldSchema } from '../../models/mib-schema';
import { FieldClassifierService, ClassifiedScalars } from '../../services/field-classifier.service';
import { StatusGridComponent } from '../status-grid/status-grid.component';
import { ConfigGroupComponent } from '../config-group/config-group.component';
import { MibTableComponent } from '../mib-table/mib-table.component';

@Component({
  selector: 'app-module-section',
  standalone: true,
  imports: [StatusGridComponent, ConfigGroupComponent, MibTableComponent],
  template: `
    <section class="module-section">
      <div class="module-header" (click)="isExpanded = !isExpanded">
        <span class="expand-icon" [class.expanded]="isExpanded">&#9654;</span>
        <span class="module-name">{{ module.moduleName }}</span>
      </div>

      <div class="collapse-wrapper" [class.expanded]="isExpanded">
        <div class="collapse-inner">
          <div class="module-body">
            <!-- Status / Monitoring Section -->
            @if (classified.status.length + classified.counters.length > 0) {
              <div class="section section-monitoring">
                <div class="section-header">
                  <span class="section-accent"></span>
                  <span class="section-icon">&#9673;</span>
                  <span class="section-title">Monitoring</span>
                  <span class="section-count">{{ monitorFields.length }}</span>
                  @if (monitorSummary.ok + monitorSummary.warning + monitorSummary.alarm > 0) {
                    <span class="section-summary">
                      @if (monitorSummary.ok > 0) {
                        <span class="sum-ok">{{ monitorSummary.ok }} OK</span>
                      }
                      @if (monitorSummary.warning > 0) {
                        <span class="sum-warn">{{ monitorSummary.warning }} Warn</span>
                      }
                      @if (monitorSummary.alarm > 0) {
                        <span class="sum-alarm">{{ monitorSummary.alarm }} Alarm</span>
                      }
                    </span>
                  }
                </div>
                <app-status-grid [fields]="monitorFields" />
              </div>
            }

            <!-- Configuration Section -->
            @if (classified.config.length > 0) {
              <div class="section section-config">
                <div class="section-header">
                  <span class="section-accent"></span>
                  <span class="section-icon">&#9881;</span>
                  <span class="section-title">Configuration</span>
                  <span class="section-count">{{ classified.config.length }}</span>
                </div>
                <app-config-group [fields]="classified.config" />
              </div>
            }

            <!-- Tables Section -->
            @if (module.tables.length > 0) {
              <div class="section section-tables">
                <div class="section-header">
                  <span class="section-accent"></span>
                  <span class="section-icon">&#9638;</span>
                  <span class="section-title">Tables</span>
                  <span class="section-count">{{ module.tables.length }}</span>
                </div>
                <div class="tables-list">
                  @for (table of module.tables; track table.oid) {
                    <app-mib-table [table]="table" />
                  }
                </div>
              </div>
            }
          </div>
        </div>
      </div>
    </section>
  `,
  styles: [`
    .module-section {
      border: 1px solid #252d42;
      border-radius: 14px;
      background: #171b28;
      overflow: hidden;
    }

    .module-header {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 16px 20px;
      cursor: pointer;
      background: linear-gradient(135deg, #1c2133 0%, #1e2740 100%);
      transition: all 0.2s;

      &:hover {
        background: linear-gradient(135deg, #1f2538 0%, #222c48 100%);
      }
    }

    .expand-icon {
      display: inline-block;
      font-size: 12px;
      color: #6C9FFF;
      width: 16px;
      transition: transform 0.25s cubic-bezier(0.4, 0, 0.2, 1);

      &.expanded { transform: rotate(90deg); }
    }

    .module-name {
      font-weight: 700;
      font-size: 15px;
      color: #E8EAED;
      flex: 1;
      letter-spacing: 0.2px;
    }

    // Smooth collapse animation
    .collapse-wrapper {
      display: grid;
      grid-template-rows: 0fr;
      transition: grid-template-rows 0.3s cubic-bezier(0.4, 0, 0.2, 1);

      &.expanded {
        grid-template-rows: 1fr;
      }
    }

    .collapse-inner {
      overflow: hidden;
    }

    .module-body {
      padding: 4px 18px 18px;
    }

    .section {
      margin-top: 16px;
    }

    .section-header {
      display: flex;
      align-items: center;
      gap: 8px;
      padding-bottom: 10px;
      margin-bottom: 10px;
      border-bottom: 1px solid #252d42;
      position: relative;
    }

    .section-accent {
      position: absolute;
      bottom: -1px;
      left: 0;
      width: 40px;
      height: 2px;
      border-radius: 1px;
    }

    .section-monitoring .section-accent {
      background: #57D9A3;
      box-shadow: 0 0 8px rgba(87, 217, 163, 0.3);
    }
    .section-config .section-accent {
      background: #4C9AFF;
      box-shadow: 0 0 8px rgba(76, 154, 255, 0.3);
    }
    .section-tables .section-accent {
      background: #A78BFA;
      box-shadow: 0 0 8px rgba(167, 139, 250, 0.3);
    }

    .section-monitoring .section-icon { color: #57D9A3; }
    .section-config .section-icon { color: #4C9AFF; }
    .section-tables .section-icon { color: #A78BFA; }

    .section-icon {
      font-size: 14px;
    }

    .section-title {
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 1.5px;
      color: #5A6888;
    }

    .section-count {
      font-size: 10px;
      font-weight: 700;
      background: rgba(255, 255, 255, 0.06);
      color: #7A849A;
      padding: 1px 7px;
      border-radius: 10px;
    }

    .section-summary {
      margin-left: auto;
      display: flex;
      gap: 10px;
      font-size: 11px;
      font-weight: 600;

      .sum-ok { color: #57D9A3; }
      .sum-warn { color: #FFAB00; }
      .sum-alarm { color: #FF5252; }
    }

    .tables-list {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }
  `]
})
export class ModuleSectionComponent implements OnChanges {
  @Input({ required: true }) module!: MibModuleSchema;

  private classifier = inject(FieldClassifierService);

  isExpanded = true;
  classified: ClassifiedScalars = { identity: [], status: [], config: [], counters: [] };
  monitorFields: MibFieldSchema[] = [];
  monitorSummary = { ok: 0, warning: 0, alarm: 0 };

  ngOnChanges(): void {
    this.classified = this.classifier.classifyScalars(this.module.scalars);
    this.monitorFields = [...this.classified.status, ...this.classified.counters];

    let ok = 0, warning = 0, alarm = 0;
    for (const field of this.classified.status) {
      if (!field.options?.length || !field.currentValue) continue;
      const num = parseInt(field.currentValue, 10);
      const label = (field.options.find(o => o.value === num)?.label || '').toLowerCase();
      if (/up|active|ok|normal|enabled|true|running|online|ready/.test(label)) ok++;
      else if (/warning|degraded|standby|testing|suspended/.test(label)) warning++;
      else if (/down|error|fail|critical|disabled|false|offline|alarm|fault/.test(label)) alarm++;
    }
    this.monitorSummary = { ok, warning, alarm };
  }
}
