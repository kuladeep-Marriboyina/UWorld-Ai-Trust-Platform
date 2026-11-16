import { Component, OnInit } from '@angular/core';
import { ApiService } from '../api.service';

@Component({
  selector: 'app-rules',
  templateUrl: './rules.component.html'
})
export class RulesComponent implements OnInit {
  rules: any[] = [];
  catalog: any;
  text = '';
  result: any;
  answers: any = {};
  busy = false;
  activated = '';

  examples = [
    'Flag any order that shows as paid but has no Braintree transaction, over $50, excluding free trials',
    'Flag any payment over $500',
    'Flag a discount code used more than 40 times',
    'Flag access from Iran'
  ];

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.loadRules();
    this.api.catalog().subscribe(c => this.catalog = c);
  }

  loadRules(): void { this.api.rules().subscribe(r => this.rules = r); }

  toggle(r: any): void { this.api.toggleRule(r.ruleId, !r.enabled).subscribe(() => this.loadRules()); }

  use(ex: string): void { this.text = ex; this.answers = {}; this.result = null; this.interpret(); }

  interpret(): void {
    this.busy = true; this.activated = '';
    this.api.author(this.text, this.answers).subscribe(res => { this.result = res; this.busy = false; }, () => this.busy = false);
  }

  keyOf(q: string): string { const m = q.match(/'([a-zA-Z]+)'/); return m ? m[1] : ''; }

  activate(): void {
    this.api.confirmRule(this.result.proposedRule).subscribe((r: any) => {
      this.activated = r.activated; this.result = null; this.text = ''; this.answers = {};
      this.loadRules();
      this.api.run().subscribe();
    });
  }
}
