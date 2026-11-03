import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { TopNavComponent } from '../top-nav/top-nav.component';

@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [CommonModule, RouterModule, TopNavComponent],
  template: `
    <div class="app-shell">
      <app-top-nav></app-top-nav>
      <main class="content-area">
        <router-outlet></router-outlet>
      </main>
    </div>
  `,
  styles: [`
    .app-shell {
      position: relative;
      display: flex;
      min-height: 100vh;
      flex-direction: column;
      isolation: isolate;
    }

    .app-shell::before {
      position: fixed;
      z-index: -1;
      top: 5rem;
      right: -8rem;
      width: 22rem;
      height: 22rem;
      content: '';
      pointer-events: none;
      background: rgba(79, 70, 229, .055);
      border-radius: 50%;
      filter: blur(35px);
    }

    .content-area {
      flex: 1;
      animation: content-enter .28s ease-out;
    }

    @keyframes content-enter {
      from { opacity: 0; transform: translateY(6px); }
      to { opacity: 1; transform: translateY(0); }
    }
  `]
})
export class MainLayoutComponent {}
