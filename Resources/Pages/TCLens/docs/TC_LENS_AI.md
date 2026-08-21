# T&C Lens AI - Architecture Plan

Credit-based. No subscriptions. No plans. Buy credits, use credits.

---

## How It Works

```
1. User signs up (gets 1 free credit)
2. User clicks Analyze → backend calls DeepSeek → deducts 1 credit
3. Credits run out → "Buy more" button → Stripe checkout → credits added
```

That's the entire product.

---

## Credit Pricing

| Package | Price | Credits | Per-analysis cost |
|---------|-------|---------|-------------------|
| Starter | $2 | 25 | $0.08 |
| Value | $5 | 75 | $0.07 |
| Bulk | $10 | 200 | $0.05 |

> Your cost per analysis (DeepSeek V4 Flash - $0.14/M input, $0.28/M output): **~$0.002**. Margin: **95%+**.
> 1M token context window - handles even the longest legal documents without truncation.
> Users get **1 free credit** on signup. No free tier beyond that.

### Your Profit (after Stripe fees + DeepSeek cost)

| Package | Price | Stripe Fee | DeepSeek Cost | **Your Profit** | **Margin** |
|---------|-------|------------|---------------|-----------------|------------|
| Starter | $2 | $0.36 | $0.08 | **$1.56** | 78% |
| Value | $5 | $0.45 | $0.23 | **$4.32** | 86% |
| Bulk | $10 | $0.59 | $0.60 | **$8.81** | 88% |

> **Note:** Stripe charges 2.9% + $0.30 per transaction. The $0.30 flat fee hits the $2 tier hardest. Hosting (Vercel + Supabase) is free at this scale.

---

## Architecture

```
Extension                          Backend (Vercel)
┌──────────────┐                  ┌──────────────────────┐
│              │  POST /analyze   │                      │
│  Options UI  │ ───────────────► │  Verify JWT          │
│              │  (JWT + text)    │  Check credits > 0   │
│              │                  │  Call DeepSeek V4    │
│              │  ◄─────────────  │  Deduct 1 credit     │
│              │  (analysis JSON) │  Return result       │
└──────────────┘                  └──────────┬───────────┘
                                             │
                                  ┌──────────┴───────────┐
                                  │  Supabase             │
                                  │  • Auth (JWT)         │
                                  │  • Postgres (credits) │
                                  └──────────────────────┘
```

---

## Database - 2 tables

```sql
-- User profiles (extends Supabase Auth)
CREATE TABLE public.profiles (
    id          UUID PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    email       TEXT NOT NULL,
    credits     INTEGER NOT NULL DEFAULT 1,   -- 1 free on signup
    created_at  TIMESTAMPTZ DEFAULT NOW()
);

-- Payment receipts
CREATE TABLE public.payments (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id         UUID NOT NULL REFERENCES public.profiles(id),
    amount_cents    INTEGER NOT NULL,          -- e.g. 200 = $2.00
    credits_added   INTEGER NOT NULL,          -- e.g. 25
    stripe_session  TEXT NOT NULL,             -- Stripe checkout session ID
    created_at      TIMESTAMPTZ DEFAULT NOW()
);
```

---

## API - 6 endpoints

| Method | Path | Auth? | What it does |
|--------|------|-------|-------------|
| `POST` | `/api/auth/signup` | No | Create account → 1 free credit |
| `POST` | `/api/auth/login` | No | Return JWT |
| `GET` | `/api/auth/me` | Yes | Return email + credit balance |
| `POST` | `/api/analyze` | Yes | Check credits → call DeepSeek V4 Flash → deduct 1 → return result |
| `POST` | `/api/credits/buy` | Yes | Create Stripe Checkout session → return URL |
| `POST` | `/api/webhooks/stripe` | No* | Stripe calls this → add credits to user |

*Webhook is verified via Stripe signature, not JWT.

---

## Backend Files - 10 total

