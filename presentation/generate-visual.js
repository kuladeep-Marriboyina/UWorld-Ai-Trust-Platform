/* Visual 16-slide deck: real app screenshots + native charts + diagrams + data + money. */
const PptxGenJS = require('pptxgenjs');
const TEAM = 'Support Sparkers · Prakash Kosuri · Kuladeep M · Vishnu';
const T = { bar: '0B3B8C', bar2: '0B5FFF', accent: '00B8A9', ink: '13233B', muted: '5A6B80', line: 'E2E8F3',
  crit: 'E4322B', high: 'F06A1E', med: 'F5B301', ok: '1F9D57', gold: 'F5B301', purple: '6A2CF5' };
const W = 13.33, H = 7.5;

const p = new PptxGenJS();
p.layout = 'LAYOUT_WIDE';
p.author = 'Support Sparkers'; p.company = 'UWorld';

let PAGE = 1;
function foot(s) {
  PAGE++;
  s.addText('UWorld AI Trust & Revenue Protection · USpark 2026', { x: 0.5, y: 7.06, w: 10, h: 0.3, fontSize: 9, color: 'A7B2C2' });
  s.addText(String(PAGE), { x: 12.4, y: 7.06, w: 0.5, h: 0.3, fontSize: 9, color: 'A7B2C2', align: 'right' });
}
function header(s, title, kicker) {
  s.addShape(p.ShapeType.rect, { x: 0, y: 0, w: W, h: 0.9, fill: { color: T.bar } });
  s.addShape(p.ShapeType.rect, { x: 0, y: 0.9, w: W, h: 0.06, fill: { color: T.accent } });
  s.addText(title, { x: 0.5, y: 0.14, w: 10.5, h: 0.62, fontSize: 24, bold: true, color: 'FFFFFF' });
  if (kicker) s.addText(kicker, { x: 10.5, y: 0.3, w: 2.4, h: 0.4, fontSize: 11, color: 'D7E2F5', align: 'right' });
}
function slide() { const s = p.addSlide(); s.background = { color: 'FFFFFF' }; return s; }

// ---------------- 1. TITLE ----------------
(() => {
  const s = p.addSlide(); s.background = { color: T.bar };
  s.addShape(p.ShapeType.rect, { x: 0, y: 4.0, w: W, h: 0.09, fill: { color: T.accent } });
  s.addText('USPARK 2026 · PRESENTATION', { x: 0.8, y: 1.4, w: 11, h: 0.4, fontSize: 13, color: T.accent, charSpacing: 2 });
  s.addText('UWorld AI Trust &\nRevenue Protection', { x: 0.8, y: 1.9, w: 11.8, h: 1.9, fontSize: 42, bold: true, color: 'FFFFFF', lineSpacingMultiple: 0.95 });
  s.addText('A read-only, AI-assisted platform that protects access, subscriptions, content and revenue — turning hours of manual investigation into seconds.',
    { x: 0.8, y: 4.3, w: 11.4, h: 1.2, fontSize: 17, color: 'D7E2F5' });
  s.addText(TEAM, { x: 0.8, y: 6.5, w: 11.5, h: 0.4, fontSize: 12, color: 'AEBFD9' });
})();

