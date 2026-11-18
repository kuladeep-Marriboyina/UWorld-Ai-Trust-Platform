import { Component, ElementRef, Input } from '@angular/core';
import { glossaryLookup } from '../core/glossary';

/**
 * ⓘ next to a term; hover or focus shows what it means. Use a glossary key (<app-info k="precision">)
 * or free text (<app-info text="…">). The bubble is fixed-positioned so cards and tables never clip it.
 */
@Component({
  selector: 'app-info',
  template: `<ng-container *ngIf="title"><span class="info-i" tabindex="0" role="button" [attr.aria-label]="'What is ' + title + '?'"
      (mouseenter)="show()" (mouseleave)="open = false" (focus)="show()" (blur)="open = false" (click)="$event.stopPropagation(); $event.preventDefault()">i</span>
    <span class="info-pop" *ngIf="open" [style.left.px]="x" [style.top.px]="y" [class.below]="below">
      <b>{{ title }}</b>{{ body }}
    </span></ng-container>`,
})
export class InfoComponent {
  @Input() k = '';
  @Input() text = '';
  @Input() label = '';
  open = false; x = 0; y = 0; below = false;

  constructor(private el: ElementRef<HTMLElement>) {}

  get title(): string { return this.label || glossaryLookup(this.k)?.term || ''; }
  get body(): string { return this.text || glossaryLookup(this.k)?.text || ''; }

  show(): void {
    const r = this.el.nativeElement.getBoundingClientRect();
    const w = 280;
    this.x = Math.max(8, Math.min(window.innerWidth - w - 8, r.left + r.width / 2 - w / 2));
    this.below = r.top < 150;
    this.y = this.below ? r.bottom + 8 : r.top - 8;
    this.open = true;
  }
}
