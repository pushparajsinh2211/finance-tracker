import { CommonModule } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, OnInit, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { environment } from '../../environments/environment';

interface FinanceHubSource {
  id: string;
  name: string;
  description: string;
  availability: 'available' | 'planned' | 'import_available' | 'derived_available';
  automatic: boolean;
  capabilities: string[];
}

interface StatementTransaction {
  occurredAt: string;
  amount: number;
  direction: 'debit' | 'credit';
  narration: string;
  externalReference?: string;
  paymentRail?: string;
  transactionType: string;
  needsReview: boolean;
}

interface StatementPreview {
  parser: string;
  parserVersion: string;
  fileType: string;
  fileChecksum: string;
  transactionCount: number;
  reviewCount: number;
  errors: string[];
  transactions: StatementTransaction[];
}

interface ImportResult {
  importId: string;
  accountId?: string;
  status: string;
  alreadyImported: boolean;
  receivedCount: number;
  importedCount: number;
  duplicateCount: number;
  reviewCount: number;
}

@Component({
  selector: 'app-connections',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './connections.component.html',
  styleUrls: ['./connections.component.css']
})
export class ConnectionsComponent implements OnInit {
  @ViewChild('importPanel') importPanel?: ElementRef<HTMLElement>;

  sources: FinanceHubSource[] = [];
  loading = true;
  error = '';

  sourceType: 'bank' | 'card' = 'bank';
  accountName = '';
  maskedIdentifier = '';
  password = '';
  selectedFile: File | null = null;
  preview: StatementPreview | null = null;
  importResult: ImportResult | null = null;
  importError = '';
  previewing = false;
  committing = false;

  constructor(private readonly http: HttpClient) {}

  ngOnInit(): void {
    this.http.get<FinanceHubSource[]>(environment.apiUrl + '/api/financehub/sources').subscribe({
      next: sources => {
        this.sources = sources;
        this.loading = false;
      },
      error: () => {
        this.error = 'Data sources could not be loaded. You can still import a statement or enter cash expenses.';
        this.loading = false;
      }
    });
  }

  availabilityLabel(source: FinanceHubSource): string {
    switch (source.availability) {
      case 'available':
        return 'Available now';
      case 'import_available':
        return 'Statement import';
      case 'derived_available':
        return 'From bank imports';
      default:
        return 'Planned integration';
    }
  }

  chooseImport(source: FinanceHubSource): void {
    if (source.id !== 'bank' && source.id !== 'card') {
      return;
    }

    this.sourceType = source.id;
    this.preview = null;
    this.importResult = null;
    this.importError = '';
    setTimeout(() => this.importPanel?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  chooseUpiImport(): void {
    this.sourceType = 'bank';
    this.preview = null;
    this.importResult = null;
    this.importError = '';
    setTimeout(() => this.importPanel?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  fileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.item(0) ?? null;
    this.preview = null;
    this.importResult = null;
    this.importError = '';
  }

  previewStatement(): void {
    if (!this.canSubmit()) {
      this.importError = 'Choose a CSV or PDF statement and enter an account name.';
      return;
    }

    this.previewing = true;
    this.preview = null;
    this.importResult = null;
    this.importError = '';

    this.http.post<StatementPreview>(
      environment.apiUrl + '/api/financehub/imports/preview',
      this.buildFormData()
    ).subscribe({
      next: preview => {
        this.preview = preview;
        this.previewing = false;
      },
      error: error => {
        this.importError = this.readError(error);
        this.previewing = false;
      }
    });
  }

  commitStatement(): void {
    if (!this.preview || !this.canSubmit()) {
      this.importError = 'Preview the current file before importing it.';
      return;
    }

    this.committing = true;
    this.importError = '';

    this.http.post<ImportResult>(
      environment.apiUrl + '/api/financehub/imports/commit',
      this.buildFormData()
    ).subscribe({
      next: result => {
        this.importResult = result;
        this.committing = false;
        this.password = '';
      },
      error: error => {
        this.importError = this.readError(error);
        this.committing = false;
      }
    });
  }

  private canSubmit(): boolean {
    return !!this.selectedFile && !!this.accountName.trim();
  }

  private buildFormData(): FormData {
    const form = new FormData();
    form.append('file', this.selectedFile!);
    form.append('sourceType', this.sourceType);
    form.append('accountName', this.accountName.trim());

    if (this.maskedIdentifier.trim()) {
      form.append('maskedIdentifier', this.maskedIdentifier.trim());
    }
    if (this.password) {
      form.append('password', this.password);
    }

    return form;
  }

  private readError(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      return error.error?.message ?? 'The statement could not be processed.';
    }
    return 'The statement could not be processed.';
  }
}
