import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  LEGACY_ASSET,
  LEGACY_MODULE_ALLOWLIST,
  MODULE_BASELINE_SHA256,
  restoreMigratedLegacySource
} from '../../scripts/prepare-phase6c-mobile-legacy-css.mjs';
import { readModuleManifestSource, renderModuleManifestSource } from '../../scripts/lib/module-manifest-source.mjs';
import crypto from 'node:crypto';
import { restorePhase6dStockSource } from './phase6e-stock-source.mjs';
import { restorePhase6eEtpGatewaySource, restorePhase6eEtpPresentationSource } from './phase6f-family-a-source.mjs';
import { restorePhase6eFamilyASource } from './phase6f-family-a-source.mjs';
import { restorePrePhase6gFamilyBSource } from './phase6g-family-b-source.mjs';
import { restorePrePhase6gShellAssets } from './phase6g-shell-source.mjs';
import { restorePrePhase6h1EtpIndex, restorePrePhase6h1GatewaySource, restorePrePhase6h1PresentationSource, restorePreEtpBatchRuntime, restorePreEtpBatchWorker, restorePreEtpBatchWorkerClient } from './phase6h1-etp-source.mjs';

const sha256 = value => crypto.createHash('sha256').update(value).digest('hex');
const ETP_SHA256 = 'b2973563b988779468471950bb777c6323580e90ac6011c9038581845b9cfa12';
const PHASE6I_MOBILE_MARKER = '/* Phase 6I: consolidated responsive migration authority (from Phase 6C) */';
const PHASE6C_MOBILE_AUTHORITY = fs.readFileSync(path.resolve(
  path.dirname(fileURLToPath(import.meta.url)), '../fixtures/phase6c/module-mobile-legacy.css'
));
const PRE_PHASE6D_BRAND_TOKENS = `:root{
  --navy:#0d2340; --navy-mid:#1a3a5c; --navy-light:#264d7a;
  --gold:#b8922a; --gold-light:#d4a843; --gold-pale:#fdf6e3;
  --cream:#faf8f3; --paper:#ffffff;
  --red:#b91c1c; --red-pale:#fef2f2; --amber:#b45309; --amber-pale:#fffbeb;
  --green:#166534; --green-pale:#f0fdf4; --blue:#1d4ed8; --blue-pale:#eff6ff;
  --gray-50:#fafafa; --gray-100:#f4f4f5; --gray-200:#e4e4e7; --gray-300:#d4d4d8;
  --gray-400:#a1a1aa; --gray-500:#71717a; --gray-600:#52525b; --gray-700:#3f3f46; --gray-800:#27272a;
  --radius:12px; --radius-lg:16px;
  --font-sans:'DM Sans',system-ui,'Segoe UI',Roboto,Arial,sans-serif;
  --font-serif:'DM Serif Display',Georgia,'Times New Roman',serif;
}
`;

