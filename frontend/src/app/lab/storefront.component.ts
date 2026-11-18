import { Component, OnDestroy, OnInit } from '@angular/core';
import { ApiService, SimIdentity } from '../core/api.service';
import { BOT_UA, newDevice, randomIp, realPerson } from './simulation';
import { Director, STORE_SCENARIOS, StoreScenario } from './store-scenarios';

type View = 'signup' | 'plan' | 'done' | 'login' | 'study';
class Stopped extends Error {}

/**
 * The simulated customer-facing store. Open it in several windows:
 *  - by default every window shares one "machine" (same IP + device, stored in localStorage) — like tabs on one laptop;
 *  - "Be someone else" gives just this window a fresh IP + device (sessionStorage) — a different real person.
 * "Demo scenarios" act a story out on screen — typing, clicking, switching identities — against the real API.
 * The presenter panel shows what the trust engine concluded after each action; a real customer never sees it.
 */
@Component({
  selector: 'app-storefront',
  templateUrl: './storefront.component.html',
  styleUrls: ['./storefront.component.css']
})
export class StorefrontComponent implements OnInit, OnDestroy {
  view: View = 'signup';
  packages: any[] = [];
  form = { firstName: '', lastName: '', email: '', password: 'Study2026!', country: 'US' };
  loginForm = { email: '', password: '' };
  user: any = null;
  plan = { packageId: 11, trial: true, cardNumber: '4242 4242 4242 4242', discountCode: '' };
  purchase: any = null;
  study = { packageId: 11, questionId: 1201 };
  contentResult: any = null;
  busy = false;
  error = '';
  identity!: SimIdentity;
  isOverride = false;
  bot = false;
  presenter = true;
  stripOpen = true;
  traces: { action: string; at: Date; d: any; status: number; note?: string }[] = [];
  windowName = '';
  logoOk = true;      // falls back to the text wordmark until assets/uworld-logo.svg is added
  Math = Math;

