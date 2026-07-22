import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { catchError, of } from 'rxjs';

import { GraphqlService, TrackedRepository } from '../services/graphql.service';

/**
 * Tracked repositories and their sync state.
 *
 * Reads the registry, so it shows whatever is currently tracked — one
 * repository or a hundred — with no code change. This is the page the
 * repository-management UI (add / remove / enable / disable / resync) will
 * build on.
 */
@Component({
  selector: 'app-repositories',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './repositories.component.html',
  styleUrls: ['./repositories.component.css']
})
export class RepositoriesComponent implements OnInit {

  searchText = '';
  loading = false;
  errorMessage = '';

  repositories: TrackedRepository[] = [];

  constructor(
    private gql: GraphqlService,
    private router: Router
  ) {}

  ngOnInit() {
    this.load();
  }

  load() {
    this.loading = true;
    this.errorMessage = '';

    this.gql.getTrackedRepositories(true)
      .pipe(catchError(err => {
        console.error('API ERROR:', err);
        this.errorMessage = 'Unable to load repositories. The API may be waking up.';
        this.loading = false;
        return of([] as TrackedRepository[]);
      }))
      .subscribe(repos => {
        this.repositories = repos;
        this.loading = false;
      });
  }

  get filtered(): TrackedRepository[] {
    const term = this.searchText.trim().toLowerCase();

    if (!term) return this.repositories;

    return this.repositories.filter(r =>
      r.fullName.toLowerCase().includes(term) ||
      (r.language || '').toLowerCase().includes(term)
    );
  }

  /** Opens the dashboard scoped to a single repository. */
  viewAnalytics(repo: TrackedRepository) {
    this.router.navigate(['/'], { queryParams: { repository: repo.fullName } });
  }

  statusClass(status: string): string {
    switch (status) {
      case 'Succeeded': return 'status-ok';
      case 'PartiallySynced': return 'status-warn';
      case 'Failed': return 'status-error';
      case 'Syncing':
      case 'Queued': return 'status-busy';
      default: return 'status-idle';
    }
  }
}
