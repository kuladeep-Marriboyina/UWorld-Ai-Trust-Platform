import { Component, OnDestroy, OnInit } from '@angular/core';
import { ApiService } from '../core/api.service';
import { LiveService, eventLook } from '../core/live.service';
import { SessionService } from '../core/session.service';
import { sevClass } from '../core/format';
import { SCENARIOS, Scenario, Step } from './simulation';

@Component({
  selector: 'app-lab',
  templateUrl: './lab.component.html',
  styleUrls: ['./lab.component.css']
})
export class LabComponent implements OnInit, OnDestroy {
  scenarios = SCENARIOS;
  sc: Scenario = SCENARIOS[0];
  steps: Step[] = [];
  idx = 0;
  running = false;
  speed = 1200;
  expanded: Step | null = null;
  cases: any[] = [];
  started = false;

  groups: any[] = [];
  openGroup: any = null;
  injecting = '';
  injections: any[] = [];

  resetting = false;
  resetMsg = '';
  machine: any = null;
  sevClass = sevClass; look = eventLook; Math = Math;
  private timer: any;

  constructor(private api: ApiService, public session: SessionService, public live: LiveService) {}

  ngOnInit(): void {
    this.select(this.sc);
    this.api.scenarios().subscribe(g => this.groups = g, () => {});
    this.readMachine();
  }

  // ---------- live scenario runner ----------
  select(s: Scenario): void {
    if (this.running) { return; }
    // mutate in place: later steps read earlier steps' results (e.g. the new account's id) through closures
    this.sc = s; this.steps = s.build(this.api); this.steps.forEach(x => x.state = 'pending');
    this.idx = 0; this.cases = []; this.started = false; this.expanded = null;
  }
  play(): void {
    if (!this.session.can('simulate')) { return; }
    if (this.idx >= this.steps.length) { this.select(this.sc); }
    this.running = true; this.started = true; this.next();
  }
  pause(): void { this.running = false; clearTimeout(this.timer); }
  restart(): void { this.pause(); this.select(this.sc); }

  private next(): void {
    if (!this.running) { return; }
    if (this.idx >= this.steps.length) { this.running = false; return; }
    const step = this.steps[this.idx];
    step.state = 'running';
    step.run().subscribe(res => this.finish(step, true, res?.status || 201, res), e => this.finish(step, e.status === 401 || e.status === 409, e.status, e.error));
  }
  private finish(step: Step, ok: boolean, status: number, body: any): void {
    step.result = { ok, status, body }; step.state = ok ? 'done' : 'error';
    (body?.detection?.cases || []).forEach((c: any) => {
      const existing = this.cases.find(x => x.caseId === c.caseId);
      if (existing) { Object.assign(existing, c, { updates: existing.updates + 1, lastStep: this.idx + 1 }); }
      else { this.cases.push({ ...c, firstStep: this.idx + 1, updates: 0 }); }
    });
    this.idx++;
    this.timer = setTimeout(() => this.next(), this.speed);
  }

  get done(): boolean { return this.started && !this.running && this.idx >= this.steps.length; }
  get lastDetection(): any {
    for (let i = this.idx - 1; i >= 0; i--) { const d = this.steps[i].result?.body?.detection; if (d) { return d; } }
    return null;
  }
  get meter(): { value: number; threshold: number; label: string; factors: any[] } {
    const d = this.lastDetection;
    if (this.sc.meter === 'stuffing') {
      const cs = d?.credentialStuffing;
      const v = cs ? Math.min(100, 100 * cs.failuresFromIp / cs.minFailures) : 0;
      return { value: v, threshold: 100, label: cs ? `${cs.failuresFromIp} failed logins across ${cs.distinctAccounts} accounts (alert at ${cs.minFailures} across ${cs.minDistinctAccounts})` : 'No failed logins yet', factors: [] };
    }
    const f = d?.accountFarm;
    return { value: f?.score || 0, threshold: f?.alertScore || 55, factors: f?.factors || [],
      label: f ? `${f.accounts} linked account(s) · ${f.corroborated ? 'identity signals corroborate' : 'shared network only'} · case needs ${f.minAccounts}+ accounts` : 'No linked accounts yet' };
  }
  verdictOf(s: Step): string { return s.result?.body?.detection?.verdict || (s.result?.ok === false ? 'error' : ''); }
  stepIcon(s: Step): string { return s.state === 'running' ? '…' : s.state === 'error' ? '!' : s.state === 'done' ? (this.verdictOf(s) === 'alert' ? '🚨' : this.verdictOf(s) === 'watching' ? '👁' : '✓') : ''; }
  json(o: any): string { return JSON.stringify(o, null, 2); }
  compactDetection(s: Step): any {
    const d = s.result?.body?.detection; if (!d) { return s.result?.body; }
    return { verdict: d.verdict, passMs: d.passMs, cases: d.cases, accountFarm: d.accountFarm && { score: d.accountFarm.score, accounts: d.accountFarm.accounts, factors: d.accountFarm.factors.map((f: any) => `${f.signal} +${f.weight}`) }, credentialStuffing: d.credentialStuffing };
  }
  get stepsRun(): number { return this.steps.filter(s => s.state === 'done' || s.state === 'error').length; }

  // ---------- multi-window ----------
  readMachine(): void { try { this.machine = JSON.parse(localStorage.getItem('sf.machine') || 'null'); } catch { this.machine = null; } }
  openWindows(n: number): void {
    const w = Math.floor(screen.availWidth / n), h = screen.availHeight - 60;
    for (let i = 0; i < n; i++) { window.open('/store', 'store-' + i + '-' + Date.now(), `width=${w},height=${h},left=${i * w},top=0`); }
    setTimeout(() => this.readMachine(), 1500);
  }
  newMachine(): void { localStorage.removeItem('sf.machine'); localStorage.removeItem('sf.seq'); this.machine = null; }

  // ---------- data-level injections ----------
  inject(v: any): void {
    this.injecting = v.key;
    this.api.simulate(v.key).subscribe(r => { this.injecting = ''; this.injections.unshift({ variant: v, result: r }); },
      e => { this.injecting = ''; this.injections.unshift({ variant: v, error: e.error?.error || 'failed' }); });
  }

  reset(): void {
    if (!confirm('Reset the demo database to its seeded baseline? All simulated accounts and cases from this session are removed.')) { return; }
    this.resetting = true; this.resetMsg = '';
    this.api.resetDemo().subscribe(r => { this.resetting = false; this.resetMsg = `Reset done — ${r.casesRaised} baseline case(s) re-detected.`; this.restart(); },
      e => { this.resetting = false; this.resetMsg = e.error?.error || 'Reset failed'; });
  }

  ngOnDestroy(): void { this.pause(); }
}
