import { Component, OnInit, OnDestroy } from '@angular/core';
import { Subject, interval } from 'rxjs';
import { startWith, takeUntil } from 'rxjs/operators';
import { ApiService } from '../api.service';

@Component({
  selector: 'app-dashboard',
  templateUrl: './dashboard.component.html'
})
export class DashboardComponent implements OnInit, OnDestroy {
  subs: any[] = [];
  patterns: any = { byRule: [], bySeverity: [] };
  alerts: any = { alerts: [], compromise: [] };
  usage: any;
  updated: Date | null = null;
  now: Date = new Date();
  windowMinutes = 15;
  showRiskInfo = false;
  private destroy$ = new Subject<void>();

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    interval(5000).pipe(startWith(0), takeUntil(this.destroy$)).subscribe(() => this.refresh());
    // tick the clock every second so the header time is live
    interval(1000).pipe(takeUntil(this.destroy$)).subscribe(() => this.now = new Date());
  }

  refresh(): void {
    this.api.activeSubs(50, this.windowMinutes).subscribe(s => this.subs = s);
    this.api.patterns().subscribe(p => this.patterns = p);
    this.api.alerts().subscribe(a => this.alerts = a);
    this.api.usageSummary().subscribe(u => this.usage = u);
    this.updated = new Date();
    this.now = new Date();
  }

  maxRule(): number {
    return Math.max(1, ...(this.patterns.byRule || []).map((r: any) => r.atRiskUsd || 0));
  }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }
}
