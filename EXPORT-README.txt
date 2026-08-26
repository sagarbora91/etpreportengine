SAAGAR CONTROL CENTRE V6 — ETP SOURCE AND REPORT ENGINE EXPORT

Source commit: 605002ff6b9319d287f3f06d0761b5ecb2cf0797
Branch: agent/modular-phase1-shared-spine-v2
Created: 2026-08-25 Asia/Calcutta
Files from commit: 315

CONTENTS
- Complete Retail ETP import, validation, storage, recovery, verified analytics, E3-E7 operational code, presentation code and synthetic demo facade.
- ETP iframe module, shared module runtime/bridge/UI support, localization and responsive assets.
- All module HTML sources required by the module manifest and cross-module financial/report-engine tests.
- Android native ETP/storage/security bridge overrides and build/test utilities. The secret-free release recipe is included only as a source dependency of the API-23 contract test; this export performs no production build, signing or publication.
- Offline Saagar PDF/report engine, CSS, PDF preview/share support, bundled jsPDF/pdf.js/JSZip libraries and local fonts.
- Focused ETP, report-engine, mobile, localization, API-23 and manifest tests, including their complete local test-support library.
- ETP architecture, authority, phase handoff and verification documentation.

BOUNDARY
This archive is generated only from committed Git source at the commit above. It intentionally excludes node_modules, Gradle/generated Android assets, APKs, keystores, credentials, graphify output, temporary fixtures, raw ETP workbooks, customer data and unrelated working-tree changes. It is a source handoff, not a production release or publication approval.

QUICK VERIFICATION
1. npm ci
2. npm run test:etp
3. npm run test:v6-etp-wave7
4. npm run test:v6-etp-wave8
5. npm run test:v6-etp-wave9
6. npm run test:v6-etp-wave10
7. npm run test:v6-etp-final-uat
8. node --test tests/report-csv.test.mjs tests/financial-golden.test.mjs

See EXPORT-FILE-MANIFEST.txt for the exact archived paths.