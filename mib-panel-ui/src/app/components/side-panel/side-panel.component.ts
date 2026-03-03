import { Component, Input, Output, EventEmitter } from '@angular/core';

@Component({
  selector: 'app-side-panel',
  standalone: true,
  template: `
    <div class="overlay" [class.open]="isOpen" (click)="close.emit()"></div>
    <aside class="panel" [class.open]="isOpen">
      <div class="panel-header">
        <ng-content select="[panel-header]"></ng-content>
        <button class="btn-close" (click)="close.emit()">✕</button>
      </div>
      <div class="panel-body">
        <ng-content></ng-content>
      </div>
    </aside>
  `,
  styles: [`
    .overlay {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.5);
      z-index: 900;
      opacity: 0;
      pointer-events: none;
      transition: opacity 0.3s;

      &.open {
        opacity: 1;
        pointer-events: auto;
      }
    }

    .panel {
      position: fixed;
      top: 0;
      right: 0;
      bottom: 0;
      width: min(680px, 90vw);
      background: #1B1F2A;
      z-index: 950;
      transform: translateX(100%);
      transition: transform 0.3s cubic-bezier(0.4, 0, 0.2, 1);
      display: flex;
      flex-direction: column;
      box-shadow: -4px 0 24px rgba(0, 0, 0, 0.4);
      border-left: 1px solid #2A3040;

      &.open {
        transform: translateX(0);
      }
    }

    .panel-header {
      display: flex;
      align-items: center;
      padding: 16px 20px;
      border-bottom: 1px solid #2A3040;
      background: linear-gradient(135deg, #1a2035 0%, #1e2740 50%, #1a2035 100%);
      min-height: 68px;
      box-shadow: 0 2px 16px rgba(76, 154, 255, 0.06);
    }

    .panel-body {
      flex: 1;
      overflow-y: auto;
      padding: 16px 20px;

      &::-webkit-scrollbar {
        width: 6px;
      }
      &::-webkit-scrollbar-track {
        background: transparent;
      }
      &::-webkit-scrollbar-thumb {
        background: #3D4663;
        border-radius: 3px;
      }
    }

    .btn-close {
      margin-left: auto;
      background: transparent;
      border: 1px solid #3D4663;
      color: #8C95A6;
      width: 32px;
      height: 32px;
      border-radius: 6px;
      cursor: pointer;
      font-size: 14px;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.15s;

      &:hover {
        color: #FF5252;
        border-color: #FF5252;
      }
    }
  `]
})
export class SidePanelComponent {
  @Input() isOpen = false;
  @Output() close = new EventEmitter<void>();
}
