import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private apiUrl = environment.apiUrl + '/api/auth';
  private tokenSubject = new BehaviorSubject<string | null>(this.getToken());

  public token$ = this.tokenSubject.asObservable();

  constructor(private http: HttpClient) { }

  private getToken(): string | null {
    return localStorage.getItem('token');
  }

  isLoggedIn(): boolean {
    return !!this.getToken();
  }

  getAuthToken(): string | null {
    return this.getToken();
  }

  login(credentials: any): Observable<any> {
    return this.http.post(`${this.apiUrl}/login`, credentials).pipe(
      tap((res: any) => {
        if (res && res.accessToken) {
          localStorage.setItem('token', res.accessToken);
          this.tokenSubject.next(res.accessToken);
        }
      })
    );
  }

  register(credentials: any): Observable<any> {
    return this.http.post(`${this.apiUrl}/register`, credentials).pipe(
      tap((res: any) => {
        if (res && res.accessToken) {
          localStorage.setItem('token', res.accessToken);
          this.tokenSubject.next(res.accessToken);
        }
      })
    );
  }

  resetPassword(email: string) {
    return this.http.post(`${this.apiUrl}/reset-password`, { email });
  }

  logout() {
    localStorage.removeItem('token');
    this.tokenSubject.next(null);
  }
}
