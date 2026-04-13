import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from '../auth/auth.service';

@Injectable({
  providedIn: 'root'
})
export class EmiService {
  private apiUrl = environment.apiUrl + '/api/emis';

  constructor(private http: HttpClient, private authService: AuthService) { }

  private getHeaders() {
    return new HttpHeaders({ 'Authorization': `Bearer ${this.authService.getAuthToken()}` });
  }

  getEmis(): Observable<any[]> {
    return this.http.get<any[]>(this.apiUrl, { headers: this.getHeaders() });
  }

  createEmi(data: any): Observable<any> {
    return this.http.post(this.apiUrl, data, { headers: this.getHeaders() });
  }

  updateEmi(id: string, data: any): Observable<any> {
    return this.http.patch(`${this.apiUrl}/${id}`, data, { headers: this.getHeaders() });
  }

  deleteEmi(id: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/${id}`, { headers: this.getHeaders() });
  }
}
