# Visual report design system

- Theme: navy `#17324D`, blue `#247BA0`, teal `#2A9D8F`, amber `#E9A23B`, red `#C94C4C`, grey `#8795A1`.
- Passed/positive states use teal; warnings amber; failed controls red. Colour is always accompanied by text.
- Values use Indian grouping and rupee formatting. Negative returns retain their sign.
- Missing is shown as **Not available**, not applicable as **N/A**, and a real zero as `0` or `₹0.00`.
- KPI cards are limited to four primary measures. Charts show no more than ten categories plus an explicit **Other** bucket.
- Visuals have readable text equivalents through Windows automation names. The detail table remains keyboard sortable and searchable.
- The screen uses native WPF controls, scalable text and no bitmap screenshots, supporting high-DPI rendering and touch-sized controls.
