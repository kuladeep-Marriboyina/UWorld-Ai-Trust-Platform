import { SimIdentity } from '../core/api.service';
import { BOT_UA, hex, newDevice, pick, randomIp, realPerson, rnd, tag } from './simulation';

/** What a scenario script can make the storefront do — every action is visible on screen. */
export interface Director {
  say(caption: string, detail?: string): Promise<void>;
  become(identity: SimIdentity, who: string): Promise<void>;
  signup(p: { firstName: string; lastName: string; email: string; password?: string }): Promise<any>;
  startTrial(packageId: number): Promise<any>;
  buy(packageId: number, card: string, code?: string): Promise<any>;
  login(email: string, password: string): Promise<any>;
  openContent(packageId: number, questionId: number, channel: 'app' | 'export' | 'cache', quiet?: boolean): Promise<any>;
  progress(done: number, total: number, label: string): void;
}

export interface StoreParam { key: string; label: string; min: number; max: number; value: number; }

export interface StoreScenario {
  key: string; icon: string; title: string; kind: 'attack' | 'legit';
  glossary?: string;              // glossary key explaining the attack
  story: string;                  // how it happens in the real world
  expect: string;                 // what Sentinel should do
  params: StoreParam[];
  run: (d: Director, p: { [k: string]: number }) => Promise<void>;
}

const LEAKED = ['amara.n', 'tom.becker', 'lena.fox', 'r.gupta', 'kim.l', 'dan.wolfe', 'ivy.reed', 'sam.cole', 'nora.b', 'leo.m', 'zoe.h', 'ben.k', 'ana.p', 'eli.v'];
const DOMAINS = ['gmail.com', 'outlook.com', 'yahoo.com', 'icloud.com'];

