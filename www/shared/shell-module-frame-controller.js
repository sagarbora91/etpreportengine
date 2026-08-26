(function (root) {
  'use strict';
  if (root.SaagarShellModuleFrameController) return;

  function open(id, shell) {
    var mod = shell.moduleById(id);
    if (!mod) return;
    if ((id === 'payroll' || id === 'tax') && !shell.allowSensitiveDeviceAction()) {
      shell.toast('This production device is restricted because its security posture is unsafe or unavailable.');
      return;
    }
    if (!shell.ensureModuleAccess(id)) return;
    try { shell.seedModuleFromMaster(id); } catch (_) {}
    shell.logActivity(id);
    if (shell.sensitiveViews.indexOf(id) >= 0) {
      try { shell.auditLog('access.open', { module: id, role: shell.currentRole(), admin: shell.isAdmin() }); } catch (_) {}
    }
    shell.setActiveModuleId(id);
    try { shell.setSecureWindowForModule(id); } catch (_) {}
    shell.element('activeTitle').textContent = mod.title;
    shell.element('activeSub').textContent = (mod.category || '') + ' \u00b7 ' + (mod.priority || '');
    shell.element('mainContent').style.setProperty('display', 'none');
    shell.element('moduleScreen').classList.remove('hidden');
    shell.element('botnav').classList.add('hidden');
    shell.hideModuleLoadError();
    shell.element('loader').classList.remove('hidden');

    root.setTimeout(function () {
      var frame = shell.element('moduleFrame');
      var host = null;
      try {
        if (root.SaagarMah4Runtime) {
          host = root.SaagarMah4Runtime.createHost({
            frame: frame,
            moduleId: id,
            onAudit: function (meta) {
              shell.auditLog(meta.action, {
                module: meta.moduleId,
                storageKeyHash: meta.storageKeyHash,
                beforeBytes: meta.beforeBytes,
                afterBytes: meta.afterBytes
              });
              if (!shell.activeModuleId() && shell.activeView() === 'home') shell.renderHome();
            },
            onClosed: function () {
              if (shell.moduleHost() !== host) return;
              shell.setModuleHost(null);
              var next = shell.takeModuleCloseNext();
              if (typeof next === 'function') next();
              else if (shell.activeModuleId() === id) {
                try { shell.toast('Planning closed because its secure runtime did not complete.'); } catch (_) {}
                shell.showMainView(true);
              }
            }
          });
          shell.setModuleHost(host);
        }
      } catch (_) { host = null; shell.setModuleHost(null); }

      var hidden = false;
      function hide() {
        if (hidden) return;
        hidden = true;
        shell.element('loader').classList.add('hidden');
      }
      var safetyTimeout = root.setTimeout(function () {
        if (hidden) return;
        hide();
        shell.showModuleLoadError(id);
      }, 9000);
      try {
        frame.addEventListener('load', function () {
          if (safetyTimeout) root.clearTimeout(safetyTimeout);
          hide();
          try { shell.applyDateToFrame(); } catch (_) {}
          try { shell.applyLangToFrame(shell.getLang()); } catch (_) {}
          try { shell.notifyModuleAccessChanged(); } catch (_) {}
          try { if (host) host.loaded({ language: shell.getLang(), date: shell.viewDate(), uiMode: shell.getUiMode() }); } catch (_) {}
          shell.clearPendingTarget();
        }, { once: true });
        frame.addEventListener('error', function () {
          if (hidden) return;
          if (safetyTimeout) root.clearTimeout(safetyTimeout);
          try { if (host) host.fail('frame-error'); } catch (_) {}
          hide();
          shell.showModuleLoadError(id);
        }, { once: true });
      } catch (_) {}
      try {
        if (!mod.src) throw new Error('Module source is unavailable.');
        frame.removeAttribute('srcdoc');
        frame.src = mod.src;
      } catch (error) {
        if (safetyTimeout) root.clearTimeout(safetyTimeout);
        try { frame.removeAttribute('src'); } catch (_) {}
        frame.srcdoc = '<h1 style="font-family:DM Sans,sans-serif;color:#b91c1c">Could not open module</h1><p>' + shell.escapeHtml(error.message) + '</p>';
        hide();
      }
    }, 50);
  }

  root.SaagarShellModuleFrameController = Object.freeze({ version: 1, open: open });
})(window);
