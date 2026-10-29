import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface FinanceHubAccountOption {
  id: string;
  name: string;
  sourceType: string;
  maskedIdentifier?: string;
}

export interface FinanceHubTransaction {
  id: string;
  occurredAt: string;
  postedAt?: string;
  accountId: string;
  accountName: string;
  sourceType: string;
  maskedIdentifier?: string;
  narration: string;
  merchantName?: string;
  amount: number;
  currency: string;
  direction: 'debit' | 'credit';
  transactionType: string;
  status: string;
  paymentRail?: string;
  externalReference?: string;
  categoryId?: string;
  categoryName?: string;
  categoryColor?: string;
  categorySource?: string;
  needsReview: boolean;
  importRunId?: string;
  draftCategoryId?: string;
  draftNeedsReview?: boolean;
  saving?: boolean;
}

export interface TransactionPage {
  items: FinanceHubTransaction[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  reviewCount: number;
  accounts: FinanceHubAccountOption[];
  paymentRails: string[];
}

export interface ImportRun {
  id: string;
  sourceKind: string;
  sourceType?: string;
  accountName?: string;
  status: string;
  receivedCount: number;
  importedCount: number;
  duplicateCount: number;
  reviewCount: number;
  startedAt?: string;
  completedAt?: string;
  createdAt: string;
}

export interface ImportPage {
  items: ImportRun[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface TransactionFilters {
  page: number;
  pageSize: number;
  search?: string;
  accountId?: string;
  categoryId?: string;
  paymentRail?: string;
  direction?: string;
  needsReview?: boolean;
  fromDate?: string;
  toDate?: string;
}

@Injectable({ providedIn: 'root' })
export class FinanceHubTransactionService {
  private readonly apiUrl = environment.apiUrl + '/api/financehub';

  constructor(private readonly http: HttpClient) {}

  getTransactions(filters: TransactionFilters): Observable<TransactionPage> {
    let params = new HttpParams()
      .set('page', filters.page)
      .set('pageSize', filters.pageSize);

    for (const [key, value] of Object.entries(filters)) {
      if (key !== 'page' && key !== 'pageSize' && value !== undefined && value !== '') {
        params = params.set(key, String(value));
      }
    }

    return this.http.get<TransactionPage>(this.apiUrl + '/transactions', { params });
  }

  reviewTransaction(
    id: string,
    categoryId: string | null,
    needsReview: boolean
  ): Observable<Partial<FinanceHubTransaction>> {
    return this.http.patch<Partial<FinanceHubTransaction>>(
      this.apiUrl + '/transactions/' + id + '/review',
      { categoryId, needsReview }
    );
  }

  getImports(page = 1, pageSize = 10): Observable<ImportPage> {
    return this.http.get<ImportPage>(this.apiUrl + '/imports', {
      params: { page, pageSize }
    });
  }
}
