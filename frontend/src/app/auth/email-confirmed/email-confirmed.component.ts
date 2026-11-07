import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '../auth.service';

@Component({
  selector: 'app-email-confirmed',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './email-confirmed.component.html',
  styleUrl: './email-confirmed.component.css'
})
export class EmailConfirmedComponent {
  readonly verified: boolean;
  readonly errorMessage: string;

  constructor(
    route: ActivatedRoute,
    authService: AuthService
  ) {
    const fragment = new URLSearchParams(
      window.location.hash.replace(/^#/, ''));
    const queryError = route.snapshot.queryParamMap.get('error_description') ||
      route.snapshot.queryParamMap.get('error');
    const fragmentError = fragment.get('error_description') ||
      fragment.get('error');
    const error = queryError || fragmentError;
    const registrationId =
      route.snapshot.queryParamMap.get('registration') || '';

    this.verified = !!registrationId && !error;
    this.errorMessage = error
      ? error.replace(/\+/g, ' ')
      : registrationId
        ? ''
        : 'This confirmation link is incomplete or has expired.';

    // Supabase can append a short-lived session in the fragment. This page
    // never signs the user in, so remove that sensitive fragment immediately.
    if (window.location.hash) {
      window.history.replaceState(
        null,
        document.title,
        window.location.pathname + window.location.search);
    }

    if (this.verified) {
      authService.markEmailConfirmed(registrationId);
    }
  }
}
