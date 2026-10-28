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
    const t = localStorage.getItem('token');
    return (t === 'undefined' || t === 'null') ? null : t;
  }

  private confirmationRedirect(): string {
    return window.location.origin + '/login?confirmed=1';
  }

  private passwordRecoveryRedirect(): string {
    return window.location.origin + '/update-password';
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
        const token = res?.accessToken || res?.AccessToken || res?.access_token;
        if (token) {
          localStorage.setItem('token', token);
          this.tokenSubject.next(token);
        }
      })
    );
  }

  register(credentials: any): Observable<any> {
    return this.http.post(`${this.apiUrl}/register`, {
      ...credentials,
      redirectTo: this.confirmationRedirect()
    }).pipe(
      tap((res: any) => {
        const token = res?.accessToken || res?.AccessToken || res?.access_token;
        if (token) {
          localStorage.setItem('token', token);
          this.tokenSubject.next(token);
        }
      })
    );
  }

  resendConfirmation(email: string): Observable<any> {
    return this.http.post(`${this.apiUrl}/resend-confirmation`, {
      email,
      redirectTo: this.confirmationRedirect()
    });
  }

  resetPassword(email: string) {
    return this.http.post(`${this.apiUrl}/reset-password`, {
      email,
      redirectTo: this.passwordRecoveryRedirect()
    });
  }

  logout() {
    localStorage.removeItem('token');
    this.tokenSubject.next(null);
  }
}
