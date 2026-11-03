import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ExpenseService } from '../expenses/expense.service';
import { CategoryService } from '../categories/category.service';
import { ExpenseFormComponent } from '../expenses/expense-form/expense-form.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, ExpenseFormComponent],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.css']
})
export class DashboardComponent implements OnInit {
  expenses: any[] = [];
  categories: any[] = [];

  selectedMonth = '';
  selectedCategoryId = '';

  showExpenseForm = false;
  selectedExpense: any = null;
  isLoading = true;

  constructor(
    private expenseService: ExpenseService,
    private categoryService: CategoryService
  ) {
    const today = new Date();
    this.selectedMonth = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}`;
  }

  ngOnInit(): void {
    this.loadCategories();
    this.loadExpenses();
  }

  get totalSpent(): number {
    return this.expenses.reduce((total, expense) => total + Number(expense.amount || 0), 0);
  }

  get averageExpense(): number {
    return this.expenses.length ? this.totalSpent / this.expenses.length : 0;
  }

  get periodLabel(): string {
    if (!this.selectedMonth) return 'All time';
    const [year, month] = this.selectedMonth.split('-').map(Number);
    return new Date(year, month - 1, 1).toLocaleDateString('en-IN', {
      month: 'long',
      year: 'numeric'
    });
  }

  get topCategory(): string {
    if (!this.expenses.length) return 'No data yet';

    const totals = new Map<string, number>();
    for (const expense of this.expenses) {
      const categoryId = expense.categoryId || '';
      totals.set(categoryId, (totals.get(categoryId) || 0) + Number(expense.amount || 0));
    }

    let topCategoryId = '';
    let topTotal = -1;
    totals.forEach((total, categoryId) => {
      if (total > topTotal) {
        topTotal = total;
        topCategoryId = categoryId;
      }
    });

    return this.getCategoryName(topCategoryId);
  }

  loadCategories(): void {
    this.categoryService.getCategories().subscribe({
      next: categories => this.categories = categories
    });
  }

  loadExpenses(): void {
    this.isLoading = true;

    const filters: any = {};
    if (this.selectedMonth) {
      const [year, month] = this.selectedMonth.split('-').map(Number);
      filters.startDate = `${year}-${String(month).padStart(2, '0')}-01`;
      const lastDay = new Date(year, month, 0).getDate();
      filters.endDate = `${year}-${String(month).padStart(2, '0')}-${String(lastDay).padStart(2, '0')}`;
    }

    if (this.selectedCategoryId) {
      filters.categoryId = this.selectedCategoryId;
    }

    this.expenseService.getExpenses(filters).subscribe({
      next: data => {
        this.expenses = data;
        this.isLoading = false;
      },
      error: error => {
        console.error(error);
        this.isLoading = false;
      }
    });
  }

  getCategoryName(id: string): string {
    const category = this.categories.find(item => item.id === id);
    return category ? category.name : 'Uncategorised';
  }

  getCategoryColor(id: string): string {
    const category = this.categories.find(item => item.id === id);
    return category?.color || '#64748b';
  }

  onFilterChange(): void {
    this.loadExpenses();
  }

  openAddForm(): void {
    this.selectedExpense = null;
    this.showExpenseForm = true;
  }

  openEditForm(expense: any): void {
    this.selectedExpense = expense;
    this.showExpenseForm = true;
  }

  onFormSaved(_event: any): void {
    this.showExpenseForm = false;
    this.loadExpenses();
  }

  onFormCancelled(): void {
    this.showExpenseForm = false;
  }
}
