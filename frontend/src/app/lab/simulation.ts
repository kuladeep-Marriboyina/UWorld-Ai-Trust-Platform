import { Observable } from 'rxjs';
import { ApiService, SimIdentity } from '../core/api.service';

export const BOT_UA = 'python-requests/2.31.0';
const FIRST = ['Olivia', 'Liam', 'Aisha', 'Mateo', 'Priya', 'Noah', 'Chen', 'Sofia', 'Kwame', 'Elena', 'Ravi', 'Hannah', 'Diego', 'Fatima', 'Lucas', 'Mei', 'Omar', 'Zara', 'Ethan', 'Nia'];
const LAST = ['Johnson', 'Nguyen', 'Okafor', 'Rossi', 'Iyer', 'Haddad', 'Silva', 'Kowalski', 'Mensah', 'Park', 'Fischer', 'Moreau', 'Reddy', 'Costa', 'Hughes', 'Novak', 'Sato', 'Ali'];
const DOMAINS = ['gmail.com', 'outlook.com', 'yahoo.com', 'icloud.com', 'proton.me', 'hotmail.com'];

export const rnd = (n: number) => Math.floor(Math.random() * n);
export const pick = <T>(a: T[]) => a[rnd(a.length)];
export const hex = (n: number) => Array.from({ length: n }, () => rnd(16).toString(16)).join('');
export const randomIp = (prefix?: string) => prefix ? `${prefix}.${2 + rnd(250)}` : `${24 + rnd(170)}.${rnd(250)}.${rnd(250)}.${2 + rnd(250)}`;
export const newDevice = () => 'dev-' + hex(8);
export const tag = () => hex(3);

/** A believable, distinct person — what real signups look like. */
export function realPerson(): { firstName: string; lastName: string; email: string } {
  const f = pick(FIRST), l = pick(LAST);
  const styles = [`${f}.${l}`, `${f[0]}${l}${1985 + rnd(15)}`, `${f}_${l}`, `${l}.${f}${rnd(90) + 10}`, `${f}${l[0]}.md`];
  return { firstName: f, lastName: l, email: (pick(styles) + '@' + pick(DOMAINS)).toLowerCase() };
}

// ------------------------------------------------------------------ live scenarios

export interface StepResult { ok: boolean; status: number; body: any; }
export interface Step {
  label: string;
  actor: string;              // who is acting, in plain words
  identity: SimIdentity;
  request: string;            // e.g. POST /api/storefront/signup
  payload: any;
  run: () => Observable<any>;
  result?: StepResult;
  state?: 'pending' | 'running' | 'done' | 'error';
}
export interface Scenario {
  key: string; icon: string; title: string; kind: 'attack' | 'legit';
  summary: string; expect: string; proves: string;
  meter: 'farm' | 'stuffing';
  build: (api: ApiService) => Step[];
}

function signup(api: ApiService, id: SimIdentity, person: any, actor: string, label?: string): Step {
  const payload = { ...person, password: person.password || 'Study2026!', country: id.country };
  return { label: label || `Sign up ${person.email}`, actor, identity: id, request: 'POST /api/storefront/signup', payload, run: () => api.sfSignup(payload, id) };
}
function subscribe(api: ApiService, id: SimIdentity, userRef: () => number, body: any, actor: string, label: string): Step {
  const step: Step = { label, actor, identity: id, request: 'POST /api/storefront/subscribe', payload: body, run: () => api.sfSubscribe({ ...body, userId: userRef() }, id) };
  return step;
}
function login(api: ApiService, id: SimIdentity, email: string, password: string, actor: string, label: string): Step {
  const payload = { email, password };
  return { label, actor, identity: id, request: 'POST /api/storefront/login', payload, run: () => api.sfLogin(payload, id) };
}

