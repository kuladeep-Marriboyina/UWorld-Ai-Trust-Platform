import { Component, HostListener, OnDestroy, OnInit } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { Subject, interval, merge } from 'rxjs';
import { debounceTime, filter, startWith, takeUntil } from 'rxjs/operators';
import { ApiService } from './core/api.service';
import { LiveService } from './core/live.service';
import { SessionService } from './core/session.service';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent implements OnInit, OnDestroy {
  bare = false;
  menu = false;
  q = '';
  ai: any = null;
  counts = { open: 0, critical: 0, unack: 0, pending: 0 };
  theme = document.documentElement.getAttribute('data-theme') || 'dark';
  private destroy$ = new Subject<void>();

  constructor(public session: SessionService, public live: LiveService, private api: ApiService, public router: Router) {}

  ngOnInit(): void {
    this.router.events.pipe(filter(e => e instanceof NavigationEnd), takeUntil(this.destroy$))
      .subscribe((e: any) => this.bare = (e.urlAfterRedirects || e.url).startsWith('/store'));
    this.bare = location.pathname.startsWith('/store');
    this.live.start();
    this.api.aiStatus().subscribe(a => this.ai = a, () => this.ai = { live: false, provider: 'offline', detail: 'API unreachable' });

    const caseEvents = this.live.events$.pipe(filter(e => e.type.startsWith('case') || e.type === 'rule'), debounceTime(400));
    merge(interval(20000).pipe(startWith(0)), caseEvents, this.session.user$).pipe(takeUntil(this.destroy$)).subscribe(() => this.refreshCounts());
  }

  refreshCounts(): void {
    if (this.bare) { return; }
    this.api.cases({ open: true }).subscribe(c => this.counts = {
      ...this.counts, open: c.length,
      critical: c.filter(x => x.severity === 'Critical').length,
      unack: c.filter(x => !x.acknowledgedUtc).length,
    }, () => {});
    this.api.pending().subscribe(p => this.counts = { ...this.counts, pending: p.length }, () => {});
  }

  search(): void {
    const q = this.q.trim();
    if (!q) { return; }
    const id = q.replace(/^#/, '');
    if (/^\d+$/.test(id) && +id < 100000) { this.router.navigate(['/cases', +id]); }
    else { this.router.navigate(['/cases'], { queryParams: { q } }); }
    this.q = '';
  }

  switchUser(u: string): void { this.session.switchTo(u); this.menu = false; }

  toggleTheme(): void {
    this.theme = this.theme === 'dark' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', this.theme);
    try { localStorage.setItem('sentinel.theme', this.theme); } catch {}
  }

  openStore(): void { window.open('/store', '_blank', 'width=1180,height=900'); }

  roleHelp(role: string): string {
    return ({ Admin: 'Everything, plus approve rules, enable/disable detections, reset demo.', RuleAuthor: 'Work cases and propose rule changes (needs Admin approval).',
      Analyst: 'Investigate, act on and resolve cases; run simulations.', Viewer: 'Read-only: dashboards and cases.' } as any)[role] || '';
  }

  @HostListener('document:keydown', ['$event'])
  key(e: KeyboardEvent): void {
    if (e.key === '/' && !(e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement)) {
      e.preventDefault(); (document.getElementById('global-search') as HTMLInputElement)?.focus();
    }
    if (e.key === 'Escape') { this.menu = false; }
  }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }
}
