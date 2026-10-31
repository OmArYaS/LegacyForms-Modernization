import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { LayoutComponent } from './features/layout.component';
import { DashboardComponent } from './features/dashboard.component';
import { SubmissionsComponent } from './features/submissions.component';
import { SubmissionDetailComponent } from './features/submission-detail.component';
import { TemplatesComponent } from './features/templates.component';

/** Route table — the modern faces of the legacy /Submissions screens (docs/legacy-inventory.md). */
export const routes: Routes = [
  { path: '', component: LayoutComponent, canActivate: [authGuard], children: [
    { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
    { path: 'dashboard', component: DashboardComponent },
    { path: 'submissions', component: SubmissionsComponent },
    { path: 'submissions/:id', component: SubmissionDetailComponent },
    { path: 'templates', component: TemplatesComponent },
  ]},
];
