import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from '../auth/auth.service';

@Injectable({
    providedIn: 'root'
})
export class CategoryService {
    private apiUrl = environment.apiUrl + '/api/categories';

    constructor(private http: HttpClient, private authService: AuthService) { }



    getCategories(): Observable<any[]> {
        return this.http.get<any[]>(this.apiUrl);
    }
}
