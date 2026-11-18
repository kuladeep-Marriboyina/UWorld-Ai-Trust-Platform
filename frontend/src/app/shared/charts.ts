import { Component, ElementRef, EventEmitter, Input, OnChanges, Output } from '@angular/core';
import { utc } from '../core/format';

interface Tip { x: number; y: number; title: string; lines: string[]; }

/* =====================================================================
   Hourly traffic vs detections — one axis (counts), bars + line + markers
   ===================================================================== */
@Component({
  selector: 'app-traffic-chart',
  template: `
  <div class="legend mb8">
    <span><i style="background:var(--series-1)"></i>New accounts</span>
    <span><i style="background:var(--series-2);height:3px;border-radius:2px"></i>Failed logins</span>
    <span><i style="background:var(--critical);transform:rotate(45deg);width:8px;height:8px"></i>Cases raised</span>
  </div>
  <svg [attr.viewBox]="'0 0 ' + W + ' ' + H" class="chart" (mouseleave)="tip=null" role="img" aria-label="Hourly new accounts, failed logins and cases raised">
    <g *ngFor="let t of ticks">
      <line [attr.x1]="L" [attr.x2]="W - R" [attr.y1]="y(t)" [attr.y2]="y(t)" stroke="var(--grid)" />
      <text [attr.x]="L - 6" [attr.y]="y(t) + 3" text-anchor="end" class="ax">{{ t }}</text>
    </g>
    <g *ngFor="let b of data; let i = index">
      <rect [attr.x]="x(i) + 1" [attr.y]="y(b.signups)" [attr.width]="bw - 2" [attr.height]="Math.max(0, y(0) - y(b.signups))" rx="2" fill="var(--series-1)" opacity=".85" />
      <rect [attr.x]="x(i)" [attr.y]="T" [attr.width]="bw" [attr.height]="H - T - B" fill="transparent" (mousemove)="hover($event, b)" />
      <text *ngIf="i % labelEvery === 0" [attr.x]="x(i) + bw / 2" [attr.y]="H - 6" text-anchor="middle" class="ax">{{ hourLabel(b.hourUtc) }}</text>
      <path *ngIf="b.casesRaised > 0" [attr.d]="diamond(x(i) + bw / 2, T + 4)" fill="var(--critical)" stroke="var(--surface-1)" stroke-width="2" />
    </g>
    <path [attr.d]="line()" fill="none" stroke="var(--series-2)" stroke-width="2" stroke-linejoin="round" />
    <line [attr.x1]="L" [attr.x2]="W - R" [attr.y1]="y(0)" [attr.y2]="y(0)" stroke="var(--border-strong)" />
  </svg>
  <div class="tooltip-box" *ngIf="tip" [style.left.px]="tip.x + 14" [style.top.px]="tip.y - 10">
    <b>{{ tip.title }}</b><div *ngFor="let l of tip.lines">{{ l }}</div>
  </div>`,
  styles: [`.chart{width:100%;height:auto;display:block}.ax{fill:var(--text-3);font-size:10px;font-family:var(--sans)}`]
})
export class TrafficChartComponent implements OnChanges {
  @Input() data: any[] = [];
  Math = Math;
  W = 640; H = 190; L = 30; R = 8; T = 14; B = 22;
  max = 1; ticks: number[] = []; bw = 10; labelEvery = 3; tip: Tip | null = null;

