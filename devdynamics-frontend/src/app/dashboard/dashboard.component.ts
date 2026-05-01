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

  commitFrequencyData: any[] = [];
  activityBreakdownData: any[] = [];
  activityShareData: any[] = [];
  prCycleTimeData: any[] = [];

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

    const start = this.startDate
      ? new Date(this.startDate).toISOString()
      : null;

    const end = this.endDate
      ? new Date(this.endDate).toISOString()
      : null;

    this.loading = true;

    this.errorMessage = '';

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

      retry(1),

      catchError(err => {

        console.error(err);

        this.errorMessage = 'Failed to load dashboard data';

        this.loading = false;

        return of(null);
      }),

      switchMap((res: any) => {

        if (!res) {
          return of(null);
        }

        const activities = res.activities || [];

        const summary = res.summary || {
          totalCommits: 0,
          totalPRs: 0,
          totalMerges: 0,
          totalMeetings: 0,
          totalDocs: 0,
          contributorCount: 0
        };

        const pr = res.pr || [];

        this.summary = summary;

        this.hasNoData = activities.length === 0;

        this.contributors = [
          ...new Set(
            activities
              .map((x: any) => x.contributor)
              .filter(Boolean)
          )
        ];

        this.buildCharts(activities, pr);

        this.loading = false;

        return of(null);
      })
    );
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

    const grouped: any = {};

    data.forEach(a => {

      if (!a.time) return;

      const date = a.time.split('T')[0];

      if (!grouped[date]) {

        grouped[date] = {
          commits: 0,
          prs: 0,
          merges: 0
        };
      }

      grouped[date].commits += Number(a.commits || 0);
      grouped[date].prs += Number(a.pullRequests || 0);
      grouped[date].merges += Number(a.merges || 0);
    });

    const dates = Object.keys(grouped);

    this.commitFrequencyData = [
      {
        name: 'Commits',
        series: dates.map(date => ({
          name: date,
          value: Number(grouped[date].commits || 0)
        }))
      }
    ];

    this.activityBreakdownData = dates.map(date => ({
      name: date,
      series: [
        {
          name: 'Commits',
          value: Number(grouped[date].commits || 0)
        },
        {
          name: 'PRs',
          value: Number(grouped[date].prs || 0)
        },
        {
          name: 'Merges',
          value: Number(grouped[date].merges || 0)
        }
      ]
    }));

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

    this.prCycleTimeData = [
      {
        name: 'PR Cycle Time',
        series: pr
          .filter(x => x?.date)
          .map(x => ({
            name: x.date.split('T')[0],
            value: Number(x.avgHours || 0)
          }))
      }
    ];

    console.log(this.commitFrequencyData);
    console.log(this.activityBreakdownData);
    console.log(this.activityShareData);
    console.log(this.prCycleTimeData);
  }
}
