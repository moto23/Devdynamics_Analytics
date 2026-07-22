import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subscription, forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

import {
  GraphqlService, TrackedRepository, RepositoryHealth, LanguageSlice,
  CommitRow, PullRequestRow, SyncRunRow, PageInfo, AnalyticsFilters
} from '../services/graphql.service';
import { AdminService } from '../core/admin.service';
import { ToastService } from '../core/toast.service';

import { IconComponent } from '../shared/icon.component';
import { StatTileComponent } from '../shared/stat-tile.component';
import { ChartCardComponent } from '../shared/chart-card.component';
import { LineChartComponent, LineSeries } from '../charts/line-chart.component';
import { BarChartComponent } from '../charts/bar-chart.component';
import {
  PaginatorComponent, StatusBadgeComponent, EmptyStateComponent
} from '../shared/table.components';
import { EditRepositoryDialogComponent } from './repository-dialogs.component';

type Tab = 'overview' | 'commits' | 'pulls' | 'history';

@Component({
  selector: 'app-repository-detail',
  standalone: true,
  imports: [
    CommonModule, RouterLink, IconComponent, StatTileComponent, ChartCardComponent,
    LineChartComponent, BarChartComponent, PaginatorComponent, StatusBadgeComponent,
    EmptyStateComponent, EditRepositoryDialogComponent
  ],
  templateUrl: './repository-detail.component.html',
  styleUrls: ['./repository-detail.component.css']
})
export class RepositoryDetailComponent implements OnInit, OnDestroy {

