import { Pipe, PipeTransform } from '@angular/core';

/** "just now", "4 min ago", "3 h ago", "2 d ago". Impure so it re-evaluates as the clock ticks. */
@Pipe({ name: 'ago', pure: false })
export class AgoPipe implements PipeTransform {
  transform(v: string | Date | null | undefined): string {
    if (!v) { return '—'; }
    const t = typeof v === 'string' ? Date.parse(v.endsWith('Z') || v.includes('+') ? v : v + 'Z') : v.getTime();
    const s = Math.max(0, (Date.now() - t) / 1000);
    if (s < 45) { return 'just now'; }
    if (s < 3600) { return `${Math.round(s / 60)} min ago`; }
    if (s < 86400) { return `${Math.round(s / 3600)} h ago`; }
    return `${Math.round(s / 86400)} d ago`;
  }
}

/** $1,234 / $1.2k / $1.4M — compact money for tiles; full precision with digits arg. */
@Pipe({ name: 'usd' })
export class UsdPipe implements PipeTransform {
  transform(v: number | null | undefined, mode: 'short' | 'full' | 'cents' = 'full'): string {
    const n = Number(v || 0);
    if (mode === 'short') {
      if (Math.abs(n) >= 1e6) { return '$' + (n / 1e6).toFixed(1) + 'M'; }
      if (Math.abs(n) >= 1e4) { return '$' + (n / 1e3).toFixed(1) + 'k'; }
    }
    return '$' + n.toLocaleString('en-US', { minimumFractionDigits: mode === 'cents' ? 2 : 0, maximumFractionDigits: mode === 'cents' ? 2 : 0 });
  }
}

/** Milliseconds as a person would say it: 44 ms · 2.3 s · 4 min. */
@Pipe({ name: 'dur' })
export class DurationPipe implements PipeTransform {
  transform(ms: number | null | undefined): string {
    if (ms == null) { return '—'; }
    if (ms < 1000) { return `${Math.round(ms)} ms`; }
    if (ms < 90000) { return `${(ms / 1000).toFixed(ms < 10000 ? 1 : 0)} s`; }
    if (ms < 5400000) { return `${Math.round(ms / 60000)} min`; }
    return `${(ms / 3600000).toFixed(1)} h`;
  }
}

/** Parse a server UTC timestamp (which may lack the Z) into a Date. */
export function utc(v: string | null | undefined): Date | null {
  if (!v) { return null; }
  return new Date(v.endsWith('Z') || v.includes('+') ? v : v + 'Z');
}

/** Human labels for internal codes. */
export const FACTOR_LABELS: { [k: string]: string } = {
  SharedNetwork: 'Shared registration IP', SharedDevice: 'Same device', SharedCard: 'Same payment card',
  TemplatedEmails: 'Templated emails', SameInbox: 'One inbox, many aliases', DisposableEmail: 'Disposable email',
  GeneratedEmails: 'Machine-generated emails', AutomationClient: 'Scripted client', ScriptedCadence: 'Inhuman signup speed',
  TrialHarvest: 'Trial / discount harvesting', FailedLoginBurst: 'Failed-login burst', AccountSpread: 'Many accounts targeted',
  SuccessfulTakeover: 'Successful takeover', AuthoredRule: 'Authored rule matched', ThresholdExceeded: 'Above threshold',
};

export function factorLabel(signal: string): string {
  return FACTOR_LABELS[signal] || signal.replace(/([a-z])([A-Z])/g, '$1 $2');
}

export function sevClass(s: string | null | undefined): string { return (s || 'low').toLowerCase(); }

export const CATEGORY_COLORS: { [k: string]: string } = {
  'Account': 'var(--series-1)', 'Account Takeover': 'var(--series-2)', 'Payment': 'var(--series-3)', 'Discount': 'var(--series-4)',
  'Access': 'var(--series-5)', 'IP': 'var(--series-7)', 'Security': 'var(--series-6)', 'Subscription': 'var(--series-8)',
  'Revenue': 'var(--series-4)', 'Custom': 'var(--series-7)', 'Other': 'var(--text-3)',
};
export function catColor(c: string | null | undefined): string { return CATEGORY_COLORS[c || 'Other'] || 'var(--text-3)'; }

export const RULE_KIND_HELP = {
  coded: 'Engineered detector — logic is versioned code; analysts tune its parameters through this portal.',
  authored: 'Authored in this portal — the detection logic itself is defined here and runs without a deploy.',
};

/** Formats a server UTC timestamp in the viewer's local time, whether or not it carries a trailing Z. */
@Pipe({ name: 'utcdate' })
export class UtcDatePipe implements PipeTransform {
  transform(v: string | null | undefined, fmt: 'time' | 'day' | 'full' | 'short' = 'short'): string {
    const d = utc(v);
    if (!d) { return '—'; }
    if (fmt === 'time') { return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false }); }
    if (fmt === 'day') { return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' }); }
    if (fmt === 'full') { return d.toLocaleString(undefined, { month: 'short', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false }); }
    return d.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });
  }
}
