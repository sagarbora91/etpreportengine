# UI/UX v4 design system

The visual target follows Windows Fluent principles with restrained Bento-style overview surfaces and enterprise information hierarchy. All production controls remain native WPF.

## Tokens

| Purpose | Token | Value |
|---|---|---|
| App background | `AppBackground` | `#F3F6FB` |
| Surface | `Surface` | `#FFFFFF` |
| Secondary surface | `SurfaceSecondary` | `#F7F9FC` |
| Primary text | `PrimaryText` | `#152038` |
| Secondary text | `SecondaryText` | `#6B778C` |
| Divider | `Divider` | `#E3E8F0` |
| Accent | `Accent` | `#246BFE` |
| Success | `Success` | `#0B8D62` |
| Warning | `Warning` | `#BB6B00` |
| Critical | `Critical` | `#C63B42` |
| Information | `Information` | `#7757D6` |
| Navigation | `NavigationBackground` | `#0C1628` |

Spacing is limited to 4, 8, 12, 16, 20, 24, 32 and 40 DIPs. Radii are 8, 12 and 20 DIPs. These resources live in `Themes/` and should be reused rather than duplicated.

## Typography

Segoe UI Variable Display is used for titles and metrics; Segoe UI Variable Text is the application default. Named styles cover Display, Page Title, Section Title, Card Title, Body, Caption and Metric.

## Density and touch

- Comfortable: 48-DIP minimum actions and 46-DIP grid rows.
- Compact: 34-DIP actions and 30-DIP grid rows.
- Primary operational actions use the accent style and remain visible without hover.
- Focus is expressed with a two-DIP accent border.

## Components

- `ModuleTile`: large role-aware launch surface with vector icon, purpose and live-status slot.
- `StatusBadge`: restrained semantic state.
- `EmptyState`: plain-language absence plus next action.
- `LoadingState`: indeterminate or progress-aware non-blocking feedback.
- Context sidebar: searchable expandable groups with touch targets.
- Detail drawer: right-side contextual inspection; Escape closes it.
- Existing DSR reusable cards and visual-report components remain part of the shared report workspace.

Cards are reserved for module launch, KPI and status summaries. Detailed work uses tables, split layouts, drawers, filters and grouped lists.

## Icons

Icons are local WPF `Geometry` resources in `Themes/Icons.xaml`. No emoji, bitmap screenshots, web fonts, downloaded icon packs or WebView runtime are used.

## Tables

Data grids use horizontal separators, touch/compact row tokens, keyboard navigation and recycling virtualisation. Missing, zero, blocked and not-applicable values remain distinct because display values continue to come from existing report contracts.

## Errors, empty and loading states

User-facing surfaces provide plain-language summaries. Technical exceptions remain available in deeper support/audit views. A missing OCR helper affects only OCR status; it never marks core reporting unavailable.

## Dependency decision

No new UI framework or icon package is introduced. Native WPF resources and controls reproduce the v4 design while avoiding a new dependency and preserving offline operation.
