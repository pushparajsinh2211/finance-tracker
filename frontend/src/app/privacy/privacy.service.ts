import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface AccountDeletionStatus {
  canDelete: boolean;
  isFamilyHead: boolean;
  familyId: string | null;
  familyMemberCount: number;
  blocker: string | null;
}

@Injectable({ providedIn: 'root' })
export class PrivacyService {
  private readonly apiUrl = environment.apiUrl + '/api/privacy';

  constructor(private readonly http: HttpClient) {}

  exportMyData(): Observable<HttpResponse<Blob>> {
    return this.http.get(this.apiUrl + '/export', {
      observe: 'response',
      responseType: 'blob'
    });
  }

  getDeletionStatus(): Observable<AccountDeletionStatus> {
    return this.http.get<AccountDeletionStatus>(this.apiUrl + '/deletion-status');
  }

  deleteAccount(confirmation: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(this.apiUrl + '/account', {
      body: { confirmation }
    });
  }
}