// ---------------- 2. PROBLEM TILES ----------------
(() => {
  const s = slide(); header(s, 'The problem — six ways the promise breaks', 'why now'); foot(s, 2);
  s.addText('“The right people get the right content, for the right product, at the right price” — increasingly hard to keep, and abuse is found days late.',
    { x: 0.5, y: 1.1, w: 12.3, h: 0.5, fontSize: 14, italic: true, color: T.muted });
  const items = [
    { i: '🔓', t: 'Access & authorization gaps', d: 'A status code never says if content actually left the building' },
    { i: '⏳', t: 'Entitlement leakage', d: 'Access after expiry, cancellation or refund' },
    { i: '💸', t: 'Revenue & discount abuse', d: 'Zero-pay orders, below-policy pricing, reused codes' },
    { i: '📚', t: 'Content scraping', d: 'Automated harvesting of the question banks' },
    { i: '👥', t: 'Coordinated accounts', d: 'Shared logins & multi-account patterns' },
    { i: '💳', t: 'Payment-integrity gaps', d: 'Paid orders that never matched the Braintree gateway' }
  ];
  const cols = 3, cw = 3.95, ch = 2.15, gx = 0.2, gy = 0.22, x0 = 0.5, y0 = 1.75;
  items.forEach((it, k) => {
    const x = x0 + (k % cols) * (cw + gx), y = y0 + Math.floor(k / cols) * (ch + gy);
    s.addShape(p.ShapeType.roundRect, { x, y, w: cw, h: ch, rectRadius: 0.08, fill: { color: 'F5F8FD' }, line: { color: T.line } });
    s.addShape(p.ShapeType.rect, { x, y, w: 0.1, h: ch, fill: { color: T.bar2 } });
    s.addText(it.i, { x: x + 0.2, y: y + 0.2, w: 0.8, h: 0.6, fontSize: 24 });
    s.addText(it.t, { x: x + 0.95, y: y + 0.25, w: cw - 1.1, h: 0.7, fontSize: 14.5, bold: true, color: T.bar });
    s.addText(it.d, { x: x + 0.25, y: y + 1.05, w: cw - 0.5, h: 0.95, fontSize: 12, color: T.ink });
  });
})();

// ---------------- 3. ARCHITECTURE DIAGRAM ----------------
(() => {
  const s = slide(); header(s, 'How it works — modular monolith', 'architecture'); foot(s, 3);
  s.addText('“Reuses the existing stack, no new microservice fleet.”', { x: 0.5, y: 1.1, w: 12, h: 0.4, fontSize: 14, italic: true, color: T.muted });
  const box = (x, y, w, h, title, sub, fill, tc) => {
    s.addShape(p.ShapeType.roundRect, { x, y, w, h, rectRadius: 0.07, fill: { color: fill }, line: { color: T.line } });
    s.addText(title, { x: x + 0.1, y: y + 0.12, w: w - 0.2, h: 0.4, fontSize: 13, bold: true, color: tc || T.bar, align: 'center' });
    if (sub) s.addText(sub, { x: x + 0.12, y: y + 0.5, w: w - 0.24, h: h - 0.6, fontSize: 10.5, color: tc ? 'E8F6FF' : T.muted, align: 'center' });
  };
  const arrow = (x, y, w) => s.addShape(p.ShapeType.rightArrow, { x, y, w, h: 0.3, fill: { color: T.accent } });
  box(0.6, 2.0, 2.6, 1.3, 'SQL Server + New Relic', 'orders · payments · entitlements · auth · access logs', 'EEF4FF');
  arrow(3.3, 2.5, 0.5);
  box(3.9, 2.0, 2.5, 1.3, 'IngestionWorker', 'BackgroundService · normalises · correlation IDs', T.bar, 'FFFFFF');
  arrow(6.5, 2.5, 0.5);
  box(7.1, 2.0, 2.6, 1.3, 'DetectionRuleEngine', '11 versioned, idempotent rules', T.bar, 'FFFFFF');
  arrow(9.8, 2.5, 0.5);
  box(10.4, 2.0, 2.4, 1.3, 'RiskCases (SQL)', '+ evidence · $ impact · audit', 'EEF4FF');
  // bottom row
  box(3.9, 4.1, 2.9, 1.3, 'REST API', 'cases · rules · dashboard · usage · simulate', 'F5F8FD');
  box(7.1, 4.1, 2.9, 1.3, 'AI copilot (ICopilot)', 'cited summary · redaction · injection guard', 'E9FBF6');
  box(10.3, 4.1, 2.5, 1.3, 'Angular workspace', 'dashboard · cases · rules portal', 'F5F8FD');
  s.addShape(p.ShapeType.upArrow, { x: 5.1, y: 3.35, w: 0.3, h: 0.7, fill: { color: T.accent } });
  s.addText('AI explains · humans decide · the existing .NET authorization enforces (single source of truth).',
    { x: 0.6, y: 5.7, w: 12, h: 0.6, fontSize: 13, bold: true, color: T.bar2, align: 'center' });
})();

