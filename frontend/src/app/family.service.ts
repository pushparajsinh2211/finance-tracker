import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';
import { AuthService } from './auth/auth.service';

@Injectable({
  providedIn: 'root'
})
export class FamilyService {
  private apiUrl = environment.apiUrl + '/api/family';

  constructor(
    private http: HttpClient,
    private authService: AuthService
  ) { }

  private getHeaders(): HttpHeaders {
    return new HttpHeaders({
      'Authorization': `Bearer ${this.authService.getAuthToken()}`
    });
  }

  createFamily(name: string): Observable<any> {
    return this.http.post(this.apiUrl, { name }, { headers: this.getHeaders() });
  }

  joinFamily(inviteCode: string, displayName: string): Observable<any> {
    return this.http.post(`${this.apiUrl}/join`, { inviteCode, displayName }, { headers: this.getHeaders() });
  }

  getMembers(): Observable<any[]> {
    return this.http.get<any[]>(`${this.apiUrl}/members`, { headers: this.getHeaders() });
  }

  toggleDependent(memberId: string): Observable<any> {
    return this.http.patch(`${this.apiUrl}/members/${memberId}/toggle`, {}, { headers: this.getHeaders() });
  }

  removeMember(memberId: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/members/${memberId}`, { headers: this.getHeaders() });
  }

  getFamilySummary(): Observable<any[]> {
    return this.http.get<any[]>(`${this.apiUrl}/summary`, { headers: this.getHeaders() });
  }
}
