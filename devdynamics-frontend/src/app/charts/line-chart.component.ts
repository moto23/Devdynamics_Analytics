import {
  Component, ElementRef, Input, OnChanges, OnDestroy, ViewChild, effect, inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  Chart, LineController, LineElement, PointElement, LinearScale, CategoryScale, Filler, Tooltip
} from 'chart.js';

import { ThemeService } from '../core/theme.service';
import {
  ChartTheme, baseChartOptions, readChartTheme, seriesColor, withAlpha
} from './chart-theme';
import { crosshairPlugin, hideTooltip, makeTooltipHandler } from './chart-tooltip';

// Registered explicitly rather than via Chart.register(...registerables) so the
// bundle only carries the controllers actually used.
Chart.register(
  LineController, LineElement, PointElement, LinearScale, CategoryScale, Filler, Tooltip, crosshairPlugin
);

export interface LineSeries {
  label: string;
  data: number[];
  /** Fixed palette slot. Never generated, never cycled. */
  colorIndex?: number;
  fill?: boolean;
}

@Component({
  selector: 'app-line-chart',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="chart-host" [style.height.px]="height">
      <canvas #canvas role="img" [attr.aria-label]="ariaLabel"></canvas>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .chart-host { position: relative; width: 100%; }
    canvas { display: block; }
  `]
})
export class LineChartComponent implements OnChanges, OnDestroy {

  @ViewChild('canvas', { static: true }) canvasRef!: ElementRef<HTMLCanvasElement>;

  @Input() labels: string[] = [];
  @Input() series: LineSeries[] = [];
  @Input() height = 260;
  @Input() ariaLabel = 'Line chart';
  @Input() valueSuffix = '';
  @Input() smooth = true;

  private chart?: Chart;
  private theme: ChartTheme = readChartTheme();

  constructor() {
    const themeService = inject(ThemeService);

    // Re-read the tokens and repaint whenever the mode changes; the dark
    // palette is a different set of steps, not a filter over the light one.
    effect(() => {
      themeService.theme();
      queueMicrotask(() => {
        this.theme = readChartTheme();
        this.render();
      });
    });
  }

  ngOnChanges() {
    this.render();
  }

  ngOnDestroy() {
    this.chart?.destroy();
    hideTooltip();
  }

  private render() {
    const canvas = this.canvasRef?.nativeElement;
    if (!canvas) return;

    this.chart?.destroy();

    const datasets = this.series.map((s, i) => {
      const color = seriesColor(this.theme, s.colorIndex ?? i);

      return {
        label: s.label,
        data: s.data,
        borderColor: color,
        // A single series gets a soft fill for weight; multiple series do not,
        // because overlapping translucent fills muddy the colours.
        backgroundColor: s.fill ?? this.series.length === 1
          ? withAlpha(color, 0.14)
          : 'transparent',
        fill: s.fill ?? this.series.length === 1,
        borderWidth: 2,
        tension: this.smooth ? 0.34 : 0,
        pointRadius: 0,
        pointHoverRadius: 5,
        pointHoverBorderWidth: 2,
        pointHoverBackgroundColor: color,
        // A ring in the surface colour separates the marker from the line.
        pointHoverBorderColor: this.theme.surface,
        pointHitRadius: 18
      };
    });

    const options = baseChartOptions(this.theme);

    this.chart = new Chart(canvas, {
      type: 'line',
      data: { labels: this.labels, datasets },
      options: {
        ...options,
        plugins: {
          ...options.plugins,
          tooltip: {
            enabled: false,
            external: makeTooltipHandler(
              () => this.theme,
              value => `${formatNumber(value)}${this.valueSuffix}`
            )
          },
          ddCrosshair: { color: this.theme.axis }
        } as any
      }
    });
  }
}

function formatNumber(value: number): string {
  if (!isFinite(value)) return '—';
  return Number.isInteger(value) ? value.toLocaleString() : value.toFixed(1);
}
