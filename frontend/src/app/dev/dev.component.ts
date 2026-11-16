import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../api.service';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-dev',
  templateUrl: './dev.component.html'
})
export class DevComponent implements OnInit, OnDestroy {
  rows: any[] = [];
  status = 'all';
  search = '';
  autoRefresh = true;
  loading = false;
  error = '';

  selected: any = null;
  investigation: any = null;
  investigating = false;
  copied = '';

  readonly filters = [
    { key: 'all', label: 'All' },
    { key: 'errors', label: 'Errors' },
    { key: '2xx', label: '2xx' },
    { key: '4xx', label: '4xx' },
    { key: '5xx', label: '5xx' }
  ];

  private timer: any;

  constructor(private api: ApiService, private route: ActivatedRoute) {}

  ngOnInit(): void {
    this.load();
    // deep link: /dev?id=42 opens that request; &investigate=1 also runs Find the bug
    const q = this.route.snapshot.queryParamMap;
    const id = +(q.get('id') || 0);
    if (id) { this.select({ id }, q.get('investigate') === '1'); }
    this.timer = setInterval(() => { if (this.autoRefresh) { this.load(); } }, 3000);
  }

  ngOnDestroy(): void { clearInterval(this.timer); }

  load(): void {
    this.loading = true;
    this.api.devRequests(this.status, this.search).subscribe(
      r => { this.rows = r; this.loading = false; this.error = ''; },
      () => { this.loading = false; this.error = 'Could not load the request log — is the API running in Development on :5080?'; }
    );
  }

  setStatus(s: string): void { this.status = s; this.load(); }

  select(row: any, investigate = false): void {
    this.investigation = null;
    this.api.devRequest(row.id).subscribe(r => { this.selected = r; if (investigate) { this.findBug(); } });
  }

  findBug(): void {
    if (!this.selected) { return; }
    this.investigating = true;
    this.api.investigate(this.selected.id).subscribe(
      r => { this.investigation = r; this.investigating = false; },
      () => { this.investigating = false; this.error = 'Investigation failed.'; }
    );
  }

  clear(): void {
    if (!confirm('Clear every captured request?')) { return; }
    this.api.clearDevRequests().subscribe(() => { this.selected = null; this.investigation = null; this.load(); });
  }

  // ---- display helpers

  statusClass(code: number): string {
    return code >= 500 ? 's5' : code >= 400 ? 's4' : code >= 300 ? 's3' : 's2';
  }

  pretty(text: string | null): string {
    if (!text) { return ''; }
    try { return JSON.stringify(JSON.parse(text), null, 2); } catch { return text; }
  }

  headers(json: string | null): { k: string, v: string }[] {
    if (!json) { return []; }
    try { const o = JSON.parse(json); return Object.keys(o).map(k => ({ k, v: o[k] })); } catch { return []; }
  }

  shortController(c: string | null): string { return c ? c.split('.').pop() as string : ''; }

  lineNo(snippet: any, i: number): number { return snippet.startLine + i; }

  copy(what: string): void {
    const s = this.selected;
    if (!s) { return; }
    let text = '';
    if (what === 'request') { text = this.pretty(s.requestBody) || '(empty)'; }
    if (what === 'response') { text = this.pretty(s.responseBody) || '(empty)'; }
    if (what === 'curl') { text = this.curl(s); }
    if (what === 'stack') { text = s.stackTrace || ''; }
    navigator.clipboard.writeText(text).then(() => { this.copied = what; setTimeout(() => this.copied = '', 1500); });
  }

  private curl(s: any): string {
    const parts = [`curl -X ${s.method} "${environment.apiBase}${s.path}${s.queryString || ''}"`];
    this.headers(s.requestHeaders)
      .filter(h => ['X-Role', 'Content-Type'].indexOf(h.k) >= 0)
      .forEach(h => parts.push(`-H "${h.k}: ${h.v}"`));
    if (s.requestBody) { parts.push(`-d '${s.requestBody.replace(/'/g, `'\\''`)}'`); }
    return parts.join(' \\\n  ');
  }
}
