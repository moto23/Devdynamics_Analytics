import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, Subscription, debounceTime, distinctUntilChanged } from 'rxjs';

import {
  GraphqlService, ContributorSummary, PageInfo, TrackedRepository, AnalyticsFilters
} from '../services/graphql.service';

import { IconComponent } from '../shared/icon.component';
import { ComboboxComponent, ComboOption } from '../shared/combobox.component';
import {
  SortHeaderComponent, PaginatorComponent, EmptyStateComponent
} from '../shared/table.components';

@Component({
  selector: 'app-contributors',
  standalone: true,
  imports: [
    CommonModule, FormsModule, IconComponent, ComboboxComponent,
    SortHeaderComponent, PaginatorComponent, EmptyStateComponent
  ],
  templateUrl: './contributors.component.html',
  styleUrls: ['./contributors.component.css']
})
export class ContributorsComponent implements OnInit, OnDestroy {

  private readonly gql = inject(GraphqlService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  contributors: ContributorSummary[] = [];
  pageInfo: PageInfo = emptyPage();
  repositories: TrackedRepository[] = [];

  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  search = '';
  sortBy = 'commits';
  descending = true;
  repository: string | null = null;
  excludeBots = false;

  private cursors: (string | null)[] = [null];
  private cursorIndex = 0;

  private readonly searchSubject = new Subject<string>();
  private searchSub!: Subscription;
  private routeSub!: Subscription;

  readonly skeletonRows = Array.from({ length: 8 });

  ngOnInit() {
    this.searchSub = this.searchSubject
      .pipe(debounceTime(320), distinctUntilChanged())
      .subscribe(() => this.reload(true));

    this.routeSub = this.route.queryParams.subscribe(params => {
      this.repository = params['repository'] ?? null;
      this.excludeBots = params['bots'] === 'exclude';
      this.reload(true);
    });

    this.gql.getTrackedRepositories(false).subscribe({
      next: repos => this.repositories = repos,
      error: () => this.repositories = []
    });
  }

  ngOnDestroy() {
    this.searchSub?.unsubscribe();
    this.routeSub?.unsubscribe();
  }

  get repositoryOptions(): ComboOption[] {
    return [
      { value: null, label: 'All repositories' },
      ...this.repositories.map(r => ({
        value: r.fullName,
        label: r.fullName,
        meta: r.totalCommits.toLocaleString()
      }))
    ];
  }

  private filters(): AnalyticsFilters {
    return {
      startDate: null,
      endDate: null,
      contributor: null,
      repository: this.repository,
      excludeBots: this.excludeBots
    };
  }

  private load() {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.gql.getContributors(this.filters(), {
      first: 25,
      after: this.cursors[this.cursorIndex],
      search: this.search.trim() || null,
      sortBy: this.sortBy
    }).subscribe({
      next: page => {
        this.contributors = page.items;
        this.pageInfo = page.pageInfo;
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('Could not load contributors. The API may still be waking up.');
        this.contributors = [];
        this.loading.set(false);
      }
    });
  }

  reload(resetPage = false) {
    if (resetPage) {
      this.cursors = [null];
      this.cursorIndex = 0;
    }
    this.load();
  }

  onSearchInput() { this.searchSubject.next(this.search); }

  onRepositoryChange(value: string | null) {
    this.repository = value;
    this.syncUrl();
  }

  onBotsChange() { this.syncUrl(); }

  private syncUrl() {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        repository: this.repository || null,
        bots: this.excludeBots ? 'exclude' : null
      },
      queryParamsHandling: 'merge',
      replaceUrl: true
    });
  }

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

  view(contributor: ContributorSummary) {
    this.router.navigate(['/contributors', contributor.login]);
  }

  viewDashboard(event: MouseEvent, contributor: ContributorSummary) {
    event.stopPropagation();
    this.router.navigate(['/dashboard'], { queryParams: { contributor: contributor.login } });
  }

  // ---- Presentation ----

  avatar(contributor: ContributorSummary): string {
    return contributor.avatarUrl || `https://github.com/${contributor.login}.png?size=64`;
  }

  /**
   * Span of activity, from real first and last commit dates.
   *
   * Not a per-row heatmap: that would need one query per contributor, and a
   * page of 25 would mean 25 extra round trips. The full heatmap lives on the
   * detail page, where a single query covers it.
   */
  activitySpan(contributor: ContributorSummary): string {
    const first = contributor.firstActivityUtc;
    const last = contributor.lastActivityUtc;

    if (!first || !last) return '—';

    const days = Math.max(1, Math.round(
      (new Date(last).getTime() - new Date(first).getTime()) / 86_400_000
    ));

    return days === 1 ? 'single day' : `${days} days`;
  }

  relative(iso: string | null | undefined): string {
    if (!iso) return '—';

    const minutes = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
    if (minutes < 60) return `${Math.max(1, minutes)}m ago`;

    const hours = Math.round(minutes / 60);
    if (hours < 24) return `${hours}h ago`;

    const days = Math.round(hours / 24);
    return days < 30 ? `${days}d ago` : `${Math.round(days / 30)}mo ago`;
  }

  get isEmpty(): boolean {
    return !this.loading() && !this.errorMessage() && this.contributors.length === 0;
  }
}

function emptyPage(): PageInfo {
  return { hasNextPage: false, hasPreviousPage: false, endCursor: null, totalCount: 0 };
}
