import { Component, OnInit, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ExpenseService } from '../expense.service';
import { CategoryService } from '../../categories/category.service';
import { StorageService } from '../../storage.service';

@Component({
  selector: 'app-expense-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './expense-form.component.html'
})
export class ExpenseFormComponent implements OnInit {
  @Input() initialExpense: any = null;
  @Output() formSaved = new EventEmitter<any>();
  @Output() formCancelled = new EventEmitter<void>();

  expenseForm: FormGroup;
  categories: any[] = [];
  isLoading = false;
  errorMsg = '';
  selectedFile: File | null = null;
  uploadProgress = false;
  receiptLoading = false;
  removeExistingReceipt = false;

  private static localDateValue(date = new Date()): string {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  constructor(
    private fb: FormBuilder,
    private expenseService: ExpenseService,
    private categoryService: CategoryService,
    private storageService: StorageService
  ) {
    this.expenseForm = this.fb.group({
      categoryId: ['', Validators.required],
      amount: ['', [Validators.required, Validators.min(0.01)]],
      date: [ExpenseFormComponent.localDateValue(), Validators.required],
      note: ['', Validators.maxLength(200)],
      isRecurring: [false],
      receiptUrl: ['']
    });
  }

  ngOnInit() {
    this.categoryService.getCategories().subscribe(cats => {
      this.categories = cats.filter(category => !category.isArchived);
    });

    if (this.initialExpense) {
      this.expenseForm.patchValue({
        categoryId: this.initialExpense.categoryId,
        amount: this.initialExpense.amount,
        date: this.initialExpense.date
          ? this.initialExpense.date.substring(0, 10)
          : ExpenseFormComponent.localDateValue(),
        note: this.initialExpense.note,
        isRecurring: this.initialExpense.isRecurring,
        receiptUrl: this.initialExpense.receiptUrl
      });
    }
  }

  onFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.[0] ?? null;
    if (this.selectedFile) {
      this.removeExistingReceipt = false;
    }
  }

  async viewReceipt() {
    const receiptPath = this.existingReceiptPath;
    if (!receiptPath) return;

    this.receiptLoading = true;
    this.errorMsg = '';
    try {
      const signedUrl = await this.storageService.getReceiptUrl(receiptPath);
      window.open(signedUrl, '_blank', 'noopener,noreferrer');
    } catch (error: any) {
      this.errorMsg = error?.message || 'Failed to open receipt.';
    } finally {
      this.receiptLoading = false;
    }
  }

  removeReceipt() {
    this.removeExistingReceipt = true;
    this.selectedFile = null;
  }

  get existingReceiptPath(): string {
    return this.initialExpense?.receiptUrl || '';
  }

  async onSubmit() {
    if (this.expenseForm.invalid) return;

    this.isLoading = true;
    this.errorMsg = '';
    const oldReceiptPath = this.existingReceiptPath;
    let uploadedPath = '';

    try {
      if (this.selectedFile) {
        this.uploadProgress = true;
        uploadedPath = await this.storageService.uploadReceipt(this.selectedFile);
      }

      const payload = {
        ...this.expenseForm.value,
        receiptUrl: uploadedPath || (this.removeExistingReceipt ? null : oldReceiptPath || null),
        removeReceipt: this.removeExistingReceipt && !uploadedPath
      };

      const savedExpense = this.initialExpense?.id
        ? await firstValueFrom(this.expenseService.updateExpense(this.initialExpense.id, payload))
        : await firstValueFrom(this.expenseService.addExpense(payload));

      if (oldReceiptPath && oldReceiptPath !== savedExpense.receiptUrl) {
        await this.cleanupReceipt(oldReceiptPath);
      }

      this.formSaved.emit(savedExpense);
    } catch (error: any) {
      if (uploadedPath) {
        await this.cleanupReceipt(uploadedPath);
      }
      this.errorMsg = error?.error?.message || error?.message || 'Failed to save expense.';
    } finally {
      this.isLoading = false;
      this.uploadProgress = false;
    }
  }

  async onDelete() {
    if (!this.initialExpense?.id) return;
    if (!confirm('Are you sure you want to delete this expense?')) return;

    this.isLoading = true;
    this.errorMsg = '';
    try {
      await firstValueFrom(this.expenseService.deleteExpense(this.initialExpense.id));
      await this.cleanupReceipt(this.existingReceiptPath);
      this.formSaved.emit({ deleted: true, id: this.initialExpense.id });
    } catch (error: any) {
      this.errorMsg = error?.error?.message || error?.message || 'Failed to delete.';
    } finally {
      this.isLoading = false;
    }
  }

  onCancel() {
    this.formCancelled.emit();
  }

  private async cleanupReceipt(receiptPath: string): Promise<void> {
    if (!receiptPath) return;

    try {
      await this.storageService.deleteReceipt(receiptPath);
    } catch (error) {
      console.warn('Expense saved, but receipt cleanup failed.', error);
    }
  }
}
