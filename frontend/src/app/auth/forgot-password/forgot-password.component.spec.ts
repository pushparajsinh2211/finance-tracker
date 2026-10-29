import { ComponentFixture, TestBed } from '@angular/core/testing';
import { throwError } from 'rxjs';
import { AuthService } from '../auth.service';
import { ForgotPasswordComponent } from './forgot-password.component';

describe('ForgotPasswordComponent', () => {
  let fixture: ComponentFixture<ForgotPasswordComponent>;
  let component: ForgotPasswordComponent;
  let authService: jasmine.SpyObj<AuthService>;

  beforeEach(async () => {
    localStorage.removeItem('password-reset-cooldown-until');
    authService = jasmine.createSpyObj<AuthService>('AuthService', ['resetPassword']);

    await TestBed.configureTestingModule({
      imports: [ForgotPasswordComponent],
      providers: [{ provide: AuthService, useValue: authService }]
    }).compileComponents();

    fixture = TestBed.createComponent(ForgotPasswordComponent);
    component = fixture.componentInstance;
    component.forgotForm.setValue({ email: 'person@example.test' });
  });

  afterEach(() => {
    fixture.destroy();
    localStorage.removeItem('password-reset-cooldown-until');
  });

  it('shows the rate-limit message and starts a resend cooldown', () => {
    authService.resetPassword.and.returnValue(throwError(() => ({
      status: 429,
      error: {
        code: 'email_rate_limited',
        message: 'The email service limit has been reached. Please wait before requesting another reset link.',
        retryAfterSeconds: 60
      }
    })));

    component.onSubmit();

    expect(component.isError).toBeTrue();
    expect(component.message).toContain('email service limit');
    expect(component.cooldownRemaining).toBeGreaterThan(0);
  });

  it('does not resend while the cooldown is active', () => {
    authService.resetPassword.and.returnValue(throwError(() => ({
      status: 429,
      error: { code: 'email_rate_limited', retryAfterSeconds: 60 }
    })));

    component.onSubmit();
    component.onSubmit();

    expect(authService.resetPassword).toHaveBeenCalledTimes(1);
  });

  it('shows a safe service message for non-rate-limit failures', () => {
    authService.resetPassword.and.returnValue(throwError(() => ({
      status: 502,
      error: {
        code: 'email_service_unavailable',
        message: 'Password reset email delivery is temporarily unavailable.'
      }
    })));

    component.onSubmit();

    expect(component.isError).toBeTrue();
    expect(component.message).toBe('Password reset email delivery is temporarily unavailable.');
    expect(component.cooldownRemaining).toBe(0);
  });
});
