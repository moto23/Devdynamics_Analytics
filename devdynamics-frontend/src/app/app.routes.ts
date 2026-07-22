import { Routes } from '@angular/router';
import { DashboardComponent } from './dashboard/dashboard.component';
import { SignupComponent } from './signup/signup.component';
import { RepositoriesComponent } from './repositories/repositories.component';

export const routes: Routes = [
  { path: '', component: DashboardComponent },
  { path: 'dashboard', redirectTo: '', pathMatch: 'full' },
  { path: 'repositories', component: RepositoriesComponent },

  // Kept so previously shared /companies links keep working.
  { path: 'companies', redirectTo: 'repositories', pathMatch: 'full' },

  { path: 'signup', component: SignupComponent },
  { path: '**', redirectTo: '' }
];
