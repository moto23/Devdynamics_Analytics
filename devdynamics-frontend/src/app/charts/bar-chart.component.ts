import {
  Component, ElementRef, Input, OnChanges, OnDestroy, ViewChild, effect, inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Chart, BarController, BarElement, LinearScale, CategoryScale, Tooltip } from 'chart.js';

import { ThemeService } from '../core/theme.service';
import { ChartTheme, baseChartOptions, readChartTheme, seriesColor } from './chart-theme';
import { hideTooltip, makeTooltipHandler } from './chart-tooltip';

Chart.register(BarController, BarElement, LinearScale, CategoryScale, Tooltip);

@Component({
  selector: 'app-bar-chart',
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
export class BarChartComponent implements OnChanges, OnDestroy {

  @ViewChild('canvas', { static: true }) canvasRef!: ElementRef<HTMLCanvasElement>;

  @Input() labels: string[] = [];
  @Input() data: number[] = [];
  @Input() label = '';
  @Input() height = 260;
  @Input() ariaLabel = 'Bar chart';
  @Input() valueSuffix = '';

  /** Horizontal bars keep long category names readable without rotation. */
  @Input() horizontal = false;

  /**
   * One hue for magnitude comparison (the default), or a distinct palette slot
   * per bar when the bars are identities rather than a ranking.
   */
  @Input() colorPerBar = false;

  private chart?: Chart;
  private theme: ChartTheme = readChartTheme();

  constructor() {
    const themeService = inject(ThemeService);

    effect(() => {
      themeService.theme();
      queueMicrotask(() => {
        this.theme = readChartTheme();
        this.render();
      });
    });
  }

  ngOnChanges() { this.render(); }

  ngOnDestroy() {
    this.chart?.destroy();
    hideTooltip();
  }

  private render() {
    const canvas = this.canvasRef?.nativeElement;
    if (!canvas) return;

    this.chart?.destroy();

    const colors = this.colorPerBar
      ? this.data.map((_, i) => seriesColor(this.theme, i))
      : seriesColor(this.theme, 0);

    const options = baseChartOptions(this.theme);

    this.chart = new Chart(canvas, {
      type: 'bar',
      data: {
        labels: this.labels,
        datasets: [{
          label: this.label,
          data: this.data,
          backgroundColor: colors,
          hoverBackgroundColor: colors,
          // Rounded data-ends, anchored to the baseline so the bar still reads
          // as starting at zero.
          borderRadius: 4,
          borderSkipped: false,
          maxBarThickness: this.horizontal ? 18 : 40
        }]
      },
      options: {
        ...options,
        indexAxis: this.horizontal ? 'y' : 'x',
        interaction: { mode: 'nearest', intersect: true },
        scales: this.horizontal
          ? {
              x: {
                beginAtZero: true,
                grid: { color: this.theme.gridline, drawTicks: false },
                border: { display: false },
                ticks: { color: this.theme.textMuted, font: { size: 11 }, maxTicksLimit: 6 }
              },
              y: {
                grid: { display: false },
                border: { display: false },
                ticks: { color: this.theme.textSecondary, font: { size: 12 }, padding: 6 }
              }
            }
          : options.scales,
        plugins: {
          ...options.plugins,
          tooltip: {
            enabled: false,
            external: makeTooltipHandler(
              () => this.theme,
              value => `${value.toLocaleString()}${this.valueSuffix}`
            )
          }
        }
      } as any
    });
  }
}
