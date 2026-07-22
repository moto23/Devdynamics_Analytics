import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';

import { IconComponent } from '../shared/icon.component';
import { ThemeService } from '../core/theme.service';

interface Capability {
  icon: string;
  title: string;
  body: string;
}

interface TechGroup {
  label: string;
  items: string[];
}

/**
 * Public entry point. Introduces the project and hands off to the dashboard.
 *
 * Deliberately static: it renders instantly even while the API is cold, so a
 * first-time visitor never lands on a spinner. The dashboard, not this page,
 * is where data loads.
 */
@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [CommonModule, RouterLink, IconComponent],
  templateUrl: './landing.component.html',
  styleUrls: ['./landing.component.css']
})
export class LandingComponent {

  private readonly themeService = inject(ThemeService);
  readonly theme = this.themeService.theme;

  toggleTheme() {
    this.themeService.toggle();
  }

  readonly capabilities: Capability[] = [
    {
      icon: 'commit',
      title: 'Commit trends',
      body: 'Daily commit activity across every tracked repository, filterable by repository, contributor and date range.'
    },
    {
      icon: 'pr',
      title: 'PR cycle analytics',
      body: 'Time from open to merge, bucketed by merge date over merged pull requests — the measure teams actually act on.'
    },
    {
      icon: 'users',
      title: 'Contributor insights',
      body: 'Who is contributing where, ranked and comparable, with automation accounts separable from people.'
    },
    {
      icon: 'repo',
      title: 'Repository analytics',
      body: 'Per-repository health, activity and language breakdowns, scoped to whatever is currently tracked.'
    },
    {
      icon: 'refresh',
      title: 'Incremental sync',
      body: 'Cursor-based synchronisation that resumes after interruption and cannot create duplicates.'
    },
    {
      icon: 'database',
      title: 'Dynamic registry',
      body: 'Track any public repository at runtime. Adding or removing one is a database change, never a code change.'
    }
  ];

  readonly stack: TechGroup[] = [
    { label: 'Frontend', items: ['Angular 19', 'TypeScript', 'Apollo Client', 'Chart.js'] },
    { label: 'API',      items: ['ASP.NET Core 8', 'HotChocolate GraphQL'] },
    { label: 'Data',     items: ['EF Core 8', 'Azure SQL Database'] },
    { label: 'Ingestion',items: ['GitHub REST API', 'Background sync worker'] }
  ];
}
