import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { AccountDeletionStatus, PrivacyService } from './privacy.service';

@Component({
  selector: 'app-privacy',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './privacy.component.html',
  styleUrls: ['./privacy.component.css']
})
export class PrivacyComponent implements OnInit {
  readonly confirmationPhrase = 'DELETE MY ACCOUNT';
  status: AccountDeletionStatus | null = null;
  confirmation = '';
  exporting = false;
  deleting = false;
  loadingStatus = true;
  exportMessage = '';
  deleteMessage = '';

  constructor(
    private readonly privacyService: PrivacyService,
    private readonly authService: AuthService,
    private readonly router: Router
  ) {}

  ngOnInit(): void {
    this.privacyService.getDeletionStatus()
      .pipe(finalize(() => this.loadingStatus = false))
      .subscribe({
        next: status => this.status = status,
        error: () => this.deleteMessage = 'Could not check account deletion status. Please try again.'
      });
  }

  downloadExport(): void {
    this.exporting = true;
    this.exportMessage = '';

    this.privacyService.exportMyData()
      .pipe(finalize(() => this.exporting = false))
      .subscribe({
        next: response => {
          const blob = response.body;
          if (!blob) {
            this.exportMessage = 'The export was empty. Please try again.';
            return;
          }

          const url = URL.createObjectURL(blob);
          const anchor = document.createElement('a');
          anchor.href = url;
          anchor.download = 'financehub-export-' + new Date().toISOString().slice(0, 10) + '.json';
          anchor.click();
          URL.revokeObjectURL(url);
          this.exportMessage = 'Your data export has been downloaded.';
        },
        error: () => this.exportMessage = 'Could not prepare your data export. Please try again.'
      });
  }

  deleteAccount(): void {
    if (this.confirmation.trim() !== this.confirmationPhrase || !this.status?.canDelete) {
      return;
    }

    this.deleting = true;
    this.deleteMessage = '';

    this.privacyService.deleteAccount(this.confirmation)
      .pipe(finalize(() => this.deleting = false))
      .subscribe({
        next: () => {
          this.authService.logout();
          this.router.navigate(['/login'], { queryParams: { accountDeleted: '1' } });
        },
        error: error => {
          this.deleteMessage = error?.error?.message
            ?? 'Account deletion failed. Your session remains active; please try again.';
        }
      });
  }
}
