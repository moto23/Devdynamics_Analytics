import { AfterViewInit, Directive, ElementRef, EventEmitter, OnDestroy, OnInit, Output } from '@angular/core';

/**
 * Moves its host element to <body> so it escapes every containing block.
 *
 * This exists because of a specific CSS rule: an ancestor with `backdrop-filter`,
 * `filter` or `transform` becomes the containing block for `position: fixed`
 * descendants. Our glass cards use backdrop-filter, so a "fixed" menu inside one
 * resolved its coordinates against the card rather than the viewport — the menu
 * appeared hundreds of pixels away from its trigger and overflowed the page.
 *
 * Relocating the node to <body> puts it outside any such ancestor, so fixed
 * positioning means what it says. Angular still owns the node through its
 * embedded view, so it is destroyed normally when the *ngIf closes.
 */
@Directive({
  selector: '[appOverlayPortal]',
  standalone: true
})
export class OverlayPortalDirective implements OnInit, AfterViewInit, OnDestroy {

  /**
   * Fires once the panel is in the DOM *with its content rendered*, so it can
   * be measured at its true size.
   *
   * Emitted from ngAfterViewInit, not ngOnInit: at ngOnInit the host element
   * exists but its *ngFor children do not, so measuring there returns the
   * height of an empty panel and the list ends up needlessly short. And it is
   * not requestAnimationFrame either — a frame scheduled when the panel opens
   * can run before Angular renders the *ngIf at all.
   */
  @Output() ready = new EventEmitter<HTMLElement>();

  constructor(private readonly host: ElementRef<HTMLElement>) {}

  ngOnInit() {
    // Relocate immediately so the panel is never painted in the wrong place.
    document.body.appendChild(this.host.nativeElement);
  }

  ngAfterViewInit() {
    this.ready.emit(this.host.nativeElement);
  }

  ngOnDestroy() {
    // Angular removes the node with its view, but if it has already been
    // relocated the parent is <body>; detach defensively so nothing is orphaned.
    this.host.nativeElement.remove();
  }
}

export interface PanelPlacement {
  top: number;
  left: number;
  width?: number;
  maxHeight: number;
}

export interface PanelOptions {
  /** Match the trigger's width instead of using the panel's natural width. */
  matchTriggerWidth?: boolean;
  minWidth?: number;
  /** Align the panel's right edge to the trigger's right edge. */
  alignEnd?: boolean;
  gap?: number;
  margin?: number;
}

/**
 * Places a panel against its trigger, inside the viewport.
 *
 * Measures the rendered panel rather than estimating: an estimate that
 * disagrees with the real size is how a panel ends up clipped. Flips above the
 * trigger when the space below is smaller, clamps to the viewport on both axes,
 * and returns a max-height so a long list scrolls internally instead of
 * running off screen.
 */
export function positionPanel(
  trigger: DOMRect,
  panel: HTMLElement,
  options: PanelOptions = {}
): PanelPlacement {
  const gap = options.gap ?? 6;
  const margin = options.margin ?? 8;

  const viewportWidth = document.documentElement.clientWidth;
  const viewportHeight = document.documentElement.clientHeight;

  const width = options.matchTriggerWidth
    ? Math.max(trigger.width, options.minWidth ?? 0)
    : Math.max(panel.offsetWidth, options.minWidth ?? 0);

  // Measure the panel's natural height by ignoring any max-height already set.
  const previousMax = panel.style.maxHeight;
  panel.style.maxHeight = 'none';
  const naturalHeight = panel.offsetHeight;
  panel.style.maxHeight = previousMax;

  const spaceBelow = viewportHeight - trigger.bottom - gap - margin;
  const spaceAbove = trigger.top - gap - margin;

  // Prefer below; flip only when above genuinely offers more room.
  const placeAbove = naturalHeight > spaceBelow && spaceAbove > spaceBelow;

  const maxHeight = Math.max(140, Math.min(naturalHeight, placeAbove ? spaceAbove : spaceBelow));
  const height = Math.min(naturalHeight, maxHeight);

  const top = placeAbove
    ? Math.max(margin, trigger.top - gap - height)
    : Math.min(trigger.bottom + gap, viewportHeight - height - margin);

  const preferredLeft = options.alignEnd ? trigger.right - width : trigger.left;

  const left = Math.max(
    margin,
    Math.min(preferredLeft, viewportWidth - width - margin)
  );

  return { top, left, width, maxHeight };
}

/**
 * Calls back whenever the page scrolls or the viewport resizes, so an open
 * panel follows its trigger instead of drifting away from it.
 *
 * Capture phase is required: scrolling usually happens in a nested container,
 * and those events do not bubble to window.
 */
export function trackViewportChanges(handler: () => void): () => void {
  window.addEventListener('scroll', handler, true);
  window.addEventListener('resize', handler);

  return () => {
    window.removeEventListener('scroll', handler, true);
    window.removeEventListener('resize', handler);
  };
}
