/* 5 on-stage DEMO decks for UWorld AI Trust & Revenue Protection (USpark 2026). */
const PptxGenJS = require('pptxgenjs');
const TEAM = 'Support Sparkers · Prakash Kosuri · Kuladeep M · Vishnu';

function build(def) {
  const p = new PptxGenJS();
  p.layout = 'LAYOUT_WIDE';
  const t = def.theme, W = 13.33;

  const foot = (s, n) => {
    s.addText('UWorld AI Trust & Revenue Protection · USpark 2026 · Live demo', { x: 0.5, y: 7.04, w: 10, h: 0.3, fontSize: 9, color: 'A7B2C2' });
    s.addText(String(n), { x: 12.4, y: 7.04, w: 0.5, h: 0.3, fontSize: 9, color: 'A7B2C2', align: 'right' });
  };
  const header = (s, title, kicker) => {
    s.addShape(p.ShapeType.rect, { x: 0, y: 0, w: W, h: 0.9, fill: { color: t.bar } });
    s.addShape(p.ShapeType.rect, { x: 0, y: 0.9, w: W, h: 0.06, fill: { color: t.accent } });
    s.addText(title, { x: 0.5, y: 0.14, w: 10.5, h: 0.62, fontSize: 24, bold: true, color: 'FFFFFF' });
    if (kicker) s.addText(kicker, { x: 10.6, y: 0.3, w: 2.3, h: 0.4, fontSize: 11, color: 'D7E2F5', align: 'right' });
  };
  const bullets = (s, items, x, y, w, h, fs) =>
    s.addText(items.map(it => ({
      text: typeof it === 'string' ? it : it.t,
      options: { bullet: { indent: 14 }, color: (it.c || t.ink), bold: !!it.b, fontSize: it.fs || fs || 15, paraSpaceAfter: 6 }
    })), { x, y, w, h, valign: 'top' });

  def.slides.forEach((sl, i) => {
    const s = p.addSlide();
    s.background = { color: 'FFFFFF' };

    if (sl.type === 'title') {
      s.background = { color: t.bar };
      s.addShape(p.ShapeType.rect, { x: 0, y: 4.0, w: W, h: 0.08, fill: { color: t.accent } });
      s.addText(def.tag, { x: 0.8, y: 1.5, w: 11, h: 0.4, fontSize: 13, color: t.accent, charSpacing: 2 });
      s.addText(def.title, { x: 0.8, y: 2.0, w: 11.8, h: 1.7, fontSize: 40, bold: true, color: 'FFFFFF', lineSpacingMultiple: 0.95 });
      s.addText(def.subtitle, { x: 0.8, y: 4.25, w: 11.5, h: 1.0, fontSize: 17, color: 'D7E2F5' });
      s.addText(TEAM, { x: 0.8, y: 6.5, w: 11.5, h: 0.4, fontSize: 12, color: 'AEBFD9' });
      return;
    }

    header(s, sl.title, sl.kicker);
    foot(s, i + 1);
    const CY = 1.25;

    if (sl.type === 'bullets') {
      if (sl.lead) s.addText(sl.lead, { x: 0.5, y: CY, w: 12.3, h: 0.6, fontSize: 15, italic: true, color: t.muted });
      bullets(s, sl.items, 0.6, sl.lead ? CY + 0.7 : CY, 12.1, 5.2, sl.fs);
      return;
    }
    if (sl.type === 'two') {
      const colW = 5.95;
      [[0.5, sl.left], [6.85, sl.right]].forEach(([x, col]) => {
        s.addShape(p.ShapeType.rect, { x, y: CY, w: colW, h: 5.1, fill: { color: 'F5F8FD' }, line: { color: t.line } });
        s.addText(col.h, { x: x + 0.25, y: CY + 0.18, w: colW - 0.5, h: 0.45, fontSize: 16, bold: true, color: t.bar });
        bullets(s, col.items, x + 0.3, CY + 0.75, colW - 0.6, 4.2, sl.fs || 14);
      });
      return;
    }
    if (sl.type === 'cards') {
      if (sl.lead) s.addText(sl.lead, { x: 0.5, y: CY, w: 12.3, h: 0.5, fontSize: 15, color: t.muted });
      const y = CY + (sl.lead ? 0.7 : 0.1);
      const n = sl.cards.length, gap = 0.3, cw = (12.33 - gap * (n - 1)) / n;
      sl.cards.forEach((c, k) => {
        const x = 0.5 + k * (cw + gap);
        s.addShape(p.ShapeType.roundRect, { x, y, w: cw, h: 1.9, rectRadius: 0.1, fill: { color: 'F3F7FF' }, line: { color: t.accent } });
        s.addText(c.v, { x, y: y + 0.28, w: cw, h: 0.8, fontSize: 28, bold: true, color: t.bar, align: 'center' });
        s.addText(c.l, { x: x + 0.15, y: y + 1.12, w: cw - 0.3, h: 0.7, fontSize: 12, color: t.muted, align: 'center' });
      });
      if (sl.note) s.addText(sl.note, { x: 0.5, y: y + 2.15, w: 12.3, h: 1.2, fontSize: 14, color: t.ink });
      return;
    }
    if (sl.type === 'table') {
      if (sl.lead) s.addText(sl.lead, { x: 0.5, y: CY, w: 12.3, h: 0.5, fontSize: 15, color: t.muted });
      const head = sl.rows[0].map(c => ({ text: c, options: { bold: true, color: 'FFFFFF', fill: { color: t.bar }, fontSize: 12 } }));
      const body = sl.rows.slice(1).map((r, ri) => r.map(c => ({ text: c, options: { fontSize: 12, color: t.ink, fill: { color: ri % 2 ? 'F3F7FF' : 'FFFFFF' } } })));
      s.addTable([head, ...body], { x: 0.5, y: CY + (sl.lead ? 0.6 : 0.1), w: 12.33, colW: sl.colW, border: { type: 'solid', color: 'E2E8F3', pt: 1 }, valign: 'middle', rowH: sl.rowH || 0.38 });
      return;
    }
    if (sl.type === 'steps') {
      let y = CY + (sl.lead ? 0.6 : 0.05);
      if (sl.lead) s.addText(sl.lead, { x: 0.5, y: CY, w: 12.3, h: 0.5, fontSize: 15, color: t.muted });
      sl.items.forEach((it, k) => {
        s.addShape(p.ShapeType.ellipse, { x: 0.55, y: y + 0.03, w: 0.42, h: 0.42, fill: { color: t.accent } });
        s.addText(String(k + 1), { x: 0.55, y: y + 0.03, w: 0.42, h: 0.42, fontSize: 15, bold: true, color: 'FFFFFF', align: 'center', valign: 'middle' });
        s.addText([{ text: it.t + '  ', options: { bold: true, color: t.ink, fontSize: 15 } }, { text: it.d || '', options: { color: t.muted, fontSize: 13 } }], { x: 1.2, y: y, w: 11.6, h: 0.52, valign: 'middle' });
        y += 0.62;
      });
      return;
    }
    if (sl.type === 'closing') {
      s.addText(sl.big, { x: 1, y: 2.5, w: 11.3, h: 2.2, fontSize: 30, bold: true, color: t.bar, align: 'center', lineSpacingMultiple: 1.05 });
      if (sl.sub) s.addText(sl.sub, { x: 1.5, y: 4.9, w: 10.3, h: 1, fontSize: 16, color: t.muted, align: 'center' });
      return;
    }
  });

  return p.writeFile({ fileName: def.file });
}

