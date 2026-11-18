import { Component, HostListener, OnInit } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs/operators';
import { SCENARIOS, Scenario } from './lab/simulation';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html'
})
export class AppComponent implements OnInit {
  menuOpen = false;
  attacks = SCENARIOS.filter(s => s.kind === 'attack');
  legit = SCENARIOS.filter(s => s.kind === 'legit');

  /** True on the customer storefront (/store), which renders without the analyst console chrome. */
  bare = location.pathname.startsWith('/store');
  onLab = location.pathname.startsWith('/lab');

  constructor(private router: Router) {}

  ngOnInit(): void {
    this.router.events.pipe(filter(e => e instanceof NavigationEnd)).subscribe((e: any) => {
      const url: string = e.urlAfterRedirects || e.url;
      this.bare = url.startsWith('/store');
      this.onLab = url.startsWith('/lab');
    });
  }

  /** Open the Simulation Lab with this live scenario loaded, ready to run. */
  openScenario(s: Scenario): void {
    this.menuOpen = false;
    this.router.navigate(['/lab'], { queryParams: { scenario: s.key } });
  }

  /** The storefront is the customer's site: open it in its own window (several windows = several "devices"). */
  openStore(ev: Event): void {
    ev.preventDefault();
    window.open('/store', '_blank', 'width=1180,height=900');
  }

  @HostListener('document:click', ['$event'])
  onDocClick(e: MouseEvent): void {
    if (this.menuOpen && !(e.target as HTMLElement).closest('.dropdown')) { this.menuOpen = false; }
  }

  @HostListener('document:keydown.escape')
  onEsc(): void { this.menuOpen = false; }
}
