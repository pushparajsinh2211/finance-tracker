import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from '../auth/auth.service';

@Injectable({
  providedIn: 'root'
})
export class SavingsService {
  private apiUrl = environment.apiUrl + '/api/savings';

  constructor(private http: HttpClient, private authService: AuthService) { }



  getSavings(): Observable<any[]> {
    return this.http.get<any[]>(this.apiUrl);
  }

  createSavings(data: any): Observable<any> {
    return this.http.post(this.apiUrl, data);
  }

  updateSavings(id: string, data: any): Observable<any> {
    return this.http.patch(`${this.apiUrl}/${id}`, data);
  }

  deleteSavings(id: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/${id}`);
  }
}
