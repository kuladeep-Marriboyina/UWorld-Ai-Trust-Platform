import { Component, OnInit } from '@angular/core';
import { ApiService } from '../api.service';

@Component({
  selector: 'app-rules',
  templateUrl: './rules.component.html'
})
export class RulesComponent implements OnInit {
  rules: any[] = [];
  catalog: any;

  selected: any = null;
  search = '';
  filterCat = 'all';
  showAuthor = false;

  // add / edit
  formRule: any = null;     // rule being edited in the right panel; null = new rule
  activated = '';           // success message after a save

  /** What each built-in rule actually checks — shown when you read a rule. */
  logic: { [k: string]: { summary: string; checks: string[] } } = {
    'authorization-gap': { summary: 'Protected content was returned even though authorization was denied or missing.', checks: ['AuthResult ∈ {Denied, Missing}', 'AND content bytes returned > 0', 'joined by CorrelationId'] },
    'entitlement-leakage': { summary: 'A package was opened after the subscription had expired, been cancelled, or refunded.', checks: ['Access to a package the user once had', 'Entitlement not valid at the access time (event-time check)'] },
    'revenue-abuse': { summary: 'Revenue leaks: unexpected free orders, below-policy pricing, reused discounts.', checks: ['Unexpected $0 (non-trial) purchase', 'Paid below the policy minimum with no discount', 'One discount code reused across linked accounts'] },
    'content-scraping': { summary: 'Automated harvesting of the question bank.', checks: ['≥ 50 questions in one session', 'within 15 minutes', '≥ 80% sequential question ids'] },
    'coordinated-account': { summary: 'Several accounts acting as one — weighted, never shared-IP alone.', checks: ['Shared device (+30)', 'Reused discount code (+25)', 'Shared IP (+15, never counted alone)', 'Needs ≥2 signals and score ≥ 40'] },
    'cve-exposure': { summary: 'A live asset runs a component + version with a known CVE.', checks: ['Asset Component + Version matches a CVE affected range'] },
    'paid-without-braintree': { summary: 'An order shows as Paid but the money never reached (or never matched) the Braintree gateway.', checks: ['PaymentStatus = Paid AND TransactionAmount > 0 AND gateway = Braintree', 'No matching Braintree transaction OR TransactionId ∈ {0, 0000} OR IsMismatch = 1', 'Excludes legitimate $0 / trial / store-credit orders'] },
    'discount-code-fraud': { summary: 'Discount / referral code abuse across six checks.', checks: ['Over usage limit (Used > Limit)', 'Per-user reuse of a single-use code', 'Expired code applied after expiry', 'Regular + Regular stacking on one order', 'Race double-use within N seconds', 'Package eligibility — code applied outside its GA_DISCOUNTS package list'] },
    'same-subscription-region': { summary: 'One subscription used from two or more countries in a short window.', checks: ['Group access logs by subscription', '≥ 2 distinct countries within the window (default 24h)'] },
    'blocked-ip-embargo': { summary: 'Access from a blocked IP or an embargoed country.', checks: ['IP ∈ BLOCKED_IPS', 'or CountryCode is an embargoed country'] }
  };

  /** DB tables + columns each built-in rule reads — shown in the collapsed Technical details. */
  schema: { [k: string]: { table: string; fields: string[] }[] } = {
    'authorization-gap': [
      { table: 'AuthOutcomes', fields: ['AuthResult', 'CorrelationId', 'OccurredUtc'] },
      { table: 'AccessEvents', fields: ['ContentReturnedBytes', 'CorrelationId', 'Resource', 'AccountId', 'OccurredUtc'] }
    ],
    'entitlement-leakage': [
      { table: 'AccessEvents', fields: ['AccountId', 'PackageId', 'OccurredUtc', 'Resource'] },
      { table: 'Entitlements', fields: ['Status', 'StartUtc', 'EndUtc', 'RefundedUtc', 'PackageId'] }
    ],
    'revenue-abuse': [
      { table: 'Orders', fields: ['OrderType', 'ListPrice', 'PaidAmount', 'PolicyMinPrice'] },
      { table: 'Discounts', fields: ['Code', 'OrderId', 'AccountId', 'Percent'] },
      { table: 'Accounts', fields: ['LinkedAccountGroupId'] }
    ],
    'content-scraping': [
      { table: 'AccessEvents', fields: ['SessionId', 'QuestionId', 'OccurredUtc', 'IpAddress', 'DeviceId', 'AccountId'] }
    ],
    'coordinated-account': [
      { table: 'Accounts', fields: ['LinkedAccountGroupId'] },
      { table: 'AccessEvents', fields: ['DeviceId', 'IpAddress', 'AccountId'] },
      { table: 'Discounts', fields: ['Code', 'AccountId'] }
    ],
    'cve-exposure': [
      { table: 'AssetInventory', fields: ['Name', 'Component', 'Version'] },
      { table: 'Cves', fields: ['Component', 'AffectedVersion', 'Severity'] }
    ],
    'paid-without-braintree': [
      { table: 'USER_PAYMENTS', fields: ['PaymentStatus', 'TransactionAmount', 'GatewayId', 'TransactionId', 'FinalAmount', 'UserId', 'CountryCode', 'DateCreated'] },
      { table: 'BRAINTREE_TRANSACTIONS', fields: ['TransactionId', 'IsMismatch', 'TransactionStatus', 'PaymentId'] },
      { table: 'USER_PAYMENT_DETAILS', fields: ['PaymentId', 'PackageId'] },
      { table: 'USER_SUBSCRIPTIONS', fields: ['UserId', 'PackageId', 'Active', 'DateExpiration'] }
    ],
    'discount-code-fraud': [
      { table: 'NCLEX_REFERRAL_CODES', fields: ['ReferralCode', 'ReferralCodeLimit', 'ReferralCodeUsed', 'CodeExpirationDate', 'DiscountGroupId', 'DiscountBehaviourTypeId', 'DiscountPercent'] },
      { table: 'REFERRAL_CODE_USAGE', fields: ['ReferralCode', 'UserId', 'PaymentId', 'DateUsed'] },
      { table: 'USER_PAYMENT_DETAILS', fields: ['PaymentId', 'PackageId', 'DiscountCode', 'DiscountAmount'] },
      { table: 'GA_DISCOUNTS', fields: ['DiscountGroupId', 'PackageId'] },
      { table: 'USER_PAYMENTS', fields: ['PaymentId', 'UserId', 'DateCreated'] },
      { table: 'USER_LOGS', fields: ['UserId', 'LogType', 'Url', 'DateCreated'] }
    ],
    'same-subscription-region': [
      { table: 'USER_LOGS', fields: ['SubscriptionId', 'CountryCode', 'IPAddress', 'UserId', 'DateCreated'] }
    ],
    'blocked-ip-embargo': [
      { table: 'USER_LOGS', fields: ['IPAddress', 'CountryCode', 'UserId', 'SubscriptionId', 'DateCreated'] },
      { table: 'BLOCKED_IPS', fields: ['IPAddress', 'Reason'] },
      { table: 'COUNTRIES', fields: ['IsoCode', 'IsEmbargoed'] }
    ]
  };

