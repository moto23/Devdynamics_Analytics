import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

import { IconComponent } from './icon.component';

/**
 * A single headline figure.
 *
 * A stat tile, not a one-bar chart: for one current value the number itself is
 * the clearest form. The value uses proportional figures because it stands
 * alone rather than aligning in a column.
 */
@Component({
  selector: 'app-stat-tile',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <div class="card-glass tile" [class.animate-in]="!loading">
      <ng-container *ngIf="!loading; else skeletonTpl">
        <div class="tile-head">
          <span class="tile-icon" *ngIf="icon">
            <app-icon [name]="icon" [size]="15"></app-icon>
          </span>
          <span class="label">{{ label }}</span>
        </div>

        <div class="tile-value">
          {{ display }}<span class="tile-unit" *ngIf="unit">{{ unit }}</span>
        </div>

        <p class="tile-note muted" *ngIf="note">{{ note }}</p>
      </ng-container>

      <ng-template #skeletonTpl>
        <div class="skeleton" style="width:52%;height:11px;border-radius:5px"></div>
        <div class="skeleton" style="width:68%;height:30px;border-radius:8px;margin-top:14px"></div>
      </ng-template>
    </div>
  `,
  styles: [`
    :host { display: block; min-width: 0; }

    .tile {
      padding: var(--space-4) var(--space-5);
      min-height: 108px;
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      transition: transform var(--dur) var(--ease), border-color var(--dur) var(--ease);
    }
    .tile:hover { transform: translateY(-2px); border-color: var(--border-strong); }

    .tile-head { display: flex; align-items: center; gap: var(--space-2); }

    .tile-icon {
      display: grid;
      place-items: center;
      width: 24px; height: 24px;
      border-radius: 7px;
      color: var(--accent);
      background: var(--accent-subtle);
      flex: none;
    }

    .tile-value {
      font-size: clamp(26px, 3vw, 32px);
      font-weight: 680;
      letter-spacing: -0.028em;
      line-height: 1.1;
      color: var(--text-primary);
    }

    .tile-unit {
      font-size: 0.5em;
      font-weight: 600;
      color: var(--text-muted);
      margin-left: 3px;
    }

    .tile-note { font-size: 12.5px; margin-top: auto; }
  `]
})
export class StatTileComponent {
  @Input({ required: true }) label!: string;
  @Input() value: number | string = 0;
  @Input() unit?: string;
  @Input() icon?: string;
  @Input() note?: string;
  @Input() loading = false;

  get display(): string {
    return typeof this.value === 'number'
      ? this.value.toLocaleString()
      : this.value;
  }
}
