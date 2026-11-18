import { Component, HostListener, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject } from 'rxjs';
import { debounceTime, filter, takeUntil } from 'rxjs/operators';
import { ApiService } from '../core/api.service';
import { LiveService } from '../core/live.service';
import { SessionService } from '../core/session.service';
import { catColor, sevClass, utc } from '../core/format';

type View = 'open' | 'unack' | 'mine' | 'overdue' | 'resolved' | 'all';
type Sort = 'priority' | 'newest' | 'impact' | 'score' | 'oldest';

const SEVERITIES = ['Critical', 'High', 'Medium', 'Low'];
const SEV_RANK: { [k: string]: number } = { Critical: 0, High: 1, Medium: 2, Low: 3 };

/**
 * Case queue. Search runs on the server (it matches linked IPs, emails, devices and cards, not just titles);
 * view, severity, category and sort are applied instantly in the browser so every control shows live counts.
 */
@Component({
  selector: 'app-cases',
  templateUrl: './cases.component.html',
  styleUrls: ['./cases.component.css']
})
export class CasesComponent implements OnInit, OnDestroy {
  all: any[] = [];
  view: View = 'open';
  severities = new Set<string>();
  categories = new Set<string>();
  sort: Sort = 'priority';
  q = '';
  loading = true;
  error = '';
  catOpen = false;
  sortOpen = false;
  fresh = new Set<number>();
  sevList = SEVERITIES;
  sevClass = sevClass; catColor = catColor;
  sortLabels: { [k in Sort]: string } = { priority: 'Priority', newest: 'Newest first', oldest: 'Oldest first', impact: 'Highest $ impact', score: 'Highest score' };
  sortKeys: Sort[] = ['priority', 'newest', 'oldest', 'impact', 'score'];
  private known = new Set<number>();
  private search$ = new Subject<void>();
  private destroy$ = new Subject<void>();

  constructor(private api: ApiService, private live: LiveService, private route: ActivatedRoute, private router: Router, public session: SessionService) {}

  ngOnInit(): void {
    const p = this.route.snapshot.queryParamMap;
    this.q = p.get('q') || '';
    (p.get('category') || '').split(',').filter(x => x).forEach(x => this.categories.add(x));
    (p.get('severity') || '').split(',').filter(x => x).forEach(x => this.severities.add(x));
    const v = p.get('view') as View; if (v) { this.view = v; }
    const s = p.get('sort') as Sort; if (s) { this.sort = s; }
    this.load();
    this.search$.pipe(debounceTime(300), takeUntil(this.destroy$)).subscribe(() => { this.syncUrl(); this.load(); });
    this.live.events$.pipe(filter(e => e.type.startsWith('case')), debounceTime(500), takeUntil(this.destroy$)).subscribe(() => this.load());
  }

  load(): void {
    this.api.cases({ q: this.q }).subscribe(c => {
      if (this.known.size) { c.filter(x => !this.known.has(x.caseId)).forEach(x => this.fresh.add(x.caseId)); }
      c.forEach(x => this.known.add(x.caseId));
      this.all = c; this.loading = false; this.error = '';
    }, () => { this.loading = false; this.error = 'Could not load cases — is the API running?'; });
  }

  // ---------- filtering ----------
  private inView(c: any, v: View): boolean {
    const open = c.status !== 'Resolved';
    switch (v) {
      case 'open': return open;
      case 'unack': return open && !c.acknowledgedUtc;
      case 'mine': return open && c.assignedTo === this.session.user.username;
      case 'overdue': return this.overdue(c);
      case 'resolved': return !open;
      default: return true;
    }
  }
  private passSev(c: any): boolean { return !this.severities.size || this.severities.has(c.severity); }
  private passCat(c: any): boolean { return !this.categories.size || this.categories.has(c.category || 'Other'); }