  schemaFor(r: any): { table: string; fields: string[] }[] {
    if (!r) { return []; }
    const d = this.declarative(r);
    if (d && d.target) {
      const fields = Array.from(new Set((d.when || []).map((c: any) => c.field)));
      return [{ table: d.target, fields: fields as string[] }];
    }
    return this.schema[r.ruleId] || [];
  }

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.loadRules();
    this.api.catalog().subscribe(c => this.catalog = c);
  }

  loadRules(): void {
    this.api.rules().subscribe(r => {
      this.rules = r;
      if (this.selected) { this.selected = r.find(x => x.ruleId === this.selected.ruleId) || null; }
    });
  }

  get categories(): string[] {
    return ['all', ...Array.from(new Set(this.rules.map(r => r.category || 'Custom'))).sort()];
  }

  get filtered(): any[] {
    const q = this.search.trim().toLowerCase();
    return this.rules
      .filter(r => this.filterCat === 'all' || (r.category || 'Custom') === this.filterCat)
      .filter(r => !q || (r.name || r.ruleId).toLowerCase().includes(q) || r.ruleId.toLowerCase().includes(q));
  }

  get grouped(): { cat: string; items: any[] }[] {
    const map: { [k: string]: any[] } = {};
    this.filtered.forEach(r => { const c = r.category || 'Custom'; (map[c] = map[c] || []).push(r); });
    return Object.keys(map).sort().map(cat => ({ cat, items: map[cat] }));
  }

  select(r: any): void { this.selected = r; this.showAuthor = false; }

  toggle(r: any, ev?: Event): void {
    if (ev) { ev.stopPropagation(); }
    this.api.toggleRule(r.ruleId, !r.enabled).subscribe(() => this.loadRules());
  }

  // ---- read helpers ----
  logicFor(r: any): { summary: string; checks: string[] } | null {
    return r ? (this.logic[r.ruleId] || null) : null;
  }

  config(r: any): { k: string; v: string }[] {
    if (!r || !r.configJson) { return []; }
    try {
      const o = JSON.parse(r.configJson);
      return Object.keys(o).map(k => ({ k, v: Array.isArray(o[k]) ? o[k].join(', ') : String(o[k]) }));
    } catch { return []; }
  }

  declarative(r: any): any {
    if (!r || !r.declarativeJson) { return null; }
    try { return JSON.parse(r.declarativeJson); } catch { return null; }
  }

  // ---- add / edit a rule ----
  openAuthor(): void { this.openForm(null); }
  openEdit(r: any): void { this.openForm(r); }

  /** Re-create the form each time so switching between "add" and "edit X" never shows stale values. */
  private openForm(r: any): void {
    this.showAuthor = false; this.activated = '';
    setTimeout(() => { this.formRule = r; this.showAuthor = true; });
  }

  onSaved(r: any): void {
    this.showAuthor = false;
    this.activated = `Saved v${r.version} of ${r.ruleId} — live now; it will raise cases on the next pass.`;
    this.api.rules().subscribe(list => {
      this.rules = list;
      this.selected = list.find(x => x.ruleId === r.ruleId) || null;
    });
    this.api.run().subscribe(() => {}, () => {});
  }
}
