import { Injectable } from '@angular/core';
import { HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { BehaviorSubject, Observable } from 'rxjs';
import { ApiService } from './api.service';

export interface Persona { username: string; displayName: string; role: string; title: string; initials: string; permissions: string[]; }

const FALLBACK: Persona[] = [
  { username: 'maya.chen', displayName: 'Maya Chen', role: 'Admin', title: 'Trust & Safety Lead', initials: 'MC', permissions: ['view', 'work-cases', 'simulate', 'propose-rules', 'approve-rules', 'toggle-rules', 'reset-demo'] },
  { username: 'jordan.patel', displayName: 'Jordan Patel', role: 'RuleAuthor', title: 'Senior Fraud Analyst', initials: 'JP', permissions: ['view', 'work-cases', 'simulate', 'propose-rules'] },
  { username: 'sam.ortiz', displayName: 'Sam Ortiz', role: 'Analyst', title: 'Fraud Analyst', initials: 'SO', permissions: ['view', 'work-cases', 'simulate'] },
  { username: 'riley.kim', displayName: 'Riley Kim', role: 'Viewer', title: 'Finance Partner', initials: 'RK', permissions: ['view'] },
];

/** Read by the interceptor (kept outside DI to avoid a SessionService ↔ HttpClient cycle). */
let currentUsername = "maya.chen";

/** The acting persona. A real deployment takes this from SSO; the demo lets you switch to show role-gated behaviour. */
@Injectable({ providedIn: 'root' })
export class SessionService {
  people: Persona[] = FALLBACK;
  readonly user$ = new BehaviorSubject<Persona>(this.load());
  private sync = this.user$.subscribe(p => currentUsername = p.username);

  constructor(private api: ApiService) {
    this.api.people().subscribe(p => {
      this.people = p;
      const cur = p.find(x => x.username === this.user.username);
      if (cur) { this.user$.next(cur); }
    }, () => {});
  }

  get user(): Persona { return this.user$.value; }
  can(permission: string): boolean { return this.user.permissions.indexOf(permission) >= 0; }

  switchTo(username: string): void {
    const p = this.people.find(x => x.username === username);
    if (!p) { return; }
    try { localStorage.setItem('sentinel.user', username); } catch {}
    this.user$.next(p);
  }

  private load(): Persona {
    let u = 'maya.chen';
    try { u = localStorage.getItem('sentinel.user') || u; } catch {}
    return FALLBACK.find(p => p.username === u) || FALLBACK[0];
  }
}

@Injectable()
export class UserHeaderInterceptor implements HttpInterceptor {
  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    return next.handle(req.clone({ setHeaders: { 'X-User': currentUsername } }));
  }
}
