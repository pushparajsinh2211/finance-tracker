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
  family: any = null;
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
      const [membersRes, catsRes, summaryRes, familyRes] = await Promise.all([
        firstValueFrom(this.familyService.getMembers()),
        firstValueFrom(this.categoryService.getCategories()),
        firstValueFrom(this.familyService.getFamilySummary()),
        firstValueFrom(this.familyService.getFamily())
      ]);

      this.members = membersRes || [];
      this.categories = catsRes || [];
      this.summary = summaryRes || [];
      this.family = familyRes;
    } catch (err: any) {
      console.error(err);
      this.errorMsg = err.error?.message || "Failed to load family details.";
    } finally {
      this.isLoading = false;
    }
  }

  toggleDependent(memberId: string) {
    this.familyService.toggleDependent(memberId).subscribe({
      next: () => this.loadData(),
      error: (err) => alert(err.error?.message || "Failed to update member.")
    });
  }

  removeMember(memberId: string) {
    if (!confirm("Are you sure you want to remove this member?")) return;
    this.familyService.removeMember(memberId).subscribe({
      next: () => this.loadData(),
      error: (err) => alert(err.error?.message || "Failed to remove member.")
    });
  }

  get isHead(): boolean {
    const userId = this.authService.getAuthToken() ? JSON.parse(atob(this.authService.getAuthToken()!.split('.')[1])).sub : '';
    return this.family?.headUserId === userId;
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
