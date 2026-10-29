import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CategoryService } from '../categories/category.service';
import {
  FinanceHubAccountOption,
  FinanceHubTransaction,
  FinanceHubTransactionService,
  ImportRun,
  TransactionFilters
} from './financehub-transaction.service';

interface CategoryOption {
  id: string;
  name: string;
  color: string;
}

@Component({
  selector: 'app-financehub-transactions',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './financehub-transactions.component.html',
  styleUrls: ['./financehub-transactions.component.css']
})
export class FinanceHubTransactionsComponent implements OnInit {
  transactions: FinanceHubTransaction[] = [];
  categories: CategoryOption[] = [];
  accounts: FinanceHubAccountOption[] = [];
  paymentRails: string[] = [];
  imports: ImportRun[] = [];

  filters = {
    search: '',
    accountId: '',
    categoryId: '',
    paymentRail: '',
    direction: '',
    reviewState: '',
    fromDate: '',
    toDate: ''
  };

  page = 1;
  pageSize = 25;
  totalCount = 0;
  totalPages = 0;
  reviewCount = 0;
  loading = true;
  importsLoading = true;
  error = '';
  success = '';

  constructor(
    private readonly financeHub: FinanceHubTransactionService,
    private readonly categoryService: CategoryService
  ) {}

  ngOnInit(): void {
    this.categoryService.getCategories().subscribe({
      next: categories => this.categories = categories,
      error: () => this.error = 'Categories could not be loaded.'
    });
    this.loadTransactions();
    this.loadImports();
  }

  loadTransactions(resetPage = false): void {
    if (resetPage) {
      this.page = 1;
    }

    this.loading = true;
    this.error = '';
    const request: TransactionFilters = {
      page: this.page,
      pageSize: this.pageSize,
      search: this.filters.search.trim() || undefined,
      accountId: this.filters.accountId || undefined,
      categoryId: this.filters.categoryId || undefined,
      paymentRail: this.filters.paymentRail || undefined,
      direction: this.filters.direction || undefined,
      needsReview: this.filters.reviewState === '' ? undefined : this.filters.reviewState === 'true',
      fromDate: this.filters.fromDate || undefined,
      toDate: this.filters.toDate || undefined
    };

    this.financeHub.getTransactions(request).subscribe({
      next: result => {
        this.transactions = result.items.map(item => ({
          ...item,
          draftCategoryId: item.categoryId ?? '',
          draftNeedsReview: item.needsReview
        }));
        this.accounts = result.accounts;
        this.paymentRails = result.paymentRails;
        this.page = result.page;
        this.totalCount = result.totalCount;
        this.totalPages = result.totalPages;
        this.reviewCount = result.reviewCount;
        this.loading = false;
      },
      error: error => {
        this.error = error.error?.message ?? 'Imported transactions could not be loaded.';
        this.loading = false;
      }
    });
  }

  loadImports(): void {
    this.importsLoading = true;
    this.financeHub.getImports().subscribe({
      next: result => {
        this.imports = result.items;
        this.importsLoading = false;
      },
      error: () => {
        this.importsLoading = false;
      }
    });
  }

  saveReview(transaction: FinanceHubTransaction): void {
    transaction.saving = true;
    this.error = '';
    this.success = '';

    this.financeHub.reviewTransaction(
      transaction.id,
      transaction.draftCategoryId || null,
      transaction.draftNeedsReview ?? false
    ).subscribe({
      next: updated => {
        Object.assign(transaction, updated);
        transaction.draftCategoryId = transaction.categoryId ?? '';
        transaction.draftNeedsReview = transaction.needsReview;
        transaction.saving = false;
        this.success = 'Transaction review saved.';
        this.loadTransactions();
      },
      error: error => {
        transaction.saving = false;
        this.error = error.error?.message ?? 'The review could not be saved.';
      }
    });
  }

  resetFilters(): void {
    this.filters = {
      search: '',
      accountId: '',
      categoryId: '',
      paymentRail: '',
      direction: '',
      reviewState: '',
      fromDate: '',
      toDate: ''
    };
    this.loadTransactions(true);
  }

  previousPage(): void {
    if (this.page > 1) {
      this.page--;
      this.loadTransactions();
    }
  }

  nextPage(): void {
    if (this.page < this.totalPages) {
      this.page++;
      this.loadTransactions();
    }
  }

  sourceLabel(value?: string): string {
    return value === 'credit_card' ? 'Credit card' : value === 'bank' ? 'Bank' : value ?? 'Statement';
  }
}