const PROOF = [
  { v: '11', l: 'Rules firing live' },
  { v: '$2,503', l: 'Revenue-at-risk / pass' },
  { v: '$0.0056', l: 'AI cost / case' },
  { v: '~443,000×', l: 'Measured ROI' }
];
const TH = {
  story:   { bar: '5B21B6', accent: 'FF7A1A', ink: '13233B', muted: '5A6B80', line: 'E7E2F5' },
  run:     { bar: '1E3A8A', accent: '00B8A9', ink: '13233B', muted: '5A6B80', line: 'DCE6F7' },
  scen:    { bar: 'A21CAF', accent: 'F5B301', ink: '13233B', muted: '5A6B80', line: 'F0D6F2' },
  qa:      { bar: '0B3B8C', accent: '1F9D57', ink: '13233B', muted: '5A6B80', line: 'DCE6F7' },
  impact:  { bar: '334155', accent: 'F5B301', ink: '13233B', muted: '5A6B80', line: 'E2E8F3' }
};

// === 1 — NARRATIVE: "A fraudster's bad day" ===
const story = {
  file: 'demo-1-A-Fraudsters-Bad-Day.pptx', theme: TH.story, tag: 'USPARK 2026 · LIVE DEMO · STORY',
  title: 'A fraudster’s\nbad day',
  subtitle: 'Watch one attacker try four tricks — and get caught, explained, and resolved, live on stage.',
  slides: [
    { type: 'title' },
    { type: 'bullets', title: 'Meet the attacker', lead: 'No deep skill required — just a few tricks.',
      items: ['Wants free access to the question banks', 'Will fake a payment, share a login, reuse a coupon, and scrape content', 'Today they’d get away with it for days', { t: 'Tonight, they won’t get past the first pass.', b: true }] },
    { type: 'steps', title: 'Act 1 — the fake payment', lead: 'They mark an order Paid without really paying.',
      items: [
        { t: 'Click ⚗ Simulate → Paid without Braintree', d: '→ injects a Paid $250 order, TransactionId “0”, no gateway row' },
        { t: 'One detection pass runs', d: '→ paid-without-braintree fires' },
        { t: 'A Critical case appears', d: '“Order marked Paid ($250) but no gateway transaction”' },
        { t: 'Dev info shows the exact request payload + response', d: 'full technical trace, per case' }
      ] },
    { type: 'steps', title: 'Act 2 — the shared login', lead: 'They share one subscription with a friend overseas.',
      items: [
        { t: 'Simulate → Same subscription, 2 regions', d: '→ access from US then India, 40 min apart' },
        { t: 'same-subscription-region fires', d: 'impossible travel / account sharing' },
        { t: 'Case names the subscription, both countries, timestamps', d: 'and the user' }
      ] },
    { type: 'steps', title: 'Act 3 — the coupon ring', lead: 'They reuse a discount code it isn’t meant for.',
      items: [
        { t: 'Simulate → Discount bypass (package-mismatch)', d: '→ SAVE50 applied to an ineligible package' },
        { t: 'discount-code-fraud fires', d: 'with the eligible-package list and the money given away' },
        { t: 'Shows subscription status + the applydiscountcode URL from the logs', d: '' }
      ] },
    { type: 'bullets', title: 'The AI explains — the human decides', lead: 'Click “Explain this case”.',
      items: ['Plain-language summary with cited evidence and labelled uncertainty', 'Recommended next checks — advisory only, never enforcing', 'Analyst assigns → escalates → resolves; every step audited', { t: 'Enforcement stays with humans and the existing .NET authorization.', b: true }] },
    { type: 'cards', title: 'End of a bad day', cards: PROOF, note: 'Four tricks, four cases, one pass — caught in seconds, explained in plain English.' },
    { type: 'closing', big: 'The attacker needed one prompt. We needed one pass.' }
  ]
};

