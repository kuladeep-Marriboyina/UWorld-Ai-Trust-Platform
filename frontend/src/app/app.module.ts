import { NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { HTTP_INTERCEPTORS, HttpClientModule } from '@angular/common/http';
import { FormsModule } from '@angular/forms';

import { AppRoutingModule } from './app-routing.module';
import { AppComponent } from './app.component';
import { UserHeaderInterceptor } from './core/session.service';
import { AgoPipe, DurationPipe, UsdPipe, UtcDatePipe } from './core/format';
import { GaugeComponent, LinkGraphComponent, TrafficChartComponent, TrendChartComponent } from './shared/charts';
import { InfoComponent } from './shared/info.component';
import { DashboardComponent } from './dashboard/dashboard.component';
import { CasesComponent } from './cases/cases.component';
import { CaseDetailComponent } from './cases/case-detail.component';
import { RulesComponent } from './rules/rules.component';
import { RuleDetailComponent } from './rules/rule-detail.component';
import { RuleEditorComponent } from './rules/rule-editor.component';
import { LabComponent } from './lab/lab.component';
import { StorefrontComponent } from './lab/storefront.component';

@NgModule({
  declarations: [
    AppComponent,
    AgoPipe, UsdPipe, DurationPipe, UtcDatePipe,
    TrafficChartComponent, TrendChartComponent, GaugeComponent, LinkGraphComponent, InfoComponent,
    DashboardComponent, CasesComponent, CaseDetailComponent,
    RulesComponent, RuleDetailComponent, RuleEditorComponent,
    LabComponent, StorefrontComponent,
  ],
  imports: [BrowserModule, HttpClientModule, FormsModule, AppRoutingModule],
  providers: [{ provide: HTTP_INTERCEPTORS, useClass: UserHeaderInterceptor, multi: true }],
  bootstrap: [AppComponent]
})
export class AppModule { }
