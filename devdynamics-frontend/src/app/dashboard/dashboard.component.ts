import { Component, OnInit, OnDestroy, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import {
  forkJoin, Subscription, Subject, debounceTime, switchMap, catchError, of
} from 'rxjs';

import {
  GraphqlService, AnalyticsFilters, SummaryStats, TrackedRepository, ContributorSummary
} from '../services/graphql.service';

import { IconComponent } from '../shared/icon.component';
import { StatTileComponent } from '../shared/stat-tile.component';
import { ChartCardComponent } from '../shared/chart-card.component';
import { LineChartComponent, LineSeries } from '../charts/line-chart.component';
import { BarChartComponent } from '../charts/bar-chart.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    CommonModule, FormsModule, IconComponent,
    StatTileComponent, ChartCardComponent, LineChartComponent, BarChartComponent
  ],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.css']
})
export class DashboardComponent implements OnInit, OnDestroy {

  private routeSub!: Subscription;
  private filterSub!: Subscription;
  private readonly filterSubject = new Subject<void>();

  // ---- Filters ----
  startDate = '';
  endDate = '';
  contributor = '';
  repository: string | null = null;
  excludeBots = false;
  activeQuickRange: number | null = null;

  contributorDropdownOpen = false;
  repositoryDropdownOpen = false;

  loading = true;
  errorMessage = '';
  dateRangeMessage = '';

  /** Set once a request has been in flight long enough that a cold start is likely. */
  slowLoad = false;
  private slowLoadTimer?: ReturnType<typeof setTimeout>;

  repositories: TrackedRepository[] = [];
  contributors: ContributorSummary[] = [];

  summary: SummaryStats = emptySummary();

  commitLabels: string[] = [];
  commitSeries: LineSeries[] = [];

  cycleLabels: string[] = [];
  cycleSeries: LineSeries[] = [];
  mergedLabels: string[] = [];
  mergedCounts: number[] = [];

  contributorLabels: string[] = [];
  contributorCounts: number[] = [];

  constructor(
    private readonly gql: GraphqlService,
    private readonly route: ActivatedRoute,
    private readonly router: Router
  ) {}

  @HostListener('document:click', ['$event'])
  onClickOutside(event: MouseEvent) {
    if (!(event.target as HTMLElement).closest('.select')) {
      this.contributorDropdownOpen = false;
      this.repositoryDropdownOpen = false;
    }
  }

  ngOnInit() {
    // The fetch pipeline must be subscribed BEFORE the route subscription:
    // queryParams emits synchronously on subscribe, and filterSubject is a
    // plain Subject, so a value pushed with no subscriber attached is dropped
    // and the first load never happens.
    this.filterSub = this.filterSubject.pipe(
      debounceTime(320),
      switchMap(() => this.fetch())
    ).subscribe();

    // Filters live in the URL so any view is shareable and back/forward works.
    this.routeSub = this.route.queryParams.subscribe(params => {
      this.repository  = params['repository'] ?? null;
      this.contributor = params['contributor'] ?? '';
      this.startDate   = params['from'] ?? '';
      this.endDate     = params['to'] ?? '';
      this.excludeBots = params['bots'] === 'exclude';
      this.trigger();
    });
  }

  ngOnDestroy() {
    this.routeSub?.unsubscribe();
    this.filterSub?.unsubscribe();
    clearTimeout(this.slowLoadTimer);
  }

  // ---- Filter actions ----

  onFiltersChanged() { this.activeQuickRange = null; this.syncUrl(); }

  selectContributor(login: string) {
    this.contributor = login;
    this.contributorDropdownOpen = false;
    this.syncUrl();
  }

  selectRepository(fullName: string | null) {
    this.repository = fullName;
    this.repositoryDropdownOpen = false;
    this.syncUrl();
  }

  setQuickRange(days: number) {
    const end = new Date();
    const start = new Date();
    start.setDate(start.getDate() - days);

    this.endDate = end.toISOString().slice(0, 10);
    this.startDate = start.toISOString().slice(0, 10);
    this.activeQuickRange = days;

    this.syncUrl();
  }

  resetFilters() {
    this.startDate = '';
    this.endDate = '';
    this.contributor = '';
    this.repository = null;
    this.excludeBots = false;
    this.activeQuickRange = null;
    this.syncUrl();
  }

  refresh() { this.trigger(); }

  get hasActiveFilters(): boolean {
    return !!(this.startDate || this.endDate || this.contributor || this.repository || this.excludeBots);
  }

