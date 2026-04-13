import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { AuthService } from '../../auth/auth.service';
import { SavingsService } from '../savings.service';
import { EmiService } from '../emi.service';
import { firstValueFrom } from 'rxjs';
import { TopNavComponent } from '../../ui/top-nav/top-nav.component';

@Component({
  selector: 'app-finance-view',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, TopNavComponent],
  templateUrl: './finance-view.component.html'
})
export class FinanceViewComponent implements OnInit {
  savings: any[] = [];
  emis: any[] = [];
  isLoading = true;

  showSavingsForm = false;
  newSavings = { name: '', targetAmount: null, currentAmount: null, deadline: '' };

  showEmiForm = false;
  newEmi = { lenderName: '', principal: null, monthlyEmi: null, startDate: '', tenureMonths: null };

  constructor(
    public authService: AuthService,
    private savingsService: SavingsService,
    private emiService: EmiService
  ) { }

  ngOnInit() {
    this.loadData();
  }

  async loadData() {
    this.isLoading = true;
    try {
      const [savingsRes, emisRes] = await Promise.all([
        firstValueFrom(this.savingsService.getSavings()),
        firstValueFrom(this.emiService.getEmis())
      ]);
      this.savings = savingsRes || [];
      this.emis = emisRes || [];
    } catch (err) {
      console.error(err);
    } finally {
      this.isLoading = false;
    }
  }

  saveSavings() {
    if (!this.newSavings.name || !this.newSavings.targetAmount) return;
    this.savingsService.createSavings(this.newSavings).subscribe(() => {
      this.showSavingsForm = false;
      this.newSavings = { name: '', targetAmount: null, currentAmount: null, deadline: '' };
      this.loadData();
    });
  }

  deleteSavings(id: string) {
    if (confirm("Delete this savings goal?")) {
      this.savingsService.deleteSavings(id).subscribe(() => this.loadData());
    }
  }

  saveEmi() {
    if (!this.newEmi.lenderName || !this.newEmi.monthlyEmi) return;
    this.emiService.createEmi(this.newEmi).subscribe(() => {
      this.showEmiForm = false;
      this.newEmi = { lenderName: '', principal: null, monthlyEmi: null, startDate: '', tenureMonths: null };
      this.loadData();
    });
  }

  deleteEmi(id: string) {
    if (confirm("Delete this EMI?")) {
      this.emiService.deleteEmi(id).subscribe(() => this.loadData());
    }
  }
}
