import { Injectable } from '@angular/core';
import { Apollo, gql } from 'apollo-angular';
import { map, Observable } from 'rxjs';

export interface TrackedRepository {
  id: number;
  fullName: string;
  owner: string;
  name: string;
  provider: string;
  description: string | null;
  language: string | null;
  htmlUrl: string | null;
  starCount: number;
  forkCount: number;
  openIssueCount: number;
  isActive: boolean;
  isDemoData: boolean;
  isPinned: boolean;
  nickname: string | null;
  notes: string | null;
  syncIntervalMinutes: number | null;
  syncStatus: string;
  lastSyncCompletedAtUtc: string | null;
  lastSyncError: string | null;
  lastSyncDurationMs: number | null;
  totalCommits: number;
  totalPullRequests: number;
  totalContributors: number;
}

export interface RepositoryHealth {
  fullName: string;
  commits: number;
  pullRequests: number;
  mergedPullRequests: number;
  openPullRequests: number;
  mergeRate: number;
  medianCycleHours: number;
  p95CycleHours: number;
  activeDays: number;
  commitsPerWeek: number;
  contributors: number;
  topContributorShare: number;
  oldestOpenPullRequestUtc: string | null;
  lastCommitUtc: string | null;
}

export interface LanguageSlice {
  language: string;
  bytes: number;
  percentage: number;
}

export interface CommitRow {
  sha: string;
  message: string;
  committedAt: string;
  authorName: string;
  contributor: { login: string; avatarUrl: string | null } | null;
  repository: { fullName: string } | null;
}

export interface PullRequestRow {
  number: number;
  title: string;
  state: string;
  createdAt: string;
  mergedAt: string | null;
  authorLogin: string | null;
  repository: { fullName: string } | null;
}

export interface SyncRunRow {
  startedAtUtc: string;
  completedAtUtc: string | null;
  status: string;
  trigger: string;
  commitsIngested: number;
  pullRequestsIngested: number;
  contributorsAdded: number;
  truncated: boolean;
  durationMs: number;
  error: string | null;
}

export interface MutationResult {
  success: boolean;
  message: string;
  repositoryId?: number | null;
  fullName?: string | null;
  syncStatus?: string | null;
}

const REPOSITORY_FIELDS = `
  id fullName owner name provider description language htmlUrl
  starCount forkCount openIssueCount
  isActive isDemoData isPinned nickname notes syncIntervalMinutes
  syncStatus lastSyncCompletedAtUtc lastSyncError lastSyncDurationMs
  totalCommits totalPullRequests totalContributors
`;

export interface SummaryStats {
  totalCommits: number;
  totalPullRequests: number;
  mergedPullRequests: number;
  openPullRequests: number;
  contributorCount: number;
  repositoryCount: number;
  /** Mean is retained as supporting detail; median is the honest headline. */
  avgPrCycleHours: number;
  medianPrCycleHours: number;
  p95PrCycleHours: number;
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
  name: string | null;
  avatarUrl: string | null;
  htmlUrl: string | null;
  isBot: boolean;
  commits: number;
  pullRequestsOpened: number;
  pullRequestsMerged: number;
  repositoryCount: number;
  score: number;
}

export interface PageInfo {
  hasNextPage: boolean;
  hasPreviousPage: boolean;
  endCursor: string | null;
  totalCount: number | null;
}

export interface Connection<T> {
  items: T[];
  pageInfo: PageInfo;
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
  contributorCount: 0, repositoryCount: 0,
  avgPrCycleHours: 0, medianPrCycleHours: 0, p95PrCycleHours: 0
};

