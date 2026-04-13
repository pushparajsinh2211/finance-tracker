import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from '../auth/auth.service';

@Injectable({
  providedIn: 'root'
})
export class NotificationService {
  private apiUrl = environment.apiUrl + '/api/notifications';

  constructor(private http: HttpClient, private authService: AuthService) { }

  private getHeaders() {
    return new HttpHeaders({ 'Authorization': `Bearer ${this.authService.getAuthToken()}` });
  }

  getNotifications(): Observable<any[]> {
    return this.http.get<any[]>(this.apiUrl, { headers: this.getHeaders() });
  }

  markAsRead(id: string): Observable<any> {
    return this.http.patch(`${this.apiUrl}/${id}/read`, {}, { headers: this.getHeaders() });
  }

  markAllAsRead(): Observable<any> {
    return this.http.patch(`${this.apiUrl}/read-all`, {}, { headers: this.getHeaders() });
  }
}
