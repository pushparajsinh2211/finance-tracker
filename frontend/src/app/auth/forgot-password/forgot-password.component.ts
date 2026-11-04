import { Component, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { AuthService } from '../auth.service';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  templateUrl: './forgot-password.component.html'
})
export class ForgotPasswordComponent implements OnDestroy {
  private readonly cooldownStorageKey = 'password-reset-cooldown-until';
  private cooldownTimer?: ReturnType<typeof setInterval>;

  forgotForm: FormGroup;
  isLoading = false;
  message = '';
  isError = false;
  cooldownRemaining = 0;

  constructor(
    private fb: FormBuilder,
    private authService: AuthService
  ) {
    this.forgotForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]]
    });
    this.restoreCooldown();
  }

  ngOnDestroy(): void {
    if (this.cooldownTimer) clearInterval(this.cooldownTimer);
  }

  onSubmit(): void {
    if (this.forgotForm.invalid || this.isLoading || this.cooldownRemaining > 0) return;

    this.isLoading = true;
    this.message = '';

    this.authService.resetPassword(this.forgotForm.value.email).subscribe({
      next: () => {
        this.isLoading = false;
        this.isError = false;
        this.message = 'If an account exists, a reset link has been sent.';
        this.startCooldown(60);
      },
      error: (err) => {
        this.isLoading = false;
        const body = err?.error;
        const code = body?.code || body?.Code;

        if (err?.status === 429 || code === 'email_rate_limited') {
          this.isError = true;
          this.message = body?.message || body?.Message ||
            'The email service limit has been reached. Please wait before requesting another reset link.';
          this.startCooldown(Number(body?.retryAfterSeconds || body?.RetryAfterSeconds || 60));
          return;
        }

        this.isError = true;
        this.message = body?.message || body?.Message ||
          'Password reset email delivery is temporarily unavailable. Please try again later.';
      }
    });
  }

  private restoreCooldown(): void {
    const storedUntil = Number(localStorage.getItem(this.cooldownStorageKey) || 0);
    if (storedUntil > Date.now()) {
      this.activateCooldown(storedUntil);
    } else {
      localStorage.removeItem(this.cooldownStorageKey);
    }
  }

  private startCooldown(seconds: number): void {
    const safeSeconds = Number.isFinite(seconds) ? Math.min(Math.max(seconds, 1), 3600) : 60;
    const until = Date.now() + safeSeconds * 1000;
    localStorage.setItem(this.cooldownStorageKey, String(until));
    this.activateCooldown(until);
  }

  private activateCooldown(until: number): void {
    if (this.cooldownTimer) clearInterval(this.cooldownTimer);

    const update = () => {
      this.cooldownRemaining = Math.max(0, Math.ceil((until - Date.now()) / 1000));
      if (this.cooldownRemaining === 0) {
        localStorage.removeItem(this.cooldownStorageKey);
        if (this.cooldownTimer) clearInterval(this.cooldownTimer);
        this.cooldownTimer = undefined;
      }
    };

    update();
    if (this.cooldownRemaining > 0) {
      this.cooldownTimer = setInterval(update, 1000);
    }
  }
}
