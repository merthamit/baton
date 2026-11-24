# Baton design: calm clinical

The ward board is an operating screen: a nurse in charge reads it in seconds, often on a shared screen in a lit ward,
sometimes on a phone at the bedside. Precision and restraint, not decoration.

## Principles

- **Colour only where attention is needed.** A normal patient is uncoloured. Blue belongs to the interface (buttons,
  links, selection), never to risk.
- **NEWS2 bands, always with a number, a word and a shape** (colour is never the only signal):
  score 0 uncoloured (circle) · 1–4 pale yellow (triangle) · a single parameter scoring 3 pale with an orange outline
  (diamond) · 5–6 solid orange (diamond) · 7+ solid red (square), the one saturated alarm on the board, with a red
  frame on a faintly red card.
- **Overdue is a task state, not a risk:** shown with a clock, separately from the score.
- **Beds keep their places** (bed order); the summary strip above them counts high and medium risk, sets overdue,
  calls waiting and concerns.
- **Light by default**, dark on request (theme switch, remembered in the browser).

## Layout

- Slim synthetic-data strip (full width) → app bar: ward name, *Board | Handover* tabs, live link, language, theme →
  mode bar: *Watch | Try | Shift* with one sentence on what the mode does → time control or the live shift banner →
  ward summary → workspace.
- Workspace ≥1200 px: beds on the left, the selected patient (or SBAR) in a sticky panel on the right (40 %). Below
  that width the patient stacks under the beds.
- Phone (375 px): tabs and mode buttons span the width, tools on one row, explanatory lines hidden.

## Tokens

Defined in `web/src/styles/tokens.css` (light and `[data-theme='dark']`). Type: Atkinson Hyperlegible for text,
IBM Plex Mono with tabular numerals for clinical numbers. Cards 12 px radius, one border (no shadow), buttons ≥38 px
high (≥44 px for the main actions on touch).
