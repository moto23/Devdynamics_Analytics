import { Component, Input, OnChanges } from '@angular/core';
import { CommonModule } from '@angular/common';

import { HeatmapDay } from '../services/graphql.service';

interface Cell {
  date: string;
  count: number;
  level: number;
  label: string;
}

/**
 * Calendar contribution heatmap.
 *
 * Built from CSS grid rather than a canvas so every cell stays a real DOM node
 * with its own tooltip and keyboard focus.
 *
 * Colour is an ordinal ramp of a single hue, validated for monotonic lightness
 * and step separation. "No activity" is deliberately NOT the palest green — it
 * is an empty state, so it gets a surface tint with a hairline instead. That
 * keeps the four active levels distinguishable rather than spending one of
 * them on zero.
 */
@Component({
  selector: 'app-heatmap',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="heatmap">
      <div class="grid-wrap scroll-x">
        <div class="months" aria-hidden="true">
          <span *ngFor="let m of months" [style.grid-column]="m.column">{{ m.label }}</span>
        </div>

        <div class="grid" role="img" [attr.aria-label]="summaryLabel">
          <span
            *ngFor="let cell of cells"
            class="cell"
            [attr.data-level]="cell.level"
            [attr.title]="cell.label"></span>
        </div>
      </div>

      <div class="legend">
        <span class="muted">{{ total.toLocaleString() }} contributions</span>
        <span class="scale">
          Less
          <span class="cell" data-level="0"></span>
          <span class="cell" data-level="1"></span>
          <span class="cell" data-level="2"></span>
          <span class="cell" data-level="3"></span>
          <span class="cell" data-level="4"></span>
          More
        </span>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }

    .grid-wrap { padding-bottom: var(--space-2); }

    .months {
      display: grid;
      grid-auto-flow: column;
      grid-template-columns: repeat(var(--weeks, 53), 13px);
      font-size: 11px;
      color: var(--text-muted);
      margin-bottom: 5px;
      min-width: max-content;
    }

    .grid {
      display: grid;
      grid-template-rows: repeat(7, 11px);
      grid-auto-flow: column;
      grid-auto-columns: 11px;
      gap: 2px;
      min-width: max-content;
    }

    .cell {
      width: 11px;
      height: 11px;
      border-radius: 2px;
      background: var(--heat-empty);
      /* A hairline keeps empty cells visible as cells rather than as gaps. */
      box-shadow: inset 0 0 0 1px var(--border);
    }
    .cell[data-level='1'] { background: var(--heat-1); box-shadow: none; }
    .cell[data-level='2'] { background: var(--heat-2); box-shadow: none; }
    .cell[data-level='3'] { background: var(--heat-3); box-shadow: none; }
    .cell[data-level='4'] { background: var(--heat-4); box-shadow: none; }

    .legend {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-4);
      flex-wrap: wrap;
      margin-top: var(--space-3);
      font-size: 12.5px;
    }

    .scale { display: inline-flex; align-items: center; gap: 4px; color: var(--text-muted); }
    .scale .cell { width: 10px; height: 10px; }
  `]
})
export class HeatmapComponent implements OnChanges {

  @Input() days: HeatmapDay[] = [];

  /** Weeks to render, ending today. */
  @Input() weeks = 53;

  cells: Cell[] = [];
  months: { label: string; column: number }[] = [];
  total = 0;

  get summaryLabel(): string {
    return `Contribution heatmap: ${this.total} contributions over the last ${this.weeks} weeks`;
  }

  ngOnChanges() {
    this.build();
  }

  private build() {
    const counts = new Map<string, number>();

    for (const day of this.days ?? []) {
      counts.set(day.date.slice(0, 10), day.count);
    }

    this.total = [...counts.values()].reduce((sum, n) => sum + n, 0);

    // Thresholds come from the data, so a quiet repository still shows
    // contrast instead of one flat colour.
    const active = [...counts.values()].filter(n => n > 0).sort((a, b) => a - b);
    const quantile = (q: number) =>
      active.length ? active[Math.min(active.length - 1, Math.floor(active.length * q))] : 1;

    const t1 = 1;
    const t2 = Math.max(2, quantile(0.5));
    const t3 = Math.max(t2 + 1, quantile(0.8));
    const t4 = Math.max(t3 + 1, quantile(0.95));

    // Start on the Sunday that begins the window, so weeks form clean columns.
    const end = new Date();
    end.setHours(0, 0, 0, 0);

    const start = new Date(end);
    start.setDate(start.getDate() - (this.weeks * 7 - 1));
    start.setDate(start.getDate() - start.getDay());

    const cells: Cell[] = [];
    const months: { label: string; column: number }[] = [];
    let lastMonth = -1;

    const cursor = new Date(start);
    let column = 1;

    while (cursor <= end) {
      const key = toKey(cursor);
      const count = counts.get(key) ?? 0;

      cells.push({
        date: key,
        count,
        level: count === 0 ? 0 : count < t2 ? 1 : count < t3 ? 2 : count < t4 ? 3 : 4,
        label: `${count} ${count === 1 ? 'contribution' : 'contributions'} on ${cursor.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })}`
      });

      if (cursor.getDay() === 0) {
        column = Math.floor(cells.length / 7) + 1;

        if (cursor.getMonth() !== lastMonth) {
          lastMonth = cursor.getMonth();
          months.push({ label: cursor.toLocaleDateString(undefined, { month: 'short' }), column });
        }
      }

      cursor.setDate(cursor.getDate() + 1);
    }

    this.cells = cells;
    this.months = months;
  }
}

function toKey(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}