// === 2 — PRESENTER RUN-OF-SHOW ===
const run = {
  file: 'demo-2-Presenter-Run-Of-Show.pptx', theme: TH.run, tag: 'USPARK 2026 · DEMO · RUN-OF-SHOW',
  title: 'Live demo —\nrun of show',
  subtitle: 'A click-by-click script: what to open, what to say, and exactly what the audience will see.',
  slides: [
    { type: 'title' },
    { type: 'bullets', title: 'Before you start', lead: 'Two things running, one browser tab.',
      items: ['API on http://localhost:5080 (Development) — the worker is already raising cases', 'Angular workspace on http://localhost:4200', 'Open the Dashboard tab; have the ⚗ Simulate menu ready', { t: 'If scenarios don’t load → the API was restarted; give it 5 seconds.', b: true }] },
    { type: 'table', title: 'The script', colW: [3.1, 4.6, 4.63], rowH: 0.62, rows: [
        ['Do', 'Say', 'Audience sees'],
        ['Open Dashboard', '“This is live — auto-refreshing.”', 'Top-50 active subs, $ at risk, patterns, alerts'],
        ['⚗ Simulate → Paid w/o Braintree', '“Let’s fake a payment.”', 'Popup explains what it injects → Run'],
        ['Open the new case', '“Denied… yet it shipped.”', 'Expected-vs-actual, evidence timeline'],
        ['Click Explain this case', '“The AI explains — it never enforces.”', 'Cited summary, uncertainty, next checks'],
        ['Expand Dev info', '“Full technical trace.”', 'Request payload + response + raw JSON'],
        ['Resolve', '“A human decides.”', 'Audit trail updates']
      ] },
    { type: 'steps', title: 'The showstopper — add a rule, live', lead: 'Rules Portal → + Add rule.',
      items: [
        { t: 'Type a rule in plain English', d: '“Flag any payment over $500”' },
        { t: 'AI maps it to real fields + shows confidence', d: 'FinalAmount > 500 on USER_PAYMENTS' },
        { t: 'Answer any clarifying question → Activate', d: 'no redeploy' },
        { t: 'Simulate / run a pass → the new rule raises a case', d: 'authored on stage' }
      ] },
    { type: 'bullets', title: 'If a judge interrupts', lead: 'Quick, confident answers.',
      items: ['“Can the AI change anything?” → No — it’s read-only, no actuators.', '“Is it real data?” → Synthetic mirror of the real schema; repointable at the dev DB.', '“What does it cost?” → ~½¢ per investigation; detection itself is free.', '“What if the DB is attacked?” → We’re the detection layer; infra owns prevention.'] },
    { type: 'bullets', title: 'Recovery / fallbacks', lead: 'Keep momentum.',
      items: ['Case list empty? → POST /api/demo/run or click Simulate', 'UI not loading? → the four HTML explainers are a backup narrative', 'Pre-load one case detail in a second tab before you start'] },
    { type: 'cards', title: 'Hold these numbers', cards: PROOF },
    { type: 'closing', big: 'Open. Simulate. Explain. Resolve. Author a rule. Sit down.', sub: 'Under three minutes, end to end.' }
  ]
};