export function reconstructPhase6cBoundaryWww(workspaceRoot) {
  const mobileLayoutPath = path.join(workspaceRoot, 'www/mobile-layout.css');
  fs.writeFileSync(mobileLayoutPath, fs.readFileSync(mobileLayoutPath, 'utf8')
    .replace('  /* Service: every stage remains visible without a sideways gesture. */', '  /* Service: stage rail stays one line and exposes a gold scroll affordance. */')
    .replace(`    flex-wrap: wrap !important;
    overflow-x: hidden !important;
    padding: 2px 2px 8px !important;`, '    padding: 2px 30px 8px 2px !important;')
    .replace(`    flex-wrap: wrap !important;
    overflow-x: hidden !important;
    padding: 4px 8px !important;
  }
  html.bcc-mobile[data-mod="expense"] .tabs > * { flex: 1 1 auto !important; }`, `    padding-right: 30px !important;
  }`)
    .replace('  /* Leave: utility actions wrap so every destination is visible. */', '  /* Leave: the large utility toolbar becomes an explicit rail, while calendar\n     and staff timelines remain locally scrollable. */')
    .replace(`    flex-wrap: wrap !important;
    width: 100% !important;
    max-width: 100% !important;
    overflow-x: hidden !important;
    padding: 8px 0 3px !important;
    -webkit-mask-image: none;`, `    flex-wrap: nowrap !important;
    width: 100% !important;
    max-width: 100% !important;
    overflow-x: auto !important;
    padding: 8px 28px 3px 0 !important;
    scrollbar-width: none;
    -webkit-overflow-scrolling: touch;
    -webkit-mask-image: linear-gradient(to right, #000 0, #000 calc(100% - 28px), transparent 100%);`)
    .replace('  html.bcc-mobile[data-mod="leave"] .header-actions > * { flex: 1 1 auto !important; }', '  html.bcc-mobile[data-mod="leave"] .header-actions > * { flex: 0 0 auto !important; }')
    .replace('  /* Tax: the sticky stack becomes normal flow and controls wrap in place. */', '  /* Tax: the 283px sticky stack becomes normal document flow. Controls remain\n     accessible in two compact horizontal rails. */')
    .replace(`    flex-wrap: wrap !important;
    max-width: 100% !important;
    overflow-x: hidden !important;
    padding: 8px 0 2px !important;
    -webkit-mask-image: none;`, `    flex-wrap: nowrap !important;
    max-width: 100% !important;
    overflow-x: auto !important;
    padding: 8px 28px 2px 0 !important;
    scrollbar-width: none;
    -webkit-overflow-scrolling: touch;
    -webkit-mask-image: linear-gradient(to right, #000 0, #000 calc(100% - 28px), transparent 100%);`)
    .replace('  html.bcc-mobile[data-mod="tax"] .hd-right > * { flex: 1 1 180px !important; min-width: 0 !important; }', '  html.bcc-mobile[data-mod="tax"] .hd-right > * { flex: 0 0 auto !important; }')
    .replace(`  html.bcc-mobile[data-mod="expense"] .tabs {
    width: 100% !important;
    flex-wrap: wrap !important;
    overflow-x: hidden !important;
    padding: 4px 8px !important;
  }
  html.bcc-mobile[data-mod="expense"] .tabs > * { flex: 1 1 auto !important; }
  html.bcc-mobile[data-mod="service"] .stage-chips {
    flex-wrap: wrap !important;
    overflow-x: hidden !important;
  }
`, '')
    .replace(`    flex-wrap: wrap !important;
    max-width: 100% !important;
    overflow-x: hidden !important;
    padding: 8px 0 2px !important;
    -webkit-mask-image: none;`, `    flex-wrap: nowrap !important;
    max-width: 100% !important;
    overflow-x: auto !important;
    padding: 8px 28px 2px 0 !important;
    scrollbar-width: none;
    -webkit-overflow-scrolling: touch;
    -webkit-mask-image: linear-gradient(to right, #000 0, #000 calc(100% - 28px), transparent 100%);`)
    .replace(`  html.bcc-mobile[data-mod="tax"] .hd-right > * { flex: 1 1 180px !important; min-width: 0 !important; }
  html.bcc-mobile[data-mod="leave"] .header-actions > * { flex: 1 1 auto !important; }`, `  html.bcc-mobile[data-mod="tax"] .hd-right > *,
  html.bcc-mobile[data-mod="leave"] .header-actions > * { flex: 0 0 auto !important; }`), 'utf8');
  const buildIdentityPath = path.join(workspaceRoot, 'www/build-identity.js');
  fs.writeFileSync(buildIdentityPath,
    fs.readFileSync(buildIdentityPath, 'utf8').replace("appVersion: 'V6'", "appVersion: 'V5.5'"), 'utf8');
  // Phase 6I removed the production legacy asset after consolidating its bytes
  // into the canonical common sheet. Recreate the earlier boundary explicitly
  // from the test-only frozen authority before applying later reverse migrations.
  const commonPath = path.join(workspaceRoot, 'www/shared/module-mobile-common.css');
  const commonSource = fs.readFileSync(commonPath, 'utf8');
  if (commonSource.includes(PHASE6I_MOBILE_MARKER)) {
    const authority = PHASE6C_MOBILE_AUTHORITY;
    fs.writeFileSync(commonPath, `${commonSource.split(PHASE6I_MOBILE_MARKER)[0].trimEnd()}\n`, 'utf8');
    fs.writeFileSync(path.join(workspaceRoot, 'www/shared/module-mobile-legacy.css'), authority);
    const commonLink = '<link rel="stylesheet" href="../../shared/module-mobile-common.css">';
    const legacyLink = '<link id="st-v5-mobile-css" rel="stylesheet" href="../../shared/module-mobile-legacy.css">';
    for (const moduleId of LEGACY_MODULE_ALLOWLIST) {
      const moduleFile = path.join(workspaceRoot, `www/modules/${moduleId}/index.html`);
      const source = fs.readFileSync(moduleFile, 'utf8');
      if (!source.includes(commonLink + '\n') || source.includes(legacyLink)) throw new Error(`${moduleId} Phase 6I cleanup boundary drift`);
      fs.writeFileSync(moduleFile, source.replace(commonLink + '\n', commonLink + '\n' + legacyLink), 'utf8');
    }
    const manifestPath = path.join(workspaceRoot, 'www/module-manifest.js');
    let manifest = fs.readFileSync(manifestPath, 'utf8')
      .replace('input.sharedAssets.length !== 66', 'input.sharedAssets.length !== 67')
      .replace('sharedAssets must contain exactly sixty-six entries', 'sharedAssets must contain exactly sixty-seven entries')
      .replace("      ,{ id: 'module-mobile-common-css', file: 'shared/module-mobile-common.css' }\n",
        "      ,{ id: 'module-mobile-common-css', file: 'shared/module-mobile-common.css' }\n      ,{ id: 'module-mobile-legacy-css', file: 'shared/module-mobile-legacy.css' }\n");
    const legacyEntry = `    {\n      "id": "module-mobile-legacy-css",\n      "version": 1,\n      "file": "shared/module-mobile-legacy.css",\n      "bytes": ${authority.length},\n      "sha256": "${sha256(authority)}"\n    }`;
    manifest = manifest.replace(/(    \{\n      "id": "module-mobile-common-css",[\s\S]*?\n    \}),/, `$1,\n${legacyEntry},`);
    fs.writeFileSync(manifestPath, manifest, 'utf8');
  }
  const phase6gShellPath = path.join(workspaceRoot, 'www/index.html');
  const phase6gShellManifestPath = path.join(workspaceRoot, 'www/shell-asset-manifest.js');
  const phase6gShell = restorePrePhase6gShellAssets({
    index: fs.readFileSync(phase6gShellPath, 'utf8'),
    manifest: fs.readFileSync(phase6gShellManifestPath, 'utf8')
  });
  fs.writeFileSync(phase6gShellPath, phase6gShell.index, 'utf8');
  fs.writeFileSync(phase6gShellManifestPath, phase6gShell.manifest, 'utf8');
  fs.rmSync(path.join(workspaceRoot, 'www/shell-responsive.css'));
  fs.rmSync(path.join(workspaceRoot, 'www/shared/shell-responsive-runtime.js'));
  const etpPhase6hPath = path.join(workspaceRoot, 'www/modules/etp/index.html');
  fs.writeFileSync(etpPhase6hPath, restorePrePhase6h1EtpIndex(fs.readFileSync(etpPhase6hPath, 'utf8')), 'utf8');
  for (const asset of ['etp-verified-analytics.js', 'etp-analytics-consumer.js', 'etp-operational-foundation.js', 'etp-operational-store.js', 'etp-operational-adapters.js', 'etp-operational-runtime.js', 'etp-e4-authority-intake.js', 'etp-e6-authority-intake.js', 'etp-e5-authority-intake.js', 'etp-e7-authority-intake.js', 'etp-e7-service-verifier.js', 'etp-e7-service-operational.js', 'etp-e7-presentation.js', 'etp-e7-presentation.css', 'etp-e7-module-host.js', 'etp-cro-reconciliation.js', 'etp-e3-orchestrator.js', 'etp-e3-presentation.js', 'etp-e3-presentation.css', 'etp-target-planning.js', 'etp-e4-orchestrator.js', 'etp-e4-presentation.js', 'etp-e4-presentation.css', 'etp-e6-presentation.js', 'etp-e6-presentation.css', 'etp-e5-presentation.js', 'etp-e5-presentation.css', 'etp-operational-i18n.js', 'etp-operational-i18n.css', 'etp-e5-payroll-bridge.js', 'etp-operational-gateway.js', 'etp-operational-mount.js', 'etp-e3-verified-join.js', 'etp-operational-bootstrap.js', 'etp-operational-shell-composer.js', 'etp-operational-module-host.js', 'etp-operational-frame-bridge.js', 'etp-exception-monitor.js', 'etp-incentive-control.js', 'etp-operations-consumer.js']) {
    const assetPath = path.join(workspaceRoot, 'www', asset); if (fs.existsSync(assetPath)) fs.rmSync(assetPath);
  }
  const phase6hGatewayPath = path.join(workspaceRoot, 'www/etp-module-gateway.js');
  const phase6hPresentationPath = path.join(workspaceRoot, 'www/etp-verified-presentation.js');
  fs.writeFileSync(phase6hGatewayPath, restorePrePhase6h1GatewaySource(fs.readFileSync(phase6hGatewayPath, 'utf8')), 'utf8');
  fs.writeFileSync(phase6hPresentationPath, restorePrePhase6h1PresentationSource(fs.readFileSync(phase6hPresentationPath, 'utf8')), 'utf8');
  const batchAssets = [
    ['etp-import-runtime.js', restorePreEtpBatchRuntime],
    ['etp-import-worker.js', restorePreEtpBatchWorker],
    ['etp-worker-client.js', restorePreEtpBatchWorkerClient]
  ];
  batchAssets.forEach(function (entry) { const assetPath = path.join(workspaceRoot, 'www', entry[0]); fs.writeFileSync(assetPath, entry[1](fs.readFileSync(assetPath, 'utf8')), 'utf8'); });
  for (const moduleId of ['expense', 'leave', 'cro_audit', 'tax', 'dsr', 'qms']) {
    const modulePath = path.join(workspaceRoot, `www/modules/${moduleId}/index.html`);
    const cssName = `${moduleId.replace('_', '-')}-ui.css`;
    const cssPath = path.join(workspaceRoot, `www/modules/${moduleId}/${cssName}`);
    fs.writeFileSync(modulePath, restorePrePhase6gFamilyBSource(
      moduleId, fs.readFileSync(modulePath, 'utf8'), fs.existsSync(cssPath) ? fs.readFileSync(cssPath, 'utf8') : ''
    ), 'utf8');
    if (fs.existsSync(cssPath)) fs.rmSync(cssPath);
  }
  fs.rmSync(path.join(workspaceRoot, 'www/modules/qms/qms-view.js'));
  for (const moduleId of ['payroll', 'grooming', 'service']) {
    const modulePath = path.join(workspaceRoot, `www/modules/${moduleId}/index.html`);
    const cssPath = path.join(workspaceRoot, `www/modules/${moduleId}/${moduleId}-ui.css`);
    fs.writeFileSync(modulePath, restorePhase6eFamilyASource(
      moduleId, fs.readFileSync(modulePath, 'utf8'), fs.readFileSync(cssPath, 'utf8')
    ), 'utf8');
    fs.rmSync(cssPath);
  }
  const stockPath = path.join(workspaceRoot, 'www/modules/stock/index.html');
  const stockCssPath = path.join(workspaceRoot, 'www/modules/stock/stock-ui.css');
  fs.writeFileSync(stockPath, restorePhase6dStockSource(
    fs.readFileSync(stockPath, 'utf8'), fs.readFileSync(stockCssPath, 'utf8')
  ), 'utf8');
  fs.rmSync(stockCssPath);
  const shellPath = path.join(workspaceRoot, 'www/index.html');
  fs.writeFileSync(shellPath, fs.readFileSync(shellPath, 'utf8')
    .replace('content="width=device-width, initial-scale=1.0, viewport-fit=cover"', 'content="width=device-width, initial-scale=1.0, viewport-fit=cover, user-scalable=no"'), 'utf8');
  const dsrPath = path.join(workspaceRoot, 'www/modules/dsr/index.html');
  fs.writeFileSync(dsrPath, fs.readFileSync(dsrPath, 'utf8')
    .replace('content="width=device-width, initial-scale=1.0"', 'content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no"'), 'utf8');
  const manifestPath = path.join(workspaceRoot, 'www/module-manifest.js');
  let manifestSource = fs.readFileSync(manifestPath, 'utf8')
    .replace("input.sharedAssets.length !== 67", "input.sharedAssets.length !== 27")
    .replace("sharedAssets must contain exactly sixty-seven entries", "sharedAssets must contain exactly twenty-seven entries")
    .replace("input.sharedAssets.length !== 27", "input.sharedAssets.length !== 25")
    .replace("sharedAssets must contain exactly twenty-seven entries", "sharedAssets must contain exactly twenty-five entries")
    .replace("input.sharedAssets.length !== 25", "input.sharedAssets.length !== 20")
    .replace("sharedAssets must contain exactly twenty-five entries", "sharedAssets must contain exactly twenty entries")
    .replace("input.sharedAssets.length !== 20", "input.sharedAssets.length !== 11")
    .replace("sharedAssets must contain exactly twenty entries", "sharedAssets must contain exactly eleven entries");
  for (const entry of [
    "      ,{ id: 'etp-verified-analytics', file: 'etp-verified-analytics.js' }\n",
    "      ,{ id: 'etp-analytics-consumer', file: 'etp-analytics-consumer.js' }\n",
    "      ,{ id: 'etp-operational-foundation', file: 'etp-operational-foundation.js' }\n",
    "      ,{ id: 'etp-operational-store', file: 'etp-operational-store.js' }\n",
    "      ,{ id: 'etp-operational-adapters', file: 'etp-operational-adapters.js' }\n",
    "      ,{ id: 'etp-operational-runtime', file: 'etp-operational-runtime.js' }\n",
    "      ,{ id: 'etp-e4-authority-intake', file: 'etp-e4-authority-intake.js' }\n",
    "      ,{ id: 'etp-e6-authority-intake', file: 'etp-e6-authority-intake.js' }\n",
    "      ,{ id: 'etp-e5-authority-intake', file: 'etp-e5-authority-intake.js' }\n",
    "      ,{ id: 'etp-e7-authority-intake', file: 'etp-e7-authority-intake.js' }\n",
    "      ,{ id: 'etp-e7-service-verifier', file: 'etp-e7-service-verifier.js' }\n",
    "      ,{ id: 'etp-e7-service-operational', file: 'etp-e7-service-operational.js' }\n",
    "      ,{ id: 'etp-cro-reconciliation', file: 'etp-cro-reconciliation.js' }\n",
    "      ,{ id: 'etp-e3-orchestrator', file: 'etp-e3-orchestrator.js' }\n",
    "      ,{ id: 'etp-e3-presentation', file: 'etp-e3-presentation.js' }\n",
    "      ,{ id: 'etp-e3-presentation-css', file: 'etp-e3-presentation.css' }\n",
    "      ,{ id: 'etp-target-planning', file: 'etp-target-planning.js' }\n",
    "      ,{ id: 'etp-e4-orchestrator', file: 'etp-e4-orchestrator.js' }\n",
    "      ,{ id: 'etp-e4-presentation', file: 'etp-e4-presentation.js' }\n",
    "      ,{ id: 'etp-e4-presentation-css', file: 'etp-e4-presentation.css' }\n",
    "      ,{ id: 'etp-e6-presentation', file: 'etp-e6-presentation.js' }\n",
    "      ,{ id: 'etp-e6-presentation-css', file: 'etp-e6-presentation.css' }\n",
    "      ,{ id: 'etp-e5-presentation', file: 'etp-e5-presentation.js' }\n",
    "      ,{ id: 'etp-e5-presentation-css', file: 'etp-e5-presentation.css' }\n",
    "      ,{ id: 'etp-operational-i18n', file: 'etp-operational-i18n.js' }\n",
    "      ,{ id: 'etp-operational-i18n-css', file: 'etp-operational-i18n.css' }\n",
    "      ,{ id: 'etp-e5-payroll-bridge', file: 'etp-e5-payroll-bridge.js' }\n",
    "      ,{ id: 'etp-e7-presentation', file: 'etp-e7-presentation.js' }\n",
    "      ,{ id: 'etp-e7-presentation-css', file: 'etp-e7-presentation.css' }\n",
    "      ,{ id: 'etp-e7-module-host', file: 'etp-e7-module-host.js' }\n",
    "      ,{ id: 'etp-operational-gateway', file: 'etp-operational-gateway.js' }\n",
    "      ,{ id: 'etp-operational-mount', file: 'etp-operational-mount.js' }\n",
    "      ,{ id: 'etp-e3-verified-join', file: 'etp-e3-verified-join.js' }\n",
    "      ,{ id: 'etp-operational-bootstrap', file: 'etp-operational-bootstrap.js' }\n",
    "      ,{ id: 'etp-operational-shell-composer', file: 'etp-operational-shell-composer.js' }\n",
    "      ,{ id: 'etp-operational-module-host', file: 'etp-operational-module-host.js' }\n",
    "      ,{ id: 'etp-operational-frame-bridge', file: 'etp-operational-frame-bridge.js' }\n",
    "      ,{ id: 'etp-exception-monitor', file: 'etp-exception-monitor.js' }\n",
    "      ,{ id: 'etp-incentive-control', file: 'etp-incentive-control.js' }\n",
    "      ,{ id: 'etp-operations-consumer', file: 'etp-operations-consumer.js' }\n",
    "      ,{ id: 'module-rendered-components', file: 'shared/module-rendered-components.js' }\n",
    "      ,{ id: 'leave-ui-css', file: 'modules/leave/leave-ui.css' }\n",
    "      ,{ id: 'cro-audit-ui-css', file: 'modules/cro_audit/cro-audit-ui.css' }\n",
    "      ,{ id: 'tax-ui-css', file: 'modules/tax/tax-ui.css' }\n",
    "      ,{ id: 'dsr-ui-css', file: 'modules/dsr/dsr-ui.css' }\n",
    "      ,{ id: 'qms-view', file: 'modules/qms/qms-view.js' }\n",
    "      ,{ id: 'qms-ui-css', file: 'modules/qms/qms-ui.css' }\n",
    "      ,{ id: 'module-responsive-css', file: 'shared/module-responsive.css' }\n",
    "      ,{ id: 'module-ui-runtime', file: 'shared/module-ui-runtime.js' }\n",
    "      ,{ id: 'module-table-css', file: 'shared/module-table.css' }\n",
    "      ,{ id: 'module-table-runtime', file: 'shared/module-table-runtime.js' }\n",
    "      ,{ id: 'module-components-css', file: 'shared/module-components.css' }\n"
    ,"      ,{ id: 'stock-ui-css', file: 'modules/stock/stock-ui.css' }\n"
    ,"      ,{ id: 'payroll-ui-css', file: 'modules/payroll/payroll-ui.css' }\n"
    ,"      ,{ id: 'grooming-ui-css', file: 'modules/grooming/grooming-ui.css' }\n"
    ,"      ,{ id: 'service-ui-css', file: 'modules/service/service-ui.css' }\n"
  ]) manifestSource = manifestSource.replace(entry, '');
  fs.writeFileSync(manifestPath, manifestSource, 'utf8');
  fs.writeFileSync(path.join(workspaceRoot, 'www/shared/module-brand-tokens.css'), PRE_PHASE6D_BRAND_TOKENS, 'utf8');
  fs.rmSync(path.join(workspaceRoot, 'www/shared/module-responsive.css'));
  fs.rmSync(path.join(workspaceRoot, 'www/shared/module-ui-runtime.js'));
  fs.rmSync(path.join(workspaceRoot, 'www/shared/module-table.css'));
  fs.rmSync(path.join(workspaceRoot, 'www/shared/module-table-runtime.js'));
  fs.rmSync(path.join(workspaceRoot, 'www/shared/module-components.css'));
  fs.rmSync(path.join(workspaceRoot, 'www/shared/module-rendered-components.js'));
  const snapshot = readModuleManifestSource(workspaceRoot);
  snapshot.data.sharedAssets = snapshot.data.sharedAssets.filter(item => ![
    'etp-verified-analytics', 'etp-analytics-consumer', 'etp-cro-reconciliation', 'etp-target-planning', 'etp-exception-monitor', 'etp-incentive-control', 'etp-operations-consumer', 'etp-operational-i18n', 'etp-operational-i18n-css',
    'module-rendered-components', 'leave-ui-css', 'cro-audit-ui-css', 'tax-ui-css', 'dsr-ui-css', 'qms-view', 'qms-ui-css',
    'module-responsive-css', 'module-ui-runtime', 'module-table-css', 'module-table-runtime', 'module-components-css',
    'stock-ui-css', 'payroll-ui-css', 'grooming-ui-css', 'service-ui-css'
  ].includes(item.id));
  for (const moduleId of ['expense', 'leave', 'cro_audit', 'tax', 'dsr', 'qms']) {
    const module = snapshot.data.modules.find(item => item.id === moduleId);
    const bytes = fs.readFileSync(path.join(workspaceRoot, 'www', module.file));
    module.bytes = bytes.length;
    module.sha256 = sha256(bytes);
  }
  const brandTokens = snapshot.data.sharedAssets.find(item => item.id === 'module-brand-tokens-css');
  const brandBytes = fs.readFileSync(path.join(workspaceRoot, 'www', brandTokens.file));
  brandTokens.bytes = brandBytes.length;
  brandTokens.sha256 = sha256(brandBytes);
  const mobileCommon = snapshot.data.sharedAssets.find(item => item.id === 'module-mobile-common-css');
  const mobileCommonBytes = fs.readFileSync(commonPath);
  mobileCommon.bytes = mobileCommonBytes.length;
  mobileCommon.sha256 = sha256(mobileCommonBytes);
  const dsrModule = snapshot.data.modules.find(item => item.id === 'dsr');
  const dsrBytes = fs.readFileSync(dsrPath);
  dsrModule.bytes = dsrBytes.length;
  dsrModule.sha256 = sha256(dsrBytes);
  const stockModule = snapshot.data.modules.find(item => item.id === 'stock');
  const stockBytes = fs.readFileSync(stockPath);
  stockModule.bytes = stockBytes.length;
  stockModule.sha256 = sha256(stockBytes);
  const etpModule = snapshot.data.modules.find(item => item.id === 'etp');
  const etpBytes = fs.readFileSync(etpPhase6hPath);
  etpModule.bytes = etpBytes.length;
  etpModule.sha256 = sha256(etpBytes);
  const presentationAsset = snapshot.data.sharedAssets.find(item => item.id === 'etp-verified-presentation');
  const presentationBytes = fs.readFileSync(phase6hPresentationPath);
  presentationAsset.bytes = presentationBytes.length;
  presentationAsset.sha256 = sha256(presentationBytes);
  for (const moduleId of ['payroll', 'grooming', 'service']) {
    const module = snapshot.data.modules.find(item => item.id === moduleId);
    const bytes = fs.readFileSync(path.join(workspaceRoot, 'www', module.file));
    module.bytes = bytes.length;
    module.sha256 = sha256(bytes);
  }
  fs.writeFileSync(manifestPath, renderModuleManifestSource(snapshot, snapshot.data), 'utf8');
  return workspaceRoot;
}

