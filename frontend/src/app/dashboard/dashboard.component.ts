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
  overview: any = null;   // KPI tiles, repeat identifiers, per-rule precision, cases per day
  updated: Date | null = null;
  now: Date = new Date();
  /** Window for "Top active subscriptions". */
  windows = [{ label: '1h', minutes: 60 }, { label: '5h', minutes: 300 }, { label: '24h', minutes: 1440 }];
  windowMinutes = 60;
  subsLoading = false;
  showRiskInfo = false;
  private destroy$ = new Subject<void>();

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    interval(5000).pipe(startWith(0), takeUntil(this.destroy$)).subscribe(() => this.refresh());
    // tick the clock every second so the header time is live
    interval(1000).pipe(takeUntil(this.destroy$)).subscribe(() => this.now = new Date());
    // the overview is a heavier query (it re-scores linked accounts) — refresh it less often
    interval(15000).pipe(startWith(0), takeUntil(this.destroy$)).subscribe(() => this.loadOverview());
  }

  loadOverview(): void {
    this.api.overview(24).subscribe(o => this.overview = o, () => {});
  }

  pct(v: number | null | undefined): string { return v == null ? '—' : Math.round(v * 100) + '%'; }

  precisionColor(p: number): string { return p >= 0.7 ? 'var(--ok)' : p >= 0.4 ? 'var(--med)' : 'var(--crit)'; }

  refresh(): void {
    this.loadSubs();
    this.api.patterns().subscribe(p => this.patterns = p);
    this.api.alerts().subscribe(a => this.alerts = a);
    this.updated = new Date();
    this.now = new Date();
  }

  /** Position of the selected window — drives the sliding highlight in the toggle. */
  get windowIndex(): number { return Math.max(0, this.windows.findIndex(w => w.minutes === this.windowMinutes)); }

  get windowLabel(): string { return (this.windows.find(w => w.minutes === this.windowMinutes) || this.windows[0]).label; }

  setWindow(minutes: number): void {
    if (minutes === this.windowMinutes) { return; }
    this.windowMinutes = minutes;
    this.subsLoading = true;   // keep the old rows (faded) until the new window arrives
    this.loadSubs();
  }

  private loadSubs(): void {
    const asked = this.windowMinutes;
    this.api.activeSubs(50, asked).subscribe(s => {
      if (asked === this.windowMinutes) { this.subs = s; this.subsLoading = false; }   // ignore a stale response after a toggle
    }, () => this.subsLoading = false);
  }

  maxRule(): number {
    return Math.max(1, ...(this.patterns.byRule || []).map((r: any) => r.atRiskUsd || 0));
  }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }
}