  /** Writing filters to the URL re-triggers the fetch through the route subscription. */
  private syncUrl() {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        repository: this.repository || null,
        contributor: this.contributor || null,
        from: this.startDate || null,
        to: this.endDate || null,
        bots: this.excludeBots ? 'exclude' : null
      },
      queryParamsHandling: 'merge',
      replaceUrl: true
    });
  }

  private trigger() { this.filterSubject.next(); }

  // ---- Data ----

  private buildFilters(): AnalyticsFilters {
    return {
      // The trailing Z is required: the GraphQL DateTime scalar rejects an
      // ISO-8601 string with no timezone offset.
      startDate: this.startDate ? `${this.startDate}T00:00:00.000Z` : null,
      endDate: this.endDate ? `${this.endDate}T23:59:59.999Z` : null,
      contributor: this.contributor || null,
      repository: this.repository,
      excludeBots: this.excludeBots
    };
  }

  private fetch() {
    this.loading = true;
    this.errorMessage = '';
    this.dateRangeMessage = '';

    // Free-tier services sleep; say so rather than showing an endless skeleton.
    clearTimeout(this.slowLoadTimer);
    this.slowLoad = false;
    this.slowLoadTimer = setTimeout(() => { this.slowLoad = true; }, 5000);

    if (!this.isDateRangeValid()) {
      this.dateRangeMessage = 'Start date must be on or before the end date.';
      this.finish();
      return of(null);
    }

    const filters = this.buildFilters();

    return forkJoin({
      summary: this.gql.getSummaryStats(filters),
      trends: this.gql.getCommitTrends(filters),
      cycle: this.gql.getPrCycleTime(filters),
      contributors: this.gql.getContributors(filters),
      repositories: this.gql.getTrackedRepositories(false)
    }).pipe(
      catchError(err => {
        console.error('API ERROR:', err);
        this.errorMessage = 'Unable to load analytics. The API may still be waking up.';
        this.resetData();
        this.finish();
        return of(null);
      }),
      switchMap((res: any) => {
        if (!res) return of(null);

        this.summary = res.summary ?? emptySummary();
        this.repositories = res.repositories ?? [];
        this.contributors = res.contributors?.items ?? [];

        this.buildCharts(res.trends ?? [], res.cycle ?? []);
        this.finish();

        return of(null);
      })
    );
  }

  private finish() {
    this.loading = false;
    clearTimeout(this.slowLoadTimer);
    this.slowLoad = false;
  }

  private isDateRangeValid(): boolean {
    if (!this.startDate || !this.endDate) return true;
    return new Date(this.startDate) <= new Date(this.endDate);
  }

  private resetData() {
    this.summary = emptySummary();
    this.commitLabels = []; this.commitSeries = [];
    this.cycleLabels = [];  this.cycleSeries = [];
    this.mergedLabels = []; this.mergedCounts = [];
    this.contributorLabels = []; this.contributorCounts = [];
    this.contributors = [];
  }

  private buildCharts(trends: any[], cycle: any[]) {
    this.commitLabels = trends.map(t => shortDate(t.date));
    this.commitSeries = [{ label: 'Commits', data: trends.map(t => Number(t.commits ?? 0)), colorIndex: 0 }];

    const validCycle = cycle.filter(c => c?.date && isFinite(Number(c.avgHours)));

    this.cycleLabels = validCycle.map(c => shortDate(c.date));
    this.cycleSeries = [{
      label: 'Avg hours to merge',
      data: validCycle.map(c => Math.round(Number(c.avgHours) * 10) / 10),
      colorIndex: 1
    }];

    // Merged volume shares the x-axis but gets its own panel — two measures on
    // one pair of axes would be a dual-axis chart, which misleads.
    this.mergedLabels = validCycle.map(c => shortDate(c.date));
    this.mergedCounts = validCycle.map(c => Number(c.mergedCount ?? 0));

    // Top contributors, with the tail folded into "Other": the palette is
    // validated for six slots, and a generated seventh hue would not be safe.
    const top = this.contributors.slice(0, 5);
    const rest = this.contributors.slice(5);

    this.contributorLabels = top.map(c => c.login);
    this.contributorCounts = top.map(c => c.commits);

    if (rest.length) {
      this.contributorLabels.push(`Other (${rest.length})`);
      this.contributorCounts.push(rest.reduce((sum, c) => sum + c.commits, 0));
    }
  }

  // ---- View helpers ----

  get hasCommitData(): boolean { return this.commitSeries[0]?.data.length > 0; }
  get hasCycleData(): boolean { return this.cycleSeries[0]?.data.length > 0; }
  get hasContributorData(): boolean { return this.contributorCounts.length > 0; }

  get isEmptyOverall(): boolean {
    return !this.loading && !this.errorMessage
      && this.summary.totalCommits === 0 && this.summary.totalPullRequests === 0;
  }

  /** Median leads; the mean is shown as supporting detail because it is skewed. */
  get cycleNote(): string {
    if (!this.summary.medianPrCycleHours) return '';
    return `mean ${this.summary.avgPrCycleHours}h · p95 ${this.summary.p95PrCycleHours}h`;
  }

  get mergeRate(): string {
    if (!this.summary.totalPullRequests) return '—';
    return `${Math.round(this.summary.mergedPullRequests / this.summary.totalPullRequests * 100)}% merged`;
  }
}

function emptySummary(): SummaryStats {
  return {
    totalCommits: 0, totalPullRequests: 0, mergedPullRequests: 0, openPullRequests: 0,
    contributorCount: 0, repositoryCount: 0,
    avgPrCycleHours: 0, medianPrCycleHours: 0, p95PrCycleHours: 0
  };
}

function shortDate(iso: string): string {
  const date = new Date(iso);
  return isNaN(date.getTime())
    ? String(iso).split('T')[0]
    : date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}
