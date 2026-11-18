import { Component, OnInit } from '@angular/core';
import { ApiService } from '../api.service';
import { impactExplain, ImpactInfo } from '../impact';

@Component({
  selector: 'app-cases',
  templateUrl: './cases.component.html'
})
export class CasesComponent implements OnInit {
  all: any[] = [];
  rules: { id: string; name: string }[] = [];
  loading = false;
  openImpact: number | null = null;

  // filters (applied client-side, instantly)
  search = '';
  filterRule = '';
  filterSeverity = '';
  filterStatus = '';

  readonly severities = ['Critical', 'High', 'Medium', 'Low'];
  readonly statuses = ['New', 'Assigned', 'Escalated', 'Resolved'];

  constructor(private api: ApiService) {}

  impact(ruleId: string): ImpactInfo { return impactExplain(ruleId); }

  toggleImpact(id: number, ev: Event): void {
    ev.stopPropagation();
    this.openImpact = this.openImpact === id ? null : id;
  }

  ngOnInit(): void {
    this.api.rules().subscribe(r => this.rules = r
      .map(x => ({ id: x.ruleId, name: x.name || x.ruleId }))
      .sort((a, b) => a.name.localeCompare(b.name)));
    this.load();
  }

  load(): void {
    this.loading = true;
    this.api.cases('').subscribe(c => { this.all = c; this.loading = false; }, () => this.loading = false);
  }

  // ---- filtering ----
  private matches(c: any): boolean {
    if (this.filterRule && c.ruleId !== this.filterRule) { return false; }
    if (this.filterSeverity && c.severity !== this.filterSeverity) { return false; }
    if (this.filterStatus && c.status !== this.filterStatus) { return false; }
    const q = this.search.trim().toLowerCase().replace(/^#/, '');
    if (!q) { return true; }
    return [c.caseId, c.title, c.ruleId, c.actual, c.entityRef, c.assignedTo, c.category]
      .some(v => v != null && String(v).toLowerCase().includes(q));
  }

  get filtered(): any[] {
    return this.all.filter(c => this.matches(c))
      .sort((a, b) => Date.parse(b.createdUtc) - Date.parse(a.createdUtc));   // newest first
  }

  get hasFilters(): boolean { return !!(this.search || this.filterRule || this.filterSeverity || this.filterStatus); }
  clear(): void { this.search = ''; this.filterRule = ''; this.filterSeverity = ''; this.filterStatus = ''; }

  ruleName(id: string): string { return this.rules.find(r => r.id === id)?.name || id; }
}
