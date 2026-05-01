import {
  Component,
  OnInit,
  OnDestroy,
  HostListener
} from '@angular/core';

import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { NgxChartsModule } from '@swimlane/ngx-charts';

import {
  forkJoin,
  Subscription,
  Subject,
  debounceTime,
  switchMap,
  retry,
  catchError,
  of
} from 'rxjs';

import { ActivatedRoute } from '@angular/router';

import {
  DevActivity,
  GraphqlService,
  PRCycleTimeResult,
  SummaryResult
} from '../services/graphql.service';

type ChartPoint = {
  name: string;
  value: number;
};

type ChartSeries = {
  name: string;
  series: ChartPoint[];
};

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, NgxChartsModule],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.css']
})
export class DashboardComponent implements OnInit, OnDestroy {

  private refreshInterval: any;
  private routeSub!: Subscription;
  private filterSubject = new Subject<void>();

  startDate = '';
  endDate = '';
  contributor = '';
  dropdownOpen = false;

  selectedCompany: string | null = null;

  loading = false;
  errorMessage = '';
  dateRangeMessage = '';
  hasNoData = false;

  contributors: string[] = [];

  summary: SummaryResult = {
    totalCommits: 0,
    totalPRs: 0,
    totalMerges: 0,
    totalMeetings: 0,
    totalDocs: 0,
    contributorCount: 0
  };

  commitFrequencyData: ChartSeries[] = [];
  activityBreakdownData: any[] = [];
  activityShareData: any[] = [];
  prCycleTimeData: ChartSeries[] = [];

  constructor(
    private gql: GraphqlService,
    private route: ActivatedRoute
  ) {}

  // =========================
  // CLICK OUTSIDE DROPDOWN
  // =========================

  @HostListener('document:click', ['$event'])
  onClickOutside(event: MouseEvent) {

    const target = event.target as HTMLElement;

    if (!target.closest('.custom-select')) {
      this.dropdownOpen = false;
    }
  }

  // =========================
  // INIT
  // =========================

  ngOnInit() {

    this.routeSub = this.route.queryParams.subscribe(params => {

      this.selectedCompany = params['company'] || null;

      this.triggerFilter();
    });

    this.filterSubject.pipe(

      debounceTime(400),

      switchMap(() => this.fetchData())

    ).subscribe();

    // AUTO REFRESH
    this.refreshInterval = setInterval(() => {
      this.triggerFilter();
    }, 5000);
  }

  ngOnDestroy() {

    if (this.refreshInterval) {
      clearInterval(this.refreshInterval);
    }

    if (this.routeSub) {
      this.routeSub.unsubscribe();
    }
  }

  // =========================
  // FILTERS
  // =========================

  onFiltersChanged() {
    this.triggerFilter();
  }

  selectContributor(val: string) {

    this.contributor = val;

    this.dropdownOpen = false;

    this.triggerFilter();
  }

  private triggerFilter() {
    this.filterSubject.next();
  }

  // =========================
  // FETCH DATA
  // =========================

  private fetchData() {

    const start = this.startDate
      ? `${this.startDate}T00:00:00`
      : null;

    const end = this.endDate
      ? `${this.endDate}T23:59:59`
      : null;

    this.loading = true;

    this.errorMessage = '';
    this.dateRangeMessage = '';
    this.hasNoData = false;

    // DATE VALIDATION

    if (!this.isDateRangeValid(start, end)) {

      this.dateRangeMessage =
        'Start date must be before or equal to end date';

      this.loading = false;

      this.buildCharts([], []);

      return of(null);
    }

    return forkJoin({

      activities: this.gql.getDevActivities(
        start,
        end,
        this.contributor || null,
        this.selectedCompany
      ),

      summary: this.gql.getSummaryStats(
        start,
        end,
        this.contributor || null,
        this.selectedCompany
      ),

      pr: this.gql.getPRCycleTime(
        start,
        end,
        this.selectedCompany
      )

    }).pipe(

      retry(2),

      catchError(err => {

        console.error('API ERROR:', err);

        this.errorMessage =
          'Unable to fetch dashboard data';

        this.loading = false;

        return of(null);
      }),

      switchMap((res: any) => {

        if (!res) {
          return of(null);
        }

        const activities: DevActivity[] =
          res.activities || [];

        const summary: SummaryResult =
          res.summary || this.summary;

        const pr: PRCycleTimeResult[] =
          res.pr || [];

        this.summary = summary;

        this.hasNoData =
          !activities || activities.length === 0;

        // CONTRIBUTORS

        this.contributors = Array.from(
          new Set(
            activities
              .map(a => a.contributor)
              .filter(Boolean)
          )
        ).sort();

        // BUILD CHARTS

        this.buildCharts(activities, pr);

        this.loading = false;

        return of(null);
      })
    );
  }

