import { Routes } from '@angular/router';
import { LoginComponent } from './auth/login/login.component';
import { RegisterComponent } from './auth/register/register.component';
import { ForgotPasswordComponent } from './auth/forgot-password/forgot-password.component';
import { OnboardingComponent } from './onboarding/onboarding.component';
import { authGuard } from './auth/auth.guard';

export const routes: Routes = [
    { path: 'login', component: LoginComponent },
    { path: 'register', component: RegisterComponent },
    { path: 'forgot-password', component: ForgotPasswordComponent },
    { path: 'onboarding', component: OnboardingComponent },
    { path: 'dashboard', canActivate: [authGuard], loadComponent: () => import('./dashboard/dashboard.component').then(m => m.DashboardComponent) },
    { path: 'budgets', canActivate: [authGuard], loadComponent: () => import('./budgets/budgets-view/budgets-view.component').then(m => m.BudgetsViewComponent) },
    { path: 'finance', canActivate: [authGuard], loadComponent: () => import('./finance/finance-view/finance-view.component').then(m => m.FinanceViewComponent) },
    { path: 'family', canActivate: [authGuard], loadComponent: () => import('./family/family-view/family-view.component').then(m => m.FamilyViewComponent) },
    { path: '', redirectTo: '/onboarding', pathMatch: 'full' }
];
