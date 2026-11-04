import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { AuthService } from '../auth.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  templateUrl: './register.component.html'
})
export class RegisterComponent {
  registerForm: FormGroup;
  isLoading = false;
  isResending = false;
  errorMessage = '';
  successMessage = '';
  pendingEmail = '';

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private router: Router
  ) {
    this.registerForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required, Validators.minLength(6)]]
    });
  }

  onSubmit() {
    if (this.registerForm.invalid) return;

    this.isLoading = true;
    this.errorMessage = '';
    this.successMessage = '';

    this.authService.register(this.registerForm.value).subscribe({
      next: (response) => {
        this.isLoading = false;

        if (response?.requiresEmailConfirmation) {
          this.pendingEmail = this.registerForm.value.email;
          this.successMessage = response?.message || 'Check your inbox and confirm your email before signing in.';
          return;
        }

        this.router.navigate(['/']);
      },
      error: (err) => {
        this.isLoading = false;
        const errorBody = err.error;
        this.errorMessage = errorBody?.message || errorBody?.Message || errorBody?.msg || (typeof errorBody === 'string' ? errorBody : 'Registration failed.');
      }
    });
  }

  resendConfirmation() {
    if (!this.pendingEmail || this.isResending) return;

    this.isResending = true;
    this.errorMessage = '';

    this.authService.resendConfirmation(this.pendingEmail).subscribe({
      next: (response) => {
        this.isResending = false;
        this.successMessage = response?.message || 'A new confirmation email has been sent.';
      },
      error: (err) => {
        this.isResending = false;
        const errorBody = err.error;
        this.errorMessage = errorBody?.message || errorBody?.Message || 'Confirmation email could not be resent.';
      }
    });
  }
}
