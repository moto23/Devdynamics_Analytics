import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';

import { IconComponent } from './icon.component';

/**
 * 404 page.
 *
 * Replaces a silent redirect to the landing page: quietly sending someone
 * somewhere else hides the fact that their link was wrong, which is worse than
 * saying so and offering the way back.
 */
@Component({
  selector: 'app-not-found',
  standalone: true,
  imports: [CommonModule, RouterLink, IconComponent],
  template: `
    <div class="wrap">
      <span class="glyph"><app-icon name="search" [size]="26"></app-icon></span>

      <p class="code">404</p>
      <h1>Page not found</h1>
      <p class="lede muted">
        <code>{{ attempted }}</code> does not exist. It may have been renamed, or the
        link may be incomplete.
      </p>

      <div class="actions">
        <a class="btn btn-primary" routerLink="/dashboard">Open dashboard</a>
        <a class="btn" routerLink="/">Back to home</a>
      </div>

      <nav class="suggestions" aria-label="Suggested pages">
        <a routerLink="/repositories">Repositories</a>
        <a routerLink="/contributors">Contributors</a>
        <a routerLink="/settings">Settings</a>
      </nav>
    </div>
  `,
  styles: [`
    :host { display: block; }

    .wrap {
      display: flex;
      flex-direction: column;
      align-items: center;
      text-align: center;
      gap: var(--space-3);
      padding: var(--space-8) var(--space-5);
      max-width: 560px;
      margin: 0 auto;
    }

    .glyph {
      display: grid;
      place-items: center;
      width: 60px; height: 60px;
      border-radius: var(--radius-lg);
      color: var(--accent);
      background: var(--accent-subtle);
      margin-bottom: var(--space-2);
    }

    .code {
      font-size: 13px;
      font-weight: 700;
      letter-spacing: 0.16em;
      color: var(--text-muted);
    }

    h1 { font-size: clamp(24px, 4vw, 32px); }

    .lede { font-size: 14.5px; line-height: 1.6; max-width: 46ch; }

    code {
      font-family: var(--font-mono);
      font-size: 0.92em;
      padding: 2px 6px;
      border-radius: 5px;
      background: var(--surface-3);
      color: var(--text-primary);
      overflow-wrap: anywhere;
    }

    .actions { display: flex; gap: var(--space-3); flex-wrap: wrap; justify-content: center; margin-top: var(--space-3); }

    .suggestions {
      display: flex;
      gap: var(--space-4);
      flex-wrap: wrap;
      justify-content: center;
      margin-top: var(--space-4);
      padding-top: var(--space-4);
      border-top: 1px solid var(--border);
      width: 100%;
      font-size: 13.5px;
    }

    /* Standalone links, so they meet the 24px minimum target size. */
    .suggestions a { display: inline-flex; align-items: center; min-height: 24px; }

    @media (max-width: 480px) {
      .actions { width: 100%; flex-direction: column; }
      .actions .btn { width: 100%; }
    }
  `]
})
export class NotFoundComponent {
  private readonly router = inject(Router);

  /** Shown so the reader can see exactly which path failed. */
  readonly attempted = this.router.url.split('?')[0];
}
