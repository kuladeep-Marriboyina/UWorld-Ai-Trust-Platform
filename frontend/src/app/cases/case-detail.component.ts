import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Subject } from 'rxjs';
import { debounceTime, filter, takeUntil } from 'rxjs/operators';
import { ApiService } from '../core/api.service';
import { LiveService } from '../core/live.service';
import { SessionService } from '../core/session.service';
import { factorLabel, sevClass, utc } from '../core/format';

type Tab = 'graph' | 'accounts' | 'timeline' | 'evidence' | 'related' | 'activity';

@Component({
  selector: 'app-case-detail',
  templateUrl: './case-detail.component.html',
  styleUrls: ['./case-detail.component.css']
})
export class CaseDetailComponent implements OnInit, OnDestroy {
  id = 0;
  d: any = null;          // dossier
  c: any = null;          // case
  error = '';
  tab: Tab = 'graph';
  busy = '';
  flash = '';
  summary: any = null;
  summaryBusy = false;
  question = '';
  answers: { q: string; a: any }[] = [];
  asking = false;
  comment = '';
  timelineFilter = 'all';

  modal: '' | 'verdict' | 'escalate' | 'share' = '';
  verdict = 'ConfirmedFraud';
  verdictNotes = '';
  resolve = true;
  escalateNotes = '';
  share: any = null;
  redact = false;
  shareTab: 'md' | 'chat' = 'chat';
  copied = '';

  sevClass = sevClass; factorLabel = factorLabel; location = location;
  private destroy$ = new Subject<void>();

