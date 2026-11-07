import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, Subject, tap } from 'rxjs';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private apiUrl = environment.apiUrl + '/api/auth';
  private tokenSubject = new BehaviorSubject<string | null>(this.getToken());
  private confirmationSubject = new Subject<string>();
  private confirmationChannel?: BroadcastChannel;
  private readonly confirmationKeyPrefix = 'financehub:email-confirmed:';

  public token$ = this.tokenSubject.asObservable();
  public emailConfirmed$ = this.confirmationSubject.asObservable();

  constructor(private http: HttpClient) {
    if (typeof window === 'undefined') return;

    window.addEventListener('storage', event => {
      if (event.key?.startsWith(this.confirmationKeyPrefix) && event.newValue) {
        this.confirmationSubject.next(
          event.key.slice(this.confirmationKeyPrefix.length));
      }
    });

    if (typeof BroadcastChannel !== 'undefined') {
      this.confirmationChannel =
        new BroadcastChannel('financehub-email-confirmation');
      this.confirmationChannel.onmessage = event => {
        if (event.data?.type === 'email-confirmed' &&
            typeof event.data?.registrationId === 'string') {
          this.confirmationSubject.next(event.data.registrationId);
        }
      };
    }
  }

  private getToken(): string | null {
    const t = localStorage.getItem('token');
    return (t === 'undefined' || t === 'null') ? null : t;
  }

  private confirmationRedirect(registrationId: string): string {
    const query = new URLSearchParams({ registration: registrationId });
    return window.location.origin + '/email-confirmed?' + query.toString();
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

  createConfirmationRequestId(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }

    return Array.from(crypto.getRandomValues(new Uint8Array(16)))
      .map(value => value.toString(16).padStart(2, '0'))
      .join('');
  }

  register(credentials: any, registrationId: string): Observable<any> {
    return this.http.post(`${this.apiUrl}/register`, {
      ...credentials,
      redirectTo: this.confirmationRedirect(registrationId)
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

  resendConfirmation(
    email: string,
    registrationId: string
  ): Observable<any> {
    return this.http.post(`${this.apiUrl}/resend-confirmation`, {
      email,
      redirectTo: this.confirmationRedirect(registrationId)
    });
  }

  confirmationStatus(token: string): Observable<{ confirmed: boolean }> {
    return this.http.post<{ confirmed: boolean }>(
      `${this.apiUrl}/confirmation-status`,
      { token });
  }

  markEmailConfirmed(registrationId: string): void {
    if (!registrationId || typeof window === 'undefined') return;

    localStorage.setItem(
      this.confirmationKeyPrefix + registrationId,
      Date.now().toString());
    this.confirmationChannel?.postMessage({
      type: 'email-confirmed',
      registrationId
    });
  }

  hasEmailConfirmationSignal(registrationId: string): boolean {
    return typeof window !== 'undefined' &&
      !!localStorage.getItem(this.confirmationKeyPrefix + registrationId);
  }

  clearEmailConfirmationSignal(registrationId: string): void {
    if (typeof window !== 'undefined') {
      localStorage.removeItem(this.confirmationKeyPrefix + registrationId);
    }
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
