import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { environment } from '../../environments/environment';

interface FinanceHubSource {
  id: string;
  name: string;
  description: string;
  availability: 'available' | 'planned';
  automatic: boolean;
  capabilities: string[];
}

@Component({
  selector: 'app-connections',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './connections.component.html',
  styleUrls: ['./connections.component.css']
})
export class ConnectionsComponent implements OnInit {
  sources: FinanceHubSource[] = [];
  loading = true;
  error = '';

  constructor(private readonly http: HttpClient) {}

  ngOnInit(): void {
    this.http.get<FinanceHubSource[]>(environment.apiUrl + '/api/financehub/sources').subscribe({
      next: sources => {
        this.sources = sources;
        this.loading = false;
      },
      error: () => {
        this.error = 'Data sources could not be loaded. You can continue entering cash expenses manually.';
        this.loading = false;
      }
    });
  }

  availabilityLabel(source: FinanceHubSource): string {
    return source.availability === 'available' ? 'Available now' : 'Planned integration';
  }
}