  ngOnChanges(): void {
    const peak = Math.max(1, ...this.data.map(d => Math.max(d.signups, d.failedLogins)));
    const step = peak <= 4 ? 1 : peak <= 10 ? 2 : peak <= 25 ? 5 : peak <= 50 ? 10 : Math.ceil(peak / 50) * 10;
    this.max = Math.ceil(peak / step) * step;
    this.ticks = []; for (let t = 0; t <= this.max; t += step) { this.ticks.push(t); }
    this.bw = (this.W - this.L - this.R) / Math.max(1, this.data.length);
    this.labelEvery = Math.max(1, Math.ceil(this.data.length / 8));
  }
  x(i: number): number { return this.L + i * this.bw; }
  y(v: number): number { return this.T + 8 + (1 - v / this.max) * (this.H - this.T - this.B - 8); }
  line(): string { return this.data.map((d, i) => `${i ? 'L' : 'M'}${(this.x(i) + this.bw / 2).toFixed(1)},${this.y(d.failedLogins).toFixed(1)}`).join(' '); }
  diamond(cx: number, cy: number): string { return `M${cx},${cy - 5} L${cx + 5},${cy} L${cx},${cy + 5} L${cx - 5},${cy} Z`; }
  hourLabel(h: string): string { const d = utc(h)!; return d.getHours().toString().padStart(2, '0') + ':00'; }
  hover(e: MouseEvent, b: any): void {
    const d = utc(b.hourUtc)!;
    this.tip = { x: e.clientX, y: e.clientY, title: `${d.toLocaleDateString(undefined, { weekday: 'short' })} ${this.hourLabel(b.hourUtc)}`,
      lines: [`${b.signups} new accounts`, `${b.logins} logins · ${b.failedLogins} failed`, `${b.subscriptions} subscriptions`, `${b.casesRaised} case(s) raised`] };
  }
}

/* =====================================================================
   14-day case trend — stacked by outcome
   ===================================================================== */
@Component({
  selector: 'app-trend-chart',
  template: `
  <div class="legend mb8">
    <span><i style="background:var(--series-1)"></i>Confirmed fraud</span>
    <span><i style="background:var(--series-3)"></i>Dismissed (false positive / benign)</span>
    <span><i style="background:var(--text-3)"></i>Awaiting verdict</span>
  </div>
  <svg [attr.viewBox]="'0 0 ' + W + ' ' + H" class="chart" (mouseleave)="tip=null" role="img" aria-label="Cases per day for the last 14 days by outcome">
    <g *ngFor="let t of ticks">
      <line [attr.x1]="L" [attr.x2]="W - R" [attr.y1]="y(t)" [attr.y2]="y(t)" stroke="var(--grid)" />
      <text [attr.x]="L - 6" [attr.y]="y(t) + 3" text-anchor="end" class="ax">{{ t }}</text>
    </g>
    <g *ngFor="let d of data; let i = index">
      <ng-container *ngFor="let s of stack(d)">
        <rect *ngIf="s.v > 0" [attr.x]="x(i) + 3" [attr.y]="y(s.top)" [attr.width]="bw - 6" [attr.height]="Math.max(0, y(s.top - s.v) - y(s.top) - 2)" rx="3" [attr.fill]="s.c" />
      </ng-container>
      <rect [attr.x]="x(i)" [attr.y]="0" [attr.width]="bw" [attr.height]="H" fill="transparent" (mousemove)="hover($event, d)" />
      <text *ngIf="i % 2 === (data.length - 1) % 2" [attr.x]="x(i) + bw / 2" [attr.y]="H - 6" text-anchor="middle" class="ax">{{ dayLabel(d.day, i) }}</text>
    </g>
    <line [attr.x1]="L" [attr.x2]="W - R" [attr.y1]="y(0)" [attr.y2]="y(0)" stroke="var(--border-strong)" />
  </svg>
  <div class="tooltip-box" *ngIf="tip" [style.left.px]="tip.x + 14" [style.top.px]="tip.y - 10">
    <b>{{ tip.title }}</b><div *ngFor="let l of tip.lines">{{ l }}</div>
  </div>`,
  styles: [`.chart{width:100%;height:auto;display:block}.ax{fill:var(--text-3);font-size:10px;font-family:var(--sans)}`]
})
export class TrendChartComponent implements OnChanges {
  @Input() data: any[] = [];
  Math = Math;
  W = 640; H = 190; L = 26; R = 6; T = 10; B = 22;
  max = 1; ticks: number[] = []; bw = 10; tip: Tip | null = null;
  ngOnChanges(): void {
    const peak = Math.max(1, ...this.data.map(d => d.total));
    const step = peak <= 5 ? 1 : peak <= 12 ? 2 : 5;
    this.max = Math.ceil(peak / step) * step;
    this.ticks = []; for (let t = 0; t <= this.max; t += step) { this.ticks.push(t); }
    this.bw = (this.W - this.L - this.R) / Math.max(1, this.data.length);
  }
  stack(d: any): { v: number; top: number; c: string }[] {
    const pending = Math.max(0, d.total - d.confirmed - d.falsePositive);
    return [
      { v: d.confirmed, top: d.confirmed, c: 'var(--series-1)' },
      { v: d.falsePositive, top: d.confirmed + d.falsePositive, c: 'var(--series-3)' },
      { v: pending, top: d.total, c: 'var(--text-3)' },
    ];
  }
  x(i: number): number { return this.L + i * this.bw; }
  y(v: number): number { return this.T + (1 - v / this.max) * (this.H - this.T - this.B); }
  dayLabel(day: string, i: number): string { return i === this.data.length - 1 ? 'Today' : new Date(day).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }); }
  hover(e: MouseEvent, d: any): void {
    const cats = Object.keys(d.byCategory || {}).map(k => `${k}: ${d.byCategory[k]}`).join(' · ');
    this.tip = { x: e.clientX, y: e.clientY, title: new Date(d.day).toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' }),
      lines: [`${d.total} case(s) · $${Math.round(d.impactUsd).toLocaleString()} impact`, `${d.confirmed} confirmed · ${d.falsePositive} dismissed`, cats].filter(x => x) };
  }
}