```
server/
├── vercel.json
├── package.json
├── .env.example
│
├── lib/
│   ├── supabase.js           ← Supabase client (5 lines)
│   ├── auth.js               ← Verify JWT from request header (20 lines)
│   └── deepseek.js           ← Call DeepSeek V4 Flash API with SYSTEM_PROMPT (40 lines)
│
├── api/
│   ├── auth/
│   │   ├── signup.js          ← Create user + profile with 1 credit (30 lines)
│   │   ├── login.js           ← Authenticate, return JWT (25 lines)
│   │   └── me.js              ← Return profile + credits (15 lines)
│   ├── analyze.js             ← The main endpoint (50 lines)
│   ├── credits/
│   │   └── buy.js             ← Create Stripe one-time checkout (30 lines)
│   └── webhooks/
│       └── stripe.js          ← Handle payment success → add credits (40 lines)
│
└── sql/
    └── schema.sql             ← The 2 tables above (15 lines)
```

**~270 lines of backend code.**

---

## Extension Changes - 9 files

| File | Action | What |
|------|--------|------|
| `lib/providers.js` | MODIFY | Remove `comingSoon`, add `tc-lens-api.vercel.app` config |
| `lib/ai-client.js` | MODIFY | Add `analyzeWithTCLens()` - sends JWT + text to backend |
| `lib/storage.js` | MODIFY | Add `tclensToken`, `tclensEmail`, `tclensCredits` |
| `options/auth.js` | NEW | Login, signup, logout functions (~80 lines) |
| `options/options.html` | MODIFY | Replace coming-soon banner → login form + account card |
| `options/options.css` | MODIFY | Auth form + credit display styles (~50 lines) |
| `options/options.js` | MODIFY | Wire auth events, buy credits flow |
| `options/utils.js` | MODIFY | Add `QUOTA_EXCEEDED` error message |
| `manifest.json` | MODIFY | Add backend URL to CSP `connect-src` |

**~200 new lines on the extension side.**

---

## What the user sees

### Settings → T&C Lens AI (logged out)
```
┌─────────────────────────────────────┐
│  T&C Lens AI                        │
│  No API key needed. Just sign in.   │
│                                     │
│  Email: [___________________]       │
│  Password: [________________]       │
│                                     │
│  [SIGN IN]    Create Account        │
└─────────────────────────────────────┘
```

### Settings → T&C Lens AI (logged in)
```
┌─────────────────────────────────────┐
│  user@email.com              FREE   │
│                                     │
│  Credits remaining: 1               │
│  ████░░░░░░░░░░░░░░░░░░░░░░░░░░░░  │
│                                     │
│  [BUY CREDITS]         Sign Out     │
└─────────────────────────────────────┘
```

### Dashboard → credits exhausted
```
┌─────────────────────────────────────┐
│  You're out of credits.             │
│  Buy more to continue analyzing.    │
│                                     │
│  [BUY 25 CREDITS - $2]             │
└─────────────────────────────────────┘
```

---

## Decisions (Locked In)

| Decision | Answer |
|----------|--------|
| **Domain** | Free Vercel subdomain: `tc-lens-api.vercel.app` (no cost) |
| **Auth** | Email/password via Supabase Auth |
| **Repo** | `server/` folder inside the current `T-C` repo |
| **AI Model** | DeepSeek V4 Flash (1M context, cheapest option) |
| **Pricing** | Credit-based, no subscriptions |

---

## Implementation Order

| Step | What | Time |
|------|------|------|
| 1 | Supabase project + database schema | 30 min |
| 2 | Auth endpoints (signup, login, me) | 1.5 hours |
| 3 | Analyze endpoint (DeepSeek proxy + credit deduction) | 1.5 hours |
| 4 | Stripe checkout + webhook (buy credits) | 2 hours |
| 5 | Extension auth UI (login form + account card) | 2 hours |
| 6 | Extension analyze flow (tclens handler) | 1 hour |
| 7 | Deploy to Vercel + test | 1 hour |
| **Total** | | **~9.5 hours** |

