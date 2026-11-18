import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { DashboardComponent } from './dashboard/dashboard.component';
import { CasesComponent } from './cases/cases.component';
import { CaseDetailComponent } from './cases/case-detail.component';
import { RulesComponent } from './rules/rules.component';
import { RuleDetailComponent } from './rules/rule-detail.component';
import { RuleEditorComponent } from './rules/rule-editor.component';
import { LabComponent } from './lab/lab.component';
import { StorefrontComponent } from './lab/storefront.component';

const routes: Routes = [
  { path: '', component: DashboardComponent },
  { path: 'cases', component: CasesComponent },
  { path: 'cases/:id', component: CaseDetailComponent },
  { path: 'rules', component: RulesComponent },
  { path: 'rules/new', component: RuleEditorComponent },
  { path: 'rules/:id', component: RuleDetailComponent },
  { path: 'rules/:id/edit', component: RuleEditorComponent },
  { path: 'lab', component: LabComponent },
  { path: 'store', component: StorefrontComponent },
  { path: '**', redirectTo: '' }
];

@NgModule({
  imports: [RouterModule.forRoot(routes, { scrollPositionRestoration: 'top' })],
  exports: [RouterModule]
})
export class AppRoutingModule { }
