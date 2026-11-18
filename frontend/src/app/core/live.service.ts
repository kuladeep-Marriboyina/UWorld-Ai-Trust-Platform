import { Injectable, NgZone } from '@angular/core';
import { BehaviorSubject, Subject } from 'rxjs';
import { ApiService } from './api.service';

export interface LiveEvent {
  id: number; type: string; level: string; title: string; detail?: string; utc: string;
  caseId?: number; ruleId?: string; ip?: string; score?: number; data?: any;
}

export interface Toast { id: number; level: string; title: string; detail?: string; link?: any[]; icon: string; ttl: number; }

/** One Server-Sent-Events connection for the whole app: customer activity + detection outcomes as they happen. */
@Injectable({ providedIn: 'root' })
export class LiveService {
  readonly events$ = new Subject<LiveEvent>();
  readonly connected$ = new BehaviorSubject<boolean>(false);
  readonly toasts$ = new BehaviorSubject<Toast[]>([]);
  recent: LiveEvent[] = [];
  sound = this.loadSound();
  private es?: EventSource;
  private seen = new Set<number>();
  private toastSeq = 0;
  private audio?: AudioContext;

  constructor(private api: ApiService, private zone: NgZone) {}

  start(): void {
    if (this.es) { return; }
    this.api.liveRecent(60).subscribe(r => {
      r.forEach(e => this.seen.add(e.id));
      this.recent = r;
    }, () => {});
    this.connect();
  }

  private connect(): void {
    this.es = new EventSource(this.api.base + '/api/live/stream');
    this.es.onopen = () => this.zone.run(() => this.connected$.next(true));
    this.es.onerror = () => this.zone.run(() => this.connected$.next(false));
    this.es.onmessage = (m: MessageEvent) => this.zone.run(() => {
      const e = JSON.parse(m.data) as LiveEvent;
      if (this.seen.has(e.id)) { return; }
      this.seen.add(e.id);
      this.recent = [e, ...this.recent].slice(0, 120);
      this.events$.next(e);
      this.maybeToast(e);
    });
  }

  private maybeToast(e: LiveEvent): void {
    if (e.type === 'case-created' || e.type === 'case-reopened') {
      this.push({ level: e.level, title: (e.type === 'case-reopened' ? 'Reopened · ' : 'New case · ') + e.title, detail: e.detail, link: ['/cases', e.caseId], icon: '🚨', ttl: e.level === 'critical' ? 16000 : 11000 });
      this.chime(e.level);
    } else if (e.type === 'case-updated' && /→/.test(e.detail || '')) {
      // only escalations interrupt the analyst; quieter growth goes to the feed and the case log
      this.push({ level: e.level, title: e.title, detail: e.detail, link: ['/cases', e.caseId], icon: '📈', ttl: 8000 });
    } else if (e.type === 'rule') {
      this.push({ level: 'info', title: e.title, detail: e.detail, link: ['/rules'], icon: '⚖', ttl: 7000 });
    }
  }

  push(t: Omit<Toast, 'id'>): void {
    const toast = { ...t, id: ++this.toastSeq };
    this.toasts$.next([...this.toasts$.value, toast].slice(-4));
    setTimeout(() => this.dismiss(toast.id), t.ttl);
  }

  dismiss(id: number): void { this.toasts$.next(this.toasts$.value.filter(t => t.id !== id)); }

  toggleSound(): void {
    this.sound = !this.sound;
    try { localStorage.setItem('sentinel.sound', this.sound ? '1' : '0'); } catch {}
    if (this.sound) { this.chime('high'); }
  }

  private loadSound(): boolean { try { return localStorage.getItem('sentinel.sound') !== '0'; } catch { return true; } }

  /** Two short tones for an alert; critical gets a third, higher one. */
  private chime(level: string): void {
    if (!this.sound || (level !== 'critical' && level !== 'high')) { return; }
    try {
      this.audio = this.audio || new AudioContext();
      const notes = level === 'critical' ? [660, 880, 1175] : [660, 880];
      notes.forEach((f, i) => {
        const o = this.audio!.createOscillator(); const g = this.audio!.createGain();
        o.type = 'sine'; o.frequency.value = f;
        const t0 = this.audio!.currentTime + i * 0.13;
        g.gain.setValueAtTime(0.0001, t0); g.gain.exponentialRampToValueAtTime(0.12, t0 + 0.02); g.gain.exponentialRampToValueAtTime(0.0001, t0 + 0.22);
        o.connect(g); g.connect(this.audio!.destination); o.start(t0); o.stop(t0 + 0.25);
      });
    } catch { /* audio blocked until user interaction — fine */ }
  }
}

/** Icon + tone for a live event in feeds. */
export function eventLook(e: LiveEvent): { icon: string; cls: string } {
  switch (e.type) {
    case 'case-created': return { icon: '🚨', cls: 'alert' };
    case 'case-reopened': return { icon: '↺', cls: 'alert' };
    case 'case-updated': return { icon: '📈', cls: 'grow' };
    case 'case-status': return { icon: '✓', cls: 'ok' };
    case 'signup': return { icon: '👤', cls: '' };
    case 'signup-rejected': return { icon: '⊘', cls: 'warn' };
    case 'login': return { icon: '🔑', cls: '' };
    case 'login-failed': return { icon: '✕', cls: 'warn' };
    case 'subscribe': return { icon: '💳', cls: '' };
    case 'rule': return { icon: '⚖', cls: 'ok' };
    case 'simulation': return { icon: '⚗', cls: '' };
    default: return { icon: '•', cls: '' };
  }
}
