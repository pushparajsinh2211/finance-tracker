import { Routes } from '@angular/router';
import { LoginComponent } from './auth/login/login.component';
import { RegisterComponent } from './auth/register/register.component';
import { ForgotPasswordComponent } from './auth/forgot-password/forgot-password.component';
import { OnboardingComponent } from './onboarding/onboarding.component';
import { authGuard } from './auth/auth.guard';
import { familyGuard } from './auth/family.guard';
import { MainLayoutComponent } from './ui/main-layout/main-layout.component';

export const routes: Routes = [
    { path: 'login', component: LoginComponent },
    { path: 'register', component: RegisterComponent },
    { path: 'forgot-password', component: ForgotPasswordComponent },
    { path: 'onboarding', canActivate: [authGuard], component: OnboardingComponent },
    
    // Authenticated Layout Routes
    {
        path: '',
        component: MainLayoutComponent,
        canActivate: [authGuard, familyGuard],
        children: [
            { path: 'dashboard', loadComponent: () => import('./dashboard/dashboard.component').then(m => m.DashboardComponent) },
            { path: 'budgets', loadComponent: () => import('./budgets/budgets-view/budgets-view.component').then(m => m.BudgetsViewComponent) },
            { path: 'finance', loadComponent: () => import('./finance/finance-view/finance-view.component').then(m => m.FinanceViewComponent) },
            { path: 'family', loadComponent: () => import('./family/family-view/family-view.component').then(m => m.FamilyViewComponent) },
            { path: '', redirectTo: 'dashboard', pathMatch: 'full' }
        ]
    },
    
    { path: '**', redirectTo: '/dashboard' }
];
