import { Injectable, signal } from '@angular/core';

export type ToastKind = 'success' | 'error' | 'info' | 'progress';

export interface Toast {
  id: number;
  kind: ToastKind;
  title: string;
  detail?: string;
  /** Progress toasts persist until explicitly resolved. */
  sticky?: boolean;
}

/**
 * Transient feedback for actions whose result would otherwise be invisible —
 * queuing a sync, saving settings, exporting a file.
 *
 * Deliberately not used for data-loading states: those belong inline as
 * skeletons, where the user is already looking.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {

  readonly toasts = signal<Toast[]>([]);

  private nextId = 1;

  success(title: string, detail?: string) { return this.push('success', title, detail); }
  error(title: string, detail?: string)   { return this.push('error', title, detail); }
  info(title: string, detail?: string)    { return this.push('info', title, detail); }

  /** Shows an indeterminate toast and returns a handle to resolve it. */
  progress(title: string, detail?: string) {
    const id = this.push('progress', title, detail, true);

    return {
      succeed: (t: string, d?: string) => { this.dismiss(id); this.success(t, d); },
      fail:    (t: string, d?: string) => { this.dismiss(id); this.error(t, d); },
      dismiss: () => this.dismiss(id)
    };
  }

  dismiss(id: number) {
    this.toasts.update(list => list.filter(t => t.id !== id));
  }

  private push(kind: ToastKind, title: string, detail?: string, sticky = false): number {
    const id = this.nextId++;

    this.toasts.update(list => [...list, { id, kind, title, detail, sticky }]);

    if (!sticky) {
      // Errors linger: they usually need reading, not glancing at.
      setTimeout(() => this.dismiss(id), kind === 'error' ? 7000 : 4000);
    }

    return id;
  }
}