export function reconstructPrePhase6cWww(workspaceRoot) {
  reconstructPhase6cBoundaryWww(workspaceRoot);
  const gatewayPath = path.join(workspaceRoot, 'www/etp-module-gateway.js');
  fs.writeFileSync(gatewayPath, restorePhase6eEtpGatewaySource(fs.readFileSync(gatewayPath, 'utf8')), 'utf8');
  const presentationPath = path.join(workspaceRoot, 'www/etp-verified-presentation.js');
  fs.writeFileSync(presentationPath, restorePhase6eEtpPresentationSource(fs.readFileSync(presentationPath, 'utf8')), 'utf8');
  const etpPath = path.join(workspaceRoot, 'www/modules/etp/index.html');
  const etpBefore = fs.readFileSync(etpPath);
  if (sha256(etpBefore) !== ETP_SHA256 || etpBefore.includes(Buffer.from('module-mobile-legacy.css'))) {
    throw new Error('ETP is not the frozen unlinked Phase 6C exclusion authority');
  }
  const authorityPath = path.join(workspaceRoot, 'www', LEGACY_ASSET);
  const authority = fs.readFileSync(authorityPath, 'utf8');
  for (const moduleId of LEGACY_MODULE_ALLOWLIST) {
    const file = path.join(workspaceRoot, 'www/modules', moduleId, 'index.html');
    const restored = restoreMigratedLegacySource(moduleId, fs.readFileSync(file, 'utf8'), authority);
    fs.writeFileSync(file, restored, 'utf8');
    if (sha256(Buffer.from(restored)) !== MODULE_BASELINE_SHA256[moduleId]) {
      throw new Error(`${moduleId} did not reconstruct to its pre-Phase6C authority`);
    }
  }
  fs.rmSync(authorityPath);

  const manifestPath = path.join(workspaceRoot, 'www/module-manifest.js');
  let manifestSource = fs.readFileSync(manifestPath, 'utf8')
    .replace("input.sharedAssets.length !== 11", "input.sharedAssets.length !== 10")
    .replace("sharedAssets must contain exactly eleven entries", "sharedAssets must contain exactly ten entries")
    .replace("      ,{ id: 'module-mobile-legacy-css', file: 'shared/module-mobile-legacy.css' }\n", '');
  fs.writeFileSync(manifestPath, manifestSource, 'utf8');
  const snapshot = readModuleManifestSource(workspaceRoot);
  snapshot.data.sharedAssets = snapshot.data.sharedAssets.filter(item => item.id !== 'module-mobile-legacy-css');
  const presentationAsset = snapshot.data.sharedAssets.find(item => item.id === 'etp-verified-presentation');
  const presentationBytes = fs.readFileSync(presentationPath);
  presentationAsset.bytes = presentationBytes.length;
  presentationAsset.sha256 = sha256(presentationBytes);
  for (const module of snapshot.data.modules) {
    if (!LEGACY_MODULE_ALLOWLIST.includes(module.id)) continue;
    const bytes = fs.readFileSync(path.join(workspaceRoot, 'www', module.file));
    module.bytes = bytes.length;
    module.sha256 = sha256(bytes);
  }
  fs.writeFileSync(manifestPath, renderModuleManifestSource(snapshot, snapshot.data), 'utf8');
  const etpAfter = fs.readFileSync(etpPath);
  if (!etpAfter.equals(etpBefore) || sha256(etpAfter) !== ETP_SHA256 || etpAfter.includes(Buffer.from('module-mobile-legacy.css'))) {
    throw new Error('ETP changed while reconstructing pre-Phase6C authority');
  }
  return workspaceRoot;
}

export function createPrePhase6cWorkspace(sourceRoot) {
  const target = fs.mkdtempSync(path.join(os.tmpdir(), 'saagar-pre-phase6c-'));
  for (const entry of ['www', 'scripts', 'tests', 'verification']) {
    fs.cpSync(path.join(sourceRoot, entry), path.join(target, entry), { recursive: true });
  }
  for (const file of ['package.json']) fs.copyFileSync(path.join(sourceRoot, file), path.join(target, file));
  reconstructPrePhase6cWww(target);
  return target;
}

export function createPhase6cBoundaryWorkspace(sourceRoot) {
  const target = fs.mkdtempSync(path.join(os.tmpdir(), 'saagar-phase6c-boundary-'));
  for (const entry of ['www', 'scripts', 'tests', 'verification']) {
    fs.cpSync(path.join(sourceRoot, entry), path.join(target, entry), { recursive: true });
  }
  for (const file of ['package.json']) fs.copyFileSync(path.join(sourceRoot, file), path.join(target, file));
  reconstructPhase6cBoundaryWww(target);
  return target;
}
