import { Routes } from '@angular/router';

/**
 * Routes are lazily loaded so the landing page — the first thing a visitor
 * sees, and the one that must render while the API is still cold — does not
 * ship the dashboard's code.
 */
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./landing/landing.component').then(m => m.LandingComponent),
    title: 'DevDynamics — GitHub Analytics Platform'
  },

  {
    // Everything inside the product renders within the shell. Apollo is
    // provided by ShellComponent itself so it lands in the lazy shell chunk
    // rather than the initial bundle.
    path: '',
    loadComponent: () => import('./layout/shell.component').then(m => m.ShellComponent),
    children: [
      {
        path: 'dashboard',
        loadComponent: () => import('./dashboard/dashboard.component').then(m => m.DashboardComponent),
        title: 'Overview — DevDynamics'
      },
      {
        path: 'repositories',
        loadComponent: () => import('./repositories/repositories.component').then(m => m.RepositoriesComponent),
        title: 'Repositories — DevDynamics'
      },
      {
        // owner/name rather than an id, so the URL is the identifier a user
        // can read and type.
        path: 'repositories/:owner/:name',
        loadComponent: () => import('./repositories/repository-detail.component').then(m => m.RepositoryDetailComponent),
        title: 'Repository — DevDynamics'
      },
      {
        // Replaced by the dedicated contributors page in 5C; routing to it must
        // not 404 in the meantime.
        path: 'contributors',
        loadComponent: () => import('./repositories/repositories.component').then(m => m.RepositoriesComponent),
        title: 'Contributors — DevDynamics'
      },
      {
        path: 'settings',
        loadComponent: () => import('./settings/settings.component').then(m => m.SettingsComponent),
        title: 'Settings — DevDynamics'
      }
    ]
  },

  // Kept so previously shared links keep working.
  { path: 'companies', redirectTo: 'repositories', pathMatch: 'full' },

  { path: '**', redirectTo: '' }
];
