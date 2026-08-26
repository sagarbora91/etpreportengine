import crypto from 'node:crypto';

export const PHASE6D_STOCK_SHA256 = 'f53711784a1e20fab3b599ccb75d5aacda57b3777de525fbf9727fd43cd33d25';

const sha256 = value => crypto.createHash('sha256').update(value).digest('hex');

export function restorePhase6dStockCss(css) {
  let restored = css
    .replace(`   MOBILE / COMPACT — widths ≤899px
   Additive layer only. Desktop screens (≥900px) stay byte-unchanged.`, `   MOBILE — phones ≤640px (≈360px target)
   Additive layer only. Wide screens (>640px) stay byte-unchanged.`)
    .replace('@media (max-width: 899px) {', '@media (max-width: 640px) {')
    .replace('#brand-inp,[onclick="addBrand()"] { display: none !important; }\n', '')
    .replace(`[data-stock-table-region="opening-counts"],
[data-stock-table-region="closing-counts"],
[data-stock-table-region="daily-summary"],
[data-stock-table-region="monthly-summary"],
[data-stock-table-region="theft-log"] { overflow-x: hidden; }
[data-stock-table-region="movement-reconciliation"] { overflow-x: auto; -webkit-overflow-scrolling: touch; }
`, '')
    .replace(/\/\* Chrome 44 \/ API-23 generated assets[\s\S]*?html\.saagar-legacy-webview \.clk-triage \.wide \{ -webkit-flex-basis: 100%; flex-basis: 100%; \}\n\n/, '')
    .replaceAll('.rtbl.saagar-table--cards', '.rtbl')
    .replace(`
  /* Summaries reduce to their reviewed priority columns instead of becoming a
     second sideways-scrolling data-entry surface. */
  .rtbl.saagar-table--priority { width: 100%; min-width: 0; }
`, '');
  if (restored.endsWith('\n')) restored = restored.slice(0, -1);
  return restored;
}

export function restorePhase6dStockSource(html, css, validate = true) {
  const common = '<link rel="stylesheet" href="../../shared/module-mobile-common.css">\n';
  const legacy = '<link id="st-v5-mobile-css" rel="stylesheet" href="../../shared/module-mobile-legacy.css">';
  if (!html.includes(legacy)) html = html.replace(common, common + legacy);
  let restored = html
    .replace(`<link rel="stylesheet" href="../../shared/module-brand-tokens.css">
<link rel="stylesheet" href="../../shared/module-responsive.css">
<link rel="stylesheet" href="../../shared/module-components.css">
<link rel="stylesheet" href="../../shared/module-table.css">
<script src="../../shared/module-ui-runtime.js"></script>
`, '')
    .replace('<link id="st-v5-hide-css" rel="stylesheet" href="stock-ui.css">', `<style>\n${restorePhase6dStockCss(css)}\n</style>\n<style id="st-v5-hide-css">#brand-inp,[onclick="addBrand()"]{display:none !important}</style>\n`)
    .replace('<body data-saagar-ui data-saagar-width="auto" data-saagar-width-resolved="mobile">\n<script id="stock-phase6e-ui-boot">SaagarUiFoundation.configure(document.body,{mode:\'auto\'});</script>', '<body>')
    .replaceAll(' class="tbl-wrap saagar-table-region--grid" data-stock-table-region="movement-reconciliation"', ' class="tbl-wrap"')
    .replace(/ class="tbl-wrap" data-stock-table-region="[^"]+"/g, ' class="tbl-wrap"')
    .replace(/ class="rtbl saagar-table saagar-table--(?:cards|priority|grid)" data-stock-table-strategy="[^"]+" data-stock-table-workflow="[^"]+"(?: data-saagar-grid-reason="[^"]+")?/g, ' class="rtbl"')
    .replace(/ data-saagar-priority="[1-4]"/g, '')
    .replace("${esc(o.time||'')}", "${o.time||''}")
    .replace("${esc(c.time||'')}", "${c.time||''}")
    .replace(`// Collision-free, deterministic DOM key. Hex code points keep every distinct
// stored brand distinct without changing the storage key or displayed name.
function bid(brand) {
  return 'b_' + Array.from(String(brand), ch => ch.codePointAt(0).toString(16)).join('_');
}`, `// Safe brand ID for DOM IDs (replace spaces with underscores, strip special chars)
function bid(brand) { return brand.replace(/\\s+/g, '_').replace(/[^A-Za-z0-9_]/g, ''); }`);
  if (validate && sha256(restored) !== PHASE6D_STOCK_SHA256) throw new Error(`Stock did not reconstruct to the Phase 6D source authority (${restored.length} bytes, ${sha256(restored)})`);
  return restored;
}
