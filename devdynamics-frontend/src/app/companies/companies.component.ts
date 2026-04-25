import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

interface Company {
  name: string;
  commits: number;
  prs: number;
  contributors: number;
}

@Component({
  selector: 'app-companies',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './companies.component.html',
  styleUrls: ['./companies.component.css']
})
export class CompaniesComponent {
  searchText = '';

  constructor(private router: Router) {}

  companies: Company[] = [
    { name: 'Google', commits: 120, prs: 45, contributors: 5 },
    { name: 'Microsoft', commits: 95, prs: 30, contributors: 4 },
    { name: 'Amazon', commits: 150, prs: 60, contributors: 6 },
    { name: 'Netflix', commits: 80, prs: 22, contributors: 3 }
  ];

  get filteredCompanies() {
    return this.companies.filter(c =>
      c.name.toLowerCase().includes(this.searchText.toLowerCase())
    );
  }

  // 🔥 THIS IS THE FIX
  goToCompany(name: string) {
    this.router.navigate(['/'], {
      queryParams: { company: name }
    });
  }
}