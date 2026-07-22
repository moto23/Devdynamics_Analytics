import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subscription, forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

import {
  GraphqlService, ContributorDetail, CommitRow, PullRequestRow, PageInfo, AnalyticsFilters
} from '../services/graphql.service';

import { IconComponent } from '../shared/icon.component';
import { StatTileComponent } from '../shared/stat-tile.component';
import { ChartCardComponent } from '../shared/chart-card.component';
import { LineChartComponent, LineSeries } from '../charts/line-chart.component';
import { BarChartComponent } from '../charts/bar-chart.component';
import { HeatmapComponent } from '../charts/heatmap.component';
import {
  PaginatorComponent, StatusBadgeComponent, EmptyStateComponent
} from '../shared/table.components';

/** Weights mirror the server's scoring so the breakdown explains the real number. */
const WEIGHTS = { commits: 1, prsOpened: 2, prsMerged: 3, repositories: 5 };

interface ScorePart {
  label: string;
  count: number;
  weight: number;
  points: number;
  colorIndex: number;
  /** Precomputed share of the total, so the template does no arithmetic. */
  share: number;
}

@Component({
  selector: 'app-contributor-detail',
  standalone: true,
  imports: [
    CommonModule, RouterLink, IconComponent, StatTileComponent, ChartCardComponent,
    LineChartComponent, BarChartComponent, HeatmapComponent,
    PaginatorComponent, StatusBadgeComponent, EmptyStateComponent
  ],
  templateUrl: './contributor-detail.component.html',
  styleUrls: ['./contributor-detail.component.css']
})
export class ContributorDetailComponent implements OnInit, OnDestroy {

  private readonly gql = inject(GraphqlService);
  private readonly route = inject(ActivatedRoute);

  private routeSub!: Subscription;

  login = '';
  detail: ContributorDetail | null = null;

  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly errorMessage = signal<string | null>(null);

  // Repository split
  repoLabels: string[] = [];
  repoCounts: number[] = [];

  /**
   * Derived values are stored as fields, never returned from template getters.
   *
   * A getter that allocates a new array on every call is read once per change
   * detection pass, so *ngFor sees brand-new object identities each time and
   * rebuilds continuously — which locked the browser's main thread solid.
   */
  scoreParts: ScorePart[] = [];
  scoreTotal = 0;
  activeDays = 0;
  mergeRate = '—';

  // Commit timeline
  timelineLabels: string[] = [];
  timelineSeries: LineSeries[] = [];

  // Recent activity
  commits: CommitRow[] = [];
  commitsPage: PageInfo = emptyPage();
  private commitCursors: (string | null)[] = [null];
  private commitIndex = 0;
  readonly commitsLoading = signal(false);

  pulls: PullRequestRow[] = [];
  readonly pullsLoading = signal(false);

  readonly skeletonRows = Array.from({ length: 5 });

  ngOnInit() {
    this.routeSub = this.route.paramMap.subscribe(params => {
      this.login = params.get('login') ?? '';
      this.commitCursors = [null];
      this.commitIndex = 0;
      this.commits = [];
      this.load();
    });
  }

  ngOnDestroy() {
    this.routeSub?.unsubscribe();
  }

  private filters(): AnalyticsFilters {
    return {
      startDate: null, endDate: null,
      contributor: this.login, repository: null, excludeBots: false
    };
  }

  private load() {
    if (!this.login) return;

    this.loading.set(true);
    this.notFound.set(false);
    this.errorMessage.set(null);

    forkJoin({
      detail: this.gql.getContributorDetail(this.login).pipe(catchError(() => of(null))),
      pulls: this.gql.getPullRequestsPage(this.filters(), null, null, 8)
        .pipe(catchError(() => of({ items: [], pageInfo: emptyPage() })))
    }).subscribe({
      next: res => {
        if (!res.detail) {
          this.notFound.set(true);
          this.loading.set(false);
          return;
        }

        this.detail = res.detail;
        this.pulls = res.pulls.items;

        this.buildCharts();
        this.buildScore();
        this.buildSummaryFigures();
        this.loading.set(false);
        this.loadCommits();
      },
      error: () => {
        this.errorMessage.set('Could not load this contributor. The API may still be waking up.');
        this.loading.set(false);
      }
    });
  }

