import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { DashboardComponent } from './dashboard/dashboard.component';
import { CasesComponent } from './cases/cases.component';
import { CaseDetailComponent } from './cases/case-detail.component';
import { RulesComponent } from './rules/rules.component';

const routes: Routes = [
  { path: '', component: DashboardComponent },
  { path: 'cases', component: CasesComponent },
  { path: 'cases/:id', component: CaseDetailComponent },
  { path: 'rules', component: RulesComponent },
  { path: '**', redirectTo: '' }
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]
})
export class AppRoutingModule { }
