import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';

import { ToastService } from '../core/toast.service';
import { IconComponent } from './icon.component';

@Component({
  selector: 'app-toast-host',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <!-- Polite: toasts confirm actions the user just took, so they should not
         interrupt whatever a screen reader is currently announcing. -->
    <div class="host" role="status" aria-live="polite" aria-atomic="false">
      <div class="toast" *ngFor="let t of toasts()" [class]="'toast toast-' + t.kind">
        <span class="toast-icon">
          <span class="spinner" *ngIf="t.kind === 'progress'"></span>
          <app-icon *ngIf="t.kind === 'success'" name="check" [size]="15"></app-icon>
          <app-icon *ngIf="t.kind === 'error'" name="alert" [size]="15"></app-icon>
          <app-icon *ngIf="t.kind === 'info'" name="clock" [size]="15"></app-icon>
        </span>

        <div class="toast-body">
          <p class="toast-title">{{ t.title }}</p>
          <p class="toast-detail" *ngIf="t.detail">{{ t.detail }}</p>
        </div>

        <button
          type="button"
          class="toast-close"
          *ngIf="t.kind !== 'progress'"
          (click)="toastService.dismiss(t.id)"
          aria-label="Dismiss notification">
          <app-icon name="close" [size]="14"></app-icon>
        </button>
      </div>
    </div>
  `,
  styles: [`
    .host {
      position: fixed;
      right: var(--space-5);
      bottom: var(--space-5);
      z-index: 90;
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      max-width: min(380px, calc(100vw - var(--space-5) * 2));
      pointer-events: none;
    }

    .toast {
      display: flex;
      align-items: flex-start;
      gap: var(--space-3);
      padding: var(--space-3) var(--space-4);
      border-radius: var(--radius-md);
      background: var(--surface-2);
      border: 1px solid var(--border-strong);
      box-shadow: var(--shadow-lg);
      pointer-events: auto;
      animation: toast-in var(--dur) var(--ease) both;
    }

    @keyframes toast-in {
      from { opacity: 0; transform: translateY(8px) scale(0.98); }
      to   { opacity: 1; transform: none; }
    }

    .toast-icon { flex: none; margin-top: 1px; }

    /* Status colour always arrives with an icon and text, never alone. */
    .toast-success .toast-icon { color: var(--status-good); }
    .toast-error   .toast-icon { color: var(--status-critical); }
    .toast-info    .toast-icon { color: var(--accent); }

    .toast-body { min-width: 0; flex: 1; }
    .toast-title { font-size: 13.5px; font-weight: 600; color: var(--text-primary); }
    .toast-detail { font-size: 12.5px; color: var(--text-secondary); margin-top: 2px; }

    .toast-close {
      flex: none;
      width: 22px; height: 22px;
      display: grid; place-items: center;
      border: 0; border-radius: 6px;
      background: transparent;
      color: var(--text-muted);
      cursor: pointer;
    }
    .toast-close:hover { background: var(--surface-3); color: var(--text-primary); }

    .spinner {
      display: block;
      width: 14px; height: 14px;
      border-radius: 50%;
      border: 2px solid var(--border-strong);
      border-top-color: var(--accent);
      animation: spin 0.7s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }

    @media (max-width: 639px) {
      .host { left: var(--space-4); right: var(--space-4); bottom: var(--space-4); max-width: none; }
    }
  `]
})
export class ToastHostComponent {
  readonly toastService = inject(ToastService);
  readonly toasts = this.toastService.toasts;
}