// === 3 — SCENARIO SHOWCASE ===
const scen = {
  file: 'demo-3-Attack-Scenario-Showcase.pptx', theme: TH.scen, tag: 'USPARK 2026 · DEMO · SCENARIOS',
  title: 'Every attack,\none click',
  subtitle: 'Five attack families, many bypass variants — each injects a real fraud and raises a real case.',
  slides: [
    { type: 'title' },
    { type: 'bullets', title: 'The ⚗ Simulate menu', lead: 'Pick a family → read what it does → Run (or Run all).',
      items: ['💳 Paid without Braintree', '🏷️ Discount-code bypasses', '🌍 Same subscription, two regions', '🚫 Blocked-IP / embargo access', '🔓 Authorization gap', { t: 'Every variant writes to the real-schema tables, then runs one detection pass.', b: true }] },
    { type: 'table', title: 'Bypass variants', colW: [3.2, 5.5, 3.63], rowH: 0.44, rows: [
        ['Family', 'Variants (bypasses)', 'Rule'],
        ['Paid w/o Braintree', 'placeholder “0” · orphan id · gateway mismatch · store-credit', 'paid-without-braintree'],
        ['Discount bypass', 'over-limit · expired · stacking · package-mismatch', 'discount-code-fraud'],
        ['Region', 'US→IN · US→KP (embargo) · triple-country', 'same-subscription-region'],
        ['Blocked / embargo', 'blocked IP · embargoed country', 'blocked-ip-embargo'],
        ['Authorization gap', 'denied · missing · bulk exfiltration', 'authorization-gap']
      ] },
    { type: 'table', title: 'What each injects → what you get', colW: [3.4, 5.6, 3.33], rowH: 0.5, rows: [
        ['Variant', 'Injects', 'Resulting case'],
        ['placeholder “0”', 'USER_PAYMENTS Paid $250, txn “0”, no gateway row', 'Critical · $250 at risk'],
        ['package-mismatch', 'SAVE50 applied to an ineligible package', 'High · discount given away'],
        ['US→KP', 'logs from US then North Korea in the hour', 'High + embargo flag'],
        ['bulk exfiltration', 'Denied authz + 540,000 bytes returned', 'Critical · content leak']
      ] },
    { type: 'bullets', title: '“Run all” — a wave of fraud', lead: 'Fire every variant in a family back-to-back.',
      items: ['Each result lists the exact rows injected and the new case links', 'Click straight through to any case', 'Idempotent — re-running the same data never duplicates cases', { t: 'The dashboard’s $ at risk ticks up live as cases land.', b: true }] },
    { type: 'bullets', title: 'Every case, fully explained', lead: 'Not just a flag.',
      items: ['Expected-vs-actual, evidence timeline, cited AI summary', 'User · payment · subscription (status + last access) · timestamps', 'The applydiscountcode URL the code came from (from the logs)', 'Dev info: the captured request payload + response'] },
    { type: 'cards', title: 'Proof it runs', cards: PROOF },
    { type: 'closing', big: 'Pick any attack. It’s caught, costed, and explained — on one click.' }
  ]
};

