import { Component, OnInit, OnDestroy, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { NgxChartsModule } from '@swimlane/ngx-charts';
import { ActivatedRoute } from '@angular/router';

import {
  forkJoin, Subscription, Subject, debounceTime, switchMap, catchError, of
} from 'rxjs';

import {
  GraphqlService,
  AnalyticsFilters,
  SummaryStats,
  TrackedRepository,
  ContributorSummary
} from '../services/graphql.service';

type ChartPoint = { name: string; value: number };
type ChartSeries = { name: string; series: ChartPoint[] };

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, NgxChartsModule],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.css']
})
export class DashboardComponent implements OnInit, OnDestroy {

  private routeSub!: Subscription;
  private filterSub!: Subscription;
  private filterSubject = new Subject<void>();

  // Filters
  startDate = '';
  endDate = '';
  contributor = '';
  repository: string | null = null;
  excludeBots = false;

  contributorDropdownOpen = false;
  repositoryDropdownOpen = false;

  loading = false;
  errorMessage = '';
  dateRangeMessage = '';
  hasNoData = false;

  /** Populated from the registry, so the filter follows whatever is tracked. */
  repositories: TrackedRepository[] = [];
  contributors: ContributorSummary[] = [];

  summary: SummaryStats = this.emptySummary();

  commitTrendData: ChartSeries[] = [];
  prCycleTimeData: ChartSeries[] = [];
  contributorShareData: ChartPoint[] = [];

  constructor(
    private gql: GraphqlService,
    private route: ActivatedRoute
  ) {}

  @HostListener('document:click', ['$event'])
  onClickOutside(event: MouseEvent) {
    const target = event.target as HTMLElement;
    if (!target.closest('.custom-select')) {
      this.contributorDropdownOpen = false;
      this.repositoryDropdownOpen = false;
    }
  }

  ngOnInit() {
    this.routeSub = this.route.queryParams.subscribe(params => {
      this.repository = params['repository'] || null;
      this.triggerFilter();
    });

    this.filterSub = this.filterSubject.pipe(
      debounceTime(400),
      switchMap(() => this.fetchData())
    ).subscribe();
  }

  ngOnDestroy() {
    this.routeSub?.unsubscribe();
    this.filterSub?.unsubscribe();
  }

  // =========================
  // Filters
  // =========================

  onFiltersChanged() { this.triggerFilter(); }

  refresh() { this.triggerFilter(); }

  selectContributor(login: string) {
    this.contributor = login;
    this.contributorDropdownOpen = false;
    this.triggerFilter();
  }

  selectRepository(fullName: string | null) {
    this.repository = fullName;
    this.repositoryDropdownOpen = false;
    this.triggerFilter();
  }

  setQuickRange(days: number) {
    const end = new Date();
    const start = new Date();
    start.setDate(start.getDate() - days);

    this.endDate = end.toISOString().slice(0, 10);
    this.startDate = start.toISOString().slice(0, 10);

    this.triggerFilter();
  }

  resetFilters() {
    this.startDate = '';
    this.endDate = '';
    this.contributor = '';
    this.repository = null;
    this.excludeBots = false;
    this.triggerFilter();
  }

  get hasActiveFilters(): boolean {
    return !!(this.startDate || this.endDate || this.contributor || this.repository || this.excludeBots);
  }

  private triggerFilter() { this.filterSubject.next(); }

  // =========================
  // Fetch
  // =========================

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

  private fetchData() {
    this.loading = true;
    this.errorMessage = '';
    this.dateRangeMessage = '';
    this.hasNoData = false;

    if (!this.isDateRangeValid()) {
      this.dateRangeMessage = 'Start date must be before or equal to end date';
      this.loading = false;
      this.resetData();
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
        this.errorMessage =
          'Unable to fetch dashboard data. The API may be waking up — try again in a moment.';
        this.loading = false;
        this.resetData();
        return of(null);
      }),
      switchMap((res: any) => {
        if (!res) return of(null);

        this.summary = res.summary || this.emptySummary();
        this.repositories = res.repositories || [];
        this.contributors = res.contributors || [];

        this.hasNoData = (this.summary.totalCommits || 0) === 0
          && (this.summary.totalPullRequests || 0) === 0;

        this.buildCharts(res.trends || [], res.cycle || []);

        this.loading = false;
        return of(null);
      })
    );
  }

  private isDateRangeValid(): boolean {
    if (!this.startDate || !this.endDate) return true;
    return new Date(this.startDate) <= new Date(this.endDate);
  }

  private emptySummary(): SummaryStats {
    return {
      totalCommits: 0,
      totalPullRequests: 0,
      mergedPullRequests: 0,
      openPullRequests: 0,
      contributorCount: 0,
      repositoryCount: 0,
      avgPrCycleHours: 0
    };
  }

  private resetData() {
    this.summary = this.emptySummary();
    this.commitTrendData = [];
    this.prCycleTimeData = [];
    this.contributorShareData = [];
    this.contributors = [];
  }

  // =========================
  // Charts
  // =========================

  private buildCharts(trends: any[], cycle: any[]) {
    this.commitTrendData = [{
      name: 'Commits',
      series: trends.map(t => ({
        name: (t.date || '').split('T')[0],
        value: Number(t.commits || 0)
      }))
    }];

    this.prCycleTimeData = [{
      name: 'Avg hours to merge',
      series: cycle
        .filter(c => c && c.date && !isNaN(Number(c.avgHours)))
        .map(c => ({
          name: (c.date || '').split('T')[0],
          value: Math.round(Number(c.avgHours) * 10) / 10
        }))
    }];

    // Top contributors by commits; the rest are grouped so the chart stays
    // readable however many contributors the tracked repositories have.
    const top = this.contributors.slice(0, 8);
    const rest = this.contributors.slice(8);

    this.contributorShareData = top.map(c => ({
      name: c.login,
      value: c.commits
    }));

    if (rest.length > 0) {
      this.contributorShareData.push({
        name: `Other (${rest.length})`,
        value: rest.reduce((sum, c) => sum + c.commits, 0)
      });
    }
  }
}
