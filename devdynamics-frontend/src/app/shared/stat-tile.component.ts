import { Component, Input, OnChanges } from '@angular/core';
import { CommonModule } from '@angular/common';

import { IconComponent } from './icon.component';

/**
 * A single headline figure.
 *
 * A stat tile, not a one-bar chart: for one current value the number itself is
 * the clearest form.
 *
 * The sparkline and delta are optional and only ever rendered from real series
 * data. Nothing here synthesises a trend to fill space — a tile with no series
 * simply shows its number.
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

        <div class="tile-figure">
          <div class="tile-value">
            {{ display }}<span class="tile-unit" *ngIf="unit">{{ unit }}</span>
          </div>

          <!-- Decorative: the number beside it carries the meaning. -->
          <svg
            class="spark"
            *ngIf="sparkPath"
            viewBox="0 0 100 32"
            preserveAspectRatio="none"
            aria-hidden="true"
            focusable="false">
            <path class="spark-area" [attr.d]="sparkArea"></path>
            <path class="spark-line" [attr.d]="sparkPath"></path>
          </svg>
        </div>

        <div class="tile-foot">
          <span class="delta" *ngIf="deltaLabel" [attr.data-dir]="deltaDirection">
            <app-icon [name]="deltaDirection === 'down' ? 'chevron-down' : 'chevron-down'"
                      [size]="12" class="delta-arrow"></app-icon>
            {{ deltaLabel }}
          </span>
          <span class="tile-note muted" *ngIf="note">{{ note }}</span>
        </div>
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
      min-height: 118px;
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

    .tile-figure {
      display: flex;
      align-items: flex-end;
      justify-content: space-between;
      gap: var(--space-3);
      min-width: 0;
    }

    .tile-value {
      font-size: clamp(24px, 2.8vw, 30px);
      font-weight: 680;
      letter-spacing: -0.028em;
      line-height: 1.1;
      color: var(--text-primary);
      min-width: 0;
    }

    .tile-unit { font-size: 0.5em; font-weight: 600; color: var(--text-muted); margin-left: 3px; }

    .spark { width: 84px; height: 30px; flex: none; overflow: visible; }
    .spark-line { fill: none; stroke: var(--accent); stroke-width: 1.75; stroke-linecap: round; stroke-linejoin: round; vector-effect: non-scaling-stroke; }
    .spark-area { fill: var(--accent-subtle); stroke: none; }

    .tile-foot {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      flex-wrap: wrap;
      margin-top: auto;
      min-height: 18px;
    }

    /* Direction is carried by an arrow and a signed number, not colour alone. */
    .delta {
      display: inline-flex;
      align-items: center;
      gap: 3px;
      font-size: 12.5px;
      font-weight: 600;
      font-variant-numeric: tabular-nums;
      color: var(--text-secondary);
    }
    .delta[data-dir='up'] { color: var(--status-good); }
    .delta[data-dir='down'] { color: var(--status-serious); }
    .delta[data-dir='up'] .delta-arrow { transform: rotate(180deg); }

    .tile-note { font-size: 12.5px; }
  `]
})
export class StatTileComponent implements OnChanges {

  @Input({ required: true }) label!: string;
  @Input() value: number | string = 0;
  @Input() unit?: string;
  @Input() icon?: string;
  @Input() note?: string;
  @Input() loading = false;

  /** Real series for the sparkline. Omit it and no sparkline is drawn. */
  @Input() series?: number[];

  /**
   * Percentage change against the preceding period of equal length.
   * Only supplied when a date range makes a comparison meaningful.
   */
  @Input() deltaPercent?: number | null;

  sparkPath = '';
  sparkArea = '';
  deltaLabel = '';
  deltaDirection: 'up' | 'down' | 'flat' = 'flat';

  ngOnChanges() {
    this.buildSpark();
    this.buildDelta();
  }

  get display(): string {
    return typeof this.value === 'number' ? this.value.toLocaleString() : this.value;
  }

  private buildSpark() {
    const data = this.series ?? [];

    // Two points is the minimum that can describe a trend.
    if (data.length < 2) {
      this.sparkPath = '';
      this.sparkArea = '';
      return;
    }

    const max = Math.max(...data);
    const min = Math.min(...data);
    const range = max - min || 1;

    const points = data.map((value, index) => {
      const x = (index / (data.length - 1)) * 100;
      const y = 30 - ((value - min) / range) * 26 + 1;
      return `${x.toFixed(2)},${y.toFixed(2)}`;
    });

    this.sparkPath = `M${points.join(' L')}`;
    this.sparkArea = `M0,32 L${points.join(' L')} L100,32 Z`;
  }

  private buildDelta() {
    if (this.deltaPercent === null || this.deltaPercent === undefined || !isFinite(this.deltaPercent)) {
      this.deltaLabel = '';
      return;
    }

    const rounded = Math.round(this.deltaPercent);

    this.deltaDirection = rounded > 0 ? 'up' : rounded < 0 ? 'down' : 'flat';
    this.deltaLabel = `${rounded > 0 ? '+' : ''}${rounded}%`;
  }
}
