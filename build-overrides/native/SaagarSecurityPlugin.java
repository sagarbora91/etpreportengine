package com.saagartraders.bcc;

import android.content.pm.ApplicationInfo;
import android.os.Build;
import android.os.Debug;
import android.provider.Settings;
import android.view.WindowManager;

import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

import java.io.File;

/* R0-W4 native security surface: truthful device/build posture and FLAG_SECURE.
   Root checks are warning signals, not a claim of perfect root detection. */
@CapacitorPlugin(name = "SaagarSecurity")
public class SaagarSecurityPlugin extends Plugin {
    private static final String[] ROOT_PATHS = new String[] {
        "/system/bin/su", "/system/xbin/su", "/sbin/su", "/su/bin/su",
        "/system/app/Superuser.apk", "/system/app/Magisk.apk", "/data/adb/magisk"
    };

    @PluginMethod
    public void posture(PluginCall call) {
        JSObject out = new JSObject();
        try {
            ApplicationInfo app = getContext().getApplicationInfo();
            boolean debuggable = (app.flags & ApplicationInfo.FLAG_DEBUGGABLE) != 0;
            boolean debugger = Debug.isDebuggerConnected() || Debug.waitingForDebugger();
            boolean testKeys = Build.TAGS != null && Build.TAGS.contains("test-keys");
            boolean rootArtifact = false;
            for (String path : ROOT_PATHS) {
                if (new File(path).exists()) { rootArtifact = true; break; }
            }
            int adb = 0;
            try { adb = Settings.Global.getInt(getContext().getContentResolver(), Settings.Global.ADB_ENABLED, 0); }
            catch (Throwable ignored) {}
            String installer = "";
            try {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                    installer = getContext().getPackageManager()
                        .getInstallSourceInfo(getContext().getPackageName())
                        .getInstallingPackageName();
                } else {
                    installer = getContext().getPackageManager()
                        .getInstallerPackageName(getContext().getPackageName());
                }
            } catch (Throwable ignored) {}
            if (installer == null) installer = "";

            out.put("apiLevel", Build.VERSION.SDK_INT);
            out.put("debuggable", debuggable);
            out.put("debuggerConnected", debugger);
            out.put("adbEnabled", adb == 1);
            out.put("testKeys", testKeys);
            out.put("rootArtifact", rootArtifact);
            out.put("installerKnown", !installer.isEmpty());
            out.put("installerPackage", installer);
            out.put("productionEligible", Build.VERSION.SDK_INT >= Build.VERSION_CODES.M && !debuggable && !debugger && !testKeys && !rootArtifact);
            call.resolve(out);
        } catch (Throwable t) {
            call.reject("posture check failed", "E_POSTURE");
        }
    }

    @PluginMethod
    public void setSecureWindow(PluginCall call) {
        final boolean enabled = Boolean.TRUE.equals(call.getBoolean("enabled", true));
        try {
            getActivity().runOnUiThread(() -> {
                if (enabled) {
                    getActivity().getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
                } else {
                    getActivity().getWindow().clearFlags(WindowManager.LayoutParams.FLAG_SECURE);
                }
                JSObject out = new JSObject();
                out.put("enabled", enabled);
                call.resolve(out);
            });
        } catch (Throwable t) {
            call.reject("secure-window change failed", "E_SECURE_WINDOW");
        }
    }
}
