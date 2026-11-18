import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiService } from '../core/api.service';
import { SessionService } from '../core/session.service';
import { sevClass } from '../core/format';
import { OPS, describeLogic } from './rule-text';

interface Cond { field: string; op: string; value: string | null; valuesText: string; }

@Component({
  selector: 'app-rule-editor',
  templateUrl: './rule-editor.component.html',
  styleUrls: ['./rule-editor.component.css']
})
export class RuleEditorComponent implements OnInit {
  mode: 'new' | 'coded' | 'authored' = 'new';
  ruleId = '';
  existing: any = null;
  catalog: any = null;
  ops = Object.keys(OPS);
  opsLabel = OPS;
  sevClass = sevClass;

  // step 1 — English
  english = '';
  draft: any = null;
  drafting = false;
  answers: { [q: string]: string } = {};
  examples = [
    'Flag when 4 or more accounts are created from the same device within 30 minutes',
    'Alert when one IP fails logins against 5 or more different accounts in 10 minutes',
    'Flag accounts with similar email format, 3 or more within 1 hour, using disposable emails',
    'Flag paid orders over $100 with no Braintree transaction, excluding trials',
    'Alert when one card pays for 3 or more different accounts in 1 day',
  ];

  // step 2 — logic
  target = 'USERS';
  when: Cond[] = [];
  exclusions: Cond[] = [];
  grouped = true;
  groupBy = 'RegistrationIp';
  windowMinutes: number | null = 60;
  aggregate = 'count';
  aggregateField = '';
  thresholdValue: number | null = 3;
  impactMode: 'none' | 'field' | 'flat' = 'none';
  impactField = '';
  impactPerMatch: number | null = null;

  // coded params
  params: any[] = [];
  paramValues: { [k: string]: any } = {};

  // step 3 — context
  name = '';
  category = 'Account';
  severity = 'High';
  description = '';
  rationale = '';
  playbook: string[] = [''];
  changeNote = '';

  // step 4 — dry run / submit
  test: any = null;
  testing = false;
  testError: string[] = [];
  submitting = false;
  submitErrors: string[] = [];
  done: any = null;

  constructor(private api: ApiService, private route: ActivatedRoute, private router: Router, public session: SessionService) {}

