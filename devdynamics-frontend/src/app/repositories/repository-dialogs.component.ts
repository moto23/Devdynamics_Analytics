import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { DialogComponent } from '../shared/dialog.component';
import { IconComponent } from '../shared/icon.component';
import { GraphqlService, TrackedRepository } from '../services/graphql.service';
import { AdminService } from '../core/admin.service';
import { ToastService } from '../core/toast.service';

/**
 * Add a repository.
 *
 * The name is validated by the API against the provider before anything is
 * stored, so a typo is reported here rather than surfacing later as a failed
 * background sync.
 */
@Component({
  selector: 'app-add-repository-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, DialogComponent, IconComponent],
  template: `
    <app-dialog
      title="Track a repository"
      subtitle="Any public repository the configured provider can see."
      [width]="520"
      (close)="close.emit()">

      <form id="addRepoForm" (ngSubmit)="submit()">
        <div class="form-field">
          <label class="label" for="repoFullName">Repository</label>
          <input
            id="repoFullName"
            class="input"
            type="text"
            name="fullName"
            placeholder="owner/name"
            autocomplete="off"
            spellcheck="false"
            [(ngModel)]="fullName"
            [attr.aria-invalid]="!!error()"
            [attr.aria-describedby]="error() ? 'repoError' : 'repoHint'" />

          <p class="form-hint" id="repoHint">
            For example <code>vercel/next.js</code>. Validated against GitHub before it is added.
          </p>
          <p class="form-error" id="repoError" *ngIf="error()">{{ error() }}</p>
        </div>
      </form>

      <div dialogFooter>
        <button type="button" class="btn" (click)="close.emit()">Cancel</button>
        <button type="submit" form="addRepoForm" class="btn btn-primary" [disabled]="busy() || !fullName.trim()">
          <app-icon *ngIf="!busy()" name="check" [size]="15"></app-icon>
          {{ busy() ? 'Validating…' : 'Track repository' }}
        </button>
      </div>
    </app-dialog>
  `,
  styles: [`code { font-family: var(--font-mono); font-size: 0.92em; padding: 1px 5px; border-radius: 5px; background: var(--surface-3); }`]
})
export class AddRepositoryDialogComponent {
  private readonly gql = inject(GraphqlService);
  private readonly admin = inject(AdminService);
  private readonly toast = inject(ToastService);

  @Output() close = new EventEmitter<void>();
  @Output() added = new EventEmitter<void>();

  fullName = '';
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  submit() {
    const name = this.fullName.trim();
    if (!name) return;

    this.error.set(null);
    this.busy.set(true);

    this.gql.addRepository(name, this.admin.context()).subscribe({
      next: result => {
        this.busy.set(false);

        if (result.success) {
          this.toast.success('Repository added', result.message);
          this.added.emit();
          this.close.emit();
        } else {
          this.error.set(result.message);
        }
      },
      error: () => {
        this.busy.set(false);
        this.error.set('Could not reach the API. It may still be waking up.');
      }
    });
  }
}

/**
 * Edit the fields a user owns.
 *
 * Only nickname, notes, pinning and sync interval are editable. Ingested
 * metadata is deliberately read-only: the next sync would overwrite an edit,
 * so offering one would be a lie.
 */
@Component({
  selector: 'app-edit-repository-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, DialogComponent],
  template: `
    <app-dialog
      [title]="'Edit ' + repository.fullName"
      subtitle="Stars, forks and language come from GitHub and refresh on each sync."
      [width]="560"
      (close)="close.emit()">

      <form id="editRepoForm" (ngSubmit)="submit()">
        <div class="form-field">
          <label class="label" for="nickname">Display name</label>
          <input id="nickname" class="input" type="text" name="nickname"
                 placeholder="Optional" maxlength="120" [(ngModel)]="nickname" />
          <p class="form-hint">Shown instead of owner/name. Leave empty to use the repository name.</p>
        </div>

        <div class="form-field">
          <label class="label" for="notes">Notes</label>
          <textarea id="notes" class="input" name="notes" rows="3" maxlength="1000"
                    placeholder="Optional context for your team" [(ngModel)]="notes"></textarea>
        </div>

        <div class="form-field">
          <label class="label" for="interval">Sync interval</label>
          <select id="interval" class="input" name="interval" [(ngModel)]="interval">
            <option [ngValue]="null">Use the global default</option>
            <option [ngValue]="60">Every hour</option>
            <option [ngValue]="360">Every 6 hours</option>
            <option [ngValue]="1440">Once a day</option>
            <option [ngValue]="0">Never sync automatically</option>
          </select>
          <p class="form-hint">
            Applies only to scheduled syncs, which are configured on the server. Manual syncs are unaffected.
          </p>
        </div>

        <label class="pin-toggle">
          <input type="checkbox" name="pinned" [(ngModel)]="pinned" />
          <span>Pin to the top of the list</span>
        </label>

        <p class="form-error" *ngIf="error()">{{ error() }}</p>
      </form>

      <div dialogFooter>
        <button type="button" class="btn" (click)="close.emit()">Cancel</button>
        <button type="submit" form="editRepoForm" class="btn btn-primary" [disabled]="busy()">
          {{ busy() ? 'Saving…' : 'Save changes' }}
        </button>
      </div>
    </app-dialog>
  `,
  styles: [`
    .pin-toggle {
      display: inline-flex; align-items: center; gap: var(--space-2);
      font-size: 13.5px; color: var(--text-secondary); cursor: pointer; user-select: none;
    }
    .pin-toggle input { width: 15px; height: 15px; accent-color: var(--accent); cursor: pointer; }
    select.input { cursor: pointer; }
  `]
})
export class EditRepositoryDialogComponent {
  private readonly gql = inject(GraphqlService);
  private readonly admin = inject(AdminService);
  private readonly toast = inject(ToastService);

