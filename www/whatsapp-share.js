/* ═══════════════════════════════════════════════════════════════════════════
   SAAGAR TRADERS — BUSINESS CONTROL CENTRE (V4)
   WHATSAPP / PDF SHARE — APK build addition
   ───────────────────────────────────────────────────────────────────────────
   Goal:
     Wherever a "Print" option exists (the shell Print button AND every
     module's own internal print buttons), let the user instead turn that
     exact print output into a PDF and send it on WhatsApp.

   How it works (fully offline):
     1. html2pdf.bundle.min.js (bundled locally) renders the open module's
        page to a real PDF — same content you would have printed.
     2. The PDF is written to the app cache, then handed to Android's native
        Share sheet via the Capacitor Share plugin.
     3. The user taps WhatsApp in that sheet and picks a contact. (Android
        does not allow an app to push a file straight into WhatsApp without
        the share sheet — this is the correct, reliable way.)

   Coverage:
     - Shell toolbar gets a "📲 WhatsApp" button next to "⎙ Print".
     - Every module loads in the same-origin iframe, so this script also
       intercepts each module's own window.print() and offers a small
       chooser: [🖨 Print] or [📲 Share to WhatsApp (PDF)].

   Browser fallback (desktop preview, no Capacitor):
     The PDF is generated and downloaded, with a note that WhatsApp sharing
     is active inside the installed APK.
   ═══════════════════════════════════════════════════════════════════════════ */
