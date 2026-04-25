import { Injectable } from '@angular/core';
import { Apollo, gql } from 'apollo-angular';
import { map, Observable } from 'rxjs';

export interface DevActivity {
  id: number;
  time: string;
  commits: number;
  pullRequests: number;
  merges: number;
  meetings: number;
  documentation: number;
  contributor: string;
  company: string; // ✅ NEW
  prOpenedAt: string;
  prMergedAt: string;
}

export interface PRCycleTimeResult {
  date: string;
  avgHours: number;
}

export interface SummaryResult {
  totalCommits: number;
  totalPRs: number;
  totalMerges: number;
  totalMeetings: number;
  totalDocs: number;
  contributorCount: number;
}

@Injectable({ providedIn: 'root' })
export class GraphqlService {
  constructor(private apollo: Apollo) {}

  getDevActivities(
    startDate: string | null,
    endDate: string | null,
    contributor: string | null,
    company: string | null
  ): Observable<DevActivity[]> {
    return this.apollo.query<any>({
      query: gql`
        query ($startDate: DateTime, $endDate: DateTime, $contributor: String, $company: String) {
          devActivities(startDate: $startDate, endDate: $endDate, contributor: $contributor, company: $company) {
            id time commits pullRequests merges meetings documentation contributor company prOpenedAt prMergedAt
          }
        }
      `,
      variables: { startDate, endDate, contributor, company }
    }).pipe(map(res => res.data.devActivities));
  }

  getSummaryStats(
    startDate: string | null,
    endDate: string | null,
    contributor: string | null,
    company: string | null
  ): Observable<SummaryResult> {
    return this.apollo.query<any>({
      query: gql`
        query ($startDate: DateTime, $endDate: DateTime, $contributor: String, $company: String) {
          summaryStats(startDate: $startDate, endDate: $endDate, contributor: $contributor, company: $company) {
            totalCommits totalPRs totalMerges totalMeetings totalDocs contributorCount
          }
        }
      `,
      variables: { startDate, endDate, contributor, company }
    }).pipe(map(res => res.data.summaryStats));
  }

  getPRCycleTime(
    startDate: string | null,
    endDate: string | null,
    company: string | null
  ): Observable<PRCycleTimeResult[]> {
    return this.apollo.query<any>({
      query: gql`
        query ($startDate: DateTime, $endDate: DateTime, $company: String) {
          prCycleTime(startDate: $startDate, endDate: $endDate, company: $company) {
            date avgHours
          }
        }
      `,
      variables: { startDate, endDate, company }
    }).pipe(map(res => res.data.prCycleTime));
  }
}