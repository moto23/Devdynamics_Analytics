import { Injectable, inject } from '@angular/core';

import { ToastService } from './toast.service';

export interface CsvColumn<T> {
  header: string;
  value: (row: T) => string | number | null | undefined;
}

/**
 * Exports the current view.
 *
 * CSV and single-chart PNG are produced natively at zero bundle cost. The
 * page-level image and PDF need html2canvas and jsPDF, which together are
 * larger than the entire application — so they are loaded with a dynamic
 * import the first time someone actually exports, and never reach the initial
 * bundle.
 *
 * XLSX is deliberately absent: the only library that produces it is several
 * hundred kilobytes, and every spreadsheet opens the CSV this already writes.
 */
@Injectable({ providedIn: 'root' })
export class ExportService {

  private readonly toast = inject(ToastService);

  // =====================================================================
  // CSV
  // =====================================================================

  exportCsv<T>(rows: T[], columns: CsvColumn<T>[], filename: string) {
    if (rows.length === 0) {
      this.toast.info('Nothing to export', 'The current filters return no rows.');
      return;
    }

    const lines = [
      columns.map(c => escapeCsv(c.header)).join(','),
      ...rows.map(row => columns.map(c => escapeCsv(c.value(row))).join(','))
    ];

    // The BOM makes Excel read the file as UTF-8 instead of the local codepage,
    // which otherwise mangles non-ASCII contributor names.
    this.download(
      new Blob(['﻿' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8;' }),
      `${filename}.csv`
    );

    this.toast.success('CSV exported', `${rows.length} rows written.`);
  }

  // =====================================================================
  // Images
  // =====================================================================

  /** Chart.js can serialise its own canvas, so no library is required. */
  exportChartPng(canvas: HTMLCanvasElement, filename: string, background: string) {
    // The canvas is transparent; compositing onto the surface colour stops the
    // exported image looking broken on a white background.
    const output = document.createElement('canvas');
    output.width = canvas.width;
    output.height = canvas.height;

    const context = output.getContext('2d');
    if (!context) {
      this.toast.error('Export failed', 'This browser did not provide a canvas context.');
      return;
    }

    context.fillStyle = background;
    context.fillRect(0, 0, output.width, output.height);
    context.drawImage(canvas, 0, 0);

    output.toBlob(blob => {
      if (blob) {
        this.download(blob, `${filename}.png`);
        this.toast.success('Chart exported', 'Saved as PNG.');
      }
    }, 'image/png');
  }

  async exportElementPng(element: HTMLElement, filename: string, background: string) {
    const handle = this.toast.progress('Rendering image…', 'Preparing the export tool.');

    try {
      const canvas = await this.renderElement(element, background);

      await new Promise<void>(resolve =>
        canvas.toBlob(blob => {
          if (blob) this.download(blob, `${filename}.png`);
          resolve();
        }, 'image/png')
      );

      handle.succeed('Dashboard exported', 'Saved as PNG.');
    } catch (error) {
      console.error('PNG export failed:', error);
      handle.fail('Export failed', 'The dashboard could not be rendered.');
    }
  }

  async exportElementPdf(element: HTMLElement, filename: string, background: string, subtitle: string) {
    const handle = this.toast.progress('Building PDF…', 'Preparing the export tool.');

    try {
      const canvas = await this.renderElement(element, background);
      const { jsPDF } = await import('jspdf');

      // Landscape A4 fits a dashboard's aspect ratio far better than portrait.
      const pdf = new jsPDF({ orientation: 'landscape', unit: 'pt', format: 'a4' });

      const pageWidth = pdf.internal.pageSize.getWidth();
      const pageHeight = pdf.internal.pageSize.getHeight();
      const margin = 28;
      const headerHeight = 46;

      pdf.setFontSize(14);
      pdf.text('DevDynamics — Analytics', margin, margin + 6);
      pdf.setFontSize(9);
      pdf.setTextColor(120);
      pdf.text(subtitle, margin, margin + 22);

      const available = {
        width: pageWidth - margin * 2,
        height: pageHeight - margin * 2 - headerHeight
      };

      // Scale to fit rather than stretch, so charts keep their proportions.
      const scale = Math.min(available.width / canvas.width, available.height / canvas.height);
      const width = canvas.width * scale;
      const height = canvas.height * scale;

      pdf.addImage(
        canvas.toDataURL('image/png'),
        'PNG',
        margin + (available.width - width) / 2,
        margin + headerHeight,
        width,
        height
      );

      pdf.save(`${filename}.pdf`);
      handle.succeed('Dashboard exported', 'Saved as PDF.');
    } catch (error) {
      console.error('PDF export failed:', error);
      handle.fail('Export failed', 'The dashboard could not be rendered.');
    }
  }

  /** Loaded on demand; html2canvas never enters the initial bundle. */
  private async renderElement(element: HTMLElement, background: string): Promise<HTMLCanvasElement> {
    const html2canvas = (await import('html2canvas')).default;

    return html2canvas(element, {
      backgroundColor: background,
      scale: Math.min(2, window.devicePixelRatio || 1),
      logging: false,
      useCORS: true,
      // Avatars are served cross-origin and would taint the canvas.
      ignoreElements: node => node.tagName === 'IMG' && !(node as HTMLImageElement).complete
    });
  }

  private download(blob: Blob, filename: string) {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');

    link.href = url;
    link.download = filename;
    link.click();

    // Revoke on the next tick so the download has started.
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
}

function escapeCsv(value: unknown): string {
  if (value === null || value === undefined) return '';

  const text = String(value);

  // A leading =, +, - or @ is executed as a formula by spreadsheet software,
  // so it is prefixed with a quote before quoting the field.
  const guarded = /^[=+\-@]/.test(text) ? `'${text}` : text;

  return /[",\r\n]/.test(guarded) ? `"${guarded.replace(/"/g, '""')}"` : guarded;
}

/** Builds a filename that records the filters the data was exported under. */
export function exportFilename(base: string, parts: (string | null | undefined)[]): string {
  const suffix = parts
    .filter(Boolean)
    .map(part => String(part).replace(/[^a-z0-9]+/gi, '-').replace(/^-|-$/g, '').toLowerCase())
    .join('_');

  const stamp = new Date().toISOString().slice(0, 10);

  return [base, suffix, stamp].filter(Boolean).join('_');
}
