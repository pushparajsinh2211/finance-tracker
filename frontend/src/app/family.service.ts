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



  createFamily(name: string): Observable<any> {
    return this.http.post(this.apiUrl, { name });
  }

  joinFamily(inviteCode: string, displayName: string): Observable<any> {
    return this.http.post(`${this.apiUrl}/join`, { inviteCode, displayName });
  }

  getFamily(): Observable<any> {
    return this.http.get<any>(this.apiUrl);
  }

  getMembers(): Observable<any[]> {
    return this.http.get<any[]>(`${this.apiUrl}/members`);
  }

  toggleDependent(memberId: string): Observable<any> {
    return this.http.patch(`${this.apiUrl}/members/${memberId}/toggle`, {});
  }

  removeMember(memberId: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/members/${memberId}`);
  }

  sendInvite(email: string): Observable<any> {
    return this.http.post(`${this.apiUrl}/invite`, { email });
  }

  getFamilySummary(): Observable<any[]> {
    return this.http.get<any[]>(`${this.apiUrl}/summary`);
  }
}
