import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subject, interval, merge } from 'rxjs';
import { debounceTime, filter, startWith, takeUntil } from 'rxjs/operators';
import { ApiService } from '../core/api.service';
import { LiveEvent, LiveService, eventLook } from '../core/live.service';
import { catColor, factorLabel, sevClass } from '../core/format';

@Component({
  selector: 'app-dashboard',
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.css']
})
export class DashboardComponent implements OnInit, OnDestroy {
  o: any = null;
  error = '';
  hours = 24;
  updated: Date | null = null;
  feedFilter: 'all' | 'alerts' | 'customer' = 'all';
  briefing: any = null;
  briefingBusy = false;
  sevClass = sevClass; catColor = catColor; factorLabel = factorLabel; look = eventLook; Math = Math;
  private destroy$ = new Subject<void>();

  constructor(private api: ApiService, public live: LiveService) {}

  ngOnInit(): void {
    const caseEvents = this.live.events$.pipe(filter(e => e.type.startsWith('case') || e.type === 'signup' || e.type === 'login-failed'), debounceTime(700));
    merge(interval(15000).pipe(startWith(0)), caseEvents).pipe(takeUntil(this.destroy$)).subscribe(() => this.load());
  }

  load(): void {
    this.api.overview(this.hours).subscribe(o => { this.o = o; this.error = ''; this.updated = new Date(); },
      () => this.error = `Can't reach the Sentinel API at ${this.api.base}. Start the backend (dotnet run) and this page will recover on its own.`);
  }

  setHours(h: number): void { this.hours = h; this.load(); }

  brief(): void {
    this.briefingBusy = true;
    this.api.briefing().subscribe(b => { this.briefing = b; this.briefingBusy = false; }, () => this.briefingBusy = false);
  }

  get feed(): LiveEvent[] {
    const all = this.live.recent;
    const f = this.feedFilter === 'alerts' ? all.filter(e => e.type.startsWith('case') || e.type === 'rule')
            : this.feedFilter === 'customer' ? all.filter(e => ['signup', 'signup-rejected', 'login', 'login-failed', 'subscribe'].indexOf(e.type) >= 0)
            : all;
    return f.slice(0, 16);
  }

  maxCat(): number { return Math.max(1, ...(this.o?.categories || []).map((c: any) => c.atRiskUsd)); }
  maxHot(): number { return Math.max(1, ...(this.o?.hotspots || []).map((h: any) => h.cases)); }
  pct(v: number | null | undefined): string { return v == null ? '—' : Math.round(v * 100) + '%'; }
  postureIcon(l: string): string { return ({ critical: '◆', elevated: '▲', guarded: '●', calm: '✓' } as any)[l] || '●'; }
  waitClass(c: any): string {
    const mins = (Date.now() - Date.parse(c.createdUtc.endsWith('Z') ? c.createdUtc : c.createdUtc + 'Z')) / 60000;
    if (c.acknowledged) { return ''; }
    return (c.severity === 'Critical' && mins > 15) || mins > 60 ? 'late' : '';
  }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }
}
