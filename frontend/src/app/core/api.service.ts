import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

/** API base: environment default, overridable at runtime (localStorage 'sentinel.api' or ?api=) for side-by-side demos. */
export function apiBase(): string {
  try {
    const q = new URLSearchParams(location.search).get('api');
    if (q) { localStorage.setItem('sentinel.api', q); }
    return (localStorage.getItem('sentinel.api') || environment.apiBase).replace(/\/$/, '');
  } catch { return environment.apiBase; }
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  readonly base = apiBase();
  constructor(private http: HttpClient) {}

  private get<T>(path: string): Observable<T> { return this.http.get<T>(this.base + path); }
  private post<T>(path: string, body: any = {}): Observable<T> { return this.http.post<T>(this.base + path, body); }

  // identity
  me(): Observable<any> { return this.get('/api/me'); }
  people(): Observable<any[]> { return this.get('/api/people'); }
  aiStatus(): Observable<any> { return this.get('/api/ai/status'); }

  // dashboard
  overview(hours = 24): Observable<any> { return this.get(`/api/dashboard/overview?hours=${hours}`); }
  briefing(): Observable<any> { return this.post('/api/dashboard/briefing'); }
  activeSubs(): Observable<any[]> { return this.get('/api/dashboard/active-subscriptions?top=12&minutes=120'); }

  // cases
  cases(params: { [k: string]: any } = {}): Observable<any[]> {
    const q = Object.keys(params).filter(k => params[k] !== '' && params[k] != null).map(k => `${k}=${encodeURIComponent(params[k])}`).join('&');
    return this.get('/api/cases' + (q ? '?' + q : ''));
  }
  case(id: number): Observable<any> { return this.get(`/api/cases/${id}`); }
  investigation(id: number): Observable<any> { return this.get(`/api/cases/${id}/investigation`); }
  cachedSummary(id: number): Observable<any> { return this.get(`/api/cases/${id}/ai-summary`); }
  aiSummary(id: number): Observable<any> { return this.post(`/api/cases/${id}/ai-summary`); }
  ask(id: number, question: string): Observable<any> { return this.post(`/api/cases/${id}/ask`, { question }); }
  acknowledge(id: number): Observable<any> { return this.post(`/api/cases/${id}/acknowledge`); }
  assign(id: number, assignedTo: string | null): Observable<any> { return this.post(`/api/cases/${id}/assign`, { assignedTo }); }
  escalate(id: number, notes: string): Observable<any> { return this.post(`/api/cases/${id}/escalate`, { notes }); }
  comment(id: number, notes: string): Observable<any> { return this.post(`/api/cases/${id}/comment`, { notes }); }
  action(id: number, notes: string): Observable<any> { return this.post(`/api/cases/${id}/action`, { notes }); }
  disposition(id: number, disposition: string, notes: string, resolve: boolean): Observable<any> {
    return this.post(`/api/cases/${id}/disposition`, { disposition, notes, resolve });
  }
  reopen(id: number, notes: string): Observable<any> { return this.post(`/api/cases/${id}/reopen`, { notes }); }
  report(id: number, redact: boolean, link: string): Observable<any> {
    return this.get(`/api/cases/${id}/report?redact=${redact}&link=${encodeURIComponent(link)}`);
  }

  // rules
  rules(): Observable<any[]> { return this.get('/api/rules'); }
  rule(id: string): Observable<any> { return this.get(`/api/rules/${encodeURIComponent(id)}`); }
  catalog(): Observable<any> { return this.get('/api/rules/catalog'); }
  pending(): Observable<any[]> { return this.get('/api/rules/pending'); }
  draftRule(text: string, answers?: any): Observable<any> { return this.post('/api/rules/draft', { text, answers }); }
  testRule(body: any): Observable<any> { return this.post('/api/rules/test', body); }
  proposeRule(body: any): Observable<any> { return this.post('/api/rules/propose', body); }
  approve(id: string, v: number, notes: string): Observable<any> { return this.post(`/api/rules/${encodeURIComponent(id)}/versions/${v}/approve`, { notes }); }
  reject(id: string, v: number, notes: string): Observable<any> { return this.post(`/api/rules/${encodeURIComponent(id)}/versions/${v}/reject`, { notes }); }
  toggle(id: string, enabled: boolean, notes: string): Observable<any> { return this.post(`/api/rules/${encodeURIComponent(id)}/toggle`, { enabled, notes }); }

  // simulation
  scenarios(): Observable<any[]> { return this.get('/api/demo/scenarios'); }
  simulate(key: string): Observable<any> { return this.post(`/api/demo/simulate/${key}`); }
  resetDemo(): Observable<any> { return this.post('/api/demo/reset'); }
  liveRecent(take = 60): Observable<any[]> { return this.get(`/api/live/recent?take=${take}`); }

  // storefront (customer-facing; identity travels in X-Sim-* headers)
  packages(): Observable<any[]> { return this.get('/api/storefront/packages'); }
  sfSignup(body: any, id: SimIdentity): Observable<any> { return this.http.post(this.base + '/api/storefront/signup', body, { headers: simHeaders(id) }); }
  sfLogin(body: any, id: SimIdentity): Observable<any> { return this.http.post(this.base + '/api/storefront/login', body, { headers: simHeaders(id) }); }
  sfContent(body: any, id: SimIdentity): Observable<any> { return this.http.post(this.base + '/api/storefront/content', body, { headers: simHeaders(id) }); }
  sfSubscribe(body: any, id: SimIdentity): Observable<any> { return this.http.post(this.base + '/api/storefront/subscribe', body, { headers: simHeaders(id) }); }
}

export interface SimIdentity { ip: string; device: string; country: string; ua?: string; }

export function simHeaders(id: SimIdentity): HttpHeaders {
  let h = new HttpHeaders({ 'X-Sim-Ip': id.ip, 'X-Sim-Device': id.device, 'X-Sim-Country': id.country || 'US' });
  if (id.ua) { h = h.set('X-Sim-UA', id.ua); }
  return h;
}
