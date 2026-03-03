import { Component, inject } from '@angular/core';
import { MibPanelService } from '../../services/mib-panel.service';

@Component({
  selector: 'app-set-feedback',
  standalone: true,
  template: `
    <div class="feedback-container">
      @for (fb of panelService.feedbacks(); track fb.id) {
        <div class="feedback-toast" [class]="'status-' + fb.status">
          <div class="toast-header">
            <span class="toast-icon">
              @if (fb.status === 'pending') { ⏳ }
              @else if (fb.status === 'success') { ✓ }
              @else { ✗ }
            </span>
            <span class="toast-name">{{ fb.name }}</span>
            <span class="toast-type">{{ fb.valueType }}</span>
          </div>
          <div class="toast-body">
            <span class="toast-oid">{{ fb.oid }}</span>
            <span class="toast-arrow">←</span>
            <span class="toast-value">{{ fb.value }}</span>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .feedback-container {
      position: fixed;
      bottom: 20px;
      left: 20px;
      z-index: 1000;
      display: flex;
      flex-direction: column-reverse;
      gap: 8px;
      max-height: 50vh;
      overflow: hidden;
    }

    .feedback-toast {
      background: #232836;
      border: 1px solid #3D4663;
      border-radius: 8px;
      padding: 10px 14px;
      min-width: 340px;
      max-width: 480px;
      animation: slideIn 0.3s ease-out;
      border-left: 3px solid #3D4663;
    }

    .status-pending { border-left-color: #FFAB00; }
    .status-success { border-left-color: #57D9A3; }
    .status-error { border-left-color: #FF5252; }

    .toast-header {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 4px;
    }

    .toast-icon { font-size: 14px; }
    .toast-name {
      font-weight: 600;
      color: #E8EAED;
      font-size: 13px;
    }
    .toast-type {
      margin-left: auto;
      color: #8C95A6;
      font-size: 11px;
      font-family: 'Consolas', monospace;
      background: #1B1F2A;
      padding: 1px 6px;
      border-radius: 3px;
    }

    .toast-body {
      display: flex;
      align-items: center;
      gap: 8px;
      font-family: 'Consolas', monospace;
      font-size: 12px;
    }
    .toast-oid { color: #8BB8D0; }
    .toast-arrow { color: #4C9AFF; }
    .toast-value { color: #57D9A3; font-weight: 600; }

    @keyframes slideIn {
      from { transform: translateX(-30px); opacity: 0; }
      to   { transform: translateX(0); opacity: 1; }
    }
  `]
})
export class SetFeedbackComponent {
  panelService = inject(MibPanelService);
}