  ngOnInit(): void {
    this.api.catalog().subscribe(c => this.catalog = c);
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) { this.mode = 'new'; this.when = [this.blank()]; return; }
    this.ruleId = id;
    this.api.rule(id).subscribe(d => {
      const r = d.rule; this.existing = r;
      this.name = r.name; this.category = r.category; this.severity = r.severity; this.description = r.description;
      this.rationale = r.rationale || ''; this.playbook = r.playbook?.length ? [...r.playbook] : [''];
      if (r.kind === 'coded') {
        this.mode = 'coded';
        this.params = r.parameters || [];
        const cfg = this.safe(r.config);
        this.params.forEach(p => this.paramValues[p.key] = cfg[p.key] !== undefined
          ? (Array.isArray(cfg[p.key]) ? cfg[p.key].join(', ') : cfg[p.key])
          : (p.type === 'list' ? (p.defaultList || []).join(', ') : p.default));
      } else {
        this.mode = 'authored';
        this.loadLogic(r.logic);
        this.english = r.logic?.englishSource || '';
      }
    });
  }

  // ---------- catalog helpers ----------
  get targets(): any[] { return this.catalog?.targetInfo || []; }
  get targetInfo(): any { return this.targets.find(t => t.table === this.target); }
  get fields(): any[] { return (this.catalog?.fields || []).filter((f: any) => f.table === this.target); }
  field(name: string): any { return this.fields.find(f => f.field === name); }
  numericFields(): any[] { return this.fields.filter(f => f.type === 'money' || f.type === 'number' || f.type === 'int'); }
  blank(): Cond { return { field: '', op: 'eq', value: '', valuesText: '' }; }
  isList(c: Cond): boolean { return c.op === 'in' || c.op === 'notin'; }
  noValue(c: Cond): boolean { return c.op === 'isnull' || c.op === 'notnull'; }
  onTarget(): void {
    this.when = [this.blank()]; this.exclusions = [];
    this.groupBy = this.fields.find(f => /Ip|IPAddress/.test(f.field))?.field || ''; this.aggregateField = ''; this.impactField = '';
    this.test = null;
  }

  // ---------- AI drafting ----------
  draftWithAi(text?: string): void {
    if (text) { this.english = text; this.answers = {}; }
    if (!this.english.trim()) { return; }
    this.drafting = true;
    const answers: any = {};
    Object.keys(this.answers).forEach(q => { const k = this.answerKey(q); if (this.answers[q]?.trim()) { answers[k] = this.answers[q].trim(); } });
    this.api.draftRule(this.english, answers).subscribe(d => {
      this.draft = d; this.drafting = false;
      if (d.proposedRule) { this.loadLogic(d.proposedRule); this.test = null; }
    }, () => this.drafting = false);
  }
  /** The deterministic drafter names the answer key in the question ("answer as 'threshold'"); Claude's questions are answered verbatim. */
  answerKey(q: string): string { const m = /as '([A-Za-z]+)'/.exec(q); return m ? m[1] : q; }

  loadLogic(l: any): void {
    if (!l) { return; }
    this.target = l.target || 'USERS';
    const map = (c: any): Cond => ({ field: c.field, op: c.op, value: c.value, valuesText: (c.values || []).map((v: string) => v === '' ? "''" : v).join(', ') });
    this.when = (l.when || []).map(map); if (!this.when.length) { this.when = [this.blank()]; }
    this.exclusions = (l.exclusions || []).map(map);
    this.grouped = !!l.groupBy;
    this.groupBy = l.groupBy || '';
    this.windowMinutes = l.windowMinutes ?? null;
    this.aggregate = l.aggregate || 'count';
    this.aggregateField = l.aggregateField || '';
    this.thresholdValue = l.thresholdValue ?? null;
    this.impactMode = l.impactField ? 'field' : l.impactPerMatchUsd ? 'flat' : 'none';
    this.impactField = l.impactField || ''; this.impactPerMatch = l.impactPerMatchUsd ?? null;
    if (this.mode === 'new') {
      if (l.name) { this.name = l.name; }
      if (l.category) { this.category = l.category; }
      if (l.severity) { this.severity = l.severity; }
      if (l.description) { this.description = l.description; }
      if (l.playbook?.length) { this.playbook = [...l.playbook]; }
    }
  }

  // ---------- build payloads ----------
  logic(): any {
    const conv = (c: Cond) => ({
      field: c.field, op: c.op,
      value: this.isList(c) || this.noValue(c) ? null : (c.value ?? ''),
      values: this.isList(c) ? this.listValues(c.valuesText) : null,
    });
    return {
      ruleId: this.mode === 'authored' ? this.ruleId : '',
      name: this.name || 'Draft rule', category: this.category, severity: this.severity, target: this.target,
      when: this.when.filter(c => c.field).map(conv), exclusions: this.exclusions.filter(c => c.field).map(conv),
      groupBy: this.grouped ? this.groupBy || null : null,
      windowMinutes: this.grouped ? this.windowMinutes : null,
      aggregate: this.aggregate, aggregateField: this.aggregate === 'count' ? null : this.aggregateField || null,
      thresholdOp: 'gte', thresholdValue: this.grouped ? this.thresholdValue : null,
      impactField: this.impactMode === 'field' ? this.impactField || null : null,
      impactPerMatchUsd: this.impactMode === 'flat' ? this.impactPerMatch : null,
      confidence: 0.8, description: this.description, playbook: this.cleanPlaybook(), englishSource: this.english || this.existing?.logic?.englishSource || '',
    };
  }
  configJson(): string {
    const o: any = {};
    this.params.forEach(p => {
      const v = this.paramValues[p.key];
      if (p.type === 'list') { o[p.key] = String(v ?? '').split(',').map((s: string) => s.trim()).filter((s: string) => s.length); }
      else if (v !== '' && v != null) { o[p.key] = Number(v); }
    });
    return JSON.stringify(o);
  }
  /** "0, 0000, ''" → ['0', '0000', ''] — a literal '' means "empty value". */
  listValues(text: string): string[] {
    return text.split(',').map(s => s.trim()).filter(s => s.length > 0).map(s => s === "''" ? '' : s);
  }
  cleanPlaybook(): string[] { return this.playbook.map(s => s.trim()).filter(s => s.length); }
  sentence(): string { return describeLogic(this.logic()); }

  // ---------- dry run ----------
  runTest(): void {
    this.testing = true; this.testError = []; this.test = null;
    const body = this.mode === 'coded' ? { ruleId: this.ruleId, configJson: this.configJson() } : { logic: this.logic() };
    this.api.testRule(body).subscribe(t => { this.test = t; this.testing = false; },
      e => { this.testing = false; this.testError = e.error?.problems || [e.error?.error || 'Dry-run failed']; });
  }

  // ---------- readiness + submit ----------
  get checks(): { ok: boolean; label: string }[] {
    const logicOk = this.mode === 'coded' || (this.when.some(c => c.field) || this.grouped) && (!this.grouped || (!!this.groupBy && this.thresholdValue != null));
    return [
      { ok: logicOk, label: this.mode === 'coded' ? 'Parameters set' : 'Detection logic complete' },
      { ok: this.name.trim().length >= 3, label: 'Clear name' },
      { ok: this.description.trim().length >= 15, label: 'Expected behaviour described' },
      { ok: this.rationale.trim().length >= 15, label: 'Business reason given' },
      { ok: this.cleanPlaybook().length > 0, label: 'At least one response step' },
      { ok: !!this.test, label: 'Dry-run against live data' },
      { ok: this.changeNote.trim().length > 0, label: 'Change note for the reviewer' },
    ];
  }
  get ready(): boolean { return this.checks.every(c => c.ok); }

  submit(): void {
    this.submitting = true; this.submitErrors = [];
    const body: any = {
      ruleId: this.mode === 'new' ? '' : this.ruleId, name: this.name, category: this.category, severity: this.severity,
      description: this.description, rationale: this.rationale, playbook: this.cleanPlaybook(), changeNote: this.changeNote,
    };
    if (this.mode === 'coded') { body.configJson = this.configJson(); } else { body.logic = this.logic(); }
    this.api.proposeRule(body).subscribe(r => { this.submitting = false; this.done = r; },
      e => { this.submitting = false; this.submitErrors = e.error?.problems || [e.error?.error || 'Submission failed']; });
  }

  addStep(): void { this.playbook.push(''); }
  removeStep(i: number): void { this.playbook.splice(i, 1); if (!this.playbook.length) { this.playbook = ['']; } }
  trackIdx(i: number): number { return i; }
  private safe(s: string): any { try { return s ? JSON.parse(s) : {}; } catch { return {}; } }
}