  @Input({ required: true }) repository!: TrackedRepository;

  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<void>();

  nickname = '';
  notes = '';
  pinned = false;
  interval: number | null = null;

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit() {
    this.nickname = this.repository.nickname ?? '';
    this.notes = this.repository.notes ?? '';
    this.pinned = this.repository.isPinned;
    this.interval = this.repository.syncIntervalMinutes;
  }

  submit() {
    this.error.set(null);
    this.busy.set(true);

    this.gql.updateRepository(
      this.repository.id,
      {
        // Empty strings clear the field server-side; null would leave it unchanged.
        nickname: this.nickname.trim(),
        notes: this.notes.trim(),
        isPinned: this.pinned,
        syncIntervalMinutes: this.interval
      },
      this.admin.context()
    ).subscribe({
      next: result => {
        this.busy.set(false);

        if (result.success) {
          this.toast.success('Repository updated', result.message);
          this.saved.emit();
          this.close.emit();
        } else {
          this.error.set(result.message);
        }
      },
      error: () => {
        this.busy.set(false);
        this.error.set('Could not reach the API.');
      }
    });
  }
}

/** Destructive confirmation. Names the consequence rather than asking "are you sure?". */
@Component({
  selector: 'app-remove-repository-dialog',
  standalone: true,
  imports: [CommonModule, DialogComponent],
  template: `
    <app-dialog
      title="Remove repository"
      [width]="480"
      (close)="close.emit()">

      <p class="confirm-text">
        <strong>{{ repository.fullName }}</strong> and its ingested data will be removed:
        {{ repository.totalCommits.toLocaleString() }} commits and
        {{ repository.totalPullRequests.toLocaleString() }} pull requests.
      </p>
      <p class="confirm-note muted">
        Tracking it again later re-ingests from GitHub. To stop syncing without losing
        history, disable it instead.
      </p>

      <div dialogFooter>
        <button type="button" class="btn" (click)="close.emit()">Cancel</button>
        <button type="button" class="btn btn-danger" (click)="submit()" [disabled]="busy()">
          {{ busy() ? 'Removing…' : 'Remove repository' }}
        </button>
      </div>
    </app-dialog>
  `,
  styles: [`
    .confirm-text { font-size: 14px; line-height: 1.6; margin-bottom: var(--space-3); }
    .confirm-note { font-size: 13px; line-height: 1.6; }
    .btn-danger {
      background: var(--status-critical);
      border-color: transparent;
      color: #fff;
    }
    .btn-danger:hover { background: color-mix(in srgb, var(--status-critical) 85%, black); }
  `]
})
export class RemoveRepositoryDialogComponent {
  private readonly gql = inject(GraphqlService);
  private readonly admin = inject(AdminService);
  private readonly toast = inject(ToastService);

  @Input({ required: true }) repository!: TrackedRepository;

  @Output() close = new EventEmitter<void>();
  @Output() removed = new EventEmitter<void>();

  readonly busy = signal(false);

  submit() {
    this.busy.set(true);

    this.gql.removeRepository(this.repository.id, this.admin.context()).subscribe({
      next: result => {
        this.busy.set(false);

        if (result.success) {
          this.toast.success('Repository removed', result.message);
          this.removed.emit();
          this.close.emit();
        } else {
          this.toast.error('Could not remove repository', result.message);
        }
      },
      error: () => {
        this.busy.set(false);
        this.toast.error('Could not remove repository', 'The API did not respond.');
      }
    });
  }
}
