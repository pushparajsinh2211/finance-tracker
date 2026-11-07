import { Component, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { Subscription, catchError, of, switchMap, takeUntil, timer } from 'rxjs';
import { AuthService } from '../auth.service';

interface PendingConfirmation {
  email: string;
  registrationId: string;
  statusToken?: string;
  expiresAt: number;
}

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  templateUrl: './register.component.html'
})
export class RegisterComponent implements OnDestroy {
  private readonly pendingStorageKey = 'financehub:pending-confirmation';
  private confirmationWatch = new Subscription();

  registerForm: FormGroup;
  isLoading = false;
  isResending = false;
  errorMessage = '';
  successMessage = '';
  pendingEmail = '';
  pendingRegistrationId = '';
  pendingStatusToken = '';

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private router: Router
  ) {
    this.registerForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required, Validators.minLength(6)]]
    });

    this.restorePendingConfirmation();
  }

  ngOnDestroy(): void {
    this.confirmationWatch.unsubscribe();
  }

  onSubmit(): void {
    if (this.registerForm.invalid) return;

    this.isLoading = true;
    this.errorMessage = '';
    this.successMessage = '';
    const registrationId = this.authService.createConfirmationRequestId();

    this.authService.register(
      this.registerForm.value,
      registrationId
    ).subscribe({
      next: response => {
        this.isLoading = false;

        if (response?.requiresEmailConfirmation) {
          this.pendingEmail = this.registerForm.value.email;
          this.pendingRegistrationId = registrationId;
          this.pendingStatusToken =
            response?.confirmationStatusToken ||
            response?.ConfirmationStatusToken ||
            '';
          this.successMessage = response?.message ||
            'Check your inbox and confirm your email before signing in.';
          this.savePendingConfirmation();
          this.startConfirmationWatch();
          return;
        }

        this.clearPendingConfirmation();
        this.router.navigate(['/']);
      },
      error: err => {
        this.isLoading = false;
        const errorBody = err.error;
        this.errorMessage = errorBody?.message ||
          errorBody?.Message ||
          errorBody?.msg ||
          (typeof errorBody === 'string'
            ? errorBody
            : 'Registration failed.');
      }
    });
  }

  resendConfirmation(): void {
    if (!this.pendingEmail ||
        !this.pendingRegistrationId ||
        this.isResending) {
      return;
    }

    this.isResending = true;
    this.errorMessage = '';

    this.authService.resendConfirmation(
      this.pendingEmail,
      this.pendingRegistrationId
    ).subscribe({
      next: response => {
        this.isResending = false;
        this.successMessage = response?.message ||
          'A new confirmation email has been sent.';
      },
      error: err => {
        this.isResending = false;
        const errorBody = err.error;
        this.errorMessage = errorBody?.message ||
          errorBody?.Message ||
          'Confirmation email could not be resent.';
      }
    });
  }

  private startConfirmationWatch(): void {
    this.confirmationWatch.unsubscribe();
    this.confirmationWatch = new Subscription();

    if (!this.pendingRegistrationId) return;

    if (this.authService.hasEmailConfirmationSignal(
      this.pendingRegistrationId
    )) {
      this.finishEmailConfirmation();
      return;
    }

    this.confirmationWatch.add(
      this.authService.emailConfirmed$.subscribe(registrationId => {
        if (registrationId === this.pendingRegistrationId) {
          this.finishEmailConfirmation();
        }
      })
    );

    if (this.pendingStatusToken) {
      this.confirmationWatch.add(
        timer(0, 3000).pipe(
          takeUntil(timer(30 * 60 * 1000)),
          switchMap(() =>
            this.authService
              .confirmationStatus(this.pendingStatusToken)
              .pipe(catchError(() => of({ confirmed: false })))
          )
        ).subscribe(status => {
          if (status.confirmed) {
            this.finishEmailConfirmation();
          }
        })
      );
    }
  }

  private finishEmailConfirmation(): void {
    const registrationId = this.pendingRegistrationId;
    this.confirmationWatch.unsubscribe();
    this.clearPendingConfirmation();
    this.authService.clearEmailConfirmationSignal(registrationId);
    this.router.navigate(['/login'], {
      queryParams: { confirmed: '1' },
      replaceUrl: true
    });
  }

  private savePendingConfirmation(): void {
    const pending: PendingConfirmation = {
      email: this.pendingEmail,
      registrationId: this.pendingRegistrationId,
      statusToken: this.pendingStatusToken || undefined,
      expiresAt: Date.now() + 30 * 60 * 1000
    };
    sessionStorage.setItem(
      this.pendingStorageKey,
      JSON.stringify(pending));
  }

  private restorePendingConfirmation(): void {
    try {
      const raw = sessionStorage.getItem(this.pendingStorageKey);
      if (!raw) return;

      const pending = JSON.parse(raw) as PendingConfirmation;
      if (!pending.email ||
          !pending.registrationId ||
          pending.expiresAt <= Date.now()) {
        this.clearPendingConfirmation();
        return;
      }

      this.pendingEmail = pending.email;
      this.pendingRegistrationId = pending.registrationId;
      this.pendingStatusToken = pending.statusToken || '';
      this.successMessage =
        'Check your inbox and confirm your email before signing in.';
      this.startConfirmationWatch();
    } catch {
      this.clearPendingConfirmation();
    }
  }

  private clearPendingConfirmation(): void {
    sessionStorage.removeItem(this.pendingStorageKey);
    this.pendingEmail = '';
    this.pendingRegistrationId = '';
    this.pendingStatusToken = '';
  }
}
