import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ExpenseService } from '../expenses/expense.service';
import { CategoryService } from '../categories/category.service';
import { ExpenseFormComponent } from '../expenses/expense-form/expense-form.component';
import { AuthService } from '../auth/auth.service';
import { Router } from '@angular/router';
import { TopNavComponent } from '../ui/top-nav/top-nav.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, ExpenseFormComponent, TopNavComponent],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.css']
})
export class DashboardComponent implements OnInit {
  expenses: any[] = [];
  categories: any[] = [];

  selectedMonth: string = '';
  selectedCategoryId: string = '';

  showExpenseForm = false;
  selectedExpense: any = null;

  isLoading = true;

  constructor(
    private expenseService: ExpenseService,
    private categoryService: CategoryService,
    public authService: AuthService,
    private router: Router
  ) {
    const today = new Date();
    this.selectedMonth = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}`;
  }

  ngOnInit(): void {
    this.loadCategories();
    this.loadExpenses();
  }

  loadCategories() {
    this.categoryService.getCategories().subscribe({
      next: (cats) => this.categories = cats
    });
  }

  loadExpenses() {
    this.isLoading = true;

    let filters: any = {};
    if (this.selectedMonth) {
      const year = parseInt(this.selectedMonth.split('-')[0]);
      const month = parseInt(this.selectedMonth.split('-')[1]);

      const startDate = new Date(year, month - 1, 1);
      const endDate = new Date(year, month, 0);

      filters.startDate = startDate.toISOString().substring(0, 10);
      filters.endDate = endDate.toISOString().substring(0, 10);
    }

    if (this.selectedCategoryId) {
      filters.categoryId = this.selectedCategoryId;
    }

    this.expenseService.getExpenses(filters).subscribe({
      next: (data) => {
        this.expenses = data;
        this.isLoading = false;
      },
      error: (err) => {
        console.error(err);
        this.isLoading = false;
      }
    });
  }

  getCategoryName(id: string): string {
    const cat = this.categories.find(c => c.id === id);
    return cat ? cat.name : 'Unknown';
  }

  getCategoryColor(id: string): string {
    const cat = this.categories.find(c => c.id === id);
    return cat?.color || '#9e9e9e';
  }

  onFilterChange() {
    this.loadExpenses();
  }

  openAddForm() {
    this.selectedExpense = null;
    this.showExpenseForm = true;
  }

  openEditForm(expense: any) {
    this.selectedExpense = expense;
    this.showExpenseForm = true;
  }

  onFormSaved(event: any) {
    this.showExpenseForm = false;
    this.loadExpenses();
  }

  onFormCancelled() {
    this.showExpenseForm = false;
  }

  logout() {
    this.authService.logout();
    this.router.navigate(['/login']);
  }
}
