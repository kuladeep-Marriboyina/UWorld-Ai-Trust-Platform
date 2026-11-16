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

  constructor(private route: ActivatedRoute, private api: ApiService) {}

  ngOnInit(): void {
    this.id = +(this.route.snapshot.paramMap.get('id') || 0);
    this.load();
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
