import {
  Component, ElementRef, EventEmitter, HostListener, Input, OnDestroy, Output, ViewChild, signal
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { IconComponent } from './icon.component';
import { OverlayPortalDirective, positionPanel, trackViewportChanges } from './overlay';

export interface ComboOption {
  value: string | null;
  label: string;
  /** Right-aligned secondary text, e.g. a count. */
  meta?: string;
}

/**
 * Searchable select with full keyboard support.
 *
 * The menu is rendered into <body> through the overlay portal rather than
 * inline. That is not cosmetic: our glass cards use backdrop-filter, which
 * makes an ancestor the containing block for `position: fixed` descendants, so
 * an inline menu resolved its coordinates against the card and appeared far
 * from its trigger. Escaping to <body> is what makes the coordinates mean what
 * they say, and also removes any chance of an ancestor's overflow clipping it.
 */
@Component({
  selector: 'app-combobox',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent, OverlayPortalDirective],
  template: `
    <div class="combo" #root>
      <button
        #trigger
        type="button"
        class="combo-trigger"
        role="combobox"
        [attr.aria-expanded]="open()"
        aria-haspopup="listbox"
        [attr.aria-controls]="open() ? panelId : null"
        [attr.aria-label]="ariaLabel || placeholder"
        (click)="toggle($event)"
        (keydown)="onTriggerKeydown($event)">
        <app-icon *ngIf="icon" [name]="icon" [size]="15"></app-icon>
        <span class="combo-value" [class.is-placeholder]="!selectedLabel">
          {{ selectedLabel || placeholder }}
        </span>
        <app-icon name="chevron-down" [size]="15" class="combo-caret"></app-icon>
      </button>
    </div>

    <!-- Lives at <body> level: outside every glass ancestor, above all content,
         and never part of page layout. -->
    <div
      *ngIf="open()"
      appOverlayPortal
      (ready)="onPanelReady($event)"
      #panel
      class="dd-panel"
      role="listbox"
      [id]="panelId"
      [style.top.px]="top()"
      [style.left.px]="left()"
      [style.width.px]="width()"
      [style.max-height.px]="maxHeight()"
      (click)="$event.stopPropagation()">

      <div class="dd-search" *ngIf="searchable">
        <app-icon name="search" [size]="14"></app-icon>
        <input
          type="text"
          [(ngModel)]="query"
          (ngModelChange)="onQueryChange()"
          (keydown)="onSearchKeydown($event)"
          [attr.placeholder]="searchPlaceholder"
          aria-label="Filter options" />
      </div>

      <div class="dd-list" (keydown)="onSearchKeydown($event)">
        <button
          type="button"
          *ngFor="let option of visible; let i = index"
          class="dd-option"
          role="option"
          [class.active]="i === activeIndex()"
          [attr.aria-selected]="option.value === value"
          (mouseenter)="activeIndex.set(i)"
          (click)="choose(option)">
          <span class="dd-label">{{ option.label }}</span>
          <span class="dd-meta" *ngIf="option.meta">{{ option.meta }}</span>
          <app-icon *ngIf="option.value === value" name="check" [size]="14" class="dd-tick"></app-icon>
        </button>

        <p class="dd-empty" *ngIf="visible.length === 0">No matches</p>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; min-width: 0; }
    .combo { position: relative; }

    .combo-trigger {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      width: 100%;
      height: 38px;
      padding: 0 var(--space-3);
      border-radius: var(--radius-md);
      border: 1px solid var(--border);
      background: var(--surface-2);
      color: var(--text-primary);
      font: inherit;
      font-size: 14px;
      cursor: pointer;
      transition: border-color var(--dur-fast) var(--ease), background var(--dur-fast) var(--ease);
    }
    .combo-trigger:hover { border-color: var(--border-strong); }
    .combo-trigger app-icon { color: var(--text-muted); flex: none; }

    .combo-value {
      flex: 1;
      min-width: 0;
      text-align: left;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .is-placeholder { color: var(--text-muted); }

    .combo-caret { transition: transform var(--dur) var(--ease); }
    :host([data-open='true']) .combo-caret { transform: rotate(180deg); }
  `],
  host: { '[attr.data-open]': 'open()' }
})
export class ComboboxComponent implements OnDestroy {

  @Input() options: ComboOption[] = [];
  @Input() value: string | null = null;
  @Input() placeholder = 'Select…';
  @Input() searchPlaceholder = 'Search…';
  @Input() searchable = true;
  @Input() icon?: string;
  @Input() ariaLabel?: string;

  @Output() valueChange = new EventEmitter<string | null>();

  @ViewChild('trigger') triggerRef!: ElementRef<HTMLButtonElement>;
  @ViewChild('panel') panelRef?: ElementRef<HTMLElement>;

  readonly open = signal(false);
  readonly activeIndex = signal(0);

