import { Routes } from '@angular/router';
import { DashboardComponent } from './dashboard/dashboard.component';
import { SignupComponent } from './signup/signup.component';
import { CompaniesComponent } from './companies/companies.component';
import { CompanyDetailComponent } from './companies/company-detail/company-detail.component';

export const routes: Routes = [
  { path: '', component: DashboardComponent },
  { path: 'dashboard', redirectTo: '', pathMatch: 'full' }, // FIX
  { path: 'signup', component: SignupComponent },
  { path: 'companies', component: CompaniesComponent },
  { path: 'companies/:name', component: CompanyDetailComponent } // NEW
];