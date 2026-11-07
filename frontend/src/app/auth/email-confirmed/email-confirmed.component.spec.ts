import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { AuthService } from '../auth.service';
import { EmailConfirmedComponent } from './email-confirmed.component';

describe('EmailConfirmedComponent', () => {
  it('shows a terminal success state and signals the registration tab', async () => {
    const authService = jasmine.createSpyObj<AuthService>(
      'AuthService',
      ['markEmailConfirmed']);
    window.history.replaceState(
      null,
      '',
      '/email-confirmed?registration=registration-123#access_token=temporary');

    await TestBed.configureTestingModule({
      imports: [EmailConfirmedComponent],
      providers: [
        { provide: AuthService, useValue: authService },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: convertToParamMap({
                registration: 'registration-123'
              })
            }
          }
        }
      ]
    }).compileComponents();

    const fixture = TestBed.createComponent(EmailConfirmedComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.verified).toBeTrue();
    expect(authService.markEmailConfirmed)
      .toHaveBeenCalledWith('registration-123');
    expect(window.location.hash).toBe('');
    expect(fixture.nativeElement.textContent).toContain('Email verified');
    expect(fixture.nativeElement.textContent).not.toContain('Log in');
  });

  it('does not signal success when Supabase returns an error', async () => {
    const authService = jasmine.createSpyObj<AuthService>(
      'AuthService',
      ['markEmailConfirmed']);

    await TestBed.configureTestingModule({
      imports: [EmailConfirmedComponent],
      providers: [
        { provide: AuthService, useValue: authService },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: convertToParamMap({
                registration: 'registration-123',
                error: 'access_denied'
              })
            }
          }
        }
      ]
    }).compileComponents();

    const fixture = TestBed.createComponent(EmailConfirmedComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.verified).toBeFalse();
    expect(authService.markEmailConfirmed).not.toHaveBeenCalled();
  });
});
