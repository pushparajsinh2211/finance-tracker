import { Component, OnInit, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
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

  constructor(
    private fb: FormBuilder,
    private expenseService: ExpenseService,
    private categoryService: CategoryService,
    private storageService: StorageService
  ) {
    this.expenseForm = this.fb.group({
      categoryId: ['', Validators.required],
      amount: ['', [Validators.required, Validators.min(0.01)]],
      date: [new Date().toISOString().substring(0, 10), Validators.required],
      note: [''],
      isRecurring: [false],
      receiptUrl: ['']
    });
  }

  ngOnInit() {
    this.categoryService.getCategories().subscribe(cats => {
      this.categories = cats.filter(c => !c.isArchived);
    });

    if (this.initialExpense) {
      this.expenseForm.patchValue({
        categoryId: this.initialExpense.categoryId,
        amount: this.initialExpense.amount,
        date: this.initialExpense.date ? this.initialExpense.date.substring(0, 10) : new Date().toISOString().substring(0, 10),
        note: this.initialExpense.note,
        isRecurring: this.initialExpense.isRecurring,
        receiptUrl: this.initialExpense.receiptUrl
      });
    }
  }

  onFileSelected(event: any) {
    const file = event.target.files[0];
    if (file) {
      this.selectedFile = file;
    }
  }

  async onSubmit() {
    if (this.expenseForm.invalid) return;

    this.isLoading = true;
    this.errorMsg = '';

    try {
      if (this.selectedFile) {
        this.uploadProgress = true;
        const url = await this.storageService.uploadReceipt(this.selectedFile);
        this.expenseForm.patchValue({ receiptUrl: url });
        this.uploadProgress = false;
      }

      if (this.initialExpense && this.initialExpense.id) {
        this.expenseService.updateExpense(this.initialExpense.id, this.expenseForm.value).subscribe({
          next: res => { this.isLoading = false; this.formSaved.emit(res); },
          error: err => { this.isLoading = false; this.errorMsg = err.error?.message || 'Failed to update'; }
        });
      } else {
        this.expenseService.addExpense(this.expenseForm.value).subscribe({
          next: res => { this.isLoading = false; this.formSaved.emit(res); },
          error: err => { this.isLoading = false; this.errorMsg = err.error?.message || 'Failed to add'; }
        });
      }
    } catch (err: any) {
      this.isLoading = false;
      this.uploadProgress = false;
      this.errorMsg = err.message || 'Upload failed.';
    }
  }

  onDelete() {
    if (!this.initialExpense || !this.initialExpense.id) return;

    if (confirm("Are you sure you want to delete this expense?")) {
      this.isLoading = true;
      this.expenseService.deleteExpense(this.initialExpense.id).subscribe({
        next: () => {
          this.isLoading = false;
          this.formSaved.emit({ deleted: true, id: this.initialExpense.id });
        },
        error: (err) => {
          this.isLoading = false;
          this.errorMsg = err.error?.message || "Failed to delete.";
        }
      });
    }
  }

  onCancel() {
    this.formCancelled.emit();
  }
}
