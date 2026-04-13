import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { BudgetService } from '../budget.service';
import { ExpenseService } from '../../expenses/expense.service';
import { CategoryService } from '../../categories/category.service';
import { RouterModule } from '@angular/router';
import { AuthService } from '../../auth/auth.service';
import { firstValueFrom } from 'rxjs';
import { TopNavComponent } from '../../ui/top-nav/top-nav.component';

@Component({
  selector: 'app-budgets-view',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, TopNavComponent],
  templateUrl: './budgets-view.component.html',
  styleUrls: ['./budgets-view.component.css']
})
export class BudgetsViewComponent implements OnInit {
  selectedMonth: string = '';

  categories: any[] = [];
  expenses: any[] = [];
  budgets: any[] = [];

  budgetItems: any[] = [];

  isLoading = true;

  showBudgetForm = false;
  selectedCategoryId = '';
  newBudgetAmount: number | null = null;
  errorMessage = '';

  constructor(
    private budgetService: BudgetService,
    private expenseService: ExpenseService,
    private categoryService: CategoryService,
    public authService: AuthService
  ) {
    const today = new Date();
    this.selectedMonth = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}`;
  }

  ngOnInit() {
    this.categoryService.getCategories().subscribe(cats => {
      this.categories = cats;
      this.loadData();
    });
  }

  onMonthChange() {
    this.loadData();
  }

  loadData() {
    this.isLoading = true;

    const year = parseInt(this.selectedMonth.split('-')[0]);
    const month = parseInt(this.selectedMonth.split('-')[1]);
    const startDate = new Date(year, month - 1, 1).toISOString().substring(0, 10);
    const endDate = new Date(year, month, 0).toISOString().substring(0, 10);

    Promise.all([
      firstValueFrom(this.budgetService.getBudgets(this.selectedMonth)),
      firstValueFrom(this.expenseService.getExpenses({ startDate, endDate }))
    ]).then(([budgetsRes, expensesRes]: [any, any]) => {
      this.budgets = budgetsRes || [];
      this.expenses = expensesRes || [];
      this.buildBudgetItems();
      this.isLoading = false;
    }).catch(err => {
      console.error(err);
      this.isLoading = false;
    });
  }

  buildBudgetItems() {
    this.budgetItems = this.budgets.map(b => {
      const cat = this.categories.find(c => c.id === b.categoryId) || { name: 'Unknown', color: '#9e9e9e' };
      const spent = this.expenses
        .filter(e => e.categoryId === b.categoryId)
        .reduce((sum, e) => sum + Number(e.amount), 0);

      const progress = b.amount > 0 ? Math.min(100, Math.round((spent / b.amount) * 100)) : 0;

      return {
        ...b,
        categoryName: cat.name,
        color: cat.color,
        spentAmount: spent,
        progress: progress
      };
    }).sort((a, b) => b.progress - a.progress);
  }

  deleteBudget(id: string) {
    if (confirm("Remove this budget?")) {
      this.budgetService.deleteBudget(id).subscribe(() => this.loadData());
    }
  }

  saveBudget() {
    if (!this.selectedCategoryId || !this.newBudgetAmount) {
      this.errorMessage = 'Please select a category and specify an amount.';
      return;
    }
    this.errorMessage = '';

    const payload = {
      categoryId: this.selectedCategoryId,
      month: this.selectedMonth,
      amount: this.newBudgetAmount
    };

    this.budgetService.createBudget(payload).subscribe({
      next: () => {
        this.showBudgetForm = false;
        this.selectedCategoryId = '';
        this.newBudgetAmount = null;
        this.loadData();
      },
      error: (err) => {
        this.errorMessage = err.error?.message || 'Failed to apply budget.';
      }
    });
  }
}
