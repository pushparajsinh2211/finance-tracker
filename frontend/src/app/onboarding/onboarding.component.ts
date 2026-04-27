import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { FamilyService } from '../family.service';
import { AuthService } from '../auth/auth.service';

@Component({
  selector: 'app-onboarding',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './onboarding.component.html',
  styleUrls: ['./onboarding.component.css']
})
export class OnboardingComponent implements OnInit {
  createForm: FormGroup;
  joinForm: FormGroup;

  isLoading = false;
  errorMsg = '';

  activeTab: 'create' | 'join' = 'create';

  constructor(
    private fb: FormBuilder,
    private familyService: FamilyService,
    public authService: AuthService,
    private router: Router
  ) {
    this.createForm = this.fb.group({
      name: ['', [Validators.required, Validators.minLength(2)]]
    });

    this.joinForm = this.fb.group({
      inviteCode: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(8)]],
      displayName: ['', Validators.required]
    });
  }

  ngOnInit(): void {
    // Check if they already have a family
    this.familyService.getMembers().subscribe({
      next: (members) => {
        if (members && members.length > 0) {
          this.router.navigate(['/dashboard']);
        }
      },
      error: (err) => { 
        if (err.status === 401) {
          this.router.navigate(['/login']);
        }
      }
    });
  }

  setTab(tab: 'create' | 'join') {
    this.activeTab = tab;
    this.errorMsg = '';
  }

  onCreate() {
    if (this.createForm.invalid) return;
    this.isLoading = true;
    this.errorMsg = '';

    this.familyService.createFamily(this.createForm.value.name).subscribe({
      next: () => {
        this.isLoading = false;
        this.router.navigate(['/dashboard']);
      },
      error: (err) => {
        this.isLoading = false;
        const errorBody = err.error;
        this.errorMsg = errorBody?.message || errorBody?.Message || errorBody?.msg || (typeof errorBody === 'string' ? errorBody : 'Failed to create family.');
      }
    });
  }

  onJoin() {
    if (this.joinForm.invalid) return;
    this.isLoading = true;
    this.errorMsg = '';

    const { inviteCode, displayName } = this.joinForm.value;

    this.familyService.joinFamily(inviteCode, displayName).subscribe({
      next: () => {
        this.isLoading = false;
        this.router.navigate(['/dashboard']);
      },
      error: (err) => {
        this.isLoading = false;
        const errorBody = err.error;
        this.errorMsg = errorBody?.message || errorBody?.Message || errorBody?.msg || (typeof errorBody === 'string' ? errorBody : 'Failed to join family.');
      }
    });
  }

  get userEmail(): string {
    const token = this.authService.getAuthToken();
    if (!token) return 'User';
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      return payload.email || 'User';
    } catch {
      return 'User';
    }
  }

  logout() {
    this.authService.logout();
    this.router.navigate(['/login']);
  }
}