  // =========================
  // HELPERS
  // =========================

  private isDateRangeValid(
    start: string | null,
    end: string | null
  ): boolean {

    if (!start || !end) {
      return true;
    }

    return new Date(start) <= new Date(end);
  }

  // =========================
  // BUILD CHARTS
  // =========================

  buildCharts(
    data: DevActivity[],
    pr: PRCycleTimeResult[]
  ) {

    // EMPTY

    if (!data || data.length === 0) {

      this.commitFrequencyData = [];
      this.activityBreakdownData = [];
      this.activityShareData = [];
      this.prCycleTimeData = [];

      return;
    }

    const grouped: Record<string, any> = {};

    // GROUP DATA

    data.forEach(a => {

      if (!a.time) return;

      const date = a.time.split('T')[0];

      if (!grouped[date]) {

        grouped[date] = {
          commits: 0,
          prs: 0,
          merges: 0,
          meetings: 0,
          docs: 0
        };
      }

      grouped[date].commits += Number(a.commits || 0);
      grouped[date].prs += Number(a.pullRequests || 0);
      grouped[date].merges += Number(a.merges || 0);
      grouped[date].meetings += Number(a.meetings || 0);
      grouped[date].docs += Number(a.documentation || 0);
    });

    const dates = Object.keys(grouped).sort();

    // =========================
    // COMMITS LINE CHART
    // =========================

    this.commitFrequencyData = [
      {
        name: 'Commits',
        series: dates.map(d => ({
          name: d,
          value: Number(grouped[d].commits || 0)
        }))
      }
    ];

    // =========================
    // BAR CHART
    // =========================

    this.activityBreakdownData = dates.map(d => ({
      name: d,
      series: [
        {
          name: 'Commits',
          value: Number(grouped[d].commits || 0)
        },
        {
          name: 'PRs',
          value: Number(grouped[d].prs || 0)
        },
        {
          name: 'Merges',
          value: Number(grouped[d].merges || 0)
        }
      ]
    }));

    // =========================
    // PIE CHART
    // =========================

    this.activityShareData = [
      {
        name: 'Commits',
        value: Number(this.summary.totalCommits || 0)
      },
      {
        name: 'PRs',
        value: Number(this.summary.totalPRs || 0)
      },
      {
        name: 'Merges',
        value: Number(this.summary.totalMerges || 0)
      }
    ];

    // =========================
    // PR CYCLE TIME CHART
    // =========================

    this.prCycleTimeData = [
      {
        name: 'PR Cycle Time',
        series: (pr || [])
          .filter(x =>
            x &&
            x.date &&
            x.avgHours !== null &&
            x.avgHours !== undefined &&
            !isNaN(Number(x.avgHours))
          )
          .map(x => ({
            name: x.date.split('T')[0],
            value: Number(x.avgHours)
          }))
      }
    ];

    console.log('commitFrequencyData', this.commitFrequencyData);
    console.log('activityBreakdownData', this.activityBreakdownData);
    console.log('activityShareData', this.activityShareData);
    console.log('prCycleTimeData', this.prCycleTimeData);
  }
}
