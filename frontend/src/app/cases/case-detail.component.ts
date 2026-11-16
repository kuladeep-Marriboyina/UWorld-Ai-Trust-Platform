import { Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../api.service';
import { impactExplain, ImpactInfo } from '../impact';

@Component({
  selector: 'app-case-detail',
  templateUrl: './case-detail.component.html'
})
export class CaseDetailComponent implements OnInit {
  id = 0;
  case: any;
  ai: any;
  aiBusy = false;
  showImpact = false;

  get impact(): ImpactInfo { return impactExplain(this.case ? this.case.ruleId : ''); }

  /** "Actual" is built as "main sentence  · subscription …  · applied from …" — split it into readable facts. */
  get actualParts(): string[] {
    return this.case && this.case.actual ? this.case.actual.split(/\s{2}·\s/).map((s: string) => s.trim()).filter((s: string) => s) : [];
  }

  /** Evidence in time order; undated rows keep their original order at the end. */
  get timeline(): any[] {
    if (!this.case) { return []; }
    const dated = this.case.evidence.filter((e: any) => e.occurredUtc)
      .sort((a: any, b: any) => a.occurredUtc.localeCompare(b.occurredUtc));
    return dated.concat(this.case.evidence.filter((e: any) => !e.occurredUtc));
  }

  get maxWeight(): number {
    return this.case && this.case.scoreBreakdown.length ? Math.max(...this.case.scoreBreakdown.map((f: any) => f.weight)) : 1;
  }

  constructor(private route: ActivatedRoute, private api: ApiService) {}

  ngOnInit(): void {
    // re-load when navigating between cases (e.g. from the simulate popup)
    this.route.paramMap.subscribe(p => {
      this.id = +(p.get('id') || 0);
      this.ai = null; this.showImpact = false;
      this.load();
    });
  }

  load(): void { this.api.case(this.id).subscribe(c => this.case = c); }

  summarise(): void {
    this.aiBusy = true;
    this.api.aiSummary(this.id).subscribe(a => { this.ai = a; this.aiBusy = false; }, () => this.aiBusy = false);
  }

  assign(): void { this.api.assign(this.id, 'kuladeep').subscribe(() => this.load()); }
  escalate(): void { this.api.escalate(this.id, 'Escalated from workspace').subscribe(() => this.load()); }
  resolve(): void { this.api.resolve(this.id, 'Reviewed and resolved').subscribe(() => this.load()); }
}