const EMPTY_CONNECTION: Connection<any> = {
  items: [],
  pageInfo: { hasNextPage: false, hasPreviousPage: false, endCursor: null, totalCount: 0 }
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
          trackedRepositories(includeInactive: $includeInactive) { ${REPOSITORY_FIELDS} }
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
            contributorCount repositoryCount
            avgPrCycleHours medianPrCycleHours p95PrCycleHours
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

  /**
   * Contributors are paginated server-side. Callers that only need a ranking
   * (charts, filter menus) request a bounded page rather than the whole set.
   */
  getContributors(
    f: AnalyticsFilters,
    options: { first?: number; after?: string | null; search?: string | null; sortBy?: string } = {}
  ): Observable<Connection<ContributorSummary>> {
    return this.apollo.query<any>({
      query: gql`
        query ($startDate: DateTime, $endDate: DateTime, $repository: String, $excludeBots: Boolean!,
               $first: Int, $after: String, $search: String, $sortBy: String) {
          contributors(
            startDate: $startDate, endDate: $endDate, repository: $repository, excludeBots: $excludeBots,
            first: $first, after: $after, search: $search, sortBy: $sortBy
          ) {
            items {
              login name avatarUrl htmlUrl isBot
              commits pullRequestsOpened pullRequestsMerged repositoryCount score
            }
            pageInfo { hasNextPage hasPreviousPage endCursor totalCount }
          }
        }
      `,
      variables: {
        startDate: f.startDate,
        endDate: f.endDate,
        repository: f.repository,
        excludeBots: f.excludeBots,
        first: options.first ?? 50,
        after: options.after ?? null,
        search: options.search ?? null,
        sortBy: options.sortBy ?? 'commits'
      }
    }).pipe(map(res => res.data?.contributors ?? EMPTY_CONNECTION));
  }

  // =====================================================================
  // Repository registry
  // =====================================================================

  /** Paginated, searchable, sortable registry listing. */
  getRepositoriesPage(options: {
    includeInactive?: boolean;
    search?: string | null;
    sortBy?: string | null;
    descending?: boolean;
    after?: string | null;
    first?: number;
  } = {}): Observable<Connection<TrackedRepository>> {
    return this.apollo.query<any>({
      query: gql`
        query ($includeInactive: Boolean!, $search: String, $sortBy: String,
               $descending: Boolean!, $after: String, $first: Int) {
          repositories(includeInactive: $includeInactive, search: $search, sortBy: $sortBy,
                       descending: $descending, after: $after, first: $first) {
            items { ${REPOSITORY_FIELDS} }
            pageInfo { hasNextPage hasPreviousPage endCursor totalCount }
          }
        }
      `,
      variables: {
        includeInactive: options.includeInactive ?? true,
        search: options.search ?? null,
        sortBy: options.sortBy ?? null,
        descending: options.descending ?? true,
        after: options.after ?? null,
        first: options.first ?? 25
      },
      fetchPolicy: 'network-only'
    }).pipe(map(res => res.data?.repositories ?? EMPTY_CONNECTION));
  }

  getRepositoryHealth(fullName: string, f: Partial<AnalyticsFilters> = {}): Observable<RepositoryHealth | null> {
    return this.apollo.query<any>({
      query: gql`
        query ($fullName: String!, $startDate: DateTime, $endDate: DateTime, $excludeBots: Boolean!) {
          repositoryHealth(fullName: $fullName, startDate: $startDate, endDate: $endDate, excludeBots: $excludeBots) {
            fullName commits pullRequests mergedPullRequests openPullRequests
            mergeRate medianCycleHours p95CycleHours activeDays commitsPerWeek
            contributors topContributorShare oldestOpenPullRequestUtc lastCommitUtc
          }
        }
      `,
      variables: {
        fullName,
        startDate: f.startDate ?? null,
        endDate: f.endDate ?? null,
        excludeBots: f.excludeBots ?? false
      }
    }).pipe(map(res => res.data?.repositoryHealth ?? null));
  }

  getLanguageDistribution(repository: string | null): Observable<LanguageSlice[]> {
    return this.apollo.query<any>({
      query: gql`
        query ($repository: String) {
          languageDistribution(repository: $repository) { language bytes percentage }
        }
      `,
      variables: { repository }
    }).pipe(map(res => res.data?.languageDistribution ?? []));
  }

  getCommitsPage(f: AnalyticsFilters, after: string | null = null, first = 15): Observable<Connection<CommitRow>> {
    return this.apollo.query<any>({
      query: gql`
        query ($startDate: DateTime, $endDate: DateTime, $contributor: String, $repository: String,
               $excludeBots: Boolean!, $after: String, $first: Int) {
          commits(startDate: $startDate, endDate: $endDate, contributor: $contributor,
                  repository: $repository, excludeBots: $excludeBots, after: $after, first: $first) {
            items {
              sha message committedAt authorName
              contributor { login avatarUrl }
              repository { fullName }
            }
            pageInfo { hasNextPage hasPreviousPage endCursor totalCount }
          }
        }
      `,
      variables: { ...this.vars(f), after, first }
    }).pipe(map(res => res.data?.commits ?? EMPTY_CONNECTION));
  }

  getPullRequestsPage(
    f: AnalyticsFilters,
    state: string | null = null,
    after: string | null = null,
    first = 15
  ): Observable<Connection<PullRequestRow>> {
    return this.apollo.query<any>({
      query: gql`
        query ($startDate: DateTime, $endDate: DateTime, $contributor: String, $repository: String,
               $excludeBots: Boolean!, $state: String, $after: String, $first: Int) {
          pullRequests(startDate: $startDate, endDate: $endDate, contributor: $contributor,
                       repository: $repository, excludeBots: $excludeBots, state: $state,
                       after: $after, first: $first) {
            items {
              number title state createdAt mergedAt authorLogin
              repository { fullName }
            }
            pageInfo { hasNextPage hasPreviousPage endCursor totalCount }
          }
        }
      `,
      variables: { ...this.vars(f), state, after, first }
    }).pipe(map(res => res.data?.pullRequests ?? EMPTY_CONNECTION));
  }

  getSyncRuns(repositoryId: number, after: string | null = null, first = 10): Observable<Connection<SyncRunRow>> {
    return this.apollo.query<any>({
      query: gql`
        query ($repositoryId: Int!, $after: String, $first: Int) {
          syncRuns(repositoryId: $repositoryId, after: $after, first: $first) {
            items {
              startedAtUtc completedAtUtc status trigger
              commitsIngested pullRequestsIngested contributorsAdded
              truncated durationMs error
            }
            pageInfo { hasNextPage hasPreviousPage endCursor totalCount }
          }
        }
      `,
      variables: { repositoryId, after, first },
      fetchPolicy: 'network-only'
    }).pipe(map(res => res.data?.syncRuns ?? EMPTY_CONNECTION));
  }

  // =====================================================================
  // Management mutations
  //
  // Every one requires the admin key, supplied through the caller's context.
  // The UI hides these actions until a key is present, so an unauthorised
  // call should not be reachable from the interface.
  // =====================================================================

  addRepository(fullName: string, context: Record<string, unknown>): Observable<MutationResult> {
    return this.apollo.mutate<any>({
      mutation: gql`
        mutation ($fullName: String!) {
          addRepository(fullName: $fullName) { success message repositoryId fullName syncStatus }
        }
      `,
      variables: { fullName },
      context
    }).pipe(map(res => res.data?.addRepository ?? failedMutation()));
  }

  updateRepository(
    id: number,
    changes: { nickname?: string | null; notes?: string | null; isPinned?: boolean | null; syncIntervalMinutes?: number | null },
    context: Record<string, unknown>
  ): Observable<MutationResult> {
    return this.apollo.mutate<any>({
      mutation: gql`
        mutation ($id: Int!, $nickname: String, $notes: String, $isPinned: Boolean, $syncIntervalMinutes: Int) {
          updateRepository(id: $id, nickname: $nickname, notes: $notes,
                           isPinned: $isPinned, syncIntervalMinutes: $syncIntervalMinutes) {
            success message
          }
        }
      `,
      variables: {
        id,
        nickname: changes.nickname ?? null,
        notes: changes.notes ?? null,
        isPinned: changes.isPinned ?? null,
        syncIntervalMinutes: changes.syncIntervalMinutes ?? null
      },
      context
    }).pipe(map(res => res.data?.updateRepository ?? failedMutation()));
  }

  removeRepository(id: number, context: Record<string, unknown>): Observable<MutationResult> {
    return this.apollo.mutate<any>({
      mutation: gql` mutation ($id: Int!) { removeRepository(id: $id) { success message } } `,
      variables: { id },
      context
    }).pipe(map(res => res.data?.removeRepository ?? failedMutation()));
  }

  setRepositoryActive(id: number, isActive: boolean, context: Record<string, unknown>): Observable<MutationResult> {
    return this.apollo.mutate<any>({
      mutation: gql`
        mutation ($id: Int!, $isActive: Boolean!) {
          setRepositoryActive(id: $id, isActive: $isActive) { success message }
        }
      `,
      variables: { id, isActive },
      context
    }).pipe(map(res => res.data?.setRepositoryActive ?? failedMutation()));
  }

  syncRepository(id: number, context: Record<string, unknown>): Observable<MutationResult> {
    return this.apollo.mutate<any>({
      mutation: gql` mutation ($id: Int!) { syncRepository(id: $id) { success message } } `,
      variables: { id },
      context
    }).pipe(map(res => res.data?.syncRepository ?? failedMutation()));
  }

  syncAllRepositories(context: Record<string, unknown>): Observable<MutationResult> {
    return this.apollo.mutate<any>({
      mutation: gql` mutation { syncAllRepositories { success message } } `,
      context
    }).pipe(map(res => res.data?.syncAllRepositories ?? failedMutation()));
  }
}

/** Used when a mutation returns no payload, so callers never see undefined. */
function failedMutation(): MutationResult {
  return { success: false, message: 'The request did not complete.' };
}
