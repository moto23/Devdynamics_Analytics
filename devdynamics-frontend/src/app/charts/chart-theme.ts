/**
 * Bridges the CSS design tokens into Chart.js.
 *
 * Chart.js needs concrete colour values, but the source of truth is the CSS
 * custom properties in styles.css — so they are read from the live computed
 * style rather than duplicated here. That keeps one definition of the palette
 * and makes charts re-theme correctly when the mode changes.
 *
 * The series colours are validated as an ORDER (adjacent-pair CVD separation),
 * so slots are assigned by index and never cycled or generated.
 */

export interface ChartTheme {
  surface: string;
  textPrimary: string;
  textSecondary: string;
  textMuted: string;
  gridline: string;
  axis: string;
  border: string;
  accent: string;
  series: string[];
  heat: string[];
  heatEmpty: string;
}

function readToken(styles: CSSStyleDeclaration, name: string, fallback: string): string {
  return styles.getPropertyValue(name).trim() || fallback;
}

export function readChartTheme(): ChartTheme {
  const styles = getComputedStyle(document.documentElement);

  return {
    surface:       readToken(styles, '--surface-1', '#141b18'),
    textPrimary:   readToken(styles, '--text-primary', '#ffffff'),
    textSecondary: readToken(styles, '--text-secondary', '#b9c4bf'),
    textMuted:     readToken(styles, '--text-muted', '#8a938f'),
    gridline:      readToken(styles, '--gridline', '#232d29'),
    axis:          readToken(styles, '--axis', '#38433e'),
    border:        readToken(styles, '--border', 'rgba(255,255,255,0.10)'),
    accent:        readToken(styles, '--accent', '#199e70'),
    series: [
      readToken(styles, '--series-1', '#199e70'),
      readToken(styles, '--series-2', '#3987e5'),
      readToken(styles, '--series-3', '#d95926'),
      readToken(styles, '--series-4', '#9085e9'),
      readToken(styles, '--series-5', '#c98500'),
      readToken(styles, '--series-6', '#d55181')
    ],
    heat: [
      readToken(styles, '--heat-1', '#1a6b49'),
      readToken(styles, '--heat-2', '#23926a'),
      readToken(styles, '--heat-3', '#2cba88'),
      readToken(styles, '--heat-4', '#63e3af')
    ],
    heatEmpty: readToken(styles, '--heat-empty', '#1b2320')
  };
}

/** Series colours are assigned by fixed slot; past the last slot, fold to "Other". */
export function seriesColor(theme: ChartTheme, index: number): string {
  return theme.series[index] ?? theme.textMuted;
}

/** Converts a hex colour to rgba, for fills under lines. */
export function withAlpha(color: string, alpha: number): string {
  const hex = color.trim();

  if (!hex.startsWith('#')) {
    return hex;
  }

  const full = hex.length === 4
    ? `#${hex[1]}${hex[1]}${hex[2]}${hex[2]}${hex[3]}${hex[3]}`
    : hex;

  const r = parseInt(full.slice(1, 3), 16);
  const g = parseInt(full.slice(3, 5), 16);
  const b = parseInt(full.slice(5, 7), 16);

  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

/**
 * Shared scale, grid and tooltip styling.
 *
 * Grid and axes are deliberately recessive so the data reads first: hairline
 * horizontal gridlines only, no vertical grid, no axis border.
 */
export function baseChartOptions(theme: ChartTheme) {
  return {
    responsive: true,
    maintainAspectRatio: false,
    interaction: { mode: 'index' as const, intersect: false },
    layout: { padding: { top: 8, right: 8, bottom: 0, left: 0 } },
    animation: { duration: 420, easing: 'easeOutQuart' as const },
    plugins: {
      legend: { display: false },
      tooltip: { enabled: false }   // replaced by an HTML tooltip; see base-chart
    },
    scales: {
      x: {
        grid: { display: false },
        border: { display: false },
        ticks: {
          color: theme.textMuted,
          font: { family: fontFamily(), size: 11 },
          maxRotation: 0,
          autoSkipPadding: 24
        }
      },
      y: {
        beginAtZero: true,
        grid: { color: theme.gridline, drawTicks: false },
        border: { display: false },
        ticks: {
          color: theme.textMuted,
          font: { family: fontFamily(), size: 11 },
          padding: 8,
          maxTicksLimit: 6
        }
      }
    }
  };
}

export function fontFamily(): string {
  return getComputedStyle(document.documentElement)
    .getPropertyValue('--font-sans').trim()
    || 'system-ui, sans-serif';
}
