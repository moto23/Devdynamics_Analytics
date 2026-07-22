import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';

import { IconComponent } from './icon.component';

/**
 * Table building blocks.
 *
 * Composed pieces rather than one generic table component: every table here
 * has different cells and actions, and a configuration-driven table would end
 * up with an escape hatch per column anyway.
 */

/** Sortable column header. Renders a real button so it is keyboard reachable. */
@Component({
  selector: 'app-sort-header',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <button
      type="button"
      class="sort-header"
      [class.active]="active"
      (click)="toggle.emit()"
      [attr.aria-sort]="active ? (descending ? 'descending' : 'ascending') : 'none'">
      <ng-content></ng-content>
      <span class="sort-arrow" [class.desc]="descending" [class.visible]="active">
        <app-icon name="chevron-down" [size]="13"></app-icon>
      </span>
    </button>
  `,
  styles: [`
    :host { display: block; }
    .sort-header {
      display: inline-flex;
      align-items: center;
      gap: 5px;
      /* The label itself is only ~17px tall; the vertical padding is what
         carries the button to the 24px minimum target size. */
      padding: 4px 0;
      min-height: 24px;
      border: 0;
      background: transparent;
      color: inherit;
      font: inherit;
      cursor: pointer;
      white-space: nowrap;
    }
    .sort-header:hover { color: var(--text-primary); }
    .sort-header.active { color: var(--text-primary); }

    .sort-arrow {
      display: inline-flex;
      opacity: 0;
      transition: opacity var(--dur-fast) var(--ease), transform var(--dur-fast) var(--ease);
    }
    .sort-header:hover .sort-arrow { opacity: 0.45; }
    .sort-arrow.visible { opacity: 1; color: var(--accent); }
    .sort-arrow.desc { transform: rotate(180deg); }
  `]
})
export class SortHeaderComponent {
  @Input() active = false;
  @Input() descending = true;
  @Output() toggle = new EventEmitter<void>();
}

/** Cursor-based pager. Exposes only what the API supports: next and previous. */
@Component({
  selector: 'app-paginator',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <div class="paginator" *ngIf="hasNext || hasPrevious || total !== null">
      <p class="paginator-count muted" *ngIf="total !== null">
        <ng-container *ngIf="total > 0">
          Showing {{ shown }} of {{ total.toLocaleString() }}
        </ng-container>
        <ng-container *ngIf="total === 0">No results</ng-container>
      </p>

      <div class="paginator-actions">
        <button type="button" class="btn btn-sm" [disabled]="!hasPrevious || loading" (click)="previous.emit()">
          <app-icon name="chevron-right" [size]="14" class="flip"></app-icon>
          Previous
        </button>
        <button type="button" class="btn btn-sm" [disabled]="!hasNext || loading" (click)="next.emit()">
          Next
          <app-icon name="chevron-right" [size]="14"></app-icon>
        </button>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .paginator {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3);
      flex-wrap: wrap;
      padding-top: var(--space-4);
      margin-top: var(--space-2);
      border-top: 1px solid var(--border);
    }
    .paginator-count { font-size: 13px; }
    .paginator-actions { display: flex; gap: var(--space-2); margin-left: auto; }
    .flip { transform: rotate(180deg); }
  `]
})
export class PaginatorComponent {
  @Input() hasNext = false;
  @Input() hasPrevious = false;
  @Input() total: number | null = null;
  @Input() shown = 0;
  @Input() loading = false;

  @Output() next = new EventEmitter<void>();
  @Output() previous = new EventEmitter<void>();
}

/** Sync/state badge. Colour is always accompanied by a label. */
@Component({
  selector: 'app-status-badge',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <span class="badge" [class]="'badge tone-' + tone">
      <span class="dot" *ngIf="!icon"></span>
      <app-icon *ngIf="icon" [name]="icon" [size]="12"></app-icon>
      {{ label }}
    </span>
  `,
  styles: [`
    :host { display: inline-flex; }
    .badge {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      height: 22px;
      padding: 0 9px;
      border-radius: 999px;
      font-size: 12px;
      font-weight: 550;
      white-space: nowrap;
      border: 1px solid transparent;
    }
    .dot { width: 6px; height: 6px; border-radius: 50%; background: currentColor; flex: none; }

    .tone-neutral { color: var(--text-secondary); background: var(--surface-3); }
    .tone-good    { color: var(--status-good);     background: color-mix(in srgb, var(--status-good) 14%, transparent); }
    .tone-warning { color: var(--status-warning);  background: color-mix(in srgb, var(--status-warning) 16%, transparent); }
    .tone-serious { color: var(--status-serious);  background: color-mix(in srgb, var(--status-serious) 16%, transparent); }
    .tone-critical{ color: var(--status-critical); background: color-mix(in srgb, var(--status-critical) 14%, transparent); }
    .tone-accent  { color: var(--accent);          background: var(--accent-subtle); }
  `]
})
export class StatusBadgeComponent {
  @Input({ required: true }) label!: string;
  @Input() tone: 'neutral' | 'good' | 'warning' | 'serious' | 'critical' | 'accent' = 'neutral';
  @Input() icon?: string;
}

/** Shared empty state so every "nothing here" reads the same way. */
@Component({
  selector: 'app-empty-state',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <div class="empty">
      <span class="empty-icon"><app-icon [name]="icon" [size]="22"></app-icon></span>
      <h3>{{ title }}</h3>
      <p class="muted" *ngIf="message">{{ message }}</p>
      <ng-content></ng-content>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .empty {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--space-2);
      padding: var(--space-7) var(--space-5);
      text-align: center;
    }
    .empty-icon {
      display: grid;
      place-items: center;
      width: 46px; height: 46px;
      margin-bottom: var(--space-2);
      border-radius: var(--radius-md);
      color: var(--text-muted);
      background: var(--surface-2);
    }
    .empty h3 { font-size: 16px; }
    .empty p { font-size: 13.5px; max-width: 42ch; }
    .empty ::ng-deep .btn { margin-top: var(--space-3); }
  `]
})
export class EmptyStateComponent {
  @Input({ required: true }) title!: string;
  @Input() message?: string;
  @Input() icon = 'search';
}