  private readonly gql = inject(GraphqlService);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);

  readonly admin = inject(AdminService);

  private routeSub!: Subscription;

  fullName = '';
  repository: TrackedRepository | null = null;
  health: RepositoryHealth | null = null;
  languages: LanguageSlice[] = [];

  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly tab = signal<Tab>('overview');
  readonly editing = signal(false);

  // Charts
  commitLabels: string[] = [];
  commitSeries: LineSeries[] = [];
  cycleLabels: string[] = [];
  cycleSeries: LineSeries[] = [];
  contributorLabels: string[] = [];
  contributorCounts: number[] = [];

  // Tab data, each paginated independently so a tab only costs what it shows.
  commits: CommitRow[] = [];
  commitsPage: PageInfo = emptyPage();
  private commitCursors: (string | null)[] = [null];
  private commitIndex = 0;
  readonly commitsLoading = signal(false);

  pulls: PullRequestRow[] = [];
  pullsPage: PageInfo = emptyPage();
  private pullCursors: (string | null)[] = [null];
  private pullIndex = 0;
  readonly pullsLoading = signal(false);

  runs: SyncRunRow[] = [];
  runsPage: PageInfo = emptyPage();
  private runCursors: (string | null)[] = [null];
  private runIndex = 0;
  readonly runsLoading = signal(false);

  readonly skeletonRows = Array.from({ length: 5 });

  ngOnInit() {
    this.routeSub = this.route.paramMap.subscribe(params => {
      const owner = params.get('owner');
      const name = params.get('name');

      this.fullName = owner && name ? `${owner}/${name}` : '';
      this.tab.set('overview');
      this.resetTabs();
      this.load();
    });
  }

  ngOnDestroy() {
    this.routeSub?.unsubscribe();
  }

  private filters(): AnalyticsFilters {
    return {
      startDate: null, endDate: null, contributor: null,
      repository: this.fullName, excludeBots: false
    };
  }

  private load() {
    if (!this.fullName) return;

    this.loading.set(true);
    this.notFound.set(false);
    this.errorMessage.set(null);

    const f = this.filters();

    forkJoin({
      // The registry is searched by name rather than fetched by id, because the
      // route carries owner/name — the identifier a user can actually type.
      repositories: this.gql.getRepositoriesPage({ includeInactive: true, search: this.fullName, first: 25 }),
      health: this.gql.getRepositoryHealth(this.fullName).pipe(catchError(() => of(null))),
      languages: this.gql.getLanguageDistribution(this.fullName).pipe(catchError(() => of([]))),
      trends: this.gql.getCommitTrends(f).pipe(catchError(() => of([]))),
      cycle: this.gql.getPrCycleTime(f).pipe(catchError(() => of([]))),
      contributors: this.gql.getContributors(f, { first: 6 }).pipe(catchError(() => of({ items: [], pageInfo: emptyPage() })))
    }).subscribe({
      next: res => {
        this.repository = res.repositories.items.find(r => r.fullName === this.fullName) ?? null;

        if (!this.repository) {
          this.notFound.set(true);
          this.loading.set(false);
          return;
        }

        this.health = res.health;
        this.languages = res.languages;

        this.buildCharts(res.trends, res.cycle, res.contributors.items);
        this.loading.set(false);
        this.loadCommits();
      },
      error: () => {
        this.errorMessage.set('Could not load this repository. The API may still be waking up.');
        this.loading.set(false);
      }
    });
  }

  private buildCharts(trends: any[], cycle: any[], contributors: any[]) {
    this.commitLabels = trends.map(t => shortDate(t.date));
    this.commitSeries = [{ label: 'Commits', data: trends.map(t => Number(t.commits ?? 0)), colorIndex: 0 }];

    const valid = cycle.filter(c => c?.date && isFinite(Number(c.avgHours)));
    this.cycleLabels = valid.map(c => shortDate(c.date));
    this.cycleSeries = [{
      label: 'Avg hours to merge',
      data: valid.map(c => Math.round(Number(c.avgHours) * 10) / 10),
      colorIndex: 1
    }];

    this.contributorLabels = contributors.map(c => c.login);
    this.contributorCounts = contributors.map(c => c.commits);
  }

  // ---- Tabs ----

  selectTab(tab: Tab) {
    this.tab.set(tab);

    // Load a tab's data the first time it is opened, not before.
    if (tab === 'commits' && this.commits.length === 0) this.loadCommits();
    if (tab === 'pulls' && this.pulls.length === 0) this.loadPulls();
    if (tab === 'history' && this.runs.length === 0) this.loadRuns();
  }

  private resetTabs() {
    this.commits = []; this.commitCursors = [null]; this.commitIndex = 0;
    this.pulls = []; this.pullCursors = [null]; this.pullIndex = 0;
    this.runs = []; this.runCursors = [null]; this.runIndex = 0;
  }

  loadCommits() {
    this.commitsLoading.set(true);
    this.gql.getCommitsPage(this.filters(), this.commitCursors[this.commitIndex], 15)
      .subscribe({
        next: page => { this.commits = page.items; this.commitsPage = page.pageInfo; this.commitsLoading.set(false); },
        error: () => this.commitsLoading.set(false)
      });
  }

  loadPulls() {
    this.pullsLoading.set(true);
    this.gql.getPullRequestsPage(this.filters(), null, this.pullCursors[this.pullIndex], 15)
      .subscribe({
        next: page => { this.pulls = page.items; this.pullsPage = page.pageInfo; this.pullsLoading.set(false); },
        error: () => this.pullsLoading.set(false)
      });
  }

  loadRuns() {
    if (!this.repository) return;

    this.runsLoading.set(true);
    this.gql.getSyncRuns(this.repository.id, this.runCursors[this.runIndex], 10)
      .subscribe({
        next: page => { this.runs = page.items; this.runsPage = page.pageInfo; this.runsLoading.set(false); },
        error: () => this.runsLoading.set(false)
      });
  }

  nextCommits() { this.advance('commits', 1); }
  prevCommits() { this.advance('commits', -1); }
  nextPulls()   { this.advance('pulls', 1); }
  prevPulls()   { this.advance('pulls', -1); }
  nextRuns()    { this.advance('history', 1); }
  prevRuns()    { this.advance('history', -1); }

  private advance(tab: Tab, direction: 1 | -1) {
    const state = {
      commits: { cursors: this.commitCursors, index: this.commitIndex, page: this.commitsPage },
      pulls:   { cursors: this.pullCursors,   index: this.pullIndex,   page: this.pullsPage },
      history: { cursors: this.runCursors,    index: this.runIndex,    page: this.runsPage }
    }[tab as 'commits' | 'pulls' | 'history'];

    if (!state) return;

    if (direction === 1) {
      if (!state.page.hasNextPage || !state.page.endCursor) return;
      state.cursors.length = state.index + 1;
      state.cursors.push(state.page.endCursor);
      state.index++;
    } else {
      if (state.index === 0) return;
      state.index--;
    }

    if (tab === 'commits') { this.commitIndex = state.index; this.loadCommits(); }
    if (tab === 'pulls')   { this.pullIndex = state.index; this.loadPulls(); }
    if (tab === 'history') { this.runIndex = state.index; this.loadRuns(); }
  }

  // ---- Actions ----

  syncNow() {
    if (!this.repository) return;

    const handle = this.toast.progress(`Queuing ${this.repository.fullName}…`);

    this.gql.syncRepository(this.repository.id, this.admin.context()).subscribe({
      next: result => {
        result.success
          ? handle.succeed('Sync queued', result.message)
          : handle.fail('Could not queue sync', result.message);

        if (result.success) setTimeout(() => this.load(), 1500);
      },
      error: () => handle.fail('Could not queue sync', 'The API did not respond.')
    });
  }

  // ---- Presentation ----

  get avatarUrl(): string {
    return this.repository ? `https://github.com/${this.repository.owner}.png?size=96` : '';
  }

  get hasCommitData(): boolean { return this.commitSeries[0]?.data.length > 0; }
  get hasCycleData(): boolean { return this.cycleSeries[0]?.data.length > 0; }
  get hasContributorData(): boolean { return this.contributorCounts.length > 0; }

  percent(value: number): string { return `${Math.round(value * 100)}%`; }

  hours(value: number | undefined | null): string {
    if (!value) return '—';
    return value < 24 ? `${value.toFixed(1)}h` : `${(value / 24).toFixed(1)}d`;
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

  prTone(pr: PullRequestRow): 'good' | 'accent' | 'neutral' {
    if (pr.mergedAt) return 'good';
    return pr.state === 'open' ? 'accent' : 'neutral';
  }

  prLabel(pr: PullRequestRow): string {
    return pr.mergedAt ? 'Merged' : pr.state === 'open' ? 'Open' : 'Closed';
  }

  shortSha(sha: string): string { return sha.slice(0, 7); }

  firstLine(message: string): string {
    return (message || '').split('\n')[0].slice(0, 120);
  }

  date(iso: string | null): string {
    if (!iso) return '—';
    return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
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

  duration(ms: number): string {
    return ms < 1000 ? `${ms}ms` : `${(ms / 1000).toFixed(1)}s`;
  }
}

function emptyPage(): PageInfo {
  return { hasNextPage: false, hasPreviousPage: false, endCursor: null, totalCount: 0 };
}

function shortDate(iso: string): string {
  const date = new Date(iso);
  return isNaN(date.getTime())
    ? String(iso).split('T')[0]
    : date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}