  constructor(private route: ActivatedRoute, private api: ApiService, private live: LiveService, public session: SessionService) {}

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntil(this.destroy$)).subscribe(p => {
      this.id = +(p.get('id') || 0);
      this.d = null; this.summary = null; this.answers = []; this.tab = 'graph';
      this.load();
      this.api.cachedSummary(this.id).subscribe(s => this.summary = s || null, () => {});
    });
    this.live.events$.pipe(filter(e => e.caseId === this.id && e.type !== 'case-status'), debounceTime(300), takeUntil(this.destroy$))
      .subscribe(e => { this.flashMsg(e.type === 'case-updated' ? 'This case just grew: ' + (e.detail || '') : e.title); this.load(); });
  }

  load(): void {
    this.api.investigation(this.id).subscribe(d => {
      this.d = d; this.c = d.case; this.error = '';
      if (!d.nodes.length && this.tab === 'graph') { this.tab = d.accounts.length ? 'accounts' : 'timeline'; }
    }, e => this.error = e.status === 404 ? `Case #${this.id} doesn't exist.` : 'Could not load this case — is the API running?');
  }

  get canWork(): boolean { return this.session.can('work-cases'); }
  get open(): boolean { return this.c && this.c.status !== 'Resolved'; }

  // ---- actions ----
  private act(label: string, obs: any, done?: () => void): void {
    this.busy = label;
    obs.subscribe((c: any) => { this.busy = ''; this.load(); if (done) { done(); } },
      (e: any) => { this.busy = ''; this.flashMsg(e.error?.error || 'Action failed'); });
  }
  acknowledge(): void { this.act('ack', this.api.acknowledge(this.id)); }
  assignMe(): void { this.act('assign', this.api.assign(this.id, this.session.user.username)); }
  doEscalate(): void { this.act('escalate', this.api.escalate(this.id, this.escalateNotes), () => { this.modal = ''; this.escalateNotes = ''; }); }
  doVerdict(): void {
    this.act('verdict', this.api.disposition(this.id, this.verdict, this.verdictNotes, this.resolve), () => { this.modal = ''; this.verdictNotes = ''; });
  }
  reopen(): void { this.act('reopen', this.api.reopen(this.id, 'Reopened for further review')); }
  addComment(): void {
    const t = this.comment.trim(); if (!t) { return; }
    this.act('comment', this.api.comment(this.id, t), () => this.comment = '');
  }
  markStep(step: string): void { this.act('step:' + step, this.api.action(this.id, step)); }
  stepDone(step: string): any { return this.c?.activity?.find((a: any) => a.action === 'ActionTaken' && a.notes === step); }
  get stepsDone(): number { return (this.c?.playbook || []).filter((s: string) => this.stepDone(s)).length; }

  // ---- copilot ----
  generateSummary(): void {
    this.summaryBusy = true;
    this.api.aiSummary(this.id).subscribe(s => { this.summary = s; this.summaryBusy = false; this.load(); }, () => this.summaryBusy = false);
  }
  ask(q?: string): void {
    const question = (q || this.question).trim(); if (!question) { return; }
    this.asking = true; this.question = '';
    this.api.ask(this.id, question).subscribe(a => { this.answers.unshift({ q: question, a }); this.asking = false; },
      () => { this.answers.unshift({ q: question, a: { answer: 'The copilot could not answer right now.', supportingFacts: [] } }); this.asking = false; });
  }
  get suggestions(): string[] {
    if (!this.c) { return []; }
    if (this.c.ruleId === 'account-farm') { return ['How fast were these accounts created?', 'Which device created these accounts?', 'Have any of them paid?']; }
    if (this.c.ruleId === 'credential-stuffing') { return ['Which accounts were breached?', 'How many accounts were targeted?', 'What client did the attacker use?']; }
    return ['What exactly happened?', 'What is the revenue impact?', 'Is this linked to other cases?'];
  }
  verdictLabel(v: string): string { return ({ 'likely-fraud': 'Likely fraud', 'needs-review': 'Needs review', 'likely-benign': 'Likely benign' } as any)[v] || v; }

  // ---- share ----
  openShare(): void { this.modal = 'share'; this.loadShare(); }
  loadShare(): void {
    this.share = null;
    this.api.report(this.id, this.redact, location.origin + '/cases/' + this.id).subscribe(r => this.share = r);
  }
  copy(text: string, what: string): void {
    navigator.clipboard.writeText(text).then(() => { this.copied = what; setTimeout(() => this.copied = '', 1800); });
  }
  download(): void {
    const blob = new Blob([this.share.markdown], { type: 'text/markdown' });
    const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = `case-${this.id}${this.redact ? '-redacted' : ''}.md`; a.click();
    URL.revokeObjectURL(a.href);
  }
  print(): void { window.print(); }

  // ---- helpers ----
  get timeline(): any[] {
    const t = this.d?.timeline || [];
    if (this.timelineFilter === 'key') { return t.filter((x: any) => x.highlight); }
    if (this.timelineFilter === 'human') { return t.filter((x: any) => x.kind === 'activity' || x.kind === 'detected'); }
    return t;
  }
  span(): string {
    if (!this.c?.firstSignalUtc || !this.c?.triggerSignalUtc) { return ''; }
    const s = (utc(this.c.triggerSignalUtc)!.getTime() - utc(this.c.firstSignalUtc)!.getTime()) / 1000;
    return s < 90 ? `${Math.max(1, Math.round(s))}s` : s < 5400 ? `${Math.round(s / 60)} min` : `${(s / 3600).toFixed(1)} h`;
  }
  maxWeight(): number { return Math.max(1, ...(this.c?.scoreBreakdown || []).map((f: any) => f.weight)); }
  kindIcon(k: string): string {
    return ({ signup: '👤', subscription: '🎟', payment: '💳', login: '🔑', 'login-failed': '✕', api: '↔', evidence: '◆', detected: '🚨', activity: '✎' } as any)[k] || '•';
  }
  activityIcon(a: string): string {
    return ({ Created: '🚨', Updated: '📈', Reopened: '↺', Acknowledged: '👁', Assigned: '👤', Escalated: '⏫', Comment: '💬', ActionTaken: '✓', Resolved: '✔', Disposition: '⚖', AiSummary: '✦', Shared: '↗' } as any)[a] || '•';
  }
  repIcon(t: string): string { return ({ ip: '🌐', device: '💻', card: '💳', domain: '@' } as any)[t] || '•'; }
  flashMsg(m: string): void { this.flash = m; setTimeout(() => { if (this.flash === m) { this.flash = ''; } }, 6000); }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }
}
