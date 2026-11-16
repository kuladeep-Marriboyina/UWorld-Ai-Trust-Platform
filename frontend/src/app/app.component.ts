import { Component, HostListener, OnInit } from '@angular/core';
import { ApiService } from './api.service';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html'
})
export class AppComponent implements OnInit {
  menuOpen = false;
  groups: any[] = [];

  // popup state
  group: any = null;       // attack family open in the popup
  variant: any = null;     // selected bypass variant
  busy = false;
  runs: any[] = [];        // results for this popup session (newest first)
  error = '';

  constructor(public api: ApiService) {}

  ngOnInit(): void {
    this.api.scenarios().subscribe(g => this.groups = g, () => this.error = 'Could not load scenarios — is the API running on :5080?');
  }

  open(g: any): void {
    this.menuOpen = false;
    this.group = g;
    this.variant = g.variants[0];
    this.runs = [];
    this.error = '';
  }

  close(): void {
    if (this.busy) { return; }
    this.group = null; this.variant = null; this.runs = [];
  }

  run(v: any): void {
    this.busy = true; this.error = '';
    this.api.simulate(v.key).subscribe(
      (r: any) => { this.runs.unshift({ variant: v, result: r }); this.busy = false; },
      (e: any) => { this.error = v.label + ' failed: ' + (e.error?.error || e.message); this.busy = false; }
    );
  }

  /** Fire every variant of the family one after another. */
  runAll(): void {
    const queue = [...this.group.variants];
    const next = () => {
      const v = queue.shift();
      if (!v) { this.busy = false; return; }
      this.variant = v;
      this.api.simulate(v.key).subscribe(
        (r: any) => { this.runs.unshift({ variant: v, result: r }); next(); },
        (e: any) => { this.error = v.label + ' failed: ' + (e.error?.error || e.message); this.busy = false; }
      );
    };
    this.busy = true; this.error = '';
    next();
  }

  @HostListener('document:keydown.escape')
  onEsc(): void { this.menuOpen = false; this.close(); }
}
