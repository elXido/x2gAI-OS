import { Routes } from '@angular/router';
import { Dashboard } from './pages/dashboard';

export const routes: Routes = [
  { path: '', component: Dashboard },
  // Discovery menu routes are dynamic; anything unknown lands on the overview.
  { path: '**', redirectTo: '' },
];
