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

  colorScheme = 'cool';

  constructor(
    private gql: GraphqlService,
    private route: ActivatedRoute
  ) {}

  @HostListener('document:click', ['$event'])
  onClickOutside(event: MouseEvent) {
    const target = event.target as HTMLElement;

    if (!target.closest('.custom-select')) {
      this.dropdownOpen = false;
    }
  }

  ngOnInit(): void {

    this.routeSub = this.route.queryParams.subscribe(params => {
      this.selectedCompany = params['company'] || null;
      this.triggerFilter();
    });

    this.filterSubject.pipe(
      debounceTime(300),
      switchMap(() => this.fetchData())
    ).subscribe();
  }

  ngOnDestroy(): void {
    if (this.routeSub) {
      this.routeSub.unsubscribe();
    }
  }

  onFiltersChanged(): void {
    this.triggerFilter();
  }

  selectContributor(val: string): void {
    this.contributor = val;
    this.dropdownOpen = false;
    this.triggerFilter();
  }

  private triggerFilter(): void {
    this.filterSubject.next();
  }

  private fetchData() {

    const start = this.startDate ? this.toISO(this.startDate) : null;
    const end = this.endDate ? this.toISO(this.endDate) : null;

    this.errorMessage = '';
    this.dateRangeMessage = '';
    this.hasNoData = false;

    if (!this.isDateRangeValid(start, end)) {
      this.dateRangeMessage = 'Start date must be before end date';
      return of(null);
    }

    this.loading = true;

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
        console.error(err);

        this.errorMessage = 'Unable to load dashboard data.';
        this.loading = false;

        return of(null);
      }),

      switchMap((res: any) => {

        if (!res) {
          return of(null);
        }

        const activities: DevActivity[] = res.activities || [];
        const summary: SummaryResult = res.summary;
        const pr: PRCycleTimeResult[] = res.pr || [];

        this.summary = summary;

        this.hasNoData = activities.length === 0;

        this.contributors = Array.from(
          new Set(
            activities
              .map(x => x.contributor)
              .filter(Boolean)
          )
        ).sort();

        this.buildCharts(activities, pr);

        this.loading = false;

        return of(null);
      })
    );
  }

  private toISO(date: string): string {
    return new Date(date).toISOString();
  }

  private isDateRangeValid(
    start: string | null,
    end: string | null
  ): boolean {

    if (!start || !end) return true;

    return new Date(start) <= new Date(end);
  }

  private buildCharts(
    data: DevActivity[],
    pr: PRCycleTimeResult[]
  ): void {

    if (!data || data.length === 0) {

      this.commitFrequencyData = [];
      this.activityBreakdownData = [];
      this.activityShareData = [];
      this.prCycleTimeData = [];

      return;
    }

    const grouped: Record<string, any> = {};

    data.forEach(a => {

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

      grouped[date].commits += a.commits;
      grouped[date].prs += a.pullRequests;
      grouped[date].merges += a.merges;
      grouped[date].meetings += a.meetings;
      grouped[date].docs += a.documentation;
    });

    const dates = Object.keys(grouped).sort();

    this.commitFrequencyData = [
      {
        name: 'Commits',
        series: dates.map(d => ({
          name: d,
          value: grouped[d].commits
        }))
      }
    ];

    this.activityBreakdownData = dates.map(d => ({
      name: d,
      series: [
        {
          name: 'Commits',
          value: grouped[d].commits
        },
        {
          name: 'PRs',
          value: grouped[d].prs
        },
        {
          name: 'Merges',
          value: grouped[d].merges
        }
      ]
    }));

    this.activityShareData = [
      {
        name: 'Commits',
        value: this.summary.totalCommits
      },
      {
        name: 'PRs',
        value: this.summary.totalPRs
      },
      {
        name: 'Merges',
        value: this.summary.totalMerges
      }
    ];

    this.prCycleTimeData = [
      {
        name: 'PR Cycle Time',
        series: pr.map(x => ({
          name: x.date.split('T')[0],
          value: Number(x.avgHours.toFixed(2))
        }))
      }
    ];
  }
}
