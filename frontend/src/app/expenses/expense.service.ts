import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from '../auth/auth.service';

@Injectable({
  providedIn: 'root'
})
export class ExpenseService {
  private apiUrl = environment.apiUrl + '/api/expenses';

  constructor(private http: HttpClient, private authService: AuthService) { }

  private getHeaders() {
    return new HttpHeaders({ 'Authorization': `Bearer ${this.authService.getAuthToken()}` });
  }

  getExpenses(filters?: any): Observable<any[]> {
    let params: any = {};
    if (filters?.startDate) params.startDate = filters.startDate;
    if (filters?.endDate) params.endDate = filters.endDate;
    if (filters?.categoryId) params.categoryId = filters.categoryId;
    return this.http.get<any[]>(this.apiUrl, { headers: this.getHeaders(), params });
  }

  addExpense(expense: any): Observable<any> {
    return this.http.post(this.apiUrl, expense, { headers: this.getHeaders() });
  }

  updateExpense(id: string, expense: any): Observable<any> {
    return this.http.patch(`${this.apiUrl}/${id}`, expense, { headers: this.getHeaders() });
  }

  deleteExpense(id: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/${id}`, { headers: this.getHeaders() });
  }
}
