import { Injectable, signal } from '@angular/core';

export type Theme = 'dark' | 'light';

const STORAGE_KEY = 'devdynamics.theme';

/**
 * Theme selection and persistence.
 *
 * Dark is the default. Until the user chooses explicitly, the OS preference
 * applies through CSS (`prefers-color-scheme`) with no `data-theme` attribute
 * present. Once chosen, the attribute is stamped on the root element and wins
 * over the OS in both directions.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {

  /** Null means "no explicit choice yet — follow the OS". */
  private readonly stored = signal<Theme | null>(this.read());

  readonly theme = signal<Theme>(this.resolve());

  constructor() {
    this.apply();

    // Track the OS while the user has not expressed a preference.
    window.matchMedia?.('(prefers-color-scheme: light)')
      .addEventListener?.('change', () => {
        if (this.stored() === null) {
          this.theme.set(this.resolve());
          this.apply();
        }
      });
  }

  toggle() {
    this.set(this.theme() === 'dark' ? 'light' : 'dark');
  }

  set(theme: Theme) {
    this.stored.set(theme);
    this.theme.set(theme);

    try {
      localStorage.setItem(STORAGE_KEY, theme);
    } catch {
      // Private browsing can reject writes; the theme still applies for the session.
    }

    this.apply();
  }

  private resolve(): Theme {
    const stored = this.stored();
    if (stored) return stored;

    return window.matchMedia?.('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
  }

  private apply() {
    const root = document.documentElement;

    if (this.stored() === null) {
      root.removeAttribute('data-theme');
    } else {
      root.setAttribute('data-theme', this.theme());
    }

    document.querySelector('meta[name="theme-color"]')
      ?.setAttribute('content', this.theme() === 'dark' ? '#0d1210' : '#f4f7f5');
  }

  private read(): Theme | null {
    try {
      const value = localStorage.getItem(STORAGE_KEY);
      return value === 'dark' || value === 'light' ? value : null;
    } catch {
      return null;
    }
  }
}
