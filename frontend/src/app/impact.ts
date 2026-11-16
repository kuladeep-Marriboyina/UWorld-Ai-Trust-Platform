export interface ImpactInfo { formula: string; note: string; }

/** Explains how a case's "revenue at risk" figure is derived, per rule. */
export function impactExplain(ruleId: string): ImpactInfo {
  switch (ruleId) {
    case 'paid-without-braintree':
      return { formula: 'FinalAmount (full order)', note: 'The order was marked Paid but no real Braintree payment was captured — the whole order amount may never have been collected.' };
    case 'discount-code-fraud':
      return { formula: 'discount % × base price (or discount given away)', note: 'The value of the discount granted improperly — over-limit uses, per-user reuse, expired/stacked codes, or a code applied to a package it is NOT valid for (package-eligibility mismatch).' };
    case 'revenue-abuse':
      return { formula: 'ListPrice − PaidAmount  ·  or Σ(discount × list)', note: 'Zero-pay = full list price not captured; below-policy = the shortfall under the policy minimum; discount reuse = total discount value across the linked orders.' };
    case 'entitlement-leakage':
      return { formula: 'package list price', note: 'Content for a package was accessed without a valid entitlement — the list price of that package is the revenue leaked.' };
    case 'content-scraping':
      return { formula: 'questions harvested × IP value ($5)', note: 'Each stolen question is proprietary IP; impact = count of harvested questions times the configured per-question value.' };
    case 'authorization-gap':
      return { formula: '≈ one subscription (protected-content value)', note: 'Protected content left the building despite a denied/missing authorization — valued at the package list price it exposed.' };
    case 'coordinated-account':
      return { formula: '(linked accounts − 1) × avg list price', note: 'Extra accounts sharing one subscription/ring that should each have been a separate paid subscription.' };
    case 'same-subscription-region':
    case 'blocked-ip-embargo':
    case 'cve-exposure':
      return { formula: '$0 direct revenue', note: 'This is an access-control / security / IP-sharing exposure, reported as risk rather than a direct dollar figure.' };
  }
  if (ruleId && ruleId.indexOf('nl-') === 0) {
    return { formula: 'configured impact field', note: 'Estimated from the numeric field chosen when this rule was authored in plain English.' };
  }
  return { formula: '—', note: 'Estimated business impact for this case.' };
}
