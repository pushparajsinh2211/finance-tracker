import { Component } from '@angular/core';
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
export class ForgotPasswordComponent {
  forgotForm: FormGroup;
  isLoading = false;
  message = '';
  isError = false;

  constructor(
    private fb: FormBuilder,
    private authService: AuthService
  ) {
    this.forgotForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]]
    });
  }

  onSubmit() {
    if (this.forgotForm.invalid) return;

    this.isLoading = true;
    this.message = '';

    this.authService.resetPassword(this.forgotForm.value.email).subscribe({
      next: () => {
        this.isLoading = false;
        this.isError = false;
        this.message = 'If an account exists, a reset link has been sent.';
      },
      error: () => {
        this.isLoading = false;
        this.isError = true;
        this.message = 'An error occurred processing your request.';
      }
    });
  }
}