export const STORE_SCENARIOS: StoreScenario[] = [
  {
    key: 'farm', icon: '🤖', title: 'Bot creates many trial accounts', kind: 'attack', glossary: 'accountFarm',
    story: 'A script on one laptop signs up numbered Gmail addresses back-to-back and grabs a free 7-day trial on each — then resells or shares the logins.',
    expect: 'Risk climbs with each signup. A case is raised on the 3rd linked account and keeps growing with every extra account.',
    params: [{ key: 'accounts', label: 'Accounts to create', min: 3, max: 10, value: 5 }],
    run: async (d, p) => {
      const t = tag(); const base = pick(['jake.miller', 'study.pro', 'nclex.ace', 'med.prep']);
      await d.become({ ip: randomIp('185.220.101'), device: newDevice(), country: 'US', ua: BOT_UA }, 'Attacker\'s laptop running a script');
      for (let i = 1; i <= p.accounts; i++) {
        d.progress(i - 1, p.accounts, 'accounts');
        await d.say(`Account ${i} of ${p.accounts}: the script signs up another numbered address`, 'Same laptop, same IP, same browser fingerprint — only the number in the email changes.');
        await d.signup({ firstName: 'Jake', lastName: 'Miller', email: `${base}${t}${String(i).padStart(2, '0')}@gmail.com` });
        await d.say(`Account ${i}: grabs a free Step 2 CK trial`, 'Each trial is $299 of access that is never paid for.');
        await d.startTrial(11);
      }
      d.progress(p.accounts, p.accounts, 'accounts');
    }
  },
  {
    key: 'authgap', icon: '🔓', title: 'Protected content after access denied', kind: 'attack', glossary: 'authGap',
    story: 'A user with no subscription is correctly blocked in the app — then discovers the "Download PDF" export endpoint skips the subscription check and hands over the whole question bank.',
    expect: 'The in-app attempt is denied (no case — that\'s the system working). The export that leaks content despite the deny raises a Critical case instantly. A paying student opening the same content stays clean.',
    params: [],
    run: async (d) => {
      const p = realPerson();
      await d.become({ ip: randomIp(), device: newDevice(), country: 'US' }, `${p.firstName}, no subscription`);
      await d.say(`${p.firstName} creates a free account but doesn't buy anything`);
      await d.signup(p);
      await d.say('Opens a Step 2 CK question in the app', 'The app checks the subscription → Denied, and returns nothing. Correct behaviour — no case.');
      await d.openContent(11, 1201, 'app');
      await d.say('Tries the "Download all as PDF" link instead', 'The export endpoint records the deny… and streams the 2.4 MB PDF anyway. This is the leak Sentinel exists to catch.');
      await d.openContent(11, 0, 'export');
      await d.say('Control: a paying student opens the same content', 'Allowed with a valid subscription — must stay clean.');
      const s = realPerson();
      await d.become({ ip: randomIp(), device: newDevice(), country: 'US' }, `${s.firstName}, paying student`);
      await d.signup(s);
      await d.buy(11, '4' + Array.from({ length: 15 }, () => rnd(10)).join(''));
      await d.openContent(11, 1201, 'app');
    }
  },
  {
    key: 'stuffing', icon: '🔐', title: 'Leaked passwords → account takeover', kind: 'attack', glossary: 'credentialStuffing',
    story: 'An attacker buys a list of emails and passwords leaked from another website and tries them against our login, hoping people reused passwords.',
    expect: 'After 8 failures across 5+ accounts a High case is raised; when one password works it escalates to Critical "account takeover".',
    params: [{ key: 'attempts', label: 'Leaked logins tried', min: 8, max: 14, value: 9 }],
    run: async (d, p) => {
      const t = tag();
      const victim = { firstName: 'Grace', lastName: 'Okafor', email: `grace.okafor.${t}@outlook.com`, password: 'Sunrise#2024' };
      await d.become({ ip: randomIp(), device: newDevice(), country: 'US' }, 'Grace at home (real student)');
      await d.say('Grace, a real student, signs up — reusing a password she also used on another site');
      await d.signup(victim);
      await d.become({ ip: randomIp('45.155.205'), device: newDevice(), country: 'NL', ua: BOT_UA }, 'Attacker with a leaked list');
      for (let i = 1; i <= p.attempts; i++) {
        d.progress(i - 1, p.attempts + 1, 'login attempts');
        await d.say(`Attempt ${i}: tries a leaked email + password`, 'Each failure is against a different account — a list being replayed, not a forgotten password.');
        await d.login(`${LEAKED[(i - 1) % LEAKED.length]}${t}@${pick(DOMAINS)}`, 'Sunrise#2024');
      }
      await d.say('Next leaked entry is Grace\'s email — the reused password works', 'A successful login from the attacking source = account takeover.');
      await d.login(victim.email, victim.password);
      d.progress(p.attempts + 1, p.attempts + 1, 'login attempts');
    }
  },
  {
    key: 'ring', icon: '💳', title: 'VPN-hopping ring, one card', kind: 'attack', glossary: 'accountFarm',
    story: 'A smarter attacker switches VPN exit and browser for every account so no IP or device repeats — but pays for all of them with the same card and a discount code.',
    expect: 'No network link exists, yet after the 3rd payment the shared card ties the accounts together and a case is raised.',
    params: [{ key: 'accounts', label: 'Accounts in the ring', min: 3, max: 6, value: 4 }],
    run: async (d, p) => {
      const t = tag();
      for (let i = 1; i <= p.accounts; i++) {
        d.progress(i - 1, p.accounts, 'accounts');
        await d.become({ ip: randomIp(), device: newDevice(), country: pick(['US', 'CA', 'GB', 'NL']) }, `Attacker via VPN exit ${i}`);
        await d.say(`Account ${i}: new VPN exit and a fresh browser`, 'Different IP and device every time — IP-based rules see nothing.');
        await d.signup({ firstName: pick(['Alex', 'Sam', 'Chris', 'Jordan']), lastName: pick(['Lee', 'Gray', 'Moss', 'Hart']), email: `${hex(3)}${t}${rnd(90) + 10}@${pick(['mailinator.com', 'yopmail.com'])}` });
        await d.say(`Account ${i}: buys NCLEX with the same card and code WELCOME10`, 'The card is the one thing the attacker can\'t rotate.');
        await d.buy(12, '4000 0566 5566 5556', 'WELCOME10');
      }
      d.progress(p.accounts, p.accounts, 'accounts');
    }
  },
  {
    key: 'coupon', icon: '🏷', title: 'One-time coupon reused on the wrong product', kind: 'attack', glossary: 'couponAbuse',
    story: 'FRIEND15 is a one-time referral code valid only for the Step 1, Step 2 CK and NCLEX banks. A customer uses it, then replays it on a second order for MCAT.',
    expect: 'Two cases: the single-use code was used twice by one person, and it was applied to a product it isn\'t valid for.',
    params: [],
    run: async (d) => {
      const p = realPerson();
      await d.become({ ip: randomIp(), device: newDevice(), country: 'US' }, `${p.firstName}`);
      await d.say(`${p.firstName} signs up and buys Step 1 with code FRIEND15`, 'Legitimate first use.');
      await d.signup(p);
      await d.buy(10, '4' + Array.from({ length: 15 }, () => rnd(10)).join(''), 'FRIEND15');
      await d.say('Places a second order — MCAT — and enters FRIEND15 again', 'Checkout doesn\'t enforce one-use-per-person or which products the code covers.');
      await d.buy(40, '4' + Array.from({ length: 15 }, () => rnd(10)).join(''), 'FRIEND15');
    }
  },
  {
    key: 'scrape', icon: '🕷', title: 'Trial account scrapes the question bank', kind: 'attack', glossary: 'scraping',
    story: 'Someone starts a free trial, then a script walks through the question bank one question after another to copy it.',
    expect: 'Every request is allowed (the trial is valid), but 50+ sequential questions in minutes raises a content-scraping case.',
    params: [{ key: 'questions', label: 'Questions to pull', min: 50, max: 90, value: 60 }],
    run: async (d, p) => {
      const person = realPerson();
      await d.become({ ip: randomIp(), device: newDevice(), country: 'IN', ua: BOT_UA }, 'Scraper with a trial');
      await d.say('Signs up and starts a free Step 1 trial');
      await d.signup(person);
      await d.startTrial(10);
      await d.say(`Script reads questions 1 → ${p.questions} in order`, 'Each request is individually allowed — it\'s the pattern that gives it away.');
      for (let q = 1; q <= p.questions; q++) {
        d.progress(q - 1, p.questions, 'questions');
        await d.openContent(10, 2000 + q, 'app', true);
      }
      d.progress(p.questions, p.questions, 'questions');
    }
  },
  {
    key: 'students', icon: '🎓', title: 'Real students signing up', kind: 'legit',
    story: 'Unrelated students sign up from their own homes and devices; some buy with their own card, some take a trial.',
    expect: 'Every step is clean. No case is recorded for real customers.',
    params: [{ key: 'people', label: 'Students', min: 2, max: 6, value: 4 }],
    run: async (d, p) => {
      for (let i = 1; i <= p.people; i++) {
        d.progress(i - 1, p.people, 'students');
        const s = realPerson();
        await d.become({ ip: randomIp(), device: newDevice(), country: pick(['US', 'US', 'IN', 'CA', 'GB']) }, `${s.firstName} at home`);
        await d.say(`${s.firstName} ${s.lastName} signs up from home`);
        await d.signup(s);
        if (i % 2 === 0) { await d.say(`${s.firstName} buys a plan with their own card`); await d.buy(pick([10, 11, 40]), '4' + Array.from({ length: 15 }, () => rnd(10)).join('')); }
        else { await d.say(`${s.firstName} starts a free trial`); await d.startTrial(pick([10, 11, 12])); }
      }
      d.progress(p.people, p.people, 'students');
    }
  },
  {
    key: 'campus', icon: '🏫', title: 'Classmates on one campus network', kind: 'legit',
    story: 'Medical students enrol together from the university Wi-Fi — one shared public IP — each on their own laptop with their own name.',
    expect: 'Sentinel notices the shared IP ("watching") but raises no case: nothing about the identities suggests one actor.',
    params: [{ key: 'people', label: 'Students', min: 3, max: 8, value: 6 }],
    run: async (d, p) => {
      const ip = randomIp('128.95.40');
      for (let i = 1; i <= p.people; i++) {
        d.progress(i - 1, p.people, 'students');
        const s = realPerson();
        await d.become({ ip, device: newDevice(), country: 'US' }, `Student ${i} on campus Wi-Fi`);
        await d.say(`${s.firstName} ${s.lastName} enrols from the campus network`, 'Same public IP as classmates, but their own laptop and their own university email.');
        await d.signup({ ...s, email: `${s.firstName[0]}${s.lastName}${rnd(9)}`.toLowerCase() + '@med.state-university.edu' });
      }
      d.progress(p.people, p.people, 'students');
    }
  },
];
