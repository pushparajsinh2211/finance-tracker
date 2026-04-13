import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { FamilyService } from '../../family.service';
import { CategoryService } from '../../categories/category.service';
import { AuthService } from '../../auth/auth.service';
import { firstValueFrom } from 'rxjs';
import { TopNavComponent } from '../../ui/top-nav/top-nav.component';

@Component({
  selector: 'app-family-view',
  standalone: true,
  imports: [CommonModule, RouterModule, TopNavComponent],
  templateUrl: './family-view.component.html'
})
export class FamilyViewComponent implements OnInit {
  summary: any[] = [];
  categories: any[] = [];
  members: any[] = [];
  isLoading = true;
  errorMsg = '';

  constructor(
    private familyService: FamilyService,
    private categoryService: CategoryService,
    public authService: AuthService
  ) { }

  ngOnInit() {
    this.loadData();
  }

  async loadData() {
    this.isLoading = true;
    try {
      const [membersRes, catsRes, summaryRes] = await Promise.all([
        firstValueFrom(this.familyService.getMembers()),
        firstValueFrom(this.categoryService.getCategories()),
        firstValueFrom(this.familyService.getFamilySummary())
      ]);

      this.members = membersRes || [];
      this.categories = catsRes || [];
      this.summary = summaryRes || [];
    } catch (err: any) {
      console.error(err);
      this.errorMsg = err.error?.message || "Failed to load summary. You must be the Family Head.";
    } finally {
      this.isLoading = false;
    }
  }

  getMemberName(id: string) {
    const m = this.members.find(x => x.userId === id);
    return m ? m.displayName : 'Unknown';
  }

  getCategoryColor(id: string) {
    const c = this.categories.find(x => x.id === id);
    return c ? c.color : '#9e9e9e';
  }

  getCategoryName(id: string) {
    const c = this.categories.find(x => x.id === id);
    return c ? c.name : 'Unknown';
  }
}
