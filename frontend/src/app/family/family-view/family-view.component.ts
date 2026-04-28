import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { FamilyService } from '../../family.service';
import { CategoryService } from '../../categories/category.service';
import { AuthService } from '../../auth/auth.service';
import { firstValueFrom } from 'rxjs';
import { TopNavComponent } from '../../ui/top-nav/top-nav.component';

import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-family-view',
  standalone: true,
  imports: [CommonModule, RouterModule, TopNavComponent, FormsModule],
  templateUrl: './family-view.component.html'
})
export class FamilyViewComponent implements OnInit {
  summary: any[] = [];
  categories: any[] = [];
  members: any[] = [];
  family: any = null;
  isLoading = true;
  errorMsg = '';
  summaryError = '';
  currentUserId = '';
  
  showInviteModal = false;
  inviteEmail = '';
  isInviting = false;
  inviteError = '';
  fallbackInviteCode = '';

  constructor(
    private familyService: FamilyService,
    private categoryService: CategoryService,
    public authService: AuthService
  ) { }

  ngOnInit() {
    this.currentUserId = this.getCurrentUserId();
    this.loadData();
  }

  async loadData() {
    this.isLoading = true;
    this.errorMsg = '';
    this.summaryError = '';
    this.summary = [];

    try {
      const [membersRes, catsRes, familyRes] = await Promise.all([
        firstValueFrom(this.familyService.getMembers()),
        firstValueFrom(this.categoryService.getCategories()),
        firstValueFrom(this.familyService.getFamily())
      ]);

      this.members = membersRes || [];
      this.categories = catsRes || [];
      this.family = familyRes;

      if (this.isHead) {
        await this.loadFamilySummary();
      }
    } catch (err: any) {
      console.error(err);
      this.errorMsg = err.error?.message || "Failed to load family details.";
    } finally {
      this.isLoading = false;
    }
  }

  async loadFamilySummary() {
    try {
      this.summary = await firstValueFrom(this.familyService.getFamilySummary()) || [];
    } catch (err: any) {
      console.error(err);
      this.summaryError = err.error?.message || "Dependent summary is unavailable right now.";
      this.summary = [];
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

  openInviteModal() {
    this.showInviteModal = true;
    this.inviteEmail = '';
    this.inviteError = '';
    this.fallbackInviteCode = '';
  }

  closeInviteModal() {
    this.showInviteModal = false;
    this.inviteError = '';
    this.fallbackInviteCode = '';
  }

  sendInvite() {
    if (!this.inviteEmail) return;
    this.isInviting = true;
    this.inviteError = '';
    this.fallbackInviteCode = '';
    
    this.familyService.sendInvite(this.inviteEmail).subscribe({
      next: (res) => {
        this.isInviting = false;
        this.showInviteModal = false;
        alert(res.message || res.Message || `Invitation sent to ${this.inviteEmail}!`);
      },
      error: (err) => {
        this.isInviting = false;
        const message = err.error?.message || err.error?.Message || "Failed to send invitation.";
        const inviteCode = err.error?.inviteCode || err.error?.InviteCode;

        if (err.status === 501 && inviteCode) {
          this.inviteError = message;
          this.fallbackInviteCode = inviteCode;
          return;
        }

        this.inviteError = message;
      }
    });
  }

  copyFallbackInviteCode() {
    if (!this.fallbackInviteCode) return;

    navigator.clipboard.writeText(this.fallbackInviteCode).then(() => {
      alert('Invite code copied to clipboard.');
    }).catch(() => {
      alert(`Invite code: ${this.fallbackInviteCode}`);
    });
  }

  get isHead(): boolean {
    return !!this.family?.headUserId && this.normalizeId(this.family.headUserId) === this.normalizeId(this.currentUserId);
  }

  copyInviteCode() {
    const code = this.family?.inviteCode;
    if (!code) return;

    navigator.clipboard.writeText(code).then(() => {
      alert('Invite code copied to clipboard.');
    }).catch(() => {
      alert(`Invite code: ${code}`);
    });
  }

  private getCurrentUserId(): string {
    const token = this.authService.getAuthToken();
    if (!token) return '';

    try {
      const payloadSegment = token.split('.')[1];
      const normalizedPayload = payloadSegment.replace(/-/g, '+').replace(/_/g, '/');
      const paddedPayload = normalizedPayload.padEnd(normalizedPayload.length + (4 - normalizedPayload.length % 4) % 4, '=');
      const payload = JSON.parse(atob(paddedPayload));
      return payload.sub || '';
    } catch {
      return '';
    }
  }

  private normalizeId(id: string): string {
    return (id || '').trim().toLowerCase();
  }

  isMemberHead(member: any): boolean {
    return (member?.relation || '').toLowerCase() === 'head';
  }

  isCurrentUser(member: any): boolean {
    return this.normalizeId(member?.userId) === this.normalizeId(this.currentUserId);
  }

  getMemberRoleLabel(member: any): string {
    if (this.isMemberHead(member)) return 'Head';
    return member?.isDependent ? 'Dependent' : 'Member';
  }

  canManageMember(member: any): boolean {
    return this.isHead && !this.isMemberHead(member);
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