// ---------------- 4. CLOSED LOOP CHEVRON ----------------
(() => {
  const s = slide(); header(s, 'The closed loop — every abuse type', 'method'); foot(s, 4);
  const steps = [['Detect', 'deterministic rule'], ['Explain', 'AI copilot, cited'], ['Decide', 'analyst reviews'], ['Act', 'human + .NET authz'], ['Audit', 'every step logged']];
  const cw = 2.3, h = 1.5, y = 2.6, gap = 0.18; let x = 0.55;
  steps.forEach((st, k) => {
    const fill = k === 0 ? T.bar : k === 1 ? '1C58B0' : k === 2 ? '2E6FC7' : k === 3 ? '0E8C7E' : '0B7A6C';
    s.addShape(p.ShapeType.pentagon, { x, y, w: cw, h, fill: { color: fill } });
    s.addText(st[0], { x: x, y: y + 0.4, w: cw - 0.2, h: 0.4, fontSize: 17, bold: true, color: 'FFFFFF', align: 'center' });
    s.addText(st[1], { x: x, y: y + 0.85, w: cw - 0.2, h: 0.4, fontSize: 11, color: 'E8F1FF', align: 'center' });
    x += cw + gap;
  });
  s.addText('Deterministic detection is free. The AI is a thin EXPLAIN layer — it never enforces.',
    { x: 0.6, y: 4.6, w: 12, h: 0.5, fontSize: 14, color: T.muted, align: 'center' });
})();

// ---------------- helper: screenshot slide ----------------
function shot(n, title, caption, img, notes) {
  const s = slide(); header(s, title, 'live screen'); foot(s, n);
  s.addText(caption, { x: 0.5, y: 1.08, w: 12.3, h: 0.5, fontSize: 14, color: T.muted });
  s.addShape(p.ShapeType.rect, { x: 0.5, y: 1.62, w: 9.3, h: 5.15, fill: { color: 'F2F5FA' }, line: { color: T.line } });
  s.addImage({ path: 'img/' + img, x: 0.6, y: 1.72, w: 9.1, h: 4.95, sizing: { type: 'contain', w: 9.1, h: 4.95 } });
  // notes panel
  s.addShape(p.ShapeType.roundRect, { x: 10.05, y: 1.62, w: 2.78, h: 5.15, rectRadius: 0.08, fill: { color: T.bar } });
  s.addText('What you see', { x: 10.25, y: 1.8, w: 2.4, h: 0.4, fontSize: 13, bold: true, color: T.accent });
  s.addText(notes.map(t => ({ text: t, options: { bullet: { indent: 12 }, color: 'EAF2FF', fontSize: 11.5, paraSpaceAfter: 7 } })),
    { x: 10.25, y: 2.25, w: 2.45, h: 4.4, valign: 'top' });
}

// 5. dashboard shot
shot(5, 'Live dashboard', 'Top-50 active users & subscriptions, fraud patterns and alerts — auto-refreshing every 5 seconds.',
  'dashboard.png', ['Top active users grid', 'Access count + region', 'Fraud patterns by rule', 'Alerts + compromise feed']);

