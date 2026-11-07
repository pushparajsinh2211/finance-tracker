import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { NEVER, Subject, of } from 'rxjs';
import { AuthService } from '../auth.service';
import { RegisterComponent } from './register.component';

describe('RegisterComponent confirmation flow', () => {
  let fixture: ComponentFixture<RegisterComponent>;
  let component: RegisterComponent;
  let router: Router;
  let confirmationEvents: Subject<string>;
  let authService: jasmine.SpyObj<AuthService>;

  beforeEach(async () => {
    sessionStorage.removeItem('financehub:pending-confirmation');
    confirmationEvents = new Subject<string>();
    authService = jasmine.createSpyObj<AuthService>(
      'AuthService',
      [
        'createConfirmationRequestId',
        'register',
        'resendConfirmation',
        'confirmationStatus',
        'hasEmailConfirmationSignal',
        'clearEmailConfirmationSignal'
      ],
      { emailConfirmed$: confirmationEvents.asObservable() }
    );
    authService.createConfirmationRequestId.and.returnValue('registration-123');
    authService.hasEmailConfirmationSignal.and.returnValue(false);
    authService.confirmationStatus.and.returnValue(NEVER);
    authService.register.and.returnValue(of({
      requiresEmailConfirmation: true,
      confirmationStatusToken: 'signed-status-token',
      message: 'Check your inbox.'
    }));

    await TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authService }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(RegisterComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    fixture.destroy();
    sessionStorage.removeItem('financehub:pending-confirmation');
  });

  it('keeps the registration page waiting and opens login after confirmation', () => {
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    component.registerForm.setValue({
      email: 'person@example.test',
      password: 'safe-password'
    });

    component.onSubmit();

    expect(authService.register).toHaveBeenCalledWith(
      jasmine.objectContaining({ email: 'person@example.test' }),
      'registration-123'
    );
    expect(component.pendingEmail).toBe('person@example.test');
    expect(navigate).not.toHaveBeenCalled();

    confirmationEvents.next('registration-123');

    expect(authService.clearEmailConfirmationSignal)
      .toHaveBeenCalledWith('registration-123');
    expect(navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { confirmed: '1' },
      replaceUrl: true
    });
  });

  it('ignores confirmation events for another registration', () => {
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    component.registerForm.setValue({
      email: 'person@example.test',
      password: 'safe-password'
    });
    component.onSubmit();

    confirmationEvents.next('different-registration');

    expect(navigate).not.toHaveBeenCalled();
  });
});
