import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { AuthService } from '../../auth/auth.service';
import { NotificationService } from '../../notifications/notification.service';

@Component({
  selector: 'app-top-nav',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './top-nav.component.html',
  styleUrls: ['./top-nav.component.css']
})
export class TopNavComponent implements OnInit {
  notifications: any[] = [];
  unreadCount = 0;
  showDropdown = false;

  constructor(public authService: AuthService, private notifService: NotificationService, private router: Router) { }

  ngOnInit() {
    this.loadNotifications();
  }

  loadNotifications() {
    this.notifService.getNotifications().subscribe(data => {
      this.notifications = data;
      this.unreadCount = data.filter(n => !n.isRead).length;
    });
  }

  toggleDropdown() {
    this.showDropdown = !this.showDropdown;
  }

  markAsRead(id: string) {
    this.notifService.markAsRead(id).subscribe(() => this.loadNotifications());
  }

  markAllAsRead() {
    this.notifService.markAllAsRead().subscribe(() => {
      this.showDropdown = false;
      this.loadNotifications();
    });
  }

  logout() {
    this.authService.logout();
    this.router.navigate(['/login']);
  }
}
