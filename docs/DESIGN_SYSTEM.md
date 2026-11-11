# Design system

How the frontend looks, and how to build a new screen so it looks the same. Decisions and reasons are in `ARCHITECTURE.md` (Frontend decisions, "Visual design system"); this file is the working reference.

## The look in one paragraph

Deep bottle green, warm cream paper, one vermilion action colour. Headlines and the big seat numbers are set in Fraunces (a warm, characterful serif); everything else is Hind Siliguri, which carries real Bangla glyphs. A class is drawn as an **admission ticket**: a card with a perforated tear line above its apply stub, and a strip of small "seats" instead of a table row. Motion is short and purposeful (entrance, a number dropping in when it changes, a pulsing "live" dot) and switches off for people who ask for less motion.

## Where things live

| File | Holds |
|---|---|
| `src/styles/tokens.css` | every colour, font, radius, shadow and easing, as CSS variables |
| `src/styles/fonts.css` | self-hosted fonts (`@fontsource`), no CDN |
| `src/styles/base.css` | reset, page defaults, focus ring, `.btn`, `.badge`, `.visually-hidden`, reduced-motion |
| `src/styles/components.css` | shared blocks: `.card`, `.field*`, `.state-card*`, `.spinner`, `.bone`, `.tick`, shared keyframes |
| next to a component | styling that only that component or page uses (`AppLayout.css`, `ClassCards.css`) |

All four `styles/` files are loaded once from `src/main.tsx` through `styles/index.css`.

## Rules

1. **Never write a colour, font or radius into a component.** Use the variables in `tokens.css` (`var(--forest-800)`, `var(--radius-md)`, `var(--font-display)`). To retune the look, change `tokens.css`.
2. **Text colours:** `--ink` for body text, `--ink-soft` for secondary text. `--ink-faint` only for large text and placeholders. `--line` is a decorative divider and is **too faint for the edge of a control**: use `--field-border` there (3.5:1 on the card).
3. The interface is English-only, so no `lang="bn"` marking is needed; Hind Siliguri is still used for its clean Latin glyphs.
4. **Headings use the display font** (`h1`-`h3` already do). Big numbers: `font-family: var(--font-display)` with `font-variant-numeric: lining-nums tabular-nums`.
5. **Motion:** do not add animation that ignores `prefers-reduced-motion`; `base.css` already turns all animation and transitions off for it. An entrance animation uses `animation-fill-mode: backwards` (not `both`), or it pins the end state and overrides hover transforms.
6. **Every new colour pair must meet WCAG contrast** (4.5:1 for text, 3:1 for large text and control edges). The existing pairs were all computed; see ARCHITECTURE.md.
7. **Layout:** every direct child of `<main>` is a centred column with page gutters (`AppLayout.css`). A full-width band (like the class list hero) gives itself the class `hero` and lays out its own inner column.

## Building blocks

```tsx
// Primary action (vermilion). Disabled looks inert, not just dimmed.
<button className="btn btn--primary">Confirm <ArrowRightIcon className="btn-icon" /></button>

// Status badge: badge--open | badge--filling | badge--full
<span className="badge badge--open">Open</span>

// A card
<section className="card">...</section>

// A form field (add aria-invalid="true" on the input to show the error state)
<div className="field">
  <label className="field-label" htmlFor="name">Class name</label>
  <input className="field-input" id="name" aria-describedby="name-hint" />
  <p className="field-hint" id="name-hint">Shown to students.</p>
  <p className="field-error">Name is required.</p>   {/* only when invalid */}
</div>

// Messages: state-card--error | state-card--warn | state-card--success | state-card--empty
// (--success is the green counterpart of --error: use role="status" for it, role="alert" for an error)
<div className="state-card state-card--error" role="alert">
  <span className="state-card-icon"><AlertIcon /></span>
  <p className="state-card-body"><strong>Headline</strong><span className="state-card-detail">Detail</span></p>
</div>

// A number that should animate when it changes: key it by its value
<span key={value} className="tick">{value}</span>

// Loading: bone blocks in the shape of the real content, with text for screen readers
<div role="status"><span className="visually-hidden">Loading…</span>
  <span className="bone" style={{ height: "1.5rem", width: "40%" }} /></div>
```

Icons are in `src/components/icons.tsx` (`ArrowRightIcon`, `IdCardIcon`, `CheckIcon`, `AlertIcon`, `InboxIcon`, `BrandMark`); they draw in `currentColor`.

## Seat numbers

`src/pages/classSeats.ts` turns a class's counts into what a card draws: `seatStatus()` (`open` / `filling` at 20% or less left / `full`), `seatSegments()` (the strip) and `splitClassName()` ("Class 6 - Morning Shift" into a title and a second line). The strip never claims a seat is free when it is not: the parts always add up, a class with one seat left always shows a free segment, a sold-out class never does, and a confirmed or held seat always shows at least one segment.

## Screens and their state

Styled: the shell (header, navigation, registration-number capsule, footer), the class list, the application status screen (`/holds/:holdId`, page-only styling in `ApplicationStatus.css`), and create class (`/admin/new-class`, a good worked example of a form screen: `.card` + `.field` blocks, a `fieldset` to switch the whole form off, an error and a success `state-card`, the primary button with a spinner; its page-only layout is in `CreateClassPage.css`). Also styled: my applications (`/me`, `MyApplications.css`: a list of `.app-card` rows, each an edge colour by status, a `.badge`, a short text and an outlined `.btn` link; the empty and "enter your registration number" cases are `state-card--empty`), the crash message (`components/ErrorFallback.css`: one narrow card with a primary and an outlined button; it uses no router or context, so it also works outside the shell) and page not found (`NotFoundPage.css`). **Every screen is now styled.** A new screen can still be built from the blocks above; one that is only a heading already sits correctly in the page.

A card or message that is a direct child of `<main>` is stretched to the page column by `.app-main > :not(.hero)` (`AppLayout.css`). If it should be narrower (the crash card is 34rem), give its override a selector that outranks that rule, as `main.app-main > .crash` does, instead of relying on the order the stylesheets load in.
