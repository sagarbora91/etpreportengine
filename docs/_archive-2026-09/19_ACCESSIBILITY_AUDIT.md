# Accessibility Audit and Release Checklist

The desktop application targets keyboard and Microsoft UI Automation compatibility. Run `scripts/test-windows-ui.ps1 -AccessibilityAudit` against the published executable on every release.

Manual acceptance remains required because automation cannot reliably assess reading order, meaning, contrast, zoom reflow, or screen-reader announcements.

- Complete every workflow without a mouse; verify logical focus order, visible focus, no keyboard traps, and working default/cancel actions.
- Inspect all controls with Accessibility Insights for Windows. Every focusable control needs a concise Name; custom/status controls also need suitable ControlType, HelpText, and live-region behavior.
- Test Narrator: window title, selected tab, filter labels, progress, completion/failure summaries, validation errors, table headers, sort state, and drill-down context must be announced.
- Test Windows at 200% text size and 200% display scaling at 1280×720. No action or status may be clipped or overlap.
- Verify normal text contrast of at least 4.5:1 and large text/UI-component contrast of at least 3:1. Never use color as the only warning, pass/fail, variance, or selection cue.
- Confirm report grids expose column headers and meaningful row/cell values, remain sortable from the keyboard, and preserve focus after refresh.
- Confirm progress/cancellation and asynchronous database state changes are announced without repeatedly stealing focus.
- Verify PDF exports have a meaningful title and readable logical order. Generated PDFs are printable summaries, not a substitute for the accessible interactive report.

Any failure affecting task completion, accessible naming, keyboard operation, or critical status interpretation blocks release. Record lesser findings with owner, severity, workaround, and due date.
