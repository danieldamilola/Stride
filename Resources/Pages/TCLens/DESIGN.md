# T&C Lens — Design System

This document captures the complete design language used in T&C Lens across both the **Browser Extension** and the **Web App (Landing Page & Dashboard)**. Use it as a foundation for all future features and components.

---

## Philosophy

The design is **editorial, highly technical, and minimal**. Inspired by tools like Linear, Stripe, and Vercel — it favours clarity over decoration. No gradients, no soft drop-shadows, no rounded cards. Everything is either a razor-thin line, a solid block, or a monospace text label. The UI stays out of the way and lets the content breathe.

Key principles:
- **Strict 2-colour foundation.** Near-black background (`#0e0e0e`), off-white foreground (`#eff2f1`). Nothing else is coloured except a single amber accent (`#d99b36`) for warnings or highlights.
- **Typography does the heavy lifting.** Size, weight, letter-spacing, and uppercase transforms replace colour as the primary hierarchy tool.
- **Borders instead of shadows.** Depth is created using `1px solid` lines (`--rule`), not box-shadows or elevation. 
- **Sharp Edges.** Primary surfaces, buttons, inputs, and containers are sharp-edged rectangles. No `border-radius`.

---

## Colour Tokens

Defined in `:root` as CSS custom properties. Do not hardcode any value — always reference a token.

```css
:root {
  --bg:          #0e0e0e;  /* Page background — near-black */
  --surface:     #161616;  /* Elevated surface 1 (panels, inputs) */
  --surface-2:   #171b1d;  /* Elevated surface 2 (hover states, heavy cards) */
  
  --fg:          #eff2f1;  /* Primary foreground (aliases: --ink) */
  --ink:         #eff2f1;
  
  --muted:       #7b858a;  /* Secondary text, labels, placeholders */
  --dim:         #586267;  /* Disabled states, inactive icons */
  
  --line:        #262c2f;  /* Dividers, grid lines, separators (aliases: --rule) */
  --rule:        #262c2f;
  --rule-strong: #343c40;  /* Stronger borders for emphasis */
  
  --amber:       #d99b36;  /* Primary brand / warning accent */
  --amber-soft:  #2b2114;  /* Background block for amber text */
  --green:       #81b9a3;  /* Success states */
}
```

---

## Typography

### Fonts

```css
--font: "Outfit", sans-serif;
--mono: "JetBrains Mono", monospace;
```

- **Outfit** is used for all body copy, headings, and conversational text.
- **JetBrains Mono** is heavily utilized for metadata, labels, navigation links, buttons, cost estimates, and numeric values that need to align.
- Load both from Google Fonts.

### Hierarchy Rules

Use `letter-spacing` and `text-transform: uppercase` to create category labels — not a larger font size.

```css
/* Label / category style — used heavily in the dashboard and nav */
font-family: var(--mono);
font-size: var(--text-xs);
letter-spacing: 0.1em;
text-transform: uppercase;
color: var(--muted);
```

---

## Layout

### Dashboard & Nav Container

Navigation bars and main layout boundaries follow a strict maximum width on the web app to maintain readability on ultra-wide screens.

```css
/* Web App Nav / Layout Max Width */
max-width: 1280px;
margin: 0 auto;
padding: 0 24px;
```

The Dashboard `nav` is sticky and `64px` tall (`h-16`), matching the landing page.

### Extension Container

The extension popup has a fixed width:
```css
width: 400px;
padding: 24px;
```

### Footer

The footer is strictly utilitarian. It typically sits at the bottom of the page, separated by a single `border-t section-rule`. It contains minimal links styled as muted monospace text that brightens on hover.

---

## Components

### Dividers (`.fade-divider`)

Standard horizontal lines `<hr>` are generally avoided. To separate major vertical sections, use the `.fade-divider` class. This creates a subtle `1px` horizontal line (`var(--rule)`) that fades to transparent on both ends, reinforcing the minimal editorial aesthetic.

```css
.fade-divider {
  position: relative;
  width: 100%;
  height: 1px;
  background: linear-gradient(90deg, transparent, var(--rule), transparent);
  margin: 4rem 0;
}
```

### Primary Button `.btn` / `.nav-link`

Solid fill, sharp edges (no border-radius). Uppercase monospace label. Used for "Sign out", "Get the Extension", and major actions.

```css
font-family: var(--mono);
display: inline-flex;
align-items: center;
gap: 8px;
background: var(--ink);
padding: 10px 20px;
font-size: 11px;
font-weight: 600;
text-transform: uppercase;
letter-spacing: 0.08em;
color: var(--bg);
transition: opacity 0.1s;
border: none;
cursor: pointer;
```
**Hover:** `opacity: 0.8` — a subtle fade, not a colour change.

### Ghost Link `.ghost-link`

No background, no border. Just an uppercase muted label that brightens on hover. Used for "← Back to Dashboard" navigation.

```css
color: var(--muted);
text-transform: uppercase;
letter-spacing: 0.06em;
font-size: var(--text-xs);
text-decoration: none;
transition: color 0.1s;
```
**Hover:** `color: var(--fg)`

---

## Progress Bars & Data Display

### Dashboard Progress Bar
The credit usage progress bar does not use standard HTML `<progress>` elements. It is a custom `<div>` with `width` calculated dynamically. It uses `--fg` for the fill and `--line` for the empty track. It has no rounded corners.

### Billing History Grid
The billing history page (`/dashboard/billing`) uses a simple flex-column list separated by `1px solid var(--line)`. 
- Dates use `--mono` and `--muted`.
- Amounts use `--mono` and `--fg`.

---

## Transitions & Animation

All interactive transitions are **fast and functional** — never decorative:

| Purpose | Value |
|---|---|
| Colour / opacity change | `0.1s` linear |
| Score bar fill | `0.6s cubic-bezier(0.4, 0, 0.2, 1)` |
| Spinner | `0.8s linear infinite` |

No bounce, no elastic, no scale transforms.

---

## What This Is NOT

To keep future projects on-brand, avoid these:

- ❌ Gradients, glowing backgrounds, or soft pastel themes
- ❌ `box-shadow` for elevation or depth
- ❌ Rounded corners (`border-radius: 0` everywhere)
- ❌ Multiple accent colours (stick to the core 2-colour palette + amber)
- ❌ Dense padding — always give content room to breathe
- ❌ "Friendly" UI elements (keep it technical, raw, and editorial)