export const SCENARIOS: Scenario[] = [
  {
    key: 'bot-farm', icon: '🤖', title: 'Trial farm from one machine', kind: 'attack', meter: 'farm',
    summary: 'A script on one laptop creates accounts with numbered Gmail addresses from the same IP and grabs a free trial on each.',
    expect: 'Risk climbs with each signup; a case is raised the moment the third linked account appears, then grows as more join.',
    proves: 'Detection happens during the attack — before the farm finishes — and the case evolves instead of duplicating.',
    build: (api) => {
      const id: SimIdentity = { ip: randomIp('185.220.101'), device: newDevice(), country: 'US', ua: BOT_UA };
      const base = pick(['jake.miller', 'study.pro', 'med.prep', 'nclex.ace']); const t = tag();
      const steps: Step[] = [];
      for (let i = 1; i <= 5; i++) {
        const email = `${base}${t}${String(i).padStart(2, '0')}@gmail.com`;
        const s = signup(api, id, { firstName: 'Jake', lastName: 'Miller', email }, 'Attacker script', `Create account #${i}: ${email}`);
        steps.push(s);
        steps.push(subscribe(api, id, () => s.result?.body?.userId, { packageId: 11, trial: true }, 'Attacker script', `Account #${i} starts a free Step 2 CK trial`));
      }
      return steps;
    }
  },
  {
    key: 'card-ring', icon: '💳', title: 'VPN-hopping ring, one card', kind: 'attack', meter: 'farm',
    summary: 'The attacker rotates IP and device for every account (so no network link exists), uses throwaway inboxes — but pays for every account with the same card and a discount code.',
    expect: 'No IP or device ties the accounts together. After the third payment, the shared card links them and a case is raised.',
    proves: 'Linking goes beyond IPs: payment instruments, email identity and discount harvesting expose rings that rotate networks.',
    build: (api) => {
      const steps: Step[] = []; const t = tag();
      const inboxes = ['qx7' + t, 'zk9' + t, 'mv4' + t, 'pl2' + t];
      inboxes.forEach((box, i) => {
        const id: SimIdentity = { ip: randomIp(), device: newDevice(), country: pick(['US', 'CA', 'GB', 'NL']) };
        const email = `${box}${rnd(90) + 10}@${pick(['mailinator.com', 'yopmail.com', 'guerrillamail.com'])}`;
        const s = signup(api, id, { firstName: pick(FIRST), lastName: pick(LAST), email }, `Attacker via VPN exit ${i + 1}`, `Account #${i + 1} from a new IP & device: ${email}`);
        steps.push(s);
        steps.push(subscribe(api, id, () => s.result?.body?.userId, { packageId: 12, trial: false, cardNumber: '4000 0566 5566 5556', discountCode: 'WELCOME10' },
          `Attacker via VPN exit ${i + 1}`, `Account #${i + 1} buys NCLEX with card •••• 5556 + code WELCOME10`));
      });
      return steps;
    }
  },
  {
    key: 'stuffing', icon: '🔐', title: 'Credential stuffing → takeover', kind: 'attack', meter: 'stuffing',
    summary: 'A real student has an account. An attacker replays a leaked password list against many emails from one source — and one of them works.',
    expect: 'After 8 failures across 5+ accounts a High case is raised; the moment a login succeeds it escalates to Critical “account takeover”.',
    proves: 'Sentinel tells a forgotten password apart from an attack, and escalates the instant damage begins.',
    build: (api) => {
      const t = tag();
      const victim = { firstName: 'Grace', lastName: 'Okafor', email: `grace.okafor.${t}@outlook.com`, password: 'Sunrise#2024' };
      const victimId: SimIdentity = { ip: randomIp(), device: newDevice(), country: 'US' };
      const attacker: SimIdentity = { ip: randomIp('45.155.205'), device: newDevice(), country: 'NL', ua: BOT_UA };
      const steps: Step[] = [signup(api, victimId, victim, 'Real student (Grace)', `Grace creates her account from home`)];
      const leaked = ['amara.n', 'tom.becker', 'lena.fox', 'r.gupta', 'kim.l', 'dan.wolfe', 'ivy.reed', 'sam.cole', 'nora.b'];
      leaked.forEach((u, i) => steps.push(login(api, attacker, `${u}${t}@${pick(DOMAINS)}`, 'Sunrise#2024', 'Attacker (leaked list)', `Attempt ${i + 1}: ${u}… with leaked password`)));
      steps.push(login(api, attacker, victim.email, victim.password, 'Attacker (leaked list)', `Attempt 10: Grace's email — the leaked password works`));
      return steps;
    }
  },
  {
    key: 'students', icon: '🎓', title: 'Real students signing up', kind: 'legit', meter: 'farm',
    summary: 'Five unrelated students sign up from their own homes and devices; two buy a plan with their own cards, one takes a trial.',
    expect: 'Every step comes back clean. No case, no alert — legitimate customers are never slowed down.',
    proves: 'Precision: Sentinel only raises cases for corroborated abuse, so analysts aren\'t buried in noise.',
    build: (api) => {
      const steps: Step[] = [];
      for (let i = 0; i < 5; i++) {
        const id: SimIdentity = { ip: randomIp(), device: newDevice(), country: pick(['US', 'US', 'IN', 'CA', 'GB']) };
        const p = realPerson(); const s = signup(api, id, p, `${p.firstName} at home`, `${p.firstName} ${p.lastName} signs up: ${p.email}`);
        steps.push(s);
        if (i === 1 || i === 3) {
          steps.push(subscribe(api, id, () => s.result?.body?.userId, { packageId: pick([10, 11, 40]), trial: false, cardNumber: '4' + Array.from({ length: 15 }, () => rnd(10)).join('') },
            `${p.firstName} at home`, `${p.firstName} buys a plan with their own card`));
        }
        if (i === 2) { steps.push(subscribe(api, id, () => s.result?.body?.userId, { packageId: 10, trial: true }, `${p.firstName} at home`, `${p.firstName} starts a free trial`)); }
      }
      return steps;
    }
  },
  {
    key: 'campus', icon: '🏫', title: 'Campus cohort on one IP', kind: 'legit', meter: 'farm',
    summary: 'Six medical students enrol together from the university network — one shared public IP — each on their own laptop with their own name.',
    expect: 'Sentinel notices the shared IP (“watching”) but raises no case: nothing about the identities corroborates abuse.',
    proves: 'A shared IP alone never convicts anyone — the classic false positive of naive fraud rules is designed out.',
    build: (api) => {
      const ip = randomIp('128.95.40'); const steps: Step[] = [];
      for (let i = 0; i < 6; i++) {
        const p = realPerson();
        const email = `${p.firstName[0]}${p.lastName}${rnd(9)}`.toLowerCase() + '@med.state-university.edu';
        steps.push(signup(api, { ip, device: newDevice(), country: 'US' }, { ...p, email }, `Student ${i + 1} on campus Wi-Fi`, `${p.firstName} ${p.lastName} enrols from the campus network`));
      }
      return steps;
    }
  },
];