// 5b. ACTIVE USERS / SUBSCRIPTIONS GRID (real data)
(() => {
  const s = slide(); header(s, 'Active subscriptions & users — live', 'who’s on right now'); foot(s);
  s.addText('The top subscriptions/users accessed in the last 15 minutes — access count, distinct regions and last-access time. Multi-region rows (⚠) are exactly what the same-subscription-region rule flags.',
    { x: 0.5, y: 1.08, w: 12.3, h: 0.62, fontSize: 13, color: T.muted });
  const data = [
    ['Subscription', 'User', 'Accesses', 'Regions', 'Last region', 'Last access (UTC)'],
    ['5002', '113', '9', '1', 'US', '06:12:37'], ['5003', '103', '8', '1', 'US', '06:12:31'],
    ['5004', '104', '8', '1', 'US', '06:12:25'], ['5005', '105', '8', '1', 'IN', '06:12:19'],
    ['5006', '106', '8', '1', 'US', '06:12:13'], ['5007', '107', '8', '1', 'US', '06:12:07'],
    ['5009', '109', '8', '1', 'US', '06:12:31'], ['5010', '110', '8', '2 ⚠', 'US', '06:12:34'],
    ['5001', '101', '8', '2 ⚠', 'US', '06:11:31'], ['5012', '114', '8', '2 ⚠', 'GB', '06:11:37'],
    ['5011', '111', '7', '1', 'US', '06:11:43'], ['5008', '108', '7', '1', 'US', '06:12:01']
  ];
  const head = data[0].map(c => ({ text: c, options: { bold: true, color: 'FFFFFF', fill: { color: T.bar }, fontSize: 12, align: 'center' } }));
  const body = data.slice(1).map((r, ri) => r.map((c, ci) => ({
    text: c, options: { fontSize: 12.5, color: (String(c).indexOf('⚠') >= 0 ? T.high : T.ink), bold: String(c).indexOf('⚠') >= 0,
      fill: { color: ri % 2 ? 'F3F7FF' : 'FFFFFF' }, align: ci === 0 || ci === 1 ? 'center' : (ci >= 2 && ci <= 3 ? 'center' : 'left') }
  })));
  s.addTable([head, ...body], { x: 0.5, y: 1.85, w: 8.6, colW: [1.7, 1.1, 1.5, 1.4, 1.6, 1.3], border: { type: 'solid', color: 'E2E8F3', pt: 1 }, rowH: 0.36, valign: 'middle' });
  // side callouts
  const notes = [['12', 'active subscriptions'], ['95+', 'access events / 15 min'], ['3', 'multi-region (⚠) sessions'], ['Top 50', 'ranked by access count']];
  let y = 1.85;
  notes.forEach(c => {
    s.addShape(p.ShapeType.roundRect, { x: 9.35, y, w: 3.48, h: 1.08, rectRadius: 0.07, fill: { color: 'F3F7FF' }, line: { color: T.accent } });
    s.addText(c[0], { x: 9.45, y: y + 0.12, w: 1.3, h: 0.82, fontSize: 22, bold: true, color: T.bar, align: 'center', valign: 'middle' });
    s.addText(c[1], { x: 10.65, y: y + 0.12, w: 2.1, h: 0.82, fontSize: 12, color: T.ink, valign: 'middle' });
    y += 1.2;
  });
})();

// ---------------- 6. CHART — $ at risk by rule ----------------
(() => {
  const s = slide(); header(s, 'Fraud patterns — revenue at risk by rule', 'data · money'); foot(s, 6);
  s.addText('Every case quantifies the revenue it puts at risk. One pass, in dollars:', { x: 0.5, y: 1.1, w: 12, h: 0.5, fontSize: 14, color: T.muted });
  const labels = ['revenue-abuse', 'discount-fraud', 'content-scraping', 'entitlement', 'paid-no-braintree', 'coordinated', 'authz-gap'];
  const values = [647.5, 606.95, 600, 497, 448, 410.5, 348];
  s.addChart(p.ChartType.bar, [{ name: '$ at risk', labels, values }], {
    x: 0.5, y: 1.7, w: 9.2, h: 4.9, barDir: 'bar', chartColors: [T.bar2],
    showValue: true, dataLabelColor: '333333', dataLabelFontSize: 10, valAxisHidden: true,
    catAxisLabelColor: '333333', catAxisLabelFontSize: 11, showLegend: false, valGridLine: { style: 'none' }
  });
  const cards = [['$3,558', '7 rules shown'], ['$2,503', 'core 13-case pass'], ['per case', 'impact is explained']];
  let y = 1.8;
  cards.forEach(c => {
    s.addShape(p.ShapeType.roundRect, { x: 10.05, y, w: 2.78, h: 1.45, rectRadius: 0.08, fill: { color: 'F3F7FF' }, line: { color: T.accent } });
    s.addText(c[0], { x: 10.05, y: y + 0.18, w: 2.78, h: 0.6, fontSize: 24, bold: true, color: T.bar, align: 'center' });
    s.addText(c[1], { x: 10.15, y: y + 0.85, w: 2.58, h: 0.45, fontSize: 11, color: T.muted, align: 'center' });
    y += 1.62;
  });
})();