/* =====================================================================
   Score gauge — 0-100 arc with severity bands and an alert threshold tick
   ===================================================================== */
@Component({
  selector: 'app-gauge',
  template: `
  <svg viewBox="0 0 200 118" class="g" role="img" [attr.aria-label]="'Risk score ' + value + ' of 100'">
    <path [attr.d]="arc(0, 100)" stroke="var(--surface-3)" stroke-width="14" fill="none" stroke-linecap="round" />
    <path *ngFor="let b of bands" [attr.d]="arc(b.from, b.to)" [attr.stroke]="b.c" stroke-width="3" fill="none" opacity=".55" transform="translate(0,0)" />
    <path [attr.d]="arc(0, Math.max(1, value))" [attr.stroke]="color()" stroke-width="14" fill="none" stroke-linecap="round" class="val" />
    <line *ngIf="threshold != null" [attr.x1]="pt(threshold, 60).x" [attr.y1]="pt(threshold, 60).y" [attr.x2]="pt(threshold, 92).x" [attr.y2]="pt(threshold, 92).y" stroke="var(--text-1)" stroke-width="2" />
    <text x="100" y="92" text-anchor="middle" class="v">{{ value | number:'1.0-0' }}</text>
    <text x="100" y="110" text-anchor="middle" class="s">{{ caption }}</text>
  </svg>`,
  styles: [`.g{width:100%;max-width:220px;display:block;margin:0 auto}.v{fill:var(--text-1);font:800 34px var(--sans)}.s{fill:var(--text-3);font:600 10.5px var(--sans);letter-spacing:.04em}.val{transition:all .6s}`]
})
export class GaugeComponent {
  @Input() value = 0;
  @Input() threshold: number | null = null;
  @Input() caption = 'RISK SCORE / 100';
  Math = Math;
  bands = [
    { from: 55, to: 70, c: 'var(--medium)' }, { from: 70, to: 85, c: 'var(--high)' }, { from: 85, to: 100, c: 'var(--critical)' },
  ];
  color(): string { return this.value >= 85 ? 'var(--critical)' : this.value >= 70 ? 'var(--high)' : this.value >= 55 ? 'var(--medium)' : 'var(--accent)'; }
  pt(v: number, r: number): { x: number; y: number } {
    const a = Math.PI * (1 - v / 100);
    return { x: 100 + r * Math.cos(a), y: 100 - r * Math.sin(a) };
  }
  arc(from: number, to: number): string {
    const a = this.pt(from, 80), b = this.pt(to, 80);
    return `M${a.x.toFixed(2)},${a.y.toFixed(2)} A80,80 0 0 1 ${b.x.toFixed(2)},${b.y.toFixed(2)}`;
  }
}

/* =====================================================================
   Entity link graph — accounts ↔ the identifiers that tie them together
   ===================================================================== */
