# V6 ETP multilingual, mobile and synthetic UAT evidence — 2026-08-25

## Engineering outcome

The E3–E7 operational presentation family now has a bounded, offline localization adapter and catalog for English, Marathi and Hindi. The adapter changes text nodes and `textContent` only. It does not use HTML sinks, remote translation, native storage or parent-window capabilities. Unknown text falls back to English, while editable controls, table bodies, scope keys, identities, amounts, evidence references and other business data remain unchanged.

The companion stylesheet contains long-label wrapping, 44 px controls, one-column mobile containment and an explicit desktop breakpoint. It does not introduce horizontal scrolling or viewport-width geometry.

## Honest acceptance boundary

The Marathi and Hindi catalog is `TEST_ONLY_UNAPPROVED`. It is suitable for automated layout and language-switch testing, not publication claims. Native-language staff review and approval remain required.

The staff UAT automation is synthetic. It exercises Staff, Store Manager and Owner contexts, WLMHW-to-HEMW scope switching, English/Marathi/Hindi rendering, role-dependent E3 controls, business-value preservation, and E3–E7 mobile/desktop CSS gates. It is not physical-device acceptance, translation approval or end-to-end staff sign-off.

## Remaining human evidence

- Native Marathi and Hindi review of every operational phrase, including domain terminology.
- Physical tablet and supported desktop walkthroughs with representative Staff, Store Manager and Owner users.
- Signed UAT evidence for authority-blocked and authority-ready E3–E7 paths using approved production inputs.
- Accessibility review with the final approved translations and device font/rendering stack.
