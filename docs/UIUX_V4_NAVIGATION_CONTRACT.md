# UI/UX v4 navigation contract

## Frozen hierarchy

```text
Global rail
  → contextual module sidebar
    → main workspace
      → right-side detail drawer
```

The rail contains only Modules Home, Global Search, Help, Settings and Profile. Business modules never move into the rail.

## Module home

The default Store Manager registry contains Dashboard, Reports, Accounting, Imports, Archive and Exceptions. The wrap layout adapts without a second UI:

- three or fewer authorised cards remain balanced;
- six cards form the normal operational home;
- Owner may display up to nine cards by adding Registers, Approvals and System Health from the same registry.

Viewer visibility is reduced by the existing role model. Visibility never grants service permission.

## Contextual sidebars

Each module reads hierarchical `NavigationGroupDefinition` and `NavigationItemDefinition` records from `UiNavigationRegistry`. Groups are expandable, scrollable, searchable and role filtered. Every sidebar ends with Help Centre, the display-density selector and Back to Modules.

- Dashboard: Overview; Business Day (including Manual Entry); Performance; Controls.
- Reports: overview, every category generated from `ProductReportCatalogue`, report packs, and controlled future entries.
- Imports: Intake; Quality; Documents & OCR; Digital Registers.
- Accounting: workflow from overview through Tally export/history; controlled future entries.
- Archive: generations, final packs, restatements, comparison, re-export, sharing and sources.
- Exceptions: open items, data quality, source/mapping/import/tender/stock/staff/OCR/accounting issues and approvals.
- Settings: general, users, stores, masters, profiles, KPI/tender/accounting rules, folders, OCR, sharing, backup, scheduler, health and audit.

Adding a production report requires registering it in `ProductReportCatalogue`; Reports navigation discovers it automatically. Adding a module requires one `ModuleDefinition` plus its sidebar groups.

## Responsive behaviour

- At wide sizes the 300-DIP detailed sidebar remains persistent.
- Below 1100 DIPs it becomes an overlay with identical content.
- The user may collapse or reopen it; content is never deleted.
- The application remains supported at 960×600 and prioritises 1366×768 and larger.

## Density

Comfortable is the default: 48-DIP actions and 46-DIP grid rows. Compact uses 34-DIP actions and 30-DIP grid rows. Both modes share the same controls, routes and permissions. Preference is stored in `%LOCALAPPDATA%\EtpReporting\ui-preferences.json` using an atomic replacement.

The selector is located in the contextual sidebar; the bottom status bar is reserved for application status and operation progress.

## Keyboard and accessibility

- Alt+Left/Alt+Right navigate backward and forward; Alt+Home opens Dashboard.
- F1 opens help for the active workspace and Ctrl+/ opens the Keyboard Shortcuts guide.
- Tab and standard WPF keyboard navigation remain available.
- Ctrl+F focuses module sidebar search in Reports/Imports or opens global investigation elsewhere.
- Escape closes the detail drawer.
- Major shell controls provide Automation names and visible keyboard focus.
- Data grids retain row/column virtualisation and standard keyboard navigation.

## Route guarantees

Automated tests confirm every live catalogue report has a reachable feature code, all critical operational areas have navigation entries, future entries have an unavailable reason, and no enabled item points to an unknown destination.