interface GNode { id: string; type: string; label: string; sub?: string; flagged: boolean; x: number; y: number; vx: number; vy: number; deg: number; }

@Component({
  selector: 'app-link-graph',
  template: `
  <div class="legend mb8">
    <span *ngFor="let t of legendTypes"><i [style.background]="typeColor(t)" [style.borderRadius]="t === 'account' ? '50%' : '3px'"></i>{{ typeName(t) }}</span>
  </div>
  <svg [attr.viewBox]="'0 0 ' + W + ' ' + H" class="graph" (mouseleave)="focus=null; tip=null" role="img" aria-label="Accounts and the identifiers they share">
    <g>
      <line *ngFor="let e of edgesView" [attr.x1]="e.a.x" [attr.y1]="e.a.y" [attr.x2]="e.b.x" [attr.y2]="e.b.y"
            [attr.stroke]="dim(e) ? 'var(--grid)' : 'var(--border-strong)'" [attr.stroke-width]="dim(e) ? 1 : 1.6" />
    </g>
    <g *ngFor="let n of nodes" (mouseenter)="focus=n.id" (mousemove)="hover($event, n)" (click)="pick.emit(n.id)" class="node" [class.dim]="focus && !near(n)">
      <circle *ngIf="n.type === 'account'" [attr.cx]="n.x" [attr.cy]="n.y" r="11" [attr.fill]="typeColor(n.type)" stroke="var(--surface-1)" stroke-width="2.5" />
      <rect *ngIf="n.type !== 'account'" [attr.x]="n.x - size(n)" [attr.y]="n.y - size(n)" [attr.width]="size(n) * 2" [attr.height]="size(n) * 2"
            [attr.rx]="n.type === 'device' ? 6 : 3" [attr.fill]="typeColor(n.type)" stroke="var(--surface-1)" stroke-width="2.5"
            [attr.transform]="n.type === 'card' ? 'rotate(45 ' + n.x + ' ' + n.y + ')' : null" />
      <text [attr.x]="n.x" [attr.y]="n.y + 3.5" text-anchor="middle" class="glyph">{{ glyph(n.type) }}</text>
      <text [attr.x]="n.x" [attr.y]="n.y + (n.type === 'account' ? 25 : size(n) + 15)" text-anchor="middle" class="lbl">{{ short(n.label) }}</text>
    </g>
  </svg>
  <div class="tooltip-box" *ngIf="tip" [style.left.px]="tip.x + 14" [style.top.px]="tip.y - 10">
    <b>{{ tip.title }}</b><div *ngFor="let l of tip.lines">{{ l }}</div>
  </div>`,
  styles: [`.graph{width:100%;height:auto;display:block;cursor:default}.node{cursor:pointer;transition:opacity .15s}.node.dim{opacity:.22}
    .glyph{fill:#fff;font:700 10px var(--sans);pointer-events:none}.lbl{fill:var(--text-2);font:500 10.5px var(--mono);pointer-events:none}`]
})
export class LinkGraphComponent implements OnChanges {
  @Input() nodesIn: any[] = [];
  @Input() edgesIn: any[] = [];
  @Output() pick = new EventEmitter<string>();
  W = 720; H = 380;
  nodes: GNode[] = []; edgesView: { a: GNode; b: GNode; label: string }[] = [];
  focus: string | null = null; tip: Tip | null = null; legendTypes: string[] = [];

  constructor(private el: ElementRef) {}

  ngOnChanges(): void {
    const n = this.nodesIn.length;
    this.H = n > 14 ? 460 : 360;
    const byId: { [k: string]: GNode } = {};
    const accounts = this.nodesIn.filter(x => x.type === 'account' || x.type === 'email');
    const others = this.nodesIn.filter(x => !(x.type === 'account' || x.type === 'email'));
    // deterministic seed: identifiers in the middle, accounts around them
    accounts.forEach((x, i) => {
      const a = (i / Math.max(1, accounts.length)) * Math.PI * 2;
      byId[x.id] = { ...x, x: this.W / 2 + Math.cos(a) * 250, y: this.H / 2 + Math.sin(a) * (this.H / 2 - 40), vx: 0, vy: 0, deg: 0 };
    });
    others.forEach((x, i) => {
      const a = (i / Math.max(1, others.length)) * Math.PI * 2 + 0.4;
      byId[x.id] = { ...x, x: this.W / 2 + Math.cos(a) * 70, y: this.H / 2 + Math.sin(a) * 50, vx: 0, vy: 0, deg: 0 };
    });
    this.nodes = Object.keys(byId).map(k => byId[k]);
    this.edgesView = this.edgesIn.filter(e => byId[e.from] && byId[e.to]).map(e => ({ a: byId[e.from], b: byId[e.to], label: e.label }));
    this.edgesView.forEach(e => { e.a.deg++; e.b.deg++; });
    this.legendTypes = Array.from(new Set(this.nodes.map(x => x.type)));
    this.simulate();
  }

