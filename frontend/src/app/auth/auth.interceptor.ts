import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
    const authService = inject(AuthService);
    const token = authService.getAuthToken();

    // If we have a valid token and it's not a login/register request, clone the request and add the header
    if (token && token !== 'null' && token !== 'undefined' && !req.url.includes('/api/auth/')) {
        console.log(`[AuthInterceptor] Attaching token to: ${req.url}`);
        const cloned = req.clone({
            setHeaders: {
                Authorization: `Bearer ${token}`
            }
        });
        return next(cloned);
    }
    
    console.log(`[AuthInterceptor] Skipping token for: ${req.url} (Token found: ${!!token})`);
    return next(req);
};
