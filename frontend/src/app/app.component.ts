import { Component } from '@angular/core';
import { ApiService } from './api.service';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html'
})
export class AppComponent {
  busy = false;
  menuOpen = false;
  active: any = null;      // scenario being simulated
  result = '';

  scenarios = [
    {
      key: 'paid-without-braintree', label: 'Paid without Braintree', tables: 'USER_PAYMENTS',
      desc: 'Insert a payment marked Paid for $250 on the Braintree gateway with TransactionId "0" and NO matching BRAINTREE_TRANSACTIONS row — the order shows as collected but the money never hit the gateway.',
      expect: 'Expect a Critical "paid-without-braintree" case for the new payment.'
    },
    {
      key: 'region-hop', label: 'Same subscription, 2 regions', tables: 'USER_SUBSCRIPTIONS + USER_LOGS',
      desc: 'Create a subscription and two access logs ~40 min apart — one from the US, one from India — so one subscription is used from two countries within the hour (impossible travel / account sharing).',
      expect: 'Expect a High "same-subscription-region" case.'
    },
    {
      key: 'code-over-limit', label: 'Discount used over its limit', tables: 'NCLEX_REFERRAL_CODES',
      desc: 'Insert a referral code with ReferralCodeUsed = 77 against a ReferralCodeLimit of 50 — a discount redeemed far beyond its allowed number of uses.',
      expect: 'Expect a High "discount-code-fraud" (over-limit) case.'
    },
    {
      key: 'blocked-ip', label: 'Access from a blocked IP', tables: 'USER_LOGS + BLOCKED_IPS',
      desc: 'Add an access log from IP 45.66.77.88, which is on the BLOCKED_IPS list (known content-scraping source) — access that should never have been allowed.',
      expect: 'Expect a High "blocked-ip-embargo" case.'
    },
    {
      key: 'authorization-gap', label: 'Authorization gap', tables: 'AuthOutcomes + AccessEvents',
      desc: 'Inject a Denied authorization plus an access event that still returned 61,000 bytes on the same correlation id — protected content left the building despite the denial.',
      expect: 'Expect a Critical "authorization-gap" case.'
    }
  ];

  constructor(public api: ApiService) {}

  simulate(scenario: any): void {
    this.busy = true; this.menuOpen = false; this.active = scenario; this.result = 'Injecting into ' + scenario.tables + ' and running one detection pass…';
    this.api.simulate(scenario.key).subscribe(
      (r: any) => { this.result = '✓ Done — ' + r.casesCreated + ' new case(s) raised. ' + scenario.expect + ' Open the Cases tab to see it.'; this.busy = false; },
      () => { this.result = '✗ ' + scenario.label + ' failed.'; this.busy = false; }
    );
  }

  closeBanner(): void { this.active = null; this.result = ''; }
}