  /** A small force simulation: repulsion between all nodes, springs on edges, gentle pull to centre. */
  private simulate(): void {
    const N = this.nodes;
    for (let it = 0; it < 320; it++) {
      const k = 1 - it / 320;
      for (let i = 0; i < N.length; i++) {
        for (let j = i + 1; j < N.length; j++) {
          const a = N[i], b = N[j];
          let dx = a.x - b.x, dy = a.y - b.y; const d2 = Math.max(80, dx * dx + dy * dy);
          const f = 9000 / d2; const d = Math.sqrt(d2); dx /= d; dy /= d;
          a.vx += dx * f; a.vy += dy * f; b.vx -= dx * f; b.vy -= dy * f;
        }
      }
      for (const e of this.edgesView) {
        const dx = e.b.x - e.a.x, dy = e.b.y - e.a.y; const d = Math.max(1, Math.sqrt(dx * dx + dy * dy));
        const f = (d - 150) * 0.03;
        e.a.vx += dx / d * f; e.a.vy += dy / d * f; e.b.vx -= dx / d * f; e.b.vy -= dy / d * f;
      }
      for (const nd of N) {
        nd.vx += (this.W / 2 - nd.x) * 0.002; nd.vy += (this.H / 2 - nd.y) * 0.004;
        nd.x += nd.vx * k; nd.y += nd.vy * k; nd.vx *= 0.55; nd.vy *= 0.55;
        nd.x = Math.min(this.W - 50, Math.max(50, nd.x)); nd.y = Math.min(this.H - 30, Math.max(22, nd.y));
      }
    }
  }

  near(n: GNode): boolean {
    if (!this.focus || n.id === this.focus) { return true; }
    return this.edgesView.some(e => (e.a.id === this.focus && e.b.id === n.id) || (e.b.id === this.focus && e.a.id === n.id));
  }
  dim(e: { a: GNode; b: GNode }): boolean { return !!this.focus && e.a.id !== this.focus && e.b.id !== this.focus; }
  size(n: GNode): number { return Math.min(17, 10 + n.deg * 1.2); }
  typeColor(t: string): string {
    return ({ account: 'var(--series-1)', email: 'var(--text-3)', ip: 'var(--series-2)', device: 'var(--series-7)', card: 'var(--series-3)', pattern: 'var(--series-5)', domain: 'var(--series-4)' } as any)[t] || 'var(--text-3)';
  }
  typeName(t: string): string {
    return ({ account: 'Account', email: 'Targeted email', ip: 'IP address', device: 'Device', card: 'Payment card', pattern: 'Email template', domain: 'Email domain' } as any)[t] || t;
  }
  glyph(t: string): string { return ({ account: '', email: '@', ip: 'IP', device: 'D', card: '$', pattern: '#', domain: '@' } as any)[t] || ''; }
  short(s: string): string { return s.length > 22 ? s.slice(0, 20) + '…' : s; }
  hover(e: MouseEvent, n: GNode): void {
    const links = this.edgesView.filter(x => x.a.id === n.id || x.b.id === n.id);
    this.tip = { x: e.clientX, y: e.clientY, title: `${this.typeName(n.type)} · ${n.label}`,
      lines: [n.sub || '', `${links.length} connection(s)`, ...links.slice(0, 4).map(l => `${l.label} ${l.a.id === n.id ? l.b.label : l.a.label}`)].filter(x => x) };
  }
}