  get rows(): any[] {
    const r = this.all.filter(c => this.inView(c, this.view) && this.passSev(c) && this.passCat(c));
    const t = (c: any) => utc(c.createdUtc)!.getTime();
    switch (this.sort) {
      case 'newest': return r.sort((a, b) => t(b) - t(a));
      case 'oldest': return r.sort((a, b) => t(a) - t(b));
      case 'impact': return r.sort((a, b) => b.businessImpactUsd - a.businessImpactUsd);
      case 'score': return r.sort((a, b) => b.score - a.score);
      default: return r.sort((a, b) => (a.status === 'Resolved' ? 1 : 0) - (b.status === 'Resolved' ? 1 : 0)
        || SEV_RANK[a.severity] - SEV_RANK[b.severity] || (a.acknowledgedUtc ? 1 : 0) - (b.acknowledgedUtc ? 1 : 0) || t(a) - t(b));
    }
  }
  /** Counts for a tab honour the other filters, so the numbers always match what you'd see. */
  viewCount(v: View): number { return this.all.filter(c => this.inView(c, v) && this.passSev(c) && this.passCat(c)).length; }
  sevCount(s: string): number { return this.all.filter(c => this.inView(c, this.view) && this.passCat(c) && c.severity === s).length; }
  catCount(cat: string): number { return this.all.filter(c => this.inView(c, this.view) && this.passSev(c) && (c.category || 'Other') === cat).length; }
  get categoryList(): string[] {
    const set = new Set(this.all.map(c => c.category || 'Other'));
    return Array.from(set).sort((a, b) => this.catCount(b) - this.catCount(a) || a.localeCompare(b));
  }

  // ---------- controls ----------
  setView(v: View): void { this.view = v; this.syncUrl(); }
  toggleSev(s: string): void { this.severities.has(s) ? this.severities.delete(s) : this.severities.add(s); this.syncUrl(); }
  toggleCat(c: string): void { this.categories.has(c) ? this.categories.delete(c) : this.categories.add(c); this.syncUrl(); }
  setSort(s: Sort): void { this.sort = s; this.sortOpen = false; this.syncUrl(); }
  onSearch(): void { this.search$.next(); }
  clearSearch(): void { this.q = ''; this.search$.next(); }
  clearAll(): void { this.q = ''; this.severities.clear(); this.categories.clear(); this.syncUrl(); this.load(); }
  get activeCount(): number { return this.severities.size + this.categories.size + (this.q ? 1 : 0); }
  get sevArr(): string[] { return Array.from(this.severities); }
  get catArr(): string[] { return Array.from(this.categories); }

  syncUrl(): void {
    this.router.navigate([], {
      queryParams: {
        q: this.q || null, view: this.view === 'open' ? null : this.view, sort: this.sort === 'priority' ? null : this.sort,
        severity: this.severities.size ? this.sevArr.join(',') : null, category: this.categories.size ? this.catArr.join(',') : null,
      }, replaceUrl: true
    });
  }

  // ---------- row helpers ----------
  ageMin(c: any): number { return (Date.now() - utc(c.createdUtc)!.getTime()) / 60000; }
  /** SLA: critical needs a first look within 15 min, high within 60. */
  overdue(c: any): boolean {
    if (c.acknowledgedUtc || c.status === 'Resolved') { return false; }
    return (c.severity === 'Critical' && this.ageMin(c) > 15) || (c.severity === 'High' && this.ageMin(c) > 60);
  }
  get totalUsd(): number { return this.rows.reduce((s, c) => s + c.businessImpactUsd, 0); }
  get overdueCount(): number { return this.rows.filter(c => this.overdue(c)).length; }
  get urgentCount(): number { return this.rows.filter(c => c.severity === 'Critical' || c.severity === 'High').length; }
  open(c: any): void { this.router.navigate(['/cases', c.caseId]); }

  @HostListener('document:click')
  closeMenus(): void { this.catOpen = false; this.sortOpen = false; }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }
}