// ---------------- 7. CHART — severity doughnut ----------------
(() => {
  const s = slide(); header(s, 'Case mix & confidence', 'data'); foot(s, 7);
  s.addChart(p.ChartType.doughnut, [{ name: 'Severity', labels: ['Critical', 'High', 'Medium'], values: [6, 13, 4] }], {
    x: 0.6, y: 1.5, w: 6, h: 5, holeSize: 55, chartColors: [T.crit, T.high, T.med],
    showLegend: true, legendPos: 'r', legendColor: '333333', legendFontSize: 12,
    showValue: true, dataLabelColor: 'FFFFFF', dataLabelFontSize: 12, title: '', showTitle: false
  });
  s.addText('Severity drives the queue', { x: 6.9, y: 1.7, w: 6, h: 0.5, fontSize: 18, bold: true, color: T.bar });
  s.addText([
    { text: 'Cases ranked by score & severity, so Critical authorization gaps and scraping surface first.', options: { fontSize: 14, color: T.ink, paraSpaceAfter: 10 } },
    { text: 'Each case carries a transparent confidence (e.g. authorization-gap 0.97, discount-fraud 0.86) and a weighted score breakdown — never one opaque number.', options: { fontSize: 14, color: T.ink, paraSpaceAfter: 10 } },
    { text: 'Idempotent by CaseKey: repeated abuse becomes one evolving case, not thousands of alerts.', options: { fontSize: 14, color: T.ink } }
  ], { x: 6.9, y: 2.3, w: 5.9, h: 4.2, valign: 'top' });
})();

// 8. cases shot
shot(8, 'The case queue', 'Filterable, severity-ranked — each row shows the rule, score, status and the $ at risk (with an info popover).',
  'cases.png', ['Filter by rule / severity', 'Severity + score chips', '$ at risk per case', 'Click through to detail']);

// 9. case detail shot
shot(9, 'Inside a case', 'Expected-vs-actual, a weighted “why flagged”, an evidence timeline, the AI copilot, and a collapsed Dev info.',
  'case-detail.png', ['Expected vs actual', 'Evidence timeline', 'AI copilot (cited)', 'Dev info: payload + response', 'Assign / escalate / resolve']);

// ---------------- 10. RULES TILES ----------------
(() => {
  const s = slide(); header(s, 'Eleven detection rules', 'coverage'); foot(s, 10);
  const items = [
    ['🔓', 'authorization-gap'], ['⏳', 'entitlement-leakage'], ['💸', 'revenue-abuse'], ['📚', 'content-scraping'],
    ['👥', 'coordinated-account'], ['🛡️', 'cve-exposure'], ['💳', 'paid-without-braintree'], ['🏷️', 'discount-code-fraud'],
    ['🌍', 'same-subscription-region'], ['🚫', 'blocked-ip-embargo'], ['✨', 'NL-authored (plain English)']
  ];
  const cols = 4, cw = 2.95, ch = 1.35, gx = 0.15, gy = 0.2, x0 = 0.5, y0 = 1.35;
  items.forEach((it, k) => {
    const x = x0 + (k % cols) * (cw + gx), y = y0 + Math.floor(k / cols) * (ch + gy);
    const custom = it[0] === '✨';
    s.addShape(p.ShapeType.roundRect, { x, y, w: cw, h: ch, rectRadius: 0.08, fill: { color: custom ? 'E9FBF6' : 'F5F8FD' }, line: { color: custom ? T.accent : T.line } });
    s.addText(it[0], { x: x + 0.15, y: y + 0.32, w: 0.8, h: 0.7, fontSize: 22 });
    s.addText(it[1], { x: x + 0.95, y: y + 0.42, w: cw - 1.05, h: 0.6, fontSize: 12.5, bold: true, color: T.bar });
  });
  s.addText('4 deep fraud rules grounded in the real schema (USER_PAYMENTS, BRAINTREE_TRANSACTIONS, NCLEX_REFERRAL_CODES, GA_DISCOUNTS, USER_SUBSCRIPTIONS, USER_LOGS) + an engine for plain-English rules.',
    { x: 0.5, y: 6.1, w: 12.3, h: 0.7, fontSize: 12.5, italic: true, color: T.muted });
})();

