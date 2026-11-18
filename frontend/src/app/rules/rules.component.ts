import { Component, OnInit } from '@angular/core';
import { ApiService } from '../core/api.service';
import { SessionService } from '../core/session.service';
import { RULE_KIND_HELP, catColor, sevClass } from '../core/format';

@Component({
  selector: 'app-rules',
  templateUrl: './rules.component.html',
  styles: [`
    .rules { display:grid; grid-template-columns:repeat(auto-fill,minmax(360px,1fr)); gap:14px; }
    .rule { display:flex; flex-direction:column; gap:10px; color:var(--text-1); transition: border-color .12s, transform .12s; }
    .rule:hover { border-color: var(--accent); text-decoration:none; transform: translateY(-1px); }
    .rule.off { opacity:.62; }
    .rule h3 { font-size:14.5px; flex:1; }
    .desc { font-size:12.5px; color:var(--text-2); display:-webkit-box; -webkit-line-clamp:3; -webkit-box-orient:vertical; overflow:hidden; min-height: 54px; }
    .nums { display:grid; grid-template-columns:repeat(4,1fr); gap:8px; padding-top:10px; border-top:1px solid var(--border); }
    .nums b { display:block; font-size:16px; font-variant-numeric:tabular-nums; }
    .nums span { font-size:11px; color:var(--text-3); }
    .pend { border-color: rgba(250,178,25,.5); }
    .pend-row { display:flex; gap:12px; align-items:center; padding:10px 0; border-bottom:1px dashed var(--border); flex-wrap: wrap; }
    .pend-row:last-child { border-bottom:0; }
    .cat-dot { width:8px; height:8px; border-radius:2px; display:inline-block; }
    .filters { display:flex; gap:10px; align-items:center; flex-wrap:wrap; margin-bottom:14px; }
  `]
})
export class RulesComponent implements OnInit {
  rules: any[] = [];
  pending: any[] = [];
  filter = 'all';
  q = '';
  loading = true;
  sevClass = sevClass; catColor = catColor; kindHelp = RULE_KIND_HELP;

  constructor(private api: ApiService, public session: SessionService) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.rules().subscribe(r => { this.rules = r; this.loading = false; }, () => this.loading = false);
    this.api.pending().subscribe(p => this.pending = p);
  }

  ruleName(id: string): string { return this.rules.find(r => r.ruleId === id)?.name || id; }

  get shown(): any[] {
    const q = this.q.toLowerCase();
    return this.rules.filter(r =>
      (this.filter === 'all' || (this.filter === 'live' && r.isLive) || (this.filter === 'off' && !r.isLive) || r.kind === this.filter) &&
      (!q || (r.name + ' ' + r.description + ' ' + r.category + ' ' + r.ruleId).toLowerCase().includes(q)))
      .sort((a, b) => (b.isLive ? 1 : 0) - (a.isLive ? 1 : 0) || b.stats.open - a.stats.open || b.stats.total - a.stats.total);
  }

  pct(v: number | null): string { return v == null ? '—' : Math.round(v * 100) + '%'; }
  statusLabel(r: any): string {
    return r.status === 'PendingApproval' ? 'Awaiting approval' : r.status === 'Rejected' ? 'Rejected' : !r.enabled ? 'Disabled' : 'Live';
  }
}
