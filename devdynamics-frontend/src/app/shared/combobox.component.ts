import {
  Component, ElementRef, EventEmitter, HostListener, Input, Output, ViewChild, signal
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { IconComponent } from './icon.component';

export interface ComboOption {
  value: string | null;
  label: string;
  /** Right-aligned secondary text, e.g. a count. */
  meta?: string;
  icon?: string;
}

/**
 * Searchable select with full keyboard support.
 *
 * Replaces the hand-rolled dropdowns: those had no search, no keyboard
 * navigation, and clipped inside scrolling containers. The menu here is
 * positioned with fixed coordinates measured from the trigger, which is what
 * stops an ancestor's overflow from cutting it off.
 */
@Component({
  selector: 'app-combobox',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    <div class="combo" #root>
      <button
        type="button"
        class="combo-trigger"
        role="combobox"
        [attr.aria-expanded]="open()"
        aria-haspopup="listbox"
        [attr.aria-label]="ariaLabel || placeholder"
        (click)="toggle($event)"
        (keydown)="onTriggerKeydown($event)">
        <app-icon *ngIf="icon" [name]="icon" [size]="15"></app-icon>
        <span class="combo-value" [class.is-placeholder]="!selectedLabel">
          {{ selectedLabel || placeholder }}
        </span>
        <app-icon name="chevron-down" [size]="15" class="combo-caret"></app-icon>
      </button>

      <div
        class="combo-menu"
        *ngIf="open()"
        role="listbox"
        [style.top.px]="menuTop()"
        [style.left.px]="menuLeft()"
        [style.width.px]="menuWidth()">

        <!-- Sticky so the search field stays reachable in a long list. -->
        <div class="combo-search" *ngIf="searchable">
          <app-icon name="search" [size]="14"></app-icon>
          <input
            #searchInput
            type="text"
            [(ngModel)]="query"
            (ngModelChange)="onQueryChange()"
            (keydown)="onSearchKeydown($event)"
            [attr.placeholder]="searchPlaceholder"
            aria-label="Filter options" />
        </div>

        <div class="combo-list" #list>
          <button
            type="button"
            *ngFor="let option of filtered(); let i = index"
            class="combo-option"
            role="option"
            [class.active]="i === activeIndex()"
            [attr.aria-selected]="option.value === value"
            (mouseenter)="activeIndex.set(i)"
            (click)="choose(option)">
            <span class="combo-option-label">{{ option.label }}</span>
            <span class="combo-option-meta" *ngIf="option.meta">{{ option.meta }}</span>
            <app-icon *ngIf="option.value === value" name="check" [size]="14" class="combo-tick"></app-icon>
          </button>

          <p class="combo-empty" *ngIf="filtered().length === 0">No matches</p>
        </div>
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
    .combo[data-open='true'] .combo-caret { transform: rotate(180deg); }

    /* Fixed positioning keeps the menu out of any ancestor's overflow. */
    .combo-menu {
      position: fixed;
      z-index: 70;
      border-radius: var(--radius-md);
      background: var(--surface-2);
      border: 1px solid var(--border-strong);
      box-shadow: var(--shadow-lg);
      overflow: hidden;
      animation: menu-in var(--dur-fast) var(--ease) both;
    }
    @keyframes menu-in {
      from { opacity: 0; transform: translateY(-4px); }
      to   { opacity: 1; transform: none; }
    }

    .combo-search {
      position: sticky;
      top: 0;
      display: flex;
      align-items: center;
      gap: var(--space-2);
      padding: var(--space-3);
      border-bottom: 1px solid var(--border);
      background: var(--surface-2);
    }
    .combo-search app-icon { color: var(--text-muted); flex: none; }
    .combo-search input {
      flex: 1;
      min-width: 0;
      border: 0;
      background: transparent;
      color: var(--text-primary);
      font: inherit;
      font-size: 13.5px;
      outline: none;
    }
    .combo-search input::placeholder { color: var(--text-muted); }

    .combo-list { max-height: 260px; overflow-y: auto; padding: var(--space-2); }

    .combo-option {
      display: flex;
      align-items: center;
      gap: var(--space-3);
      width: 100%;
      padding: 8px var(--space-3);
      border: 0;
      border-radius: var(--radius-sm);
      background: transparent;
      color: var(--text-secondary);
      font: inherit;
      font-size: 13.5px;
      text-align: left;
      cursor: pointer;
    }
    .combo-option.active { background: var(--surface-3); color: var(--text-primary); }

    .combo-option-label { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .combo-option-meta { font-size: 12px; color: var(--text-muted); flex: none; }
    .combo-tick { color: var(--accent); flex: none; }

    .combo-empty { padding: var(--space-4); text-align: center; font-size: 13px; color: var(--text-muted); }
  `],
  host: { '[attr.data-open]': 'open()' }
})
export class ComboboxComponent {

  @Input() options: ComboOption[] = [];
  @Input() value: string | null = null;
  @Input() placeholder = 'Select…';
  @Input() searchPlaceholder = 'Search…';
  @Input() searchable = true;
  @Input() icon?: string;
  @Input() ariaLabel?: string;

  @Output() valueChange = new EventEmitter<string | null>();

  @ViewChild('root') rootRef!: ElementRef<HTMLElement>;
  @ViewChild('searchInput') searchRef?: ElementRef<HTMLInputElement>;
  @ViewChild('list') listRef?: ElementRef<HTMLElement>;

  readonly open = signal(false);
  readonly activeIndex = signal(0);
  readonly menuTop = signal(0);
  readonly menuLeft = signal(0);
  readonly menuWidth = signal(240);

  query = '';

  get selectedLabel(): string | null {
    return this.options.find(o => o.value === this.value)?.label ?? null;
  }

  filtered(): ComboOption[] {
    const term = this.query.trim().toLowerCase();
    if (!term) return this.options;
    return this.options.filter(o => o.label.toLowerCase().includes(term));
  }

  toggle(event: MouseEvent) {
    event.stopPropagation();
    this.open() ? this.dismiss() : this.show();
  }

  private show() {
    this.query = '';
    this.activeIndex.set(Math.max(0, this.filtered().findIndex(o => o.value === this.value)));
    this.position();
    this.open.set(true);

    queueMicrotask(() => this.searchRef?.nativeElement.focus());
  }

  dismiss() {
    this.open.set(false);
  }

  /**
   * Places the menu, then corrects once it has rendered.
   *
   * The provisional pass uses an estimate so there is no visible jump; the
   * correction uses the real height, because an estimate that disagrees with
   * the rendered size is exactly how a menu ends up clipped.
   */
  private position() {
    const rect = this.rootRef.nativeElement.getBoundingClientRect();
    const width = Math.max(rect.width, 220);

    this.menuWidth.set(width);
    this.menuLeft.set(Math.max(8, Math.min(rect.left, window.innerWidth - width - 8)));
    this.menuTop.set(rect.bottom + 6);

    requestAnimationFrame(() => {
      const menu = document.querySelector<HTMLElement>('.combo-menu');
      if (!menu) return;

      const height = menu.getBoundingClientRect().height;
      const margin = 8;

      this.menuTop.set(
        rect.bottom + 6 + height > window.innerHeight - margin
          ? Math.max(margin, rect.top - height - 6)
          : rect.bottom + 6
      );
    });
  }

  choose(option: ComboOption) {
    this.value = option.value;
    this.valueChange.emit(option.value);
    this.dismiss();
  }

  onTriggerKeydown(event: KeyboardEvent) {
    if (['ArrowDown', 'Enter', ' '].includes(event.key) && !this.open()) {
      event.preventDefault();
      this.show();
    }
  }

  onSearchKeydown(event: KeyboardEvent) {
    const items = this.filtered();

    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.activeIndex.set(Math.min(this.activeIndex() + 1, items.length - 1));
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
        this.activeIndex.set(items.length - 1);
        this.scrollActiveIntoView();
        break;

      case 'Enter':
        event.preventDefault();
        if (items[this.activeIndex()]) this.choose(items[this.activeIndex()]);
        break;

      case 'Escape':
        event.preventDefault();
        this.dismiss();
        break;
    }
  }

  onQueryChange() {
    this.activeIndex.set(0);
  }

  private scrollActiveIntoView() {
    queueMicrotask(() => {
      const list = this.listRef?.nativeElement;
      const active = list?.querySelectorAll<HTMLElement>('.combo-option')[this.activeIndex()];
      active?.scrollIntoView({ block: 'nearest' });
    });
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (this.open() && !this.rootRef?.nativeElement.contains(event.target as Node)) {
      this.dismiss();
    }
  }

  // A fixed-position menu would otherwise drift away from its trigger.
  @HostListener('window:resize')
  @HostListener('window:scroll')
  onViewportChange() {
    if (this.open()) this.dismiss();
  }
}
