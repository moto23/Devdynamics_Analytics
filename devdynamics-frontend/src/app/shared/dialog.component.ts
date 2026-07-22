import {
  AfterViewInit, Component, ElementRef, EventEmitter, HostListener, Input, Output, ViewChild
} from '@angular/core';
import { CommonModule } from '@angular/common';

import { IconComponent } from './icon.component';

/**
 * Modal dialog.
 *
 * Accessibility is the point of having a shared component rather than ad-hoc
 * markup: focus moves in on open and is trapped while open, Escape closes,
 * the backdrop closes, and focus returns to whatever opened it. Getting that
 * wrong once per dialog is how keyboard users end up stranded.
 */
@Component({
  selector: 'app-dialog',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <div class="backdrop" (click)="onBackdrop($event)">
      <div
        #panel
        class="panel card-glass"
        role="dialog"
        aria-modal="true"
        [attr.aria-labelledby]="titleId"
        [style.max-width.px]="width">

        <header class="dialog-head">
          <div>
            <h2 [id]="titleId">{{ title }}</h2>
            <p class="dialog-sub muted" *ngIf="subtitle">{{ subtitle }}</p>
          </div>
          <button type="button" class="btn btn-ghost btn-icon" (click)="close.emit()" aria-label="Close dialog">
            <app-icon name="close" [size]="17"></app-icon>
          </button>
        </header>

        <div class="dialog-body"><ng-content></ng-content></div>

        <footer class="dialog-foot"><ng-content select="[dialogFooter]"></ng-content></footer>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }

    .backdrop {
      position: fixed;
      inset: 0;
      z-index: 80;
      display: grid;
      place-items: center;
      padding: var(--space-5);
      background: rgba(0, 0, 0, 0.55);
      backdrop-filter: blur(3px);
      animation: fade var(--dur-fast) var(--ease) both;
      overflow-y: auto;
    }
    @keyframes fade { from { opacity: 0; } to { opacity: 1; } }

    .panel {
      width: 100%;
      max-height: calc(100dvh - var(--space-6) * 2);
      display: flex;
      flex-direction: column;
      animation: rise var(--dur) var(--ease) both;
    }
    @keyframes rise {
      from { opacity: 0; transform: translateY(10px) scale(0.985); }
      to   { opacity: 1; transform: none; }
    }

    .dialog-head {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--space-4);
      padding: var(--space-5) var(--space-5) var(--space-4);
      border-bottom: 1px solid var(--border);
    }
    .dialog-head h2 { font-size: 18px; }
    .dialog-sub { font-size: 13px; margin-top: 3px; }

    /* Long forms scroll inside the dialog, never the page behind it. */
    .dialog-body { padding: var(--space-5); overflow-y: auto; flex: 1; }

    .dialog-foot:not(:empty) {
      display: flex;
      justify-content: flex-end;
      gap: var(--space-3);
      padding: var(--space-4) var(--space-5);
      border-top: 1px solid var(--border);
    }

    @media (max-width: 639px) {
      .backdrop { padding: var(--space-3); align-items: flex-end; }
      .panel { max-height: 88dvh; }
      .dialog-foot:not(:empty) { flex-direction: column-reverse; }
      .dialog-foot ::ng-deep .btn { width: 100%; }
    }
  `]
})
export class DialogComponent implements AfterViewInit {

  @Input({ required: true }) title!: string;
  @Input() subtitle?: string;
  @Input() width = 520;

  @Output() close = new EventEmitter<void>();

  @ViewChild('panel') panelRef!: ElementRef<HTMLElement>;

  readonly titleId = `dlg-${Math.random().toString(36).slice(2, 9)}`;

  private previouslyFocused: HTMLElement | null = null;

  ngAfterViewInit() {
    this.previouslyFocused = document.activeElement as HTMLElement;

    // Prefer the first real control; fall back to the panel so focus is never
    // left behind on the page underneath.
    const target = this.focusable()[0] ?? this.panelRef.nativeElement;
    queueMicrotask(() => target.focus());

    document.body.style.overflow = 'hidden';
  }

  ngOnDestroy() {
    document.body.style.overflow = '';
    this.previouslyFocused?.focus?.();
  }

  @HostListener('document:keydown.escape')
  onEscape() { this.close.emit(); }

  /** Keeps Tab cycling inside the dialog while it is open. */
  @HostListener('document:keydown.tab', ['$event'])
  onTab(event: KeyboardEvent) {
    const items = this.focusable();
    if (items.length === 0) return;

    const first = items[0];
    const last = items[items.length - 1];
    const active = document.activeElement;

    if (event.shiftKey && active === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }

  onBackdrop(event: MouseEvent) {
    if (event.target === event.currentTarget) this.close.emit();
  }

  private focusable(): HTMLElement[] {
    if (!this.panelRef) return [];

    return Array.from(this.panelRef.nativeElement.querySelectorAll<HTMLElement>(
      'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
    )).filter(el => el.offsetParent !== null);
  }
}
