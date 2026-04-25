import { Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-company-detail',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './company-detail.component.html',
  styleUrls: ['./company-detail.component.css']
})
export class CompanyDetailComponent implements OnInit {

  companyName = '';

  // Fake data (later connect GraphQL)
  data = {
    commits: 0,
    prs: 0,
    contributors: 0
  };

  constructor(private route: ActivatedRoute) {}

  ngOnInit() {
    this.companyName = this.route.snapshot.paramMap.get('name') || '';

    // simulate data load
    this.loadCompanyData();
  }

  loadCompanyData() {
    this.data = {
      commits: Math.floor(Math.random() * 200),
      prs: Math.floor(Math.random() * 100),
      contributors: Math.floor(Math.random() * 10)
    };
  }
}