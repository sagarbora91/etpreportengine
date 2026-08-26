import crypto from 'node:crypto';

const INDEX_SHA256 = 'dc9a5832bcad119b5794df210bb95b40db15293b515f91b497045bd27d9d395c';
const MANIFEST_SHA256 = '4a2b046c3baa24434c11eb7c0ecffa8c2caac8617373a52bf75bdfd7f38fab78';
const sha256 = value => crypto.createHash('sha256').update(value).digest('hex');

export function restorePrePhase6gShellAssets(input) {
  let index = String(input.index);
  index = index
    // Phase 6J release-label approval is newer than the frozen pre-6G shell authority.
    .replaceAll('V6', 'V5.5')
    .replace('<span id="aboutBuild">6</span>', '<span id="aboutBuild">2.9</span>')
    .replace('<script src="etp-verified-analytics.js"></script>\n', '')
    .replace('<script src="etp-analytics-consumer.js"></script>\n', '')
    .replace('<script src="etp-cro-reconciliation.js"></script>\n', '')
    .replace('<script src="etp-operational-foundation.js"></script>\n', '')
    .replace('<script src="etp-operational-store.js"></script>\n', '')
    .replace('<script src="etp-operational-adapters.js"></script>\n', '')
    .replace('<script src="etp-operational-runtime.js"></script>\n', '')
    .replace('<script src="etp-e4-authority-intake.js"></script>\n', '')
    .replace('<script src="etp-e6-authority-intake.js"></script>\n', '')
    .replace('<script src="etp-e5-authority-intake.js"></script>\n', '')
    .replace('<script src="etp-e7-authority-intake.js"></script>\n', '')
    .replace('<script src="etp-e7-service-verifier.js"></script>\n', '')
    .replace('<script src="etp-e7-service-operational.js"></script>\n', '')
    .replace('<script src="etp-target-planning.js"></script>\n', '')
    .replace('<script src="etp-e3-orchestrator.js"></script>\n', '')
    .replace('<script src="etp-e3-presentation.js"></script>\n', '')
    .replace('<script src="etp-e4-orchestrator.js"></script>\n', '')
    .replace('<script src="etp-e4-presentation.js"></script>\n', '')
    .replace('<script src="etp-e6-presentation.js"></script>\n', '')
    .replace('<script src="etp-e5-presentation.js"></script>\n', '')
    .replace('<script src="etp-operational-i18n.js"></script>\n', '')
    .replace('<script src="etp-e5-payroll-bridge.js"></script>\n', '')
    .replace('<script src="etp-operational-gateway.js"></script>\n', '')
    .replace('<script src="etp-operational-mount.js"></script>\n', '')
    .replace('<script src="etp-e3-verified-join.js"></script>\n', '')
    .replace('<script src="etp-operational-bootstrap.js"></script>\n', '')
    .replace('<script src="etp-operational-shell-composer.js"></script>\n', '')
    .replace('<script src="etp-exception-monitor.js"></script>\n', '')
    .replace('<script src="etp-incentive-control.js"></script>\n', '')
    .replace('<script src="etp-operations-consumer.js"></script>\n', '')
    .replace(/\n      <!-- Phase 6H\.1: sanitized, read-only ETP E2 summary; never a declaration\. -->\n      <div class="card card-pad etp-e2-home" id="etpAnalyticsHome" aria-live="polite"><\/div>\n/, '')
    .replace('  try{ renderEtpAnalyticsHome(); }catch(e){} // Phase 6H.1: verified ETP only; never declaration totals.\n', '')
    .replace('      <div class="card card-pad etp-e6-home" id="etpExceptionHome" aria-live="polite"></div>\n', '')
    .replace('  try{ renderEtpExceptionHome(); }catch(e){} // Phase 6H.4: sanitized controlled-state summary; never raw evidence.\n', '')
    .replace(/function renderEtpExceptionHome\(\)\{[\s\S]*?\n\}\n(?=\/\* TODAY DETAIL)/, '')
    .replace(/var __etpHomeAnalyticsSeq=0;\nasync function renderEtpAnalyticsHome\(\)\{[\s\S]*?\n\}\n(?=\/\* TODAY DETAIL)/, '')
    .replace(/<script>try\{var __nativeAuthority[\s\S]*?<\/script>/,
      "<script>try{var __nativeAuthority=localStorage.getItem('saagar_native_store_migrated_v1')==='1';var __m=__nativeAuthority?null:localStorage.getItem('saagar_ui_mode');var __mob=(__m==='mobile')||(__m!=='desktop'&&(window.innerWidth||document.documentElement.clientWidth||0)<900);if(__mob)document.documentElement.classList.add('bcc-mobile');}catch(e){}</script>")
    .replace(/<link rel="stylesheet" href="shell-responsive\.css">\r?\n/, '')
    .replace(/\s*<button type="button" class="seg-btn" data-mode="auto"[^\n]+\r?\n/, '\n')
    .replace(/<script src="shared\/shell-responsive-runtime\.js"><\/script>\r?\n/, '')
    .replace(/function getUiModePreference\(\)[\s\S]*?(?=function applyUiModeToFrame\(\))/, "function getUiMode(){ try{ var m=safeGet(UI_MODE_KEY); if(m==='desktop'||m==='mobile') return m; }catch(e){} try{ return (window.innerWidth||document.documentElement.clientWidth||0) < 900 ? 'mobile' : 'desktop'; }catch(e){ return 'mobile'; } }\n")
    .replace(/function reflectUiModeUI\(\)\{[\s\S]*?function toggleUiMode\(\)\{[^\n]+\}\r?\n/, `function reflectUiModeUI(){ var m=getUiMode();
  try{ document.documentElement.classList.toggle('bcc-mobile', m==='mobile'); }catch(e){}
  try{ var ic=$('uiModeIcon'); if(ic) ic.textContent=(m==='mobile'?'📱':'🖥'); var b=$('uiModeBtn'); if(b) b.title='Layout: '+(m==='mobile'?'Mobile':'Desktop')+' — tap to switch'; }catch(e){}
  try{ var mic=$('moduleUiModeIcon'); if(mic) mic.textContent=(m==='mobile'?'📱':'🖥'); var mb=$('moduleUiModeBtn'); if(mb) mb.title='Layout: '+(m==='mobile'?'Mobile':'Desktop')+' — tap to switch'; }catch(e){}
  try{ var segs=document.querySelectorAll('#uiModeSeg .seg-btn'); for(var i=0;i<segs.length;i++){ segs[i].classList.toggle('active', segs[i].getAttribute('data-mode')===m); } }catch(e){}
}
function setUiMode(mode){ if(mode!=='mobile'&&mode!=='desktop') return; try{ safeSet(UI_MODE_KEY, mode); }catch(e){} reflectUiModeUI(); applyUiModeToFrame(); try{ if(activeView==='config') switchConfigTab(activeConfigTab||'appearance'); else syncSettingsLayoutClass(); }catch(e){} try{ toast((mode==='mobile'?'📱 Mobile':'🖥 Desktop')+' layout'); }catch(e){} }
function toggleUiMode(){ setUiMode(getUiMode()==='mobile'?'desktop':'mobile'); }
`)
    .replace(/\s*try\{ if\(window\.SaagarShellResponsive\)\{ window\.SaagarShellResponsive\.onChange[^\n]+\r?\n/, '\n');

  const manifest = String(input.manifest)
    .replace(/,\r?\n\s*Object\.freeze\(\{ id: 'shell-responsive-css'[^\n]+\r?\n\s*Object\.freeze\(\{ id: 'shell-responsive-runtime'[^\n]+/, '');

  if (sha256(index) !== INDEX_SHA256) throw new Error('Shell did not reconstruct to pre-Phase6G index authority');
  if (sha256(manifest) !== MANIFEST_SHA256) throw new Error('Shell manifest did not reconstruct to pre-Phase6G authority');
  return { index, manifest };
}