// 11. rules portal shot
shot(11, 'Rules Portal', 'Read any rule’s logic, thresholds and DB schema fields — or add a new one in plain English.',
  'rules.png', ['Select to read a rule', 'What it checks + thresholds', 'DB schema fields', 'Enable / disable toggle', '+ Add rule (NL wizard)']);

// ---------------- 12. NL AUTHORING FLOW ----------------
(() => {
  const s = slide(); header(s, 'Plain English → live rule, no redeploy', 'the standout'); foot(s, 12);
  const steps = [
    ['① Describe', '“Flag any order paid but with no Braintree transaction, over $50, excluding trials”'],
    ['② Map & ask', 'AI maps to USER_PAYMENTS.PaymentStatus=1, TransactionId∈{0,0000}, FinalAmount>50 — asks “exclude $0 store-credit?”'],
    ['③ Activate', 'Confirm → persisted → the next detection pass raises a case. A detector authored on stage.']
  ];
  let y = 1.5;
  steps.forEach((st, k) => {
    s.addShape(p.ShapeType.roundRect, { x: 0.8, y, w: 11.7, h: 1.45, rectRadius: 0.08, fill: { color: 'F5F8FD' }, line: { color: T.line } });
    s.addShape(p.ShapeType.rect, { x: 0.8, y, w: 0.12, h: 1.45, fill: { color: [T.bar2, T.accent, T.ok][k] } });
    s.addText(st[0], { x: 1.1, y: y + 0.2, w: 2.6, h: 0.9, fontSize: 18, bold: true, color: T.bar });
    s.addText(st[1], { x: 3.7, y: y + 0.2, w: 8.6, h: 1.05, fontSize: 14, color: T.ink, valign: 'middle' });
    y += 1.68;
  });
})();

// ---------------- 13. COST & ROI ----------------
(() => {
  const s = slide(); header(s, 'Cost & ROI — a few cents protects thousands', 'data · money'); foot(s, 13);
  s.addChart(p.ChartType.bar, [{ name: 'AI spend', labels: ['1k', '10k', '100k', '1M'], values: [5.64, 56.43, 564.3, 5643] }], {
    x: 0.5, y: 1.7, w: 7.2, h: 4.8, barDir: 'col', chartColors: [T.accent], showValue: true, dataLabelColor: '333333', dataLabelFontSize: 10,
    catAxisTitle: 'AI investigations', showCatAxisTitle: true, catAxisTitleColor: T.muted, catAxisTitleFontSize: 11,
    valAxisHidden: true, showLegend: false, catAxisLabelColor: '333333'
  });
  const cards = [['$0.0056', 'per investigation (221/332 tokens)'], ['$2,503', 'revenue-at-risk / pass'], ['~443,000×', 'return (business $ ÷ AI $)'], ['~$280 / mo', 'heavy daily use, all teams']];
  let y = 1.7;
  cards.forEach(c => {
    s.addShape(p.ShapeType.roundRect, { x: 8.0, y, w: 4.83, h: 1.1, rectRadius: 0.07, fill: { color: 'F3F7FF' }, line: { color: T.accent } });
    s.addText(c[0], { x: 8.1, y: y + 0.12, w: 1.9, h: 0.85, fontSize: 22, bold: true, color: T.bar, align: 'center', valign: 'middle' });
    s.addText(c[1], { x: 10.0, y: y + 0.15, w: 2.75, h: 0.8, fontSize: 12, color: T.ink, valign: 'middle' });
    y += 1.22;
  });
})();

