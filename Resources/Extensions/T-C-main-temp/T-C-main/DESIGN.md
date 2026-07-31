# T&C Lens — Design System

This document captures the complete design language used in T&C Lens.
Use it as a foundation for future projects built on the same visual identity.

---

## Philosophy

The design is **editorial and minimal**. Inspired by tools like Linear, Stripe, and Vercel's dashboard — it favours clarity over decoration. No gradients, no shadows, no rounded cards. Everything is either a line, a block, or a text label. The UI stays out of the way and lets the content breathe.

Key principles:
- **2-colour palette only.** Near-black background, off-white foreground. Nothing else is coloured except one amber accent for warnings.
- **Typography does the heavy lifting.** Size, weight, letter-spacing, and opacity replace colour as the hierarchy tool.
- **Borders instead of shadows.** Depth is created using `1px solid` lines, not box-shadows or elevation.
- **No border-radius** on primary surfaces. Buttons, inputs, and containers are sharp-edged rectangles.

---

## Colour Tokens

Defined in `:root` as CSS custom properties. Do not hardcode any value — always reference a token.

```css
:root {
  --bg:      #0e0e0e;  /* Page background — near-black, not pure black */
  --surface: #161616;  /* Slightly elevated surface (cards, panels) */
  --fg:      #efefef;  /* Primary foreground — off-white text and icons */
  --muted:   #5a5a5a;  /* Secondary text, labels, placeholders */
  --dim:     #2a2a2a;  /* Disabled states, subtle borders on interactive elements */
  --line:    #1f1f1f;  /* Dividers, grid lines, section separators */
}
```

The only exception to the 2-colour rule is the **truncation/warning accent**:
```css
/* Used sparingly — only for genuine warnings */
color: #f59e0b;
background: rgba(245, 158, 11, 0.1);
border-left: 2px solid #f59e0b;
```

---

## Typography

### Fonts

```css
--font: "Inter", system-ui, -apple-system, sans-serif;
--mono: "JetBrains Mono", "Fira Code", ui-monospace, monospace;
```

- **Inter** is used for all body copy, headings, labels, and buttons.
- **JetBrains Mono / Fira Code** is used for metadata, quotes, cost estimates, and numeric values that need to align (scores, dates).
- Load Inter from Google Fonts. JetBrains Mono can also be loaded from Google Fonts.

### Type Scale

```css
--text-2xs: 10px;   /* Uppercase labels, status tags */
--text-xs:  11px;   /* Secondary metadata, button labels, captions */
--text-sm:  13px;   /* Body copy, descriptions, form inputs */
--text-md:  15px;   /* Card values, finding titles */
--text-lg:  20px;   /* Page titles, analysis titles */
--text-xl:  32px;   /* (Reserved for future hero/display use) */
--text-2xl: 52px;   /* The large risk score number */
```

### Hierarchy Rules

Use `letter-spacing` and `text-transform: uppercase` to create category labels — not a larger font size.

```css
/* Label / category style — used throughout */
font-size: var(--text-2xs);
letter-spacing: 0.1em;
text-transform: uppercase;
color: var(--muted);
```

Use `font-weight: 600` only for the primary number (score) or the brand name. Everything else is `400` or `500`.

---

## Layout

### Container

All content lives inside a max-width container centred on the page:

```css
max-width: 800px;
margin: 0 auto;
padding: 56px 40px 80px;
```

### Header / Nav

Sticky header, 48px tall, separated from content with a single `1px` line:

```css
position: sticky;
top: 0;
height: 48px;
border-bottom: 1px solid var(--line);
background: var(--bg);
```

Nav links use an **active underline** pattern — no background highlight, just a `border-bottom` that appears on the active item.

---

## Components

### Primary Button `.btn`

White fill, dark text. Sharp edges (no border-radius). Uppercase small-caps label.

```css
background: var(--fg);
color: var(--bg);
padding: 9px 20px;
font-size: var(--text-xs);
font-weight: 600;
letter-spacing: 0.08em;
text-transform: uppercase;
transition: opacity 0.1s;
```

Hover: `opacity: 0.82` — a subtle fade, not a colour change.
Disabled: `opacity: 0.2`.

### Ghost Button `.btn-ghost`

