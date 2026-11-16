import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';

const base = environment.apiBase;
// Demo role for the workspace. A real deployment uses the existing .NET auth stack.
const lead = { headers: new HttpHeaders({ 'X-Role': 'Lead' }) };

@Injectable({ providedIn: 'root' })
export class ApiService {
  constructor(private http: HttpClient) {}

  cases(query = ''): Observable<any[]> { return this.http.get<any[]>(`${base}/api/cases${query}`); }
  case(id: number): Observable<any> { return this.http.get<any>(`${base}/api/cases/${id}`); }
  aiSummary(id: number) { return this.http.post<any>(`${base}/api/cases/${id}/ai-summary`, {}, lead); }
  assign(id: number, assignedTo: string) { return this.http.post<any>(`${base}/api/cases/${id}/assign`, { assignedTo }, lead); }
  escalate(id: number, notes: string) { return this.http.post<any>(`${base}/api/cases/${id}/escalate`, { notes }, lead); }
  resolve(id: number, notes: string) { return this.http.post<any>(`${base}/api/cases/${id}/resolve`, { notes }, lead); }

  rules(): Observable<any[]> { return this.http.get<any[]>(`${base}/api/rules`); }
  toggleRule(id: string, enabled: boolean) { return this.http.post<any>(`${base}/api/rules/${id}/toggle?enabled=${enabled}`, {}, lead); }
  catalog() { return this.http.get<any>(`${base}/api/rules/catalog`); }
  author(text: string, answers?: any) { return this.http.post<any>(`${base}/api/rules/author`, { text, answers }, lead); }
  confirmRule(rule: any) { return this.http.post<any>(`${base}/api/rules/author/confirm`, rule, lead); }

  activeSubs(top = 50, minutes = 15): Observable<any[]> { return this.http.get<any[]>(`${base}/api/dashboard/active-subscriptions?top=${top}&minutes=${minutes}`); }
  patterns() { return this.http.get<any>(`${base}/api/dashboard/patterns`); }
  alerts() { return this.http.get<any>(`${base}/api/alerts`); }
  usageSummary() { return this.http.get<any>(`${base}/api/usage/summary`); }

  simulate(scenario: string) { return this.http.post<any>(`${base}/api/demo/simulate/${scenario}`, {}, lead); }
  run() { return this.http.post<any>(`${base}/api/demo/run`, {}, lead); }
}