// ---------------- 14. RESPONSIBLE AI TILES ----------------
(() => {
  const s = slide(); header(s, 'Responsible AI — safe to deploy', 'governance'); foot(s, 14);
  const items = [
    ['🚫', 'No actuators', 'The AI can’t change access, accounts or payments'],
    ['🧼', 'Redaction', 'Emails & IPs stripped in code before the model sees them'],
    ['🛡️', 'Injection guard', '3/3 red-team “hack us” payloads neutralised'],
    ['📜', 'Full audit trail', 'Every case, action & AI call logged with cost'],
    ['👤', 'Human-approved', 'RBAC-gated; .NET authorization stays the source of truth'],
    ['∞', 'Always-on', '24/7 worker; idempotent — no alert fatigue']
  ];
  const cols = 3, cw = 3.95, ch = 2.15, gx = 0.2, gy = 0.22, x0 = 0.5, y0 = 1.3;
  items.forEach((it, k) => {
    const x = x0 + (k % cols) * (cw + gx), y = y0 + Math.floor(k / cols) * (ch + gy);
    s.addShape(p.ShapeType.roundRect, { x, y, w: cw, h: ch, rectRadius: 0.08, fill: { color: 'E9FBF6' }, line: { color: T.accent } });
    s.addText(it[0], { x: x + 0.2, y: y + 0.2, w: 0.9, h: 0.7, fontSize: 24 });
    s.addText(it[1], { x: x + 0.95, y: y + 0.28, w: cw - 1.1, h: 0.6, fontSize: 15, bold: true, color: '0B5C50' });
    s.addText(it[2], { x: x + 0.25, y: y + 1.0, w: cw - 0.5, h: 1.0, fontSize: 12, color: T.ink });
  });
})();

// ---------------- 15. PROOF ----------------
(() => {
  const s = slide(); header(s, 'Proof it runs — verified', 'not a slide'); foot(s, 15);
  const cards = [['11', 'rules firing live'], ['$2,503', 'revenue-at-risk / pass'], ['$0.0056', 'AI cost / case'], ['~443,000×', 'measured ROI']];
  const cw = 2.95, gap = 0.17; let x = 0.5;
  cards.forEach(c => {
    s.addShape(p.ShapeType.roundRect, { x, y: 1.3, w: cw, h: 1.7, rectRadius: 0.1, fill: { color: T.bar } });
    s.addText(c[0], { x, y: 1.5, w: cw, h: 0.8, fontSize: 30, bold: true, color: 'FFFFFF', align: 'center' });
    s.addText(c[1], { x: x + 0.1, y: 2.35, w: cw - 0.2, h: 0.5, fontSize: 12, color: 'D7E2F5', align: 'center' });
    x += cw + gap;
  });
  const rows = [
    ['Checked', 'Result'],
    ['All rules fire on seeded data', '13 → 27 cases'],
    ['Prompt-injection payloads neutralised', '3 / 3'],
    ['RBAC — read-only blocked from resolving', 'HTTP 403'],
    ['Live simulate spawns a real case', 'traced (payload + response)'],
    ['Plain-English rule → live, no redeploy', 'verified'],
    ['Backend + frontend build', '0 errors']
  ];
  const head = rows[0].map(c => ({ text: c, options: { bold: true, color: 'FFFFFF', fill: { color: T.bar }, fontSize: 12 } }));
  const body = rows.slice(1).map((r, ri) => r.map(c => ({ text: c, options: { fontSize: 12.5, color: T.ink, fill: { color: ri % 2 ? 'F3F7FF' : 'FFFFFF' } } })));
  s.addTable([head, ...body], { x: 0.5, y: 3.3, w: 12.33, colW: [8.0, 4.33], border: { type: 'solid', color: 'E2E8F3', pt: 1 }, rowH: 0.42, valign: 'middle' });
})();

// ---------------- 16. CLOSING ----------------
(() => {
  const s = p.addSlide(); s.background = { color: T.bar };
  s.addShape(p.ShapeType.rect, { x: 0, y: 3.5, w: W, h: 0.08, fill: { color: T.accent } });
  s.addText('Protects the revenue and the IP UWorld sells —\nproves its own ROI in dollars — and it works today.',
    { x: 1, y: 2.3, w: 11.3, h: 1.8, fontSize: 28, bold: true, color: 'FFFFFF', align: 'center', lineSpacingMultiple: 1.05 });
  s.addText('On the existing stack · B2B and B2C · read-only, advisory, governed by design.',
    { x: 1, y: 4.4, w: 11.3, h: 0.6, fontSize: 16, color: 'D7E2F5', align: 'center' });
  s.addText(TEAM, { x: 1, y: 6.4, w: 11.3, h: 0.4, fontSize: 12, color: 'AEBFD9', align: 'center' });
})();

p.writeFile({ fileName: 'UWorld-AI-Trust-VISUAL.pptx' }).then(f => console.log('wrote', f));
