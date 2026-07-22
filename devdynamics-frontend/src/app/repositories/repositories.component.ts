import { Component, HostListener, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, Subscription, debounceTime, distinctUntilChanged } from 'rxjs';

import { GraphqlService, TrackedRepository, PageInfo } from '../services/graphql.service';
import { AdminService } from '../core/admin.service';
import { ToastService } from '../core/toast.service';

import { IconComponent } from '../shared/icon.component';
import { OverlayPortalDirective, positionPanel, trackViewportChanges } from '../shared/overlay';
import {
  SortHeaderComponent, PaginatorComponent, StatusBadgeComponent, EmptyStateComponent
} from '../shared/table.components';
import {
  AddRepositoryDialogComponent, EditRepositoryDialogComponent, RemoveRepositoryDialogComponent
} from './repository-dialogs.component';

type Dialog = 'add' | 'edit' | 'remove' | null;

@Component({
  selector: 'app-repositories',
  standalone: true,
  imports: [
    CommonModule, FormsModule, IconComponent, OverlayPortalDirective,
    SortHeaderComponent, PaginatorComponent, StatusBadgeComponent, EmptyStateComponent,
    AddRepositoryDialogComponent, EditRepositoryDialogComponent, RemoveRepositoryDialogComponent
  ],
  templateUrl: './repositories.component.html',
  styleUrls: ['./repositories.component.css']
})
export class RepositoriesComponent implements OnInit, OnDestroy {

