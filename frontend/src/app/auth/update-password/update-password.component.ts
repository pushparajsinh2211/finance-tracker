import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { createClient, SupabaseClient } from '@supabase/supabase-js';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-update-password',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  templateUrl: './update-password.component.html'
})
export class UpdatePasswordComponent implements OnInit {
  passwordForm: FormGroup;
  isPreparing = true;
  isLoading = false;
  recoveryReady = false;
  errorMessage = '';

  private readonly supabase: SupabaseClient = createClient(
    environment.supabaseUrl,
    environment.supabaseKey,
    {
      auth: {
        persistSession: true,
        storage: window.sessionStorage,
        storageKey: 'financehub-password-recovery',
        autoRefreshToken: false,
        detectSessionInUrl: false
      }
    }
  );

  constructor(
    private fb: FormBuilder,
    private router: Router
  ) {
    this.passwordForm = this.fb.group({
      password: ['', [Validators.required, Validators.minLength(6)]],
      confirmPassword: ['', Validators.required]
    });
  }

  async ngOnInit(): Promise<void> {
    try {
      const currentUrl = new URL(window.location.href);
      const fragment = new URLSearchParams(currentUrl.hash.substring(1));
      const callbackError = fragment.get('error_description') || currentUrl.searchParams.get('error_description');

      if (callbackError) {
        throw new Error(callbackError);
      }

      const code = currentUrl.searchParams.get('code');
      const accessToken = fragment.get('access_token');
      const refreshToken = fragment.get('refresh_token');

      if (code) {
        const { error } = await this.supabase.auth.exchangeCodeForSession(code);
        if (error) throw error;
        this.clearRecoveryTokens();
      } else if (accessToken && refreshToken) {
        const { error } = await this.supabase.auth.setSession({
          access_token: accessToken,
          refresh_token: refreshToken
        });
        if (error) throw error;
        this.clearRecoveryTokens();
      } else {
        const { data, error } = await this.supabase.auth.getSession();
        if (error) throw error;
        if (!data.session) {
          throw new Error('This password reset link is invalid or has expired.');
        }
      }

      this.recoveryReady = true;
    } catch (error: any) {
      this.errorMessage = error?.message || 'This password reset link is invalid or has expired.';
    } finally {
      this.isPreparing = false;
    }
  }

  async onSubmit(): Promise<void> {
    if (this.passwordForm.invalid || !this.recoveryReady || this.isLoading) return;

    const password = this.passwordForm.value.password;
    const confirmPassword = this.passwordForm.value.confirmPassword;

    if (password !== confirmPassword) {
      this.errorMessage = 'Passwords do not match.';
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';

    try {
      const { error } = await this.supabase.auth.updateUser({ password });
      if (error) throw error;

      await this.supabase.auth.signOut();
      window.sessionStorage.removeItem('financehub-password-recovery');
      await this.router.navigate(['/login'], { queryParams: { passwordReset: '1' } });
    } catch (error: any) {
      this.errorMessage = error?.message || 'Password could not be updated. Request a new reset link and try again.';
    } finally {
      this.isLoading = false;
    }
  }

  private clearRecoveryTokens(): void {
    window.history.replaceState({}, document.title, window.location.pathname);
  }
}