// === 4 — WOW MOMENTS + JUDGE Q&A ===
const qa = {
  file: 'demo-4-Wow-Moments-And-QA.pptx', theme: TH.qa, tag: 'USPARK 2026 · DEMO · WOW + Q&A',
  title: 'The moments that\nwin the room',
  subtitle: 'Three things judges remember — and crisp answers to every question they’ll ask.',
  slides: [
    { type: 'title' },
    { type: 'cards', title: 'Three wow moments', cards: [
        { v: '①', l: '“Denied — yet 54,000 bytes left the building.”' },
        { v: '②', l: 'A new fraud rule, authored in English, live.' },
        { v: '③', l: '~½¢ of AI protects $2,503 of revenue.' }
      ], note: 'Land these three and the demo is won. Everything else is supporting evidence.' },
    { type: 'bullets', title: 'Moment 1 — the insight', lead: 'The thing a status code can’t tell you.',
      items: ['Authorization said Denied — but the content was still returned', 'We correlate the decision with the bytes that actually shipped', 'Event-time entitlement checks catch access after a refund that “current state” misses'] },
    { type: 'bullets', title: 'Moment 2 — author a rule on stage', lead: 'No code, no redeploy.',
      items: ['Plain English → AI maps to real fields → asks if unclear → Activate', '“Flag paid orders with no Braintree transaction over $50, excluding trials”', 'Next pass raises a case — a detector that didn’t exist 20 seconds ago'] },
    { type: 'table', title: 'Judge Q&A', colW: [4.6, 7.73], rowH: 0.58, rows: [
        ['They ask', 'You answer'],
        ['Can the AI change anything?', 'No — read-only, no actuators. It explains; humans + .NET authz enforce.'],
        ['What if someone jailbreaks the AI?', 'Nothing to pull. Plus a guard neutralised 3/3 red-team payloads.'],
        ['Is this real data?', 'Synthetic mirror of the real schema; repointable at the dev DB read-only.'],
        ['What does the AI cost?', '~$0.0056 per investigation; detection is free. ~$280/mo at heavy use.'],
        ['Does it scale / dedupe?', 'Idempotent by CaseKey; one evolving case, not thousands of alerts.'],
        ['Why not just rules?', 'Rules flag; the AI explains with cited evidence so humans act in seconds.']
      ] },
    { type: 'bullets', title: 'If something breaks', lead: 'Stay calm, stay credible.',
      items: ['Empty list → click Simulate or run a pass', 'API hiccup → the four HTML explainers carry the story', 'Everything is deterministic — re-running gives the same result'] },
    { type: 'cards', title: 'Close on the numbers', cards: PROOF },
    { type: 'closing', big: 'Three moments, honest answers, real numbers — that’s the win.' }
  ]
};

// === 5 — BEFORE / AFTER + IMPACT ===
const impact = {
  file: 'demo-5-Before-After-Impact.pptx', theme: TH.impact, tag: 'USPARK 2026 · DEMO · IMPACT',
  title: 'Hours → seconds,\nlive',
  subtitle: 'Show the old way, then the new way — and let the dollars make the case.',
  slides: [
    { type: 'title' },
    { type: 'two', title: 'The same abuse — two worlds', fs: 14,
      left: { h: 'Today (manual)', items: ['Notice something odd days later', 'Dig by hand across SQL + New Relic', 'Correlate access, entitlement, payment by eye', 'No dollar figure, no audit trail', 'Damage already done'] },
      right: { h: 'With the platform', items: ['Caught on the next pass (seconds)', 'One evidence-backed case, auto-correlated', 'Expected-vs-actual + cited AI summary', '$ at risk + full audit trail', 'Risk window closed while still open'] } },
    { type: 'steps', title: 'Watch it happen', lead: 'One case, start to finish.',
      items: [
        { t: 'Simulate a fake payment', d: '→ a Critical case appears instantly' },
        { t: 'Open it', d: '→ “Paid $250, no gateway transaction”, with evidence' },
        { t: 'Explain', d: '→ cited AI summary + recommended checks' },
        { t: 'Resolve', d: '→ audited; done in seconds' }
      ] },
    { type: 'cards', title: 'The dollars', cards: PROOF, note: 'One pass surfaced $2,503 of revenue-at-risk; a single AI investigation cost about half a cent.' },
    { type: 'table', title: 'Revenue at risk, by rule (one pass)', colW: [5.3, 3.5, 3.5], rows: [
        ['Rule', 'Cases', '$ at risk'], ['revenue-abuse', '3', '$647.50'], ['content-scraping', '1', '$600.00'], ['entitlement-leakage', '3', '$497.00'], ['paid-without-braintree', '2', '$448.00'], ['coordinated-account', '1', '$410.50'], ['Total', '13', '$2,503.00']] },
    { type: 'bullets', title: 'What it means for UWorld', lead: 'Across B2B and B2C.',
      items: ['Less revenue leakage — caught early, quantified', 'The question banks (the product) protected from theft', 'Analyst hours saved; releases diagnosed faster', 'Trust that helps close B2B deals', { t: 'All on the existing stack. Running today.', b: true }] },
    { type: 'closing', big: 'Hours of manual digging become a reviewed case in seconds — worth thousands.' }
  ]
};

(async () => {
  for (const d of [story, run, scen, qa, impact]) { await build(d); console.log('wrote', d.file); }
})();
