import { Component, OnInit } from '@angular/core';
import { ApiService } from '../api.service';
import { impactExplain, ImpactInfo } from '../impact';

@Component({
  selector: 'app-cases',
  templateUrl: './cases.component.html'
})
export class CasesComponent implements OnInit {
  cases: any[] = [];
  rules: string[] = [];
  filterRule = '';
  filterSeverity = '';
  openImpact: number | null = null;

  constructor(private api: ApiService) {}

  impact(ruleId: string): ImpactInfo { return impactExplain(ruleId); }

  toggleImpact(id: number, ev: Event): void {
    ev.stopPropagation();
    this.openImpact = this.openImpact === id ? null : id;
  }

  ngOnInit(): void {
    this.api.rules().subscribe(r => this.rules = r.map(x => x.ruleId));
    this.load();
  }

  load(): void {
    const q: string[] = [];
    if (this.filterRule) { q.push('ruleId=' + this.filterRule); }
    if (this.filterSeverity) { q.push('severity=' + this.filterSeverity); }
    this.api.cases(q.length ? '?' + q.join('&') : '').subscribe(c => this.cases = c);
  }
}