No background, no border. Just an uppercase muted label that brightens on hover.

```css
color: var(--muted);
text-transform: uppercase;
letter-spacing: 0.06em;
transition: color 0.1s;
```

### Small Action Button `.btn-copy` / `.btn-export`

Used inside content rows (e.g. quote copy). Has a very subtle border that brightens on hover.

```css
background: transparent;
border: 1px solid var(--dim);
color: var(--muted);
font-size: var(--text-2xs);
padding: 4px 8px;
border-radius: 4px; /* Only component with border-radius */
text-transform: uppercase;
letter-spacing: 0.05em;
transition: all 0.1s;
```

> Note: This is the **only** component that uses `border-radius`. Everything else is sharp.

---

### Forms

Inputs and selects use a **bottom-border only** style — no box, no background:

```css
background: transparent;
border: none;
border-bottom: 1px solid var(--dim);
color: var(--fg);
padding: 8px 0;
font-size: var(--text-sm);
border-radius: 0;
appearance: none;
transition: border-bottom-color 0.1s;
```

On focus, the bottom border brightens to `var(--fg)`. No focus ring, no box shadow.

Labels sit above inputs using the uppercase label style (`--text-2xs`, `letter-spacing: 0.1em`, `color: var(--muted)`).

---

### Data Grid `.status-grid`

A 2-column grid used on the dashboard to display status cells. The grid lines are created by setting `gap: 1px` and `background: var(--line)` on the grid container, while each cell has `background: var(--bg)`. This creates razor-thin dividers without any border declarations on individual cells.

```css
display: grid;
grid-template-columns: 1fr 1fr;
gap: 1px;
background: var(--line);
border: 1px solid var(--line);
```

---

### Score Display

The large risk number uses the biggest type size (`--text-2xl: 52px`), tight letter-spacing, and `font-variant-numeric: tabular-nums` to keep it stable as digits change.

Below the number, a thin 2px progress bar animates in with an eased cubic-bezier transition:

```css
transition: width 0.6s cubic-bezier(0.4, 0, 0.2, 1);
```

---

### Findings List

Each finding is separated by a `1px` bottom border — no cards, no shadows. The finding title uses `--text-md` and `font-weight: 500`. The importance tag (`high`, `medium`, `low`) is rendered as an uppercase label; **colour is its only differentiator**:

```css
.importance-tag--high   { color: var(--fg); }
.importance-tag--medium { color: var(--muted); }
.importance-tag--low    { color: var(--dim); }
```

Quoted text uses the monospace font with a `2px` left border in `var(--dim)`. The copy button inside the quote is hidden (`opacity: 0`) and only appears on hover of the quote block.

---

### Loading Overlay

A full-screen overlay with a spinning circle loader — no logo, no progress bar, just a small spinner and an uppercase status label:

```css
.loading-spinner {
  width: 20px;
  height: 20px;
  border: 1px solid var(--dim);
  border-top-color: var(--fg);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}
```

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

## Spacing

Spacing is applied manually — no utility classes. Common values used throughout:

| Use | Value |
|---|---|
| Section gap | `36px` |
| Content padding (main) | `56px 40px 80px` |
| Nav height | `48px` |
| Card padding | `24px` |
| Row padding (list items) | `14px 0` |
| Finding padding | `24px 0` |
| Form group gap | `28px` |

---

## What This Is NOT

To keep future projects on-brand, avoid these:

- ❌ Gradients or coloured backgrounds
- ❌ `box-shadow` for elevation or depth
- ❌ Rounded corners on containers, buttons, or inputs (only small utility buttons get `border-radius: 4px`)
- ❌ Multiple accent colours (the amber `#f59e0b` is the **only** allowed exception)
- ❌ Animations longer than `0.6s` or with bouncy easings
- ❌ Dense padding — always give content room to breathe

---

## Future Improvements

This is the **foundation**. Future iterations of this design language can explore:
- Subtle surface elevation using `--surface: #161616` more aggressively
- A proper motion system for view transitions (e.g. slide-in from right for sub-pages)
- Responsive / mobile layout (currently desktop-first at 800px max)
- A richer type scale for larger screens (display-size headings)
- A dark/light mode toggle (the token system is already set up for this — just add a `[data-theme="light"]` override)