  private buildCharts() {
    const repos = this.detail?.repositories ?? [];

    // Six validated palette slots; the tail folds into "Other" rather than
    // generating a seventh hue that would not be colourblind-safe.
    const top = repos.slice(0, 5);
    const rest = repos.slice(5);

    this.repoLabels = top.map(r => r.fullName);
    this.repoCounts = top.map(r => r.commits);

    if (rest.length) {
      this.repoLabels.push(`Other (${rest.length})`);
      this.repoCounts.push(rest.reduce((sum, r) => sum + r.commits, 0));
    }

    // Weekly buckets keep a year of daily data readable as a timeline.
    const weeks = new Map<string, number>();

    for (const day of this.detail?.heatmap ?? []) {
      const date = new Date(day.date);
      date.setDate(date.getDate() - date.getDay());

      const key = date.toISOString().slice(0, 10);
      weeks.set(key, (weeks.get(key) ?? 0) + day.count);
    }

    const ordered = [...weeks.entries()].sort((a, b) => a[0].localeCompare(b[0]));

    this.timelineLabels = ordered.map(([key]) =>
      new Date(key).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }));

    this.timelineSeries = [{
      label: 'Commits per week',
      data: ordered.map(([, count]) => count),
      colorIndex: 0
    }];
  }

  /** Cheap derived figures, computed once rather than per change detection. */
  private buildSummaryFigures() {
    const s = this.detail?.stats;

    this.activeDays = (this.detail?.heatmap ?? []).filter(d => d.count > 0).length;

    this.mergeRate = s?.pullRequestsOpened
      ? `${Math.round(s.pullRequestsMerged / s.pullRequestsOpened * 100)}% merged`
      : '—';
  }

  loadCommits() {
    this.commitsLoading.set(true);

    this.gql.getCommitsPage(this.filters(), this.commitCursors[this.commitIndex], 10)
      .subscribe({
        next: page => {
          this.commits = page.items;
          this.commitsPage = page.pageInfo;
          this.commitsLoading.set(false);
        },
        error: () => this.commitsLoading.set(false)
      });
  }

  nextCommits() {
    if (!this.commitsPage.hasNextPage || !this.commitsPage.endCursor) return;

    this.commitCursors = this.commitCursors.slice(0, this.commitIndex + 1);
    this.commitCursors.push(this.commitsPage.endCursor);
    this.commitIndex++;
    this.loadCommits();
  }

  prevCommits() {
    if (this.commitIndex === 0) return;
    this.commitIndex--;
    this.loadCommits();
  }

  // ---- Score breakdown ----

  /**
   * Builds the score explanation once, when the data arrives.
   *
   * Each input, its weight and its contribution are shown: a composite number
   * nobody can decompose is not worth trusting.
   */
  private buildScore() {
    const s = this.detail?.stats;

    if (!s) {
      this.scoreParts = [];
      this.scoreTotal = 0;
      return;
    }

    this.scoreParts = [
      { label: 'Commits', count: s.commits, weight: WEIGHTS.commits, points: s.commits * WEIGHTS.commits, colorIndex: 0, share: 0 },
      { label: 'PRs opened', count: s.pullRequestsOpened, weight: WEIGHTS.prsOpened, points: s.pullRequestsOpened * WEIGHTS.prsOpened, colorIndex: 1, share: 0 },
      { label: 'PRs merged', count: s.pullRequestsMerged, weight: WEIGHTS.prsMerged, points: s.pullRequestsMerged * WEIGHTS.prsMerged, colorIndex: 2, share: 0 },
      { label: 'Repositories', count: s.repositoryCount, weight: WEIGHTS.repositories, points: s.repositoryCount * WEIGHTS.repositories, colorIndex: 3, share: 0 }
    ].filter(part => part.count > 0);

    this.scoreTotal = this.scoreParts.reduce((sum, p) => sum + p.points, 0);

    // Percentages are precomputed for the same reason.
    for (const part of this.scoreParts) {
      part.share = this.scoreTotal ? (part.points / this.scoreTotal) * 100 : 0;
    }
  }

  // ---- Presentation ----

  get avatar(): string {
    return this.detail?.stats.avatarUrl || `https://github.com/${this.login}.png?size=96`;
  }

  get profileUrl(): string {
    return this.detail?.stats.htmlUrl || `https://github.com/${this.login}`;
  }

  get hasTimeline(): boolean { return this.timelineSeries[0]?.data.length > 0; }
  get hasRepos(): boolean { return this.repoCounts.length > 0; }



  prTone(pr: PullRequestRow): 'good' | 'accent' | 'neutral' {
    if (pr.mergedAt) return 'good';
    return pr.state === 'open' ? 'accent' : 'neutral';
  }

  prLabel(pr: PullRequestRow): string {
    return pr.mergedAt ? 'Merged' : pr.state === 'open' ? 'Open' : 'Closed';
  }

  shortSha(sha: string): string { return sha.slice(0, 7); }

  firstLine(message: string): string {
    return (message || '').split('\n')[0].slice(0, 110);
  }

  date(iso: string | null | undefined): string {
    if (!iso) return '—';
    return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
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
}

function emptyPage(): PageInfo {
  return { hasNextPage: false, hasPreviousPage: false, endCursor: null, totalCount: 0 };
}
