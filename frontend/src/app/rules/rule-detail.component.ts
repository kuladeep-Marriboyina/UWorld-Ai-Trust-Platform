import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiService } from '../core/api.service';
import { SessionService } from '../core/session.service';
import { RULE_KIND_HELP, sevClass } from '../core/format';
import { condText, describeLogic } from './rule-text';

interface DiffRow { label: string; before: string; after: string; changed: boolean; }

@Component({
  selector: 'app-rule-detail',
  templateUrl: './rule-detail.component.html',
  styles: [`
    .params td:first-child { width: 38%; }
    .sentence { font-size: 14px; line-height: 1.6; padding: 12px 14px; background: var(--surface-2); border-radius: 10px; border: 1px solid var(--border); }
    .ver { display:grid; grid-template-columns: 54px 1fr; gap: 10px; padding: 10px 0; border-bottom: 1px dashed var(--border); }
    .ver:last-child { border-bottom: 0; }
    .vno { font-weight: 800; font-size: 14px; }
    .review { border-color: rgba(250,178,25,.55); }
    .diff td { vertical-align: top; font-size: 12.5px; }
    .diff tr.changed td { background: var(--medium-soft); }
    .diff .after { color: var(--text-1); font-weight: 600; }
    .diff .before { color: var(--text-3); }
    .statgrid { display:grid; grid-template-columns: repeat(3, 1fr); gap: 10px; }
    .statgrid div { background: var(--surface-2); border: 1px solid var(--border); border-radius: 10px; padding: 10px; }
    .statgrid b { display:block; font-size: 18px; }
    .statgrid span { font-size: 11.5px; color: var(--text-3); }
  `]
})
export class RuleDetailComponent implements OnInit {
  id = '';
  d: any = null;
  error = '';
  busy = false;
  note = '';
  msg = '';
  toggleModal = false;
  toggleNote = '';
  reviewVersion: number | null = null;
  sevClass = sevClass; kindHelp = RULE_KIND_HELP; describe = describeLogic; cond = condText;

  constructor(private route: ActivatedRoute, private api: ApiService, public session: SessionService, private router: Router) {}

  ngOnInit(): void {
    this.route.paramMap.subscribe(p => { this.id = p.get('id') || ''; this.load(); });
    this.route.queryParamMap.subscribe(q => this.reviewVersion = q.get('review') ? +q.get('review')! : null);
  }

  load(): void {
    this.api.rule(this.id).subscribe(d => { this.d = d; this.error = ''; }, e => this.error = e.status === 404 ? 'No such rule.' : 'Could not load rule.');
  }

  get r(): any { return this.d?.rule; }
  get pendingVersion(): any { return this.d?.versions?.find((v: any) => v.status === 'PendingApproval'); }
  get activeVersion(): any { return this.d?.versions?.find((v: any) => v.status === 'Approved'); }
  get isOwnPending(): boolean { return this.pendingVersion?.author === this.session.user.username; }
  get history(): any[] { return this.d?.versions || []; }

  params(): { label: string; value: string; unit: string; help: string }[] {
    const cfg = this.safeJson(this.r?.config);
    return (this.r?.parameters || []).map((p: any) => ({
      label: p.label, unit: p.unit || '', help: p.help,
      value: cfg[p.key] !== undefined ? (Array.isArray(cfg[p.key]) ? cfg[p.key].join(', ') || '(none)' : String(cfg[p.key])) : `${p.default ?? (p.defaultList || []).join(', ')} (default)`,
    }));
  }

  /** Field-by-field comparison of the live definition and the proposal under review. */
  diff(): DiffRow[] {
    const p = this.pendingVersion?.definition; if (!p) { return []; }
    const cur = this.r;
    const curCfg = this.safeJson(cur.config), newCfg = this.safeJson(p.configJson);
    const rows: DiffRow[] = [
      { label: 'Name', before: cur.name, after: p.name },
      { label: 'Severity', before: cur.severity, after: p.severity },
      { label: 'Category', before: cur.category, after: p.category },
      { label: 'Expected behaviour', before: cur.description, after: p.description },
      { label: 'Business reason', before: cur.rationale || '—', after: p.rationale || '—' },
    ].map(x => ({ ...x, changed: (x.before || '') !== (x.after || '') }));
    (cur.parameters || []).forEach((sp: any) => {
      const b = curCfg[sp.key], a = newCfg[sp.key];
      const show = (v: any) => v === undefined ? '(default)' : Array.isArray(v) ? v.join(', ') : String(v);
      rows.push({ label: `${sp.label}${sp.unit ? ' (' + sp.unit + ')' : ''}`, before: show(b), after: show(a), changed: show(b) !== show(a) });
    });
    if (p.logic || cur.logic) {
      const b = cur.logic ? describeLogic({ ...cur.logic, severity: cur.severity }) : '— (new rule)';
      const a = p.logic ? describeLogic({ ...p.logic, severity: p.severity }) : '—';
      rows.push({ label: 'Detection logic', before: b, after: a, changed: a !== b });
    }
    const pb = (x: string[]) => (x || []).map((s, i) => `${i + 1}. ${s}`).join('\n');
    rows.push({ label: 'Playbook', before: pb(cur.playbook) || '—', after: pb(p.playbook) || '—', changed: pb(cur.playbook) !== pb(p.playbook) });
    return rows;
  }

  approve(): void {
    const v = this.pendingVersion; if (!v) { return; }
    this.busy = true;
    this.api.approve(this.id, v.versionNo, this.note).subscribe(() => { this.busy = false; this.note = ''; this.msg = `v${v.versionNo} approved — live on the next event.`; this.load(); },
      e => { this.busy = false; this.msg = e.error?.error || 'Approval failed'; });
  }
  reject(): void {
    const v = this.pendingVersion; if (!v) { return; }
    if (!this.note.trim()) { this.msg = 'Tell the author why it is rejected.'; return; }
    this.busy = true;
    this.api.reject(this.id, v.versionNo, this.note).subscribe(() => { this.busy = false; this.note = ''; this.msg = `v${v.versionNo} rejected.`; this.load(); },
      e => { this.busy = false; this.msg = e.error?.error || 'Rejection failed'; });
  }
  doToggle(): void {
    const enable = !this.r.enabled;
    this.busy = true;
    this.api.toggle(this.id, enable, this.toggleNote).subscribe(() => { this.busy = false; this.toggleModal = false; this.toggleNote = ''; this.load(); },
      e => { this.busy = false; this.msg = e.error?.error || 'Failed'; });
  }

  logicSentence(): string { return describeLogic({ ...this.r.logic, severity: this.r.severity }); }
  pct(v: number | null): string { return v == null ? '—' : Math.round(v * 100) + '%'; }
  verClass(s: string): string { return ({ Approved: 'good', PendingApproval: 'warn', Rejected: 'bad', Superseded: '', Enabled: 'good', Disabled: 'bad' } as any)[s] || ''; }
  private safeJson(s: string | null | undefined): any { try { return s ? JSON.parse(s) : {}; } catch { return {}; } }
}
