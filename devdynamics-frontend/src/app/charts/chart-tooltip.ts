import { Chart, TooltipModel } from 'chart.js';

import { ChartTheme } from './chart-theme';

/**
 * HTML tooltip for Chart.js.
 *
 * The built-in canvas tooltip cannot be styled with the design tokens, so it is
 * disabled and this external handler renders a real DOM node instead — which
 * also means it inherits the app's typography and respects the theme.
 *
 * A colour swatch sits beside every value so a series is never identified by
 * colour alone.
 */

const TOOLTIP_ID = 'dd-chart-tooltip';

function ensureElement(): HTMLElement {
  let el = document.getElementById(TOOLTIP_ID);

  if (!el) {
    el = document.createElement('div');
    el.id = TOOLTIP_ID;
    el.setAttribute('role', 'tooltip');
    el.style.cssText = [
      'position:fixed',
      'pointer-events:none',
      'opacity:0',
      'z-index:60',
      'transition:opacity 120ms ease, transform 120ms ease',
      'border-radius:12px',
      'padding:10px 12px',
      'font-size:12.5px',
      'line-height:1.45',
      'min-width:120px',
      'box-shadow:0 8px 28px rgba(0,0,0,0.28)'
    ].join(';');
    document.body.appendChild(el);
  }

  return el;
}

export function makeTooltipHandler(
  getTheme: () => ChartTheme,
  formatValue: (value: number, datasetIndex: number) => string = v => String(v)
) {
  return (context: { chart: Chart; tooltip: TooltipModel<any> }) => {
    const el = ensureElement();
    const { chart, tooltip } = context;
    const theme = getTheme();

    if (tooltip.opacity === 0) {
      el.style.opacity = '0';
      return;
    }

    el.style.background = theme.surface;
    el.style.border = `1px solid ${theme.border}`;
    el.style.color = theme.textPrimary;

    const title = tooltip.title?.[0] ?? '';

    const rows = (tooltip.dataPoints ?? []).map(point => {
      const color = (point.dataset as any).borderColor ?? (point.dataset as any).backgroundColor;
      const swatch = typeof color === 'string' ? color : theme.accent;
      const label = point.dataset.label ?? '';
      const value = formatValue(point.parsed.y ?? point.parsed, point.datasetIndex);

      return `
        <div style="display:flex;align-items:center;gap:8px;justify-content:space-between;margin-top:4px">
          <span style="display:flex;align-items:center;gap:7px;color:${theme.textSecondary};min-width:0">
            <span style="width:9px;height:9px;border-radius:3px;background:${swatch};flex:none"></span>
            <span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${escapeHtml(label)}</span>
          </span>
          <span style="font-weight:650;font-variant-numeric:tabular-nums">${escapeHtml(value)}</span>
        </div>`;
    }).join('');

    el.innerHTML = `
      <div style="font-weight:650;font-size:12px;color:${theme.textMuted};letter-spacing:0.01em">
        ${escapeHtml(title)}
      </div>
      ${rows}`;

    const rect = chart.canvas.getBoundingClientRect();
    const x = rect.left + tooltip.caretX;
    const y = rect.top + tooltip.caretY;

    // Flip to the left of the cursor near the right edge so the tooltip is
    // never clipped by the viewport.
    const width = el.offsetWidth || 160;
    const flip = x + width + 24 > window.innerWidth;

    el.style.opacity = '1';
    el.style.left = `${flip ? x - width - 14 : x + 14}px`;
    el.style.top = `${Math.max(8, y - el.offsetHeight / 2)}px`;
  };
}

export function hideTooltip() {
  const el = document.getElementById(TOOLTIP_ID);
  if (el) el.style.opacity = '0';
}

function escapeHtml(value: string): string {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

/**
 * Draws a vertical crosshair at the active point on line charts, so the reader
 * can trace a value back to the x-axis.
 */
export const crosshairPlugin = {
  id: 'ddCrosshair',
  afterDatasetsDraw(chart: Chart, _args: unknown, options: { color?: string }) {
    const active = chart.tooltip?.getActiveElements?.() ?? [];
    if (!active.length) return;

    const { ctx, chartArea } = chart;
    const x = active[0].element.x;

    ctx.save();
    ctx.beginPath();
    ctx.setLineDash([4, 4]);
    ctx.lineWidth = 1;
    ctx.strokeStyle = options?.color ?? 'rgba(255,255,255,0.22)';
    ctx.moveTo(x, chartArea.top);
    ctx.lineTo(x, chartArea.bottom);
    ctx.stroke();
    ctx.restore();
  }
};