  readonly top = signal(0);
  readonly left = signal(0);
  readonly width = signal(240);
  readonly maxHeight = signal(300);

  readonly panelId = `dd-${Math.random().toString(36).slice(2, 9)}`;

  query = '';

  /**
   * Filtered options as a stable field, not a getter.
   *
   * A getter returning a new array is re-evaluated on every change detection
   * pass, so *ngFor sees fresh identities and rebuilds continuously.
   */
  visible: ComboOption[] = [];

  private stopTracking?: () => void;
  private panelElement?: HTMLElement;

  get selectedLabel(): string | null {
    return this.options.find(o => o.value === this.value)?.label ?? null;
  }

  ngOnDestroy() {
    this.stopTracking?.();
  }

  toggle(event: MouseEvent) {
    event.stopPropagation();
    this.open() ? this.dismiss() : this.show();
  }

  private show() {
    this.query = '';
    this.applyFilter();
    this.activeIndex.set(Math.max(0, this.visible.findIndex(o => o.value === this.value)));

    this.open.set(true);
    this.stopTracking = trackViewportChanges(() => this.reposition());
  }

  dismiss(returnFocus = false) {
    if (!this.open()) return;

    this.open.set(false);
    this.panelElement = undefined;
    this.stopTracking?.();
    this.stopTracking = undefined;

    if (returnFocus) this.triggerRef?.nativeElement.focus();
  }

  /** Positions as soon as the panel exists, then moves focus into it. */
  onPanelReady(element: HTMLElement) {
    this.panelElement = element;
    this.reposition();

    // Queried from the panel rather than through ViewChild: the portal emits
    // from its own ngAfterViewInit, which runs before the host component's
    // view queries have been updated, so searchRef is not reliably set yet.
    // Without this, focus never enters the panel and no key handler fires —
    // every keyboard interaction silently does nothing.
    element.querySelector<HTMLInputElement>('.dd-search input')?.focus();
  }

  /** Re-anchors the panel; called on open and whenever the viewport moves. */
  private reposition() {
    const panel = this.panelElement ?? this.panelRef?.nativeElement;
    const trigger = this.triggerRef?.nativeElement;
    if (!panel || !trigger) return;

    const placement = positionPanel(trigger.getBoundingClientRect(), panel, {
      matchTriggerWidth: true,
      minWidth: 220
    });

    this.top.set(placement.top);
    this.left.set(placement.left);
    this.width.set(placement.width ?? 240);
    this.maxHeight.set(placement.maxHeight);
  }

  private applyFilter() {
    const term = this.query.trim().toLowerCase();

    this.visible = term
      ? this.options.filter(o => o.label.toLowerCase().includes(term))
      : [...this.options];
  }

  onQueryChange() {
    this.applyFilter();
    this.activeIndex.set(0);
    // The list shrinks as it filters, so the panel is re-measured.
    requestAnimationFrame(() => this.reposition());
  }

  choose(option: ComboOption) {
    this.value = option.value;
    this.valueChange.emit(option.value);
    this.dismiss(true);
  }

  onTriggerKeydown(event: KeyboardEvent) {
    if (['ArrowDown', 'Enter', ' '].includes(event.key) && !this.open()) {
      event.preventDefault();
      this.show();
    }
  }

  onSearchKeydown(event: KeyboardEvent) {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.activeIndex.set(Math.min(this.activeIndex() + 1, this.visible.length - 1));
        this.scrollActiveIntoView();
        break;

      case 'ArrowUp':
        event.preventDefault();
        this.activeIndex.set(Math.max(this.activeIndex() - 1, 0));
        this.scrollActiveIntoView();
        break;

      case 'Home':
        event.preventDefault();
        this.activeIndex.set(0);
        this.scrollActiveIntoView();
        break;

      case 'End':
        event.preventDefault();
        this.activeIndex.set(this.visible.length - 1);
        this.scrollActiveIntoView();
        break;

      case 'Enter':
        event.preventDefault();
        if (this.visible[this.activeIndex()]) this.choose(this.visible[this.activeIndex()]);
        break;

      case 'Escape':
        event.preventDefault();
        event.stopPropagation();
        this.dismiss(true);
        break;

      case 'Tab':
        this.dismiss();
        break;
    }
  }

  private scrollActiveIntoView() {
    requestAnimationFrame(() => {
      this.panelElement
        ?.querySelectorAll<HTMLElement>('.dd-option')[this.activeIndex()]
        ?.scrollIntoView({ block: 'nearest' });
    });
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (!this.open()) return;

    const target = event.target as Node;
    const insideTrigger = this.triggerRef?.nativeElement.contains(target);
    const insidePanel = this.panelRef?.nativeElement.contains(target);

    if (!insideTrigger && !insidePanel) this.dismiss();
  }

  @HostListener('document:keydown.escape')
  onEscape() { this.dismiss(true); }
}
