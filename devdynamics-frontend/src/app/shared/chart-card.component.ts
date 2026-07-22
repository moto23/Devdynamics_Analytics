import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

import { IconComponent } from './icon.component';

export interface LegendEntry {
  label: string;
  colorIndex: number;
}

/**
 * Frame for a chart: title, optional legend, and the loading / empty / content
 * states.
 *
 * The skeleton matches the final height so the card does not resize when data
 * arrives — the layout shift is what makes a dashboard feel cheap.
 */
@Component({
  selector: 'app-chart-card',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <section class="card chart-card" [class.animate-in]="!loading">
      <header class="chart-head">
        <div class="chart-titles">
          <h2>{{ title }}</h2>
          <p class="chart-sub muted" *ngIf="subtitle">{{ subtitle }}</p>
        </div>

        <!-- Identity is never colour alone: the legend pairs a swatch with a label. -->
        <ul class="legend" *ngIf="legend?.length && !loading && !isEmpty">
          <li *ngFor="let entry of legend">
            <span class="swatch" [style.background]="'var(--series-' + (entry.colorIndex + 1) + ')'"></span>
            {{ entry.label }}
          </li>
        </ul>
      </header>

      <div class="chart-body" [style.min-height.px]="height">
        <div class="skeleton chart-skeleton" *ngIf="loading" [style.height.px]="height"></div>

        <div class="chart-empty" *ngIf="!loading && isEmpty">
          <app-icon name="chart" [size]="22"></app-icon>
          <p>{{ emptyText }}</p>
          <p class="muted chart-empty-hint" *ngIf="emptyHint">{{ emptyHint }}</p>
        </div>

        <ng-content *ngIf="!loading && !isEmpty"></ng-content>
      </div>
    </section>
  `,
  styles: [`
    :host { display: block; min-width: 0; }

    .chart-card {
      padding: var(--space-5);
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
      height: 100%;
    }

    .chart-head {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--space-4);
      flex-wrap: wrap;
    }

    .chart-titles { min-width: 0; }
    .chart-sub { font-size: 13px; margin-top: 2px; }

    .legend {
      list-style: none;
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
      margin: 0;
      padding: 0;
      font-size: 12.5px;
      color: var(--text-secondary);
    }
    .legend li { display: flex; align-items: center; gap: 7px; }

    .swatch { width: 9px; height: 9px; border-radius: 3px; flex: none; }

    .chart-body { position: relative; }

    .chart-skeleton { width: 100%; border-radius: var(--radius-md); }

    .chart-empty {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: var(--space-2);
      height: 100%;
      min-height: inherit;
      text-align: center;
      color: var(--text-secondary);
      font-size: 14px;
    }
    .chart-empty app-icon { color: var(--text-muted); margin-bottom: var(--space-1); }
    .chart-empty-hint { font-size: 12.5px; }
  `]
})
export class ChartCardComponent {
  @Input({ required: true }) title!: string;
  @Input() subtitle?: string;
  @Input() height = 260;
  @Input() loading = false;
  @Input() isEmpty = false;
  @Input() emptyText = 'No data for the current filters';
  @Input() emptyHint?: string;
  @Input() legend?: LegendEntry[];
}
