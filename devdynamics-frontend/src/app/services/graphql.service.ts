import { Injectable } from '@angular/core';
import { Apollo, gql } from 'apollo-angular';
import { map, Observable } from 'rxjs';

export interface TrackedRepository {
  id: number;
  fullName: string;
  owner: string;
  name: string;
  provider: string;
  language: string | null;
  htmlUrl: string | null;
  starCount: number;
  isActive: boolean;
  isDemoData: boolean;
  syncStatus: string;
  lastSyncCompletedAtUtc: string | null;
  lastSyncError: string | null;
  totalCommits: number;
  totalPullRequests: number;
  totalContributors: number;
}

export interface SummaryStats {
  totalCommits: number;
  totalPullRequests: number;
  mergedPullRequests: number;
  openPullRequests: number;
  contributorCount: number;
  repositoryCount: number;
  avgPrCycleHours: number;
}

export interface CommitTrendPoint {
  date: string;
  commits: number;
}

export interface PrCyclePoint {
  date: string;
  avgHours: number;
  mergedCount: number;
}

export interface ContributorSummary {
  login: string;
  avatarUrl: string | null;
  isBot: boolean;
  commits: number;
}

/**
 * Analytics filters. `repository` is a full name ("owner/name"); omitting it
 * reports across every active tracked repository, whatever is registered at
 * the time. Nothing here assumes a fixed set or count of repositories.
 */
export interface AnalyticsFilters {
  startDate: string | null;
  endDate: string | null;
  contributor: string | null;
  repository: string | null;
  excludeBots: boolean;
}

/**
 * Provided by ShellComponent, not the root injector.
 *
 * Apollo is supplied at the shell so it stays out of the landing page's
 * bundle, and a root-provided service cannot resolve a component-provided
 * dependency — so this must live in the same injector as Apollo.
 */
/** Returned when a response carries no summary, so callers never dereference undefined. */
const EMPTY_SUMMARY: SummaryStats = {
  totalCommits: 0, totalPullRequests: 0, mergedPullRequests: 0, openPullRequests: 0,
  contributorCount: 0, repositoryCount: 0, avgPrCycleHours: 0
};

@Injectable()
export class GraphqlService {
  constructor(private apollo: Apollo) {}

  private vars(f: AnalyticsFilters) {
    return {
      startDate: f.startDate,
      endDate: f.endDate,
      contributor: f.contributor,
      repository: f.repository,
      excludeBots: f.excludeBots
    };
  }

  getTrackedRepositories(includeInactive = false): Observable<TrackedRepository[]> {
    return this.apollo.query<any>({
      query: gql`
        query ($includeInactive: Boolean!) {
          trackedRepositories(includeInactive: $includeInactive) {
            id fullName owner name provider language htmlUrl starCount
            isActive isDemoData syncStatus lastSyncCompletedAtUtc lastSyncError
            totalCommits totalPullRequests totalContributors
          }
        }
      `,
      variables: { includeInactive },
      // Sync state changes server-side; never serve it from cache.
      fetchPolicy: 'network-only'
    }).pipe(map(res => res.data?.trackedRepositories ?? []));
  }

  getSummaryStats(f: AnalyticsFilters): Observable<SummaryStats> {
    return this.apollo.query<any>({
      query: gql`
        query ($startDate: DateTime, $endDate: DateTime, $contributor: String, $repository: String, $excludeBots: Boolean!) {
          summaryStats(startDate: $startDate, endDate: $endDate, contributor: $contributor, repository: $repository, excludeBots: $excludeBots) {
            totalCommits totalPullRequests mergedPullRequests openPullRequests
            contributorCount repositoryCount avgPrCycleHours
          }
        }
      `,
      variables: this.vars(f)
    }).pipe(map(res => res.data?.summaryStats ?? EMPTY_SUMMARY));
  }

  getCommitTrends(f: AnalyticsFilters): Observable<CommitTrendPoint[]> {
    return this.apollo.query<any>({
      query: gql`
        query ($startDate: DateTime, $endDate: DateTime, $contributor: String, $repository: String, $excludeBots: Boolean!) {
          commitTrends(startDate: $startDate, endDate: $endDate, contributor: $contributor, repository: $repository, excludeBots: $excludeBots) {
            date commits
          }
        }
      `,
      variables: this.vars(f)
    }).pipe(map(res => res.data?.commitTrends ?? []));
  }

  getPrCycleTime(f: AnalyticsFilters): Observable<PrCyclePoint[]> {
    return this.apollo.query<any>({
      query: gql`
        query ($startDate: DateTime, $endDate: DateTime, $contributor: String, $repository: String, $excludeBots: Boolean!) {
          prCycleTime(startDate: $startDate, endDate: $endDate, contributor: $contributor, repository: $repository, excludeBots: $excludeBots) {
            date avgHours mergedCount
          }
        }
      `,
      variables: this.vars(f)
    }).pipe(map(res => res.data?.prCycleTime ?? []));
  }

  getContributors(f: AnalyticsFilters): Observable<ContributorSummary[]> {
    return this.apollo.query<any>({
      query: gql`
        query ($startDate: DateTime, $endDate: DateTime, $repository: String, $excludeBots: Boolean!) {
          contributors(startDate: $startDate, endDate: $endDate, repository: $repository, excludeBots: $excludeBots) {
            login avatarUrl isBot commits
          }
        }
      `,
      variables: {
        startDate: f.startDate,
        endDate: f.endDate,
        repository: f.repository,
        excludeBots: f.excludeBots
      }
    }).pipe(map(res => res.data?.contributors ?? []));
  }
}
