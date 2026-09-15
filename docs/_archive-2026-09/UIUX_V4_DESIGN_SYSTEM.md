# UI/UX v4 design system

> Candidate implementation: the [12 September redesign plan](design/ETP-UI-REDESIGN-FOUR-PHASE-SPRINT-2026-09-12.md) preserves this palette/native-WPF direction and specifies the shared layout and interaction contract. Implementation and observed acceptance are recorded separately.

The visual target follows Windows Fluent principles with restrained Bento-style overview surfaces and enterprise information hierarchy. All production controls remain native WPF.

## Tokens

| Purpose | Token | Value |
|---|---|---|
| App background | `AppBackground` | `#F3F7F7` |
| Surface | `Surface` | `#FFFFFF` |
| Secondary surface | `SurfaceSecondary` | `#F0F5F5` |
| Primary text | `PrimaryText` | `#10252D` |
| Secondary text | `SecondaryText` | `#65757A` |
| Divider | `Divider` | `#D9E3E3` |
| Accent | `Accent` | `#008D78` |
| Success | `Success` | `#087C68` |
| Warning | `Warning` | `#A76500` |
| Critical | `Critical` | `#C33D49` |
| Information | `Information` | `#315EBA` |
| Navigation | `NavigationBackground` | `#062A36` |

Spacing is limited to 4, 8, 12, 16, 20, 24, 32 and 40 DIPs. Radii are 8, 12 and 20 DIPs. These resources live in `Themes/` and should be reused rather than duplicated.

## Typography

Segoe UI Variable Display is used for titles and metrics; Segoe UI Variable Text is the application default. Named styles cover Display, Page Title, Section Title, Card Title, Body, Caption and Metric.

## Density and touch

- Comfortable: 48-DIP minimum actions and 46-DIP grid rows.
- Compact: 34-DIP actions and 30-DIP grid rows.
- Primary operational actions use the existing darker teal `AccentDark` with white text; hover preserves that contrast. Palette token values are unchanged.
- Keyboard focus uses a black/white outline that remains visible on light and dark surfaces. Body text is 14 DIP; metadata is at least 12 DIP.
- Calendar opening buttons and all 42 native calendar day buttons are at least 44 × 44 DIP. Date parsing and selection remain native WPF behaviour.

## Components

- `ModuleTile`: large role-aware launch surface with vector icon, purpose and live-status slot.
- `StatusBadge`: restrained semantic state.
- `EmptyState`: plain-language absence plus next action.
- `LoadingState`: indeterminate or progress-aware non-blocking feedback.
- Module/category overview: focused task tiles backed by canonical routes, with persistent search and breadcrumbs.
- Detail drawer: right-side contextual inspection; Escape closes it.
- Existing DSR reusable cards and visual-report components remain part of the shared report workspace.
- DSR has Summary, Titan World, Helios and Service & targets tabs. Each selected tab owns one bounded content scroll region. Availability details open a labelled dialog.
- At constrained widths, generic reports expose Period & store in a labelled Apply/Cancel dialog. Standard widths retain inline filters. Status details preserve complete messages in a bounded dialog with a reachable Close action.
- Focused multi-table tasks place each table and its input fields on labelled tabs. The owning workspace retains their selection, drafts and operation state.

Cards are reserved for module launch, KPI and status summaries. Detailed work uses tables, split layouts, drawers, filters and grouped lists.

## Icons

Icons are local WPF `Geometry` resources in `Themes/Icons.xaml`. No emoji, bitmap screenshots, web fonts, downloaded icon packs or WebView runtime are used.

## Tables

Data grids use horizontal separators, touch/compact row tokens, keyboard navigation and recycling virtualisation. Missing, zero, blocked and not-applicable values remain distinct because display values continue to come from existing report contracts.

## Errors, empty and loading states

User-facing surfaces provide plain-language summaries. Technical exceptions remain available in deeper support/audit views. A missing OCR helper affects only OCR status; it never marks core reporting unavailable.

The short-window footer uses a one-line summary with Status details. Missing report queries replace the loading state with the current failure; stale results cannot re-enable exports. Offscreen contrast and target audits exclude disabled text and business table rows and do not establish Narrator or physical-touch acceptance.

## Dependency decision

No new UI framework or icon package is introduced. Native WPF resources and controls reproduce the v4 design while avoiding a new dependency and preserving offline operation.
