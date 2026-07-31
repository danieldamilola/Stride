# Design System: T&C Lens

## 1. Visual Theme & Atmosphere

T&C Lens now uses a forensic studio tone: cool slate surfaces, restrained asymmetry, and instrument-grade typography that feels like reading evidence under calibrated light. Density is Daily App Balanced (5/10), variance is Offset Asymmetric (7/10), and motion is Fluid CSS (6/10). The interface should feel technical and intentional, never playful.

## 2. Color Palette & Roles

- **Ledger Canvas** (`#EEF2F4`) — Primary workspace background.
- **Case Surface** (`#F8FAFB`) — Main panel surfaces and section fills.
- **Paper White** (`#FFFFFF`) — Elevated cards and active containers.
- **Charcoal Ink** (`#161A1F`) — Primary text and high-priority controls.
- **Docket Gray** (`#64707D`) — Secondary copy, metadata, helper text.
- **Rule Line** (`#CBD5DF`) — Structural borders and separators.
- **Verdict Teal** (`#2B6C63`) — Single accent for active states, CTAs, focus, and key status chips.

Dark theme maps to the same semantic roles with darker surfaces (`#11161C`, `#171E26`, `#1D2630`) and lighter ink (`#E6ECF2`) while preserving the same single accent.

## 3. Typography Rules

- **Display:** Outfit / Satoshi / Geist fallback — tight tracking, medium-large scale, high clarity.
- **Body:** Outfit / Satoshi / Geist fallback — readable paragraph rhythm, max 65ch.
- **Mono:** JetBrains Mono / Geist Mono fallback — metadata, labels, scores, URLs, and timestamps.
- **Banned:** Inter, generic serif faces, pure black, neon/purple glows, oversized decorative gradients.

## 4. Component Stylings

- **Buttons:** Rounded asymmetric geometry; tactile `translateY(1px)` on active; accent fill for primary action.
- **Navigation:** Capsule tab group with mono labels and clear active inversion.
- **Cards:** Soft-radius panels with subtle depth only where hierarchy is needed; no ornamental over-layering.
- **Inputs:** Label above input, helper/error text below, accent focus ring.
- **Loaders:** Skeletal scanner-style striping matching layout dimensions; no circular spinner-only state.
- **Findings:** Left-rail severity cue with compact importance chips and quote blocks styled as extracted evidence.

## 5. Layout Principles

Use an asymmetric hero split (content + specimen rail) and two-column dashboard body that collapses to one column below 900px. Keep content constrained (`max-width` container), avoid element overlap, and preserve clean spatial zones. Full-height behavior uses `min-height: 100dvh`.

## 6. Motion & Interaction

Primary motion language is controlled sweep and subtle entry reveals. Use transform/opacity animation only. Scanner sweep and skeleton shimmer are continuous but low-intensity. Respect `prefers-reduced-motion` by reducing animation and transition duration.

## 7. Anti-Patterns (Banned)

- No emojis.
- No Inter font.
- No pure black (`#000000`).
- No neon outer glows or purple/blue AI gradients.
- No centered high-variance hero layout.
- No generic 3-column equal card feature rows.
- No fabricated metrics or fake statistics.
- No AI filler copy ("Elevate", "Seamless", "Unleash", "Next-Gen").
- No overlapping content layers.
- No custom mouse cursors.