  private readonly gql = inject(GraphqlService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly admin = inject(AdminService);

  repositories: TrackedRepository[] = [];
  pageInfo: PageInfo = { hasNextPage: false, hasPreviousPage: false, endCursor: null, totalCount: 0 };

  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  search = '';
  sortBy: string | null = null;
  descending = true;
  includeInactive = true;

  /** Cursor stack: the API exposes forward cursors, so "previous" is a pop. */
  private cursors: (string | null)[] = [null];
  private cursorIndex = 0;

  readonly openMenuId = signal<number | null>(null);
  readonly menuTop = signal(0);
  readonly menuLeft = signal(0);
  readonly menuMaxHeight = signal(400);

  private menuTrigger: HTMLElement | null = null;
  private menuPanel: HTMLElement | null = null;
  private stopTrackingMenu?: () => void;

  readonly dialog = signal<Dialog>(null);
  activeRepository: TrackedRepository | null = null;

  private readonly searchSubject = new Subject<string>();
  private searchSub!: Subscription;

  readonly skeletonRows = Array.from({ length: 6 });

  ngOnInit() {
    this.searchSub = this.searchSubject
      .pipe(debounceTime(320), distinctUntilChanged())
      .subscribe(() => this.reload(true));

    this.load();
  }

  ngOnDestroy() {
    this.searchSub?.unsubscribe();
    this.stopTrackingMenu?.();
  }

  // ---- Data ----

  private load() {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.gql.getRepositoriesPage({
      includeInactive: this.includeInactive,
      search: this.search.trim() || null,
      sortBy: this.sortBy,
      descending: this.descending,
      after: this.cursors[this.cursorIndex],
      first: 25
    }).subscribe({
      next: page => {
        this.repositories = page.items;
        this.pageInfo = page.pageInfo;
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('Could not load repositories. The API may still be waking up.');
        this.repositories = [];
        this.loading.set(false);
      }
    });
  }

  /** Reloads, resetting pagination when the query itself changed. */
  reload(resetPage = false) {
    if (resetPage) {
      this.cursors = [null];
      this.cursorIndex = 0;
    }
    this.load();
  }

  onSearchInput() { this.searchSubject.next(this.search); }

  toggleSort(column: string) {
    if (this.sortBy === column) {
      this.descending = !this.descending;
    } else {
      this.sortBy = column;
      this.descending = true;
    }
    this.reload(true);
  }

  nextPage() {
    if (!this.pageInfo.hasNextPage || !this.pageInfo.endCursor) return;

    this.cursors = this.cursors.slice(0, this.cursorIndex + 1);
    this.cursors.push(this.pageInfo.endCursor);
    this.cursorIndex++;
    this.load();
  }

  previousPage() {
    if (this.cursorIndex === 0) return;
    this.cursorIndex--;
    this.load();
  }

  // ---- Row actions ----

  openMenu(event: MouseEvent, repository: TrackedRepository) {
    event.stopPropagation();

    const wasOpen = this.openMenuId() === repository.id;

    if (wasOpen) {
      this.closeMenu();
      return;
    }

    this.menuTrigger = event.currentTarget as HTMLElement;
    this.activeRepository = repository;
    this.openMenuId.set(repository.id);

    this.stopTrackingMenu = trackViewportChanges(() => this.positionMenu());
  }

  /** Positions as soon as the panel exists, avoiding a frame-timing race. */
  onMenuReady(element: HTMLElement) {
    this.menuPanel = element;
    this.positionMenu();
  }

  /** Anchors the menu to its trigger, inside the viewport. */
  private positionMenu() {
    const panel = this.menuPanel;
    if (!panel || !this.menuTrigger) return;

    const placement = positionPanel(this.menuTrigger.getBoundingClientRect(), panel, {
      alignEnd: true,
      minWidth: 196
    });

    this.menuTop.set(placement.top);
    this.menuLeft.set(placement.left);
    this.menuMaxHeight.set(placement.maxHeight);
  }

  closeMenu() {
    this.openMenuId.set(null);
    this.stopTrackingMenu?.();
    this.stopTrackingMenu = undefined;
    this.menuTrigger = null;
    this.menuPanel = null;
  }

  @HostListener('document:click')
  onDocumentClick() { this.closeMenu(); }

  @HostListener('document:keydown.escape')
  onEscape() { this.closeMenu(); }

  openDialog(kind: Dialog, repository?: TrackedRepository) {
    if (repository) this.activeRepository = repository;
    this.dialog.set(kind);
    this.closeMenu();
  }

  closeDialog() { this.dialog.set(null); }

  view(repository: TrackedRepository) {
    this.router.navigate(['/repositories', repository.owner, repository.name]);
  }

  viewDashboard(repository: TrackedRepository) {
    this.closeMenu();
    this.router.navigate(['/dashboard'], { queryParams: { repository: repository.fullName } });
  }

  syncNow(repository: TrackedRepository) {
    this.closeMenu();
    const handle = this.toast.progress(`Queuing ${repository.fullName}…`);

    this.gql.syncRepository(repository.id, this.admin.context()).subscribe({
      next: result => {
        result.success
          ? handle.succeed('Sync queued', result.message)
          : handle.fail('Could not queue sync', result.message);

        if (result.success) setTimeout(() => this.load(), 1500);
      },
      error: () => handle.fail('Could not queue sync', 'The API did not respond.')
    });
  }

  toggleActive(repository: TrackedRepository) {
    this.closeMenu();
    const next = !repository.isActive;

    this.gql.setRepositoryActive(repository.id, next, this.admin.context()).subscribe({
      next: result => {
        result.success
          ? this.toast.success(next ? 'Repository enabled' : 'Repository disabled', result.message)
          : this.toast.error('Could not update repository', result.message);

        this.load();
      },
      error: () => this.toast.error('Could not update repository', 'The API did not respond.')
    });
  }

  togglePin(repository: TrackedRepository) {
    this.closeMenu();

    this.gql.updateRepository(
      repository.id,
      { isPinned: !repository.isPinned },
      this.admin.context()
    ).subscribe({
      next: result => {
        result.success
          ? this.toast.success(repository.isPinned ? 'Unpinned' : 'Pinned', result.message)
          : this.toast.error('Could not update repository', result.message);

        this.load();
      },
      error: () => this.toast.error('Could not update repository', 'The API did not respond.')
    });
  }

  syncAll() {
    const handle = this.toast.progress('Queuing all active repositories…');

    this.gql.syncAllRepositories(this.admin.context()).subscribe({
      next: result => {
        result.success
          ? handle.succeed('Sync queued', result.message)
          : handle.fail('Could not queue syncs', result.message);

        if (result.success) setTimeout(() => this.load(), 1500);
      },
      error: () => handle.fail('Could not queue syncs', 'The API did not respond.')
    });
  }

  // ---- Presentation ----

  /** Owner avatars come straight from GitHub, so no extra field is needed. */
  avatarUrl(repository: TrackedRepository): string {
    return `https://github.com/${repository.owner}.png?size=64`;
  }

  displayName(repository: TrackedRepository): string {
    return repository.nickname?.trim() || repository.fullName;
  }

  syncTone(status: string): 'good' | 'warning' | 'critical' | 'accent' | 'neutral' {
    switch (status) {
      case 'Succeeded': return 'good';
      case 'PartiallySynced': return 'warning';
      case 'Failed': return 'critical';
      case 'Syncing':
      case 'Queued': return 'accent';
      default: return 'neutral';
    }
  }

  syncLabel(status: string): string {
    if (status === 'PartiallySynced') return 'Partial';
    if (status === 'NeverSynced') return 'Never synced';
    return status;
  }

  /**
   * Freshness derived from data already on the row.
   *
   * Deliberately not the full health score: that needs a per-repository query,
   * and issuing one per row would be an N+1 for a single column. The complete
   * health metrics live on the detail page, where one query covers them.
   */
  freshnessTone(repository: TrackedRepository): 'good' | 'warning' | 'neutral' {
    if (!repository.lastSyncCompletedAtUtc) return 'neutral';

    const hours = (Date.now() - new Date(repository.lastSyncCompletedAtUtc).getTime()) / 3_600_000;

    if (hours < 24) return 'good';
    if (hours < 24 * 7) return 'warning';
    return 'neutral';
  }

  relative(iso: string | null): string {
    if (!iso) return 'never';

    const minutes = Math.round((Date.now() - new Date(iso).getTime()) / 60000);

    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes}m ago`;

    const hours = Math.round(minutes / 60);
    if (hours < 24) return `${hours}h ago`;

    const days = Math.round(hours / 24);
    return days < 30 ? `${days}d ago` : `${Math.round(days / 30)}mo ago`;
  }

  get isEmpty(): boolean {
    return !this.loading() && !this.errorMessage() && this.repositories.length === 0;
  }
}