(function () {
  'use strict';

  function dateStamp() {
    var d = new Date();
    function p(n) { return (n < 10 ? '0' : '') + n; }
    return d.getFullYear() + p(d.getMonth() + 1) + p(d.getDate()) + '-' + p(d.getHours()) + p(d.getMinutes());
  }

  function sanitize(s) {
    return String(s || 'Saagar').replace(/[^\w\- ]+/g, '').replace(/\s+/g, '_').slice(0, 60) || 'Saagar';
  }

  function notify(msg) {
    try { if (typeof window.toast === 'function') { window.toast(msg); return; } } catch (e) {}
    console.log('[whatsapp-share]', msg);
  }
  function authorizeExport(meta) {
    try {
      if (window.SaagarExportControl && typeof window.SaagarExportControl.authorize === 'function') {
        return window.SaagarExportControl.authorize(meta);
      }
    } catch (e) {}
    notify('Export control unavailable — sharing is blocked');
    return false;
  }
  function finishExport(token, outcome) {
    try {
      return !!(window.SaagarExportControl && window.SaagarExportControl.recordOutcome(token, outcome));
    } catch (e) { return false; }
  }
  function beginExport(token) {
    try {
      return !!(window.SaagarExportControl && window.SaagarExportControl.beginDelivery(token));
    } catch (e) { return false; }
  }

  function blobToBase64(blob) {
    return new Promise(function (resolve, reject) {
      var r = new FileReader();
      r.onloadend = function () {
        var s = String(r.result || '');
        var i = s.indexOf(',');
        resolve(i >= 0 ? s.slice(i + 1) : s);
      };
      r.onerror = reject;
      r.readAsDataURL(blob);
    });
  }

  function caps() {
    var C = window.Capacitor;
    if (C && C.Plugins && C.Plugins.Share && C.Plugins.Filesystem) {
      return { Share: C.Plugins.Share, FS: C.Plugins.Filesystem };
    }
    return null;
  }

  /* html2canvas onclone transform: make the rasterised clone look like the module's
     designed PRINT document, not the live interactive screen. Three steps:
       1. Promote every `@media print` rule to an unconditional rule, so each module's
          own one-page print layout (hide chrome, reveal print headers, compact fonts,
          scale-to-fit) actually applies — html2canvas renders screen media and would
          otherwise ignore all of it.
       2. Remove the shell-injected chrome (home FAB, next chips, grooming save FAB,
          share chooser) that is only hidden via @media print.
       3. Drop the mobile single-column skin so the document renders full A4 width. */
  function applyPrintLook(srcDoc, cloneDoc) {
    try {
      var css = '', sheets = srcDoc.styleSheets;
      for (var i = 0; i < sheets.length; i++) {
        var rules; try { rules = sheets[i].cssRules; } catch (e) { continue; }
        if (!rules) continue;
        for (var j = 0; j < rules.length; j++) {
          var r = rules[j];
          if (r.type === 4 && /print/i.test((r.media && r.media.mediaText) || '')) {
            for (var k = 0; k < r.cssRules.length; k++) css += r.cssRules[k].cssText + '\n';
          }
        }
      }
      if (css) { var st = cloneDoc.createElement('style'); st.setAttribute('data-share-print', '1'); st.textContent = css; (cloneDoc.head || cloneDoc.documentElement).appendChild(st); }
    } catch (e) {}
    ['st-v5-home-fab', 'st-v5-next-chips', 'grm-float-save', '__saagarShareChooser'].forEach(function (id) {
      var n = cloneDoc.getElementById(id); if (n && n.parentNode) n.parentNode.removeChild(n);
    });
    try { cloneDoc.documentElement.classList.remove('bcc-mobile'); } catch (e) {}
  }

  /* Generate a PDF (Blob) that fits EXACTLY ONE A4 page (scale-to-fit, aspect preserved). */
  function buildPdf(doc) {
    if (!window.html2pdf) return Promise.reject(new Error('PDF engine not loaded'));
    var el = doc.body || doc.documentElement;
    var W = 794; // A4 content width @96dpi — force desktop document width regardless of UI mode
    // Cap the canvas on heavy modules so rasterisation + encoding stay fast (a giant canvas can
    // block the main thread on toDataURL). Scale only sets render crispness; the output is scaled
    // to one A4 page regardless, so 1× is plenty for a tall/dense document.
    var nodes = 0; try { nodes = el.getElementsByTagName('*').length; } catch (e) {}
    var scale = nodes > 2200 ? 1 : (nodes > 1200 ? 1.4 : 2);
    var opt = {
      image: { type: 'jpeg', quality: 0.94 },
      html2canvas: { scale: scale, useCORS: true, backgroundColor: '#ffffff', windowWidth: W, width: W, scrollX: 0, scrollY: 0, logging: false, onclone: function (cd) { applyPrintLook(doc, cd); } },
      jsPDF: { unit: 'mm', format: 'a4', orientation: 'portrait' }
    };
    var worker = window.html2pdf().set(opt).from(el);
    // toCanvas() runs the onclone transform + rasterises (canvas lands in worker.prop.canvas);
    // toPdf() builds a jsPDF instance (paginated). The bundled jsPDF v2 is factory-built (its
    // constructor is plain Object), so we can't `new` it — instead we REUSE this instance:
    // append one blank A4 page with the whole content scaled to fit, then drop the paginated pages.
    return worker.toCanvas().toPdf().get('pdf').then(function (pdf) {
      var canvas = worker.prop && worker.prop.canvas;
      if (!canvas) return pdf.output('blob'); // fallback: html2pdf's own output
      var n = pdf.internal.getNumberOfPages();
      var pageW = 210, pageH = 297, m = 6, availW = pageW - 2 * m, availH = pageH - 2 * m;
      var iw = canvas.width, ih = canvas.height;
      var s = Math.min(availW / iw, availH / ih);            // fit ONE page, preserve aspect
      var w = iw * s, h = ih * s, x = (pageW - w) / 2, y = m;  // centre horizontally, top-align
      pdf.addPage('a4', 'portrait');                          // fresh blank page (becomes current)
      pdf.addImage(canvas.toDataURL('image/jpeg', 0.95), 'JPEG', x, y, w, h, undefined, 'FAST');
      for (var i = 0; i < n; i++) pdf.deletePage(1);          // remove the old paginated pages → single page left
      return pdf.output('blob');
    });
  }
  // exposed for headless validation (harmless in production)
  window.SaagarBuildSharePdf = buildPdf;

  /* Share a PDF blob: native share sheet in the app, download in a browser. */
  function shareBlob(blob, filename, token) {
    if (!beginExport(token)) {
      notify('Share blocked — approval token is no longer valid');
      return Promise.resolve({ blocked: true });
    }
    var c = caps();
    if (c) {
      return blobToBase64(blob).then(function (b64) {
        return c.FS.writeFile({ path: filename, data: b64, directory: 'CACHE' });
      }).then(function () {
        return c.FS.getUri({ directory: 'CACHE', path: filename });
      }).then(function (res) {
        return c.Share.share({
          title: filename,
          text: 'Saagar Traders — ' + filename,
          files: [res.uri],
          dialogTitle: 'Share PDF via WhatsApp'
        });
      }).then(function () {
        finishExport(token, 'shared');
        notify('Pick WhatsApp in the share menu');
      }).catch(function (e) {
        // user cancelling the share sheet also lands here — keep it quiet-ish
        var m = (e && e.message) ? e.message : String(e);
        if (/cancel|dismiss/i.test(m)) { finishExport(token, 'cancelled'); return; }
        finishExport(token, 'failed');
        notify('Share failed: ' + m);
      });
    }
    // Browser fallback
    var a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = filename;
    a.click();
    setTimeout(function () { URL.revokeObjectURL(a.href); }, 1500);
    finishExport(token, 'downloaded');
    notify('PDF downloaded — WhatsApp share is active in the installed app');
    return Promise.resolve();
  }

  function activeModuleName(doc) {
    var t = document.getElementById('activeTitle');
    var name = (t && t.textContent && t.textContent.trim())
      || (doc && doc.title)
      || 'Saagar_Report';
    return sanitize(name) + '-' + dateStamp() + '.pdf';
  }

  /* Public: called by the shell "📲 WhatsApp" button and by the in-module
     chooser. mode:'share' (default) makes & shares a PDF; mode:'print'
     falls back to the original print. */
  window.SaagarShareCurrent = async function (opt) {
    opt = opt || {};
    var frame = document.getElementById('moduleFrame');
    var doc = frame && (frame.contentDocument || (frame.contentWindow && frame.contentWindow.document));
    var win = frame && frame.contentWindow;
    if (!doc) { notify('Open a module first, then share its print output'); return; }
    removeChooser(doc);
    if (opt.mode === 'print') {
      var printToken = await authorizeExport({
        exportId: 'module-print-chooser', kind: 'print', scopeId: 'current-module-print',
        scopeLabel: 'current module print', module: (typeof activeModuleId !== 'undefined' && activeModuleId) || 'unknown',
        rowCount: 0, purposeId: 'hard-copy-business-record'
      });
      if (!printToken) return;
      if (!beginExport(printToken)) return;
      try { (win && win.__saagarOrigPrint ? win.__saagarOrigPrint() : win.print()); finishExport(printToken, 'printed'); }
      catch (e) { finishExport(printToken, 'failed'); notify('Print is not available here'); }
      return;
    }
    // Prefer the new enterprise report engine: build a clean, data-driven A4 report for the
    // open module instead of screenshotting its live screen. Falls back to the legacy buildPdf
    // only if no report is mapped or the engine isn't present.
    var modId = null; try { modId = (typeof activeModuleId !== 'undefined') ? activeModuleId : null; } catch (e) {}
    var rType = (window.SaagarReport && modId) ? window.SaagarReport.forModule(modId) : null;
    if (rType && window.SaagarReport) { window.SaagarReport.generate(rType, {}); return; }
    notify('Preparing PDF for WhatsApp…');
    setTimeout(function () {
      buildPdf(doc)
        .then(async function (blob) {
          var fname = activeModuleName(doc), token = await authorizeExport({
            exportId: 'module-pdf-share', kind: 'pdf', scopeId: 'current-module-pdf',
            scopeLabel: 'current module PDF', module: modId || 'unknown', rowCount: 0,
            fileName: fname, purposeId: 'business-record-export'
          });
          return token ? shareBlob(blob, fname, token) : null;
        })
        .catch(function (e) { notify('Could not create PDF: ' + (e && e.message || e)); });
    }, 60);
  };

  /* In-module chooser overlay (injected into the iframe document). */
  function removeChooser(doc) {
    try {
      var ex = doc.getElementById('__saagarShareChooser');
      if (ex) ex.parentNode.removeChild(ex);
    } catch (e) {}
  }

  function showChooser(doc) {
    try {
      removeChooser(doc);
      var wrap = doc.createElement('div');
      wrap.id = '__saagarShareChooser';
      wrap.setAttribute('style',
        'position:fixed;inset:0;z-index:2147483647;background:rgba(10,20,35,.55);' +
        'display:flex;align-items:center;justify-content:center;font-family:Arial,Helvetica,sans-serif');
      wrap.innerHTML =
        '<div style="background:#fff;border-radius:16px;max-width:340px;width:86%;padding:22px;' +
        'box-shadow:0 20px 50px rgba(0,0,0,.35);text-align:center">' +
        '<div style="font-size:17px;font-weight:800;color:#0d2340;margin-bottom:6px">Send this page</div>' +
        '<div style="font-size:13px;color:#5b6a82;margin-bottom:18px">Turn what you would print into a PDF and share it.</div>' +
        '<button id="__sgShare" style="display:block;width:100%;padding:13px;margin-bottom:10px;border:0;' +
        'border-radius:10px;background:#1f9d4f;color:#fff;font-size:15px;font-weight:800">📲 Share to WhatsApp (PDF)</button>' +
        '<button id="__sgPrint" style="display:block;width:100%;padding:12px;margin-bottom:10px;border:1px solid #cbd6e4;' +
        'border-radius:10px;background:#fff;color:#0d2340;font-size:14px;font-weight:700">🖨 Print instead</button>' +
        '<button id="__sgCancel" style="display:block;width:100%;padding:10px;border:0;background:transparent;' +
        'color:#8fa0b6;font-size:13px;font-weight:700">Cancel</button>' +
        '</div>';
      doc.body.appendChild(wrap);
      doc.getElementById('__sgShare').onclick = function () { removeChooser(doc); window.SaagarShareCurrent({ mode: 'share' }); };
      doc.getElementById('__sgPrint').onclick = function () { removeChooser(doc); window.SaagarShareCurrent({ mode: 'print' }); };
      doc.getElementById('__sgCancel').onclick = function () { removeChooser(doc); };
      wrap.addEventListener('click', function (ev) { if (ev.target === wrap) removeChooser(doc); });
    } catch (e) {
      // If injection fails for any reason, just share directly.
      window.SaagarShareCurrent({ mode: 'share' });
    }
  }

  /* Intercept each module's own window.print() so existing in-module
     Print buttons offer the WhatsApp/PDF choice. Modules load via srcdoc
     (same origin) so this is safe and direct. */
  function hookFrame() {
    var frame = document.getElementById('moduleFrame');
    if (!frame || frame.__saagarBound) return;
    frame.__saagarBound = true;
    frame.addEventListener('load', function () {
      try {
        var win = frame.contentWindow;
        var doc = frame.contentDocument || (win && win.document);
        if (!win || !doc || win.__saagarPrintHooked) return;
        win.__saagarPrintHooked = true;
        win.__saagarOrigPrint = win.print ? win.print.bind(win) : function () {};
        win.print = function () { showChooser(doc); };
      } catch (e) { /* ignore */ }
    });
  }

  function init() {
    hookFrame();
    // The shell may build its toolbar after load; ensure the frame hook is set.
    var tries = 0;
    var iv = setInterval(function () {
      hookFrame();
      if (++tries > 20 || (document.getElementById('moduleFrame') && document.getElementById('moduleFrame').__saagarBound)) {
        clearInterval(iv);
      }
    }, 500);
  }

  if (document.readyState === 'complete' || document.readyState === 'interactive') init();
  else document.addEventListener('DOMContentLoaded', init);
})();
