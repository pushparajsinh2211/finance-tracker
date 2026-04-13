import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from '../auth/auth.service';

@Injectable({
  providedIn: 'root'
})
export class BudgetService {
  private apiUrl = environment.apiUrl + '/api/budgets';

  constructor(private http: HttpClient, private authService: AuthService) { }

  private getHeaders() {
    return new HttpHeaders({ 'Authorization': `Bearer ${this.authService.getAuthToken()}` });
  }

  getBudgets(month: string): Observable<any[]> {
    return this.http.get<any[]>(`${this.apiUrl}?month=${month}`, { headers: this.getHeaders() });
  }

  createBudget(budget: any): Observable<any> {
    return this.http.post(this.apiUrl, budget, { headers: this.getHeaders() });
  }

  updateBudget(id: string, amount: number): Observable<any> {
    return this.http.patch(`${this.apiUrl}/${id}`, { amount }, { headers: this.getHeaders() });
  }

  deleteBudget(id: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/${id}`, { headers: this.getHeaders() });
  }
}