  // ---- autopilot ----
  scenarios = STORE_SCENARIOS;
  attacks = STORE_SCENARIOS.filter(s => s.kind === 'attack');
  legit = STORE_SCENARIOS.filter(s => s.kind === 'legit');
  picker = false;
  picked: StoreScenario | null = null;
  params: { [k: string]: number } = {};
  auto: { s: StoreScenario; caption: string; detail?: string; who: string; done: number; total: number; unit: string; finished: boolean; cases: any[]; stopped: boolean } | null = null;
  paused = false;
  speed: 'slow' | 'normal' | 'fast' = 'normal';
  focus = '';           // which control the autopilot is touching (for the glow)
  private stopFlag = false;
  private savedIdentity: SimIdentity | null = null;

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.loadIdentity();
    this.api.packages().subscribe(p => this.packages = p, () => this.error = 'Store is offline — is the API running?');
    try { this.presenter = localStorage.getItem('sf.presenter') !== '0'; } catch {}
    this.windowName = 'Window ' + (Math.floor(Math.random() * 900) + 100);
  }
  ngOnDestroy(): void { this.stopFlag = true; }

  // ---------- identity ----------
  private loadIdentity(): void {
    try {
      const o = sessionStorage.getItem('sf.override');
      if (o) { this.identity = JSON.parse(o); this.isOverride = true; return; }
      let m = localStorage.getItem('sf.machine');
      if (!m) { m = JSON.stringify({ ip: randomIp('203.0.113'), device: newDevice(), country: 'US' }); localStorage.setItem('sf.machine', m); }
      this.identity = JSON.parse(m); this.isOverride = false;
    } catch { this.identity = { ip: randomIp('203.0.113'), device: newDevice(), country: 'US' }; }
  }
  beSomeoneElse(): void {
    const id = { ip: randomIp(), device: newDevice(), country: this.identity.country };
    sessionStorage.setItem('sf.override', JSON.stringify(id)); this.loadIdentity();
  }
  backToShared(): void { sessionStorage.removeItem('sf.override'); this.loadIdentity(); }
  newSharedMachine(): void { localStorage.removeItem('sf.machine'); sessionStorage.removeItem('sf.override'); this.loadIdentity(); }
  setCountry(c: string): void {
    this.identity = { ...this.identity, country: c };
    if (this.auto) { return; }
    if (this.isOverride) { sessionStorage.setItem('sf.override', JSON.stringify(this.identity)); } else { localStorage.setItem('sf.machine', JSON.stringify(this.identity)); }
  }
  get id(): SimIdentity { return { ...this.identity, ua: this.bot ? BOT_UA : this.identity.ua }; }
  togglePresenter(): void { this.presenter = !this.presenter; try { localStorage.setItem('sf.presenter', this.presenter ? '1' : '0'); } catch {} }

  // ---------- autofill ----------
  fillReal(): void { const p = realPerson(); this.form = { ...this.form, ...p }; }
  fillPattern(): void {
    let n = 1;
    try { n = +(localStorage.getItem('sf.seq') || '0') + 1; localStorage.setItem('sf.seq', String(n)); } catch {}
    this.form = { ...this.form, firstName: 'Jake', lastName: 'Miller', email: `jake.miller${String(n).padStart(2, '0')}@gmail.com` };
  }

  // ---------- customer actions (buttons call these; the autopilot awaits them) ----------
  signup(): Promise<any> {
    this.busy = true; this.error = '';
    return new Promise(res => this.api.sfSignup(this.form, this.id).subscribe(r => {
      this.busy = false; this.user = r; this.view = 'plan';
      this.trace('Signed up ' + r.email, r.detection, 201); res(r);
    }, e => {
      this.busy = false; this.error = e.error?.error || 'Signup failed';
      if (e.error?.detection) { this.trace('Signup rejected', e.error.detection, e.status); } res(null);
    }));
  }

  login(): Promise<any> {
    this.busy = true; this.error = '';
    return new Promise(res => this.api.sfLogin(this.loginForm, this.id).subscribe(r => {
      this.busy = false; this.user = r; this.view = 'study';
      this.trace('Logged in ' + r.email, r.detection, 200); res(r);
    }, e => {
      this.busy = false; this.error = e.error?.error || 'Login failed';
      if (e.error?.detection) { this.trace('Failed login ' + this.loginForm.email, e.error.detection, e.status); } res(null);
    }));
  }

  subscribe(): Promise<any> {
    this.busy = true; this.error = '';
    const body = { userId: this.user.userId, packageId: +this.plan.packageId, trial: this.plan.trial, cardNumber: this.plan.trial ? null : this.plan.cardNumber, discountCode: this.plan.trial ? null : this.plan.discountCode || null };
    return new Promise(res => this.api.sfSubscribe(body, this.id).subscribe(r => {
      this.busy = false; this.purchase = r.subscription; this.view = 'done';
      const s = r.subscription;
      this.trace(this.plan.trial ? `Started ${s.package} trial` : `Paid $${s.charged} for ${s.package}`, r.detection, 201,
        s.codeApplied ? `code ${s.codeApplied} −$${s.discount}` : s.codeRejectedReason ? `code rejected: ${s.codeRejectedReason}` : undefined);
      res(r);
    }, e => { this.busy = false; this.error = e.error?.error || 'Checkout failed'; res(null); }));
  }

  openContent(channel: 'app' | 'export' | 'cache', quiet = false): Promise<any> {
    if (!this.user) { this.error = 'Log in first.'; return Promise.resolve(null); }
    this.busy = true; this.error = '';
    const body = { userId: this.user.userId, packageId: +this.study.packageId, questionId: +this.study.questionId, channel, quiet };
    return new Promise(res => this.api.sfContent(body, this.id).subscribe(r => this.contentDone(r, 200, res), e => this.contentDone(e.error, e.status, res)));
  }
  private contentDone(r: any, status: number, res: (v: any) => void): void {
    this.busy = false; this.contentResult = { ...r, status };
    if (r?.detection) {
      const what = r.channel === 'export' ? 'PDF export' : r.channel === 'cache' ? 'offline cache' : `question ${this.study.questionId}`;
      this.trace(`${status === 403 ? 'Denied' : 'Opened'} ${what}`, r.detection, status,
        `authorization ${r.authorization} · ${r.bytesReturned.toLocaleString()} bytes returned`);
    }
    res(r);
  }

  another(): void {
    this.user = null; this.purchase = null; this.view = 'signup'; this.contentResult = null;
    this.form = { firstName: '', lastName: '', email: '', password: 'Study2026!', country: this.identity.country };
  }

  private trace(action: string, d: any, status: number, note?: string): void {
    this.traces.unshift({ action, at: new Date(), d, status, note }); this.traces = this.traces.slice(0, 14);
    (d?.cases || []).forEach((c: any) => {
      if (!this.auto) { return; }
      const ex = this.auto.cases.find(x => x.caseId === c.caseId);
      if (ex) { Object.assign(ex, c); } else { this.auto.cases.push(c); }
    });
  }

  get pkg(): any { return this.packages.find(p => p.packageId === +this.plan.packageId); }
  pkgName(id: number): string { return this.packages.find(p => p.packageId === +id)?.name || 'package ' + id; }
  openConsole(caseId: number): void { window.open('/cases/' + caseId, 'sentinel-console'); }
  verdictText(v: string): string { return ({ clean: 'Clean — nothing suspicious', watching: 'Watching — risk building, below alert line', alert: 'ALERT — case raised / updated' } as any)[v] || v; }

  // =====================================================================
  // Autopilot — Director implementation (every step is visible on screen)
  // =====================================================================
  openPicker(): void { this.picker = true; if (!this.picked) { this.pick(this.scenarios[0]); } }
  pick(s: StoreScenario): void { this.picked = s; this.params = {}; s.params.forEach(p => this.params[p.key] = p.value); }

  async runScenario(): Promise<void> {
    const s = this.picked; if (!s || this.auto) { return; }
    s.params.forEach(p => this.params[p.key] = Math.max(p.min, Math.min(p.max, Math.round(+this.params[p.key] || p.value))));
    this.picker = false; this.stopFlag = false; this.paused = false;
    this.savedIdentity = this.identity; this.bot = false; this.traces = [];
    this.auto = { s, caption: 'Starting…', who: '', done: 0, total: 0, unit: '', finished: false, cases: [], stopped: false };
    this.presenter = true;
    const d: Director = {
      say: (c, dt) => this.say(c, dt), become: (i, w) => this.become(i, w), progress: (a, b, u) => this.progress(a, b, u),
      signup: p => this.signupDirect(p), startTrial: pk => this.startTrial(pk), buy: (pk, c, code) => this.buy(pk, c, code),
      login: (e, pw) => this.loginStep(e, pw), openContent: (pk, q, ch, quiet) => this.contentStep(pk, q, ch, quiet),
    };
    try { await s.run(d, { ...this.params }); await this.say('Scenario finished', s.expect); }
    catch (e) { if (!(e instanceof Stopped)) { this.auto.caption = 'Scenario hit an error'; this.auto.detail = String(e); } else { this.auto.stopped = true; } }
    this.auto.finished = true; this.focus = '';
  }
  stop(): void { this.stopFlag = true; this.paused = false; }
  closeAuto(): void {
    this.auto = null; this.focus = '';
    if (this.savedIdentity) { this.identity = this.savedIdentity; this.savedIdentity = null; }
    this.loadIdentity(); this.another();
  }

  private get pace(): number { return this.speed === 'slow' ? 1.8 : this.speed === 'fast' ? 0.25 : 1; }
  private async wait(ms: number): Promise<void> {
    let left = ms * this.pace;
    while (left > 0 || this.paused) {
      if (this.stopFlag) { throw new Stopped(); }
      await new Promise(r => setTimeout(r, 50)); if (!this.paused) { left -= 50; }
    }
    if (this.stopFlag) { throw new Stopped(); }
  }
  private async type(target: any, field: string, value: string, focusKey: string): Promise<void> {
    this.focus = focusKey; target[field] = '';
    const per = this.speed === 'fast' ? 0 : this.speed === 'slow' ? 45 : 22;
    if (!per) { target[field] = value; await this.wait(40); return; }
    for (const ch of value) { target[field] += ch; await new Promise(r => setTimeout(r, per)); if (this.stopFlag) { throw new Stopped(); } }
    await this.wait(120);
  }
  private async press(focusKey: string, action: () => Promise<any>): Promise<any> {
    this.focus = focusKey; await this.wait(350);
    const r = await action(); this.focus = ''; await this.wait(700);
    return r;
  }

  async say(caption: string, detail?: string): Promise<void> { if (this.auto) { this.auto.caption = caption; this.auto.detail = detail; } await this.wait(900); }
  async become(identity: SimIdentity, who: string): Promise<void> {
    this.identity = { ip: identity.ip, device: identity.device, country: identity.country, ua: identity.ua };
    this.bot = !!identity.ua; this.isOverride = true;
    if (this.auto) { this.auto.who = who; }
    this.focus = 'identity'; await this.wait(700); this.focus = '';
  }
  progress(done: number, total: number, unit: string): void { if (this.auto) { this.auto.done = done; this.auto.total = total; this.auto.unit = unit; } }

  async signupDirect(p: { firstName: string; lastName: string; email: string; password?: string }): Promise<any> {
    this.user = null; this.view = 'signup'; this.error = '';
    this.form = { firstName: '', lastName: '', email: '', password: '', country: this.identity.country };
    await this.wait(200);
    await this.type(this.form, 'firstName', p.firstName, 'fn');
    await this.type(this.form, 'lastName', p.lastName, 'ln');
    await this.type(this.form, 'email', p.email, 'em');
    await this.type(this.form, 'password', p.password || 'Study2026!', 'pw');
    return this.press('create', () => this.signup());
  }

  async startTrial(packageId: number): Promise<any> {
    if (!this.user) { return null; }
    this.view = 'plan'; await this.wait(250);
    this.focus = 'pkg' + packageId; this.plan.packageId = packageId; await this.wait(400);
    this.focus = 'trial'; this.plan.trial = true; await this.wait(300);
    return this.press('pay', () => this.subscribe());
  }
  async buy(packageId: number, card: string, code?: string): Promise<any> {
    if (!this.user) { return null; }
    this.view = 'plan'; await this.wait(250);
    this.focus = 'pkg' + packageId; this.plan.packageId = packageId; await this.wait(400);
    this.focus = 'buy'; this.plan.trial = false; await this.wait(300);
    await this.type(this.plan, 'cardNumber', card, 'card');
    await this.type(this.plan, 'discountCode', code || '', 'code');
    return this.press('pay', () => this.subscribe());
  }
  async loginStep(email: string, password: string): Promise<any> {
    this.user = null; this.view = 'login'; this.error = ''; this.loginForm = { email: '', password: '' };
    await this.wait(150);
    await this.type(this.loginForm, 'email', email, 'lem');
    await this.type(this.loginForm, 'password', password, 'lpw');
    return this.press('login', () => this.login());
  }
  async contentStep(packageId: number, questionId: number, channel: 'app' | 'export' | 'cache', quiet = false): Promise<any> {
    this.view = 'study'; this.study = { packageId, questionId: questionId || 1 };
    if (quiet) { const r = await this.openContent(channel, true); await this.wait(60); return r; }
    await this.wait(300);
    return this.press(channel, () => this.openContent(channel));
  }
}

