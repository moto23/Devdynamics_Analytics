import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

/**
 * Inline SVG icons.
 *
 * Deliberately hand-rolled rather than an icon package: the set is small, and
 * bundling a whole icon library for a dozen glyphs is not worth the weight on a
 * bundle already over budget. All icons share a 24-unit grid and inherit
 * `currentColor`.
 */
@Component({
  selector: 'app-icon',
  standalone: true,
  imports: [CommonModule],
  template: `
    <svg
      [attr.width]="size"
      [attr.height]="size"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      [attr.stroke-width]="strokeWidth"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      <ng-container [ngSwitch]="name">
        <g *ngSwitchCase="'grid'"><rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/></g>
        <g *ngSwitchCase="'repo'"><path d="M4 19.5V5a2 2 0 0 1 2-2h13v18H6.5A2.5 2.5 0 0 1 4 16.5Z"/><path d="M6 17h13"/></g>
        <g *ngSwitchCase="'users'"><circle cx="9" cy="8" r="3.2"/><path d="M2.5 20a6.5 6.5 0 0 1 13 0"/><path d="M16.5 5.3a3.2 3.2 0 0 1 0 5.9"/><path d="M18 14.4a6.5 6.5 0 0 1 3.5 5.6"/></g>
        <g *ngSwitchCase="'settings'"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-2.9 1.2V21a2 2 0 1 1-4 0v-.1A1.7 1.7 0 0 0 7 19.4a1.7 1.7 0 0 0-1.9.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0-1.2-2.9H1a2 2 0 1 1 0-4h.1A1.7 1.7 0 0 0 2.6 7a1.7 1.7 0 0 0-.3-1.9l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.9.3H7a1.7 1.7 0 0 0 1-1.5V1a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 2.9 1.2 1.7 1.7 0 0 0 1.9-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.9V7a1.7 1.7 0 0 0 1.5 1H23a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z"/></g>
        <g *ngSwitchCase="'sun'"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></g>
        <g *ngSwitchCase="'moon'"><path d="M20 14.5A8.5 8.5 0 1 1 9.5 4a6.7 6.7 0 0 0 10.5 10.5Z"/></g>
        <g *ngSwitchCase="'refresh'"><path d="M3 12a9 9 0 0 1 15.3-6.4L21 8"/><path d="M21 3v5h-5"/><path d="M21 12a9 9 0 0 1-15.3 6.4L3 16"/><path d="M3 21v-5h5"/></g>
        <g *ngSwitchCase="'menu'"><path d="M4 6h16M4 12h16M4 18h16"/></g>
        <g *ngSwitchCase="'close'"><path d="M18 6 6 18M6 6l12 12"/></g>
        <g *ngSwitchCase="'chevron-right'"><path d="m9 18 6-6-6-6"/></g>
        <g *ngSwitchCase="'chevron-down'"><path d="m6 9 6 6 6-6"/></g>
        <g *ngSwitchCase="'search'"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></g>
        <g *ngSwitchCase="'commit'"><circle cx="12" cy="12" r="3.5"/><path d="M2 12h6.5M15.5 12H22"/></g>
        <g *ngSwitchCase="'pr'"><circle cx="6" cy="6" r="2.5"/><circle cx="6" cy="18" r="2.5"/><circle cx="18" cy="18" r="2.5"/><path d="M6 8.5v7"/><path d="M18 15.5V11a3 3 0 0 0-3-3h-4"/><path d="m13 5.5-2 2.5 2 2.5"/></g>
        <g *ngSwitchCase="'clock'"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3.5 2"/></g>
        <g *ngSwitchCase="'chart'"><path d="M3 3v18h18"/><path d="m7 15 3.5-4 3 2.5L20 7"/></g>
        <g *ngSwitchCase="'check'"><path d="m4 12.5 5 5L20 6.5"/></g>
        <g *ngSwitchCase="'alert'"><path d="M12 3 2 20h20L12 3Z"/><path d="M12 9v5M12 17.2v.1"/></g>
        <g *ngSwitchCase="'external'"><path d="M14 4h6v6"/><path d="M20 4 10 14"/><path d="M19 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1h5"/></g>
        <g *ngSwitchCase="'database'"><ellipse cx="12" cy="5.5" rx="8" ry="3"/><path d="M4 5.5v13c0 1.7 3.6 3 8 3s8-1.3 8-3v-13"/><path d="M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"/></g>
        <g *ngSwitchCase="'github'"><path d="M9 19c-4.3 1.4-4.3-2.5-6-3m12 5v-3.9a3.4 3.4 0 0 0-.9-2.6c3-.3 6.2-1.5 6.2-6.7A5.2 5.2 0 0 0 19 4.8a4.9 4.9 0 0 0-.1-3.6s-1.1-.3-3.7 1.4a12.6 12.6 0 0 0-6.6 0C6 .9 4.9 1.2 4.9 1.2a4.9 4.9 0 0 0-.1 3.6 5.2 5.2 0 0 0-1.4 3.6c0 5.2 3.2 6.4 6.2 6.7a3.4 3.4 0 0 0-.9 2.6V22"/></g>
      </ng-container>
    </svg>
  `,
  styles: [`
    :host { display: inline-flex; align-items: center; justify-content: center; flex: none; }
  `]
})
export class IconComponent {
  @Input({ required: true }) name!: string;
  @Input() size = 18;
  @Input() strokeWidth = 1.8;
}
