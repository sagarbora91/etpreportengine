#!/usr/bin/env node
/* Make security-hardening AndroidManifest edits PERMANENT across Capacitor regeneration.
   `android/` is git-ignored (Capacitor regenerates it), so a `cap add`/regen reverts the
   source manifest to the AGP defaults. This script re-applies the deliberate hardening after
   `cap sync`, so the shipped APK always carries it. It PATCHES only the specific attribute
   (never rewrites the whole manifest), so plugin-merged permissions/receivers are untouched.

   Currently enforced:
     • android:allowBackup="false"  (audit sec-apk-2 — block `adb backup` exfiltration of the
       offline SQLite business/financial data).
     • SaagarKeystore native plugin (R0-W2 W2-S2a) — copy the canonical Java source into the
       regenerated android project AND register it in MainActivity. `android/` reverts MainActivity
       to a bare BridgeActivity and drops any hand-added Java on every `cap sync`, so this re-stamp
       is LOAD-BEARING: a missed re-apply = plugin absent = getDEK silently falls back to plaintext
       (fail-open, but the encryption is gone). DT-check the plugin every build.

   Idempotent + safe to run any number of times. If the manifest isn't present yet (fresh clone
   before `cap add`), it no-ops with a note. No new dependencies — Node stdlib only. */
'use strict';
const fs = require('fs');
const path = require('path');
const BUILD_IDENTITY = require('../www/build-identity.js');

const ANDROID_PKG_DIR = path.join(__dirname, '..', 'android', 'app', 'src', 'main', 'java', 'com', 'saagartraders', 'bcc');
const MANIFEST = path.join(__dirname, '..', 'android', 'app', 'src', 'main', 'AndroidManifest.xml');
const PLUGIN_SRC = path.join(__dirname, 'native', 'SaagarKeystorePlugin.java');
const PLUGIN_DST = path.join(ANDROID_PKG_DIR, 'SaagarKeystorePlugin.java');   /* filename MUST match the public class name (Java rule) */
const SECURITY_PLUGIN_SRC = path.join(__dirname, 'native', 'SaagarSecurityPlugin.java');
const SECURITY_PLUGIN_DST = path.join(ANDROID_PKG_DIR, 'SaagarSecurityPlugin.java');
const OFFDEVICE_PLUGIN_SRC = path.join(__dirname, 'native', 'SaagarOffDevicePlugin.java');
const OFFDEVICE_PLUGIN_DST = path.join(ANDROID_PKG_DIR, 'SaagarOffDevicePlugin.java');
const NATIVE_STORE_PLUGIN_SRC = path.join(__dirname, 'native', 'SaagarNativeStorePlugin.java');
const NATIVE_STORE_PLUGIN_DST = path.join(ANDROID_PKG_DIR, 'SaagarNativeStorePlugin.java');
const ETP_STORE_PLUGIN_SRC = path.join(__dirname, 'native', 'SaagarEtpStorePlugin.java');
const ETP_STORE_PLUGIN_DST = path.join(ANDROID_PKG_DIR, 'SaagarEtpStorePlugin.java');
const MAIN_ACTIVITY = path.join(ANDROID_PKG_DIR, 'MainActivity.java');
const BUILD_GRADLE = path.join(__dirname, '..', 'android', 'app', 'build.gradle');
const ANDROID_VARIABLES = path.join(__dirname, '..', 'android', 'variables.gradle');

const signing = `
    // SAAGAR_RELEASE_SIGNING_BEGIN — release builds fail closed unless the production key is supplied.
    signingConfigs {
        release {
            def ks = System.getenv("SAAGAR_KEYSTORE_FILE")
            def ksp = System.getenv("SAAGAR_KEYSTORE_PASSWORD")
            def ka = System.getenv("SAAGAR_KEY_ALIAS")
            def kap = System.getenv("SAAGAR_KEY_PASSWORD")
            def wantsRelease = gradle.startParameter.taskNames.any { it.toLowerCase().contains("release") }
            if (wantsRelease && (!ks || !ksp || !ka || !kap)) {
                throw new GradleException("Signed release blocked: set SAAGAR_KEYSTORE_FILE, SAAGAR_KEYSTORE_PASSWORD, SAAGAR_KEY_ALIAS and SAAGAR_KEY_PASSWORD")
            }
            if (ks && ksp && ka && kap) {
                storeFile file(ks)
                storePassword ksp
                keyAlias ka
                keyPassword kap
            }
        }
    }
    // SAAGAR_RELEASE_SIGNING_END
`;

/* The exact MainActivity form that registers the in-app plugin (Capacitor 6: registerPlugin BEFORE super.onCreate). */
const MAIN_ACTIVITY_REGISTERED =
`package com.saagartraders.bcc;

import android.os.Bundle;
import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    public void onCreate(Bundle savedInstanceState) {
        registerPlugin(SaagarKeystorePlugin.class);
        registerPlugin(SaagarSecurityPlugin.class);
        registerPlugin(SaagarOffDevicePlugin.class);
        registerPlugin(SaagarNativeStorePlugin.class);
        registerPlugin(SaagarEtpStorePlugin.class);
        super.onCreate(savedInstanceState);
    }
}
`;

function stampPlugin(srcPath, dstPath, label) {
  const src = fs.readFileSync(srcPath, 'utf8');
  const dstExists = fs.existsSync(dstPath);
  if (!dstExists || fs.readFileSync(dstPath, 'utf8') !== src) {
    fs.writeFileSync(dstPath, src);
    console.log('[apply-overrides] stamped ' + label);
  } else {
    console.log('[apply-overrides] ' + label + ' already current — no change');
  }
}

function applyNativePlugins() {
  if (!fs.existsSync(ANDROID_PKG_DIR)) {
    console.log('[apply-overrides] android package dir not found — skipping SaagarKeystore stamp:', ANDROID_PKG_DIR);
    return;
  }
  // (a) copy canonical plugin sources byte-for-byte over regenerated/stale copies
  stampPlugin(PLUGIN_SRC, PLUGIN_DST, 'SaagarKeystorePlugin.java');
  stampPlugin(SECURITY_PLUGIN_SRC, SECURITY_PLUGIN_DST, 'SaagarSecurityPlugin.java');
  stampPlugin(OFFDEVICE_PLUGIN_SRC, OFFDEVICE_PLUGIN_DST, 'SaagarOffDevicePlugin.java');
  stampPlugin(NATIVE_STORE_PLUGIN_SRC, NATIVE_STORE_PLUGIN_DST, 'SaagarNativeStorePlugin.java');
  stampPlugin(ETP_STORE_PLUGIN_SRC, ETP_STORE_PLUGIN_DST, 'SaagarEtpStorePlugin.java');
  // (b) register the plugin in MainActivity — idempotent: only rewrite if not already the registered form
  if (fs.existsSync(MAIN_ACTIVITY)) {
    const cur = fs.readFileSync(MAIN_ACTIVITY, 'utf8');
    if (cur.indexOf('registerPlugin(SaagarKeystorePlugin.class)') === -1 ||
        cur.indexOf('registerPlugin(SaagarSecurityPlugin.class)') === -1 ||
        cur.indexOf('registerPlugin(SaagarOffDevicePlugin.class)') === -1 ||
        cur.indexOf('registerPlugin(SaagarNativeStorePlugin.class)') === -1 ||
        cur.indexOf('registerPlugin(SaagarEtpStorePlugin.class)') === -1) {
      fs.writeFileSync(MAIN_ACTIVITY, MAIN_ACTIVITY_REGISTERED);
      console.log('[apply-overrides] patched MainActivity to register Saagar native plugins');
    } else {
      console.log('[apply-overrides] MainActivity already registers Saagar native plugins — no change');
    }
  } else {
    // Adversarial P2 fold: the package dir exists (this IS a real build) but MainActivity is gone — a partial/
    // interrupted cap regen. Copying the plugin without registering it = plugin compiled-but-absent at runtime =
    // silent plaintext (once the writer flips). Hard-fail rather than ship that shape.
    console.error('[apply-overrides] FATAL: package dir present but MainActivity.java missing — cannot register SaagarKeystorePlugin:', MAIN_ACTIVITY);
    process.exit(1);
  }
}

function applyReleaseHardening() {
  if (!fs.existsSync(BUILD_GRADLE)) {
    console.error('[apply-overrides] FATAL: app build.gradle missing — release identity/signing cannot be enforced:', BUILD_GRADLE);
    process.exit(1);
  }
  let gradle = fs.readFileSync(BUILD_GRADLE, 'utf8');
  gradle = gradle.replace(/versionCode\s+\d+/, 'versionCode ' + BUILD_IDENTITY.versionCode);
  gradle = gradle.replace(/versionName\s+"[^"]*"/, 'versionName "' + BUILD_IDENTITY.versionName + '"');

  const signingBlock = /\s*\/\/ SAAGAR_RELEASE_SIGNING_BEGIN[^\n]*[\s\S]*?\/\/ SAAGAR_RELEASE_SIGNING_END\s*/g;
  const signingMatches = gradle.match(signingBlock) || [];
  if (signingMatches.length > 1) {
    console.error('[apply-overrides] FATAL: duplicate release-signing authority blocks found');
    process.exit(1);
  }
  if (signingMatches.length === 1) {
    gradle = gradle.replace(signingBlock, '\n' + signing);
  } else {
    if (/signingConfigs\s*\{[\s\S]*?release\s*\{/.test(gradle)) {
      console.error('[apply-overrides] FATAL: ungoverned release signing configuration found');
      process.exit(1);
    }
    gradle = gradle.replace(/android\s*\{/, match => match + signing);
  }
  /* Repair the pre-R1 matcher if it ever put build-type properties inside
     signingConfigs.release (the first generic "release {" in the file). */
  gradle = gradle.replace(
    /(SAAGAR_RELEASE_SIGNING_BEGIN[\s\S]*?signingConfigs\s*\{\s*release\s*\{)\s*debuggable false\s*signingConfig signingConfigs\.release/,
    '$1'
  );
  if (!/buildTypes\s*\{[\s\S]*?release\s*\{[\s\S]*?signingConfig\s+signingConfigs\.release/.test(gradle)) {
    gradle = gradle.replace(
      /(buildTypes\s*\{\s*release\s*\{)/,
      '$1\n            debuggable false\n            signingConfig signingConfigs.release'
    );
  }
  fs.writeFileSync(BUILD_GRADLE, gradle);
  if (!fs.existsSync(ANDROID_VARIABLES)) {
    console.error('[apply-overrides] FATAL: variables.gradle missing — minSdk 23 cannot be enforced:', ANDROID_VARIABLES);
    process.exit(1);
  }
  let variables = fs.readFileSync(ANDROID_VARIABLES, 'utf8');
  if (!/minSdkVersion\s*=\s*\d+/.test(variables)) {
    console.error('[apply-overrides] FATAL: minSdkVersion missing from variables.gradle');
    process.exit(1);
  }
  variables = variables.replace(/minSdkVersion\s*=\s*\d+/, 'minSdkVersion = ' + BUILD_IDENTITY.minSdk);
  fs.writeFileSync(ANDROID_VARIABLES, variables);
  console.log('[apply-overrides] enforced versionCode ' + BUILD_IDENTITY.versionCode + ', versionName ' + BUILD_IDENTITY.versionName + ', minSdk ' + BUILD_IDENTITY.minSdk + ' and fail-closed release signing');
}

function main() {
  if (!fs.existsSync(MANIFEST)) {
    console.log('[apply-overrides] AndroidManifest not found — run `npx cap add android` / `cap sync` first. Skipping:', MANIFEST);
    return; // exit 0: not an error during a partial setup
  }
  let xml = fs.readFileSync(MANIFEST, 'utf8');
  const before = xml;

  // android:allowBackup="false"
  if (/android:allowBackup\s*=\s*"true"/.test(xml)) {
    xml = xml.replace(/android:allowBackup\s*=\s*"true"/, 'android:allowBackup="false"');
  } else if (!/android:allowBackup\s*=/.test(xml)) {
    // attribute absent (AGP default is true) → inject it on the <application> element
    xml = xml.replace(/<application\b/, '<application\n        android:allowBackup="false"');
  }

  if (xml !== before) {
    fs.writeFileSync(MANIFEST, xml);
    console.log('[apply-overrides] set android:allowBackup="false"');
  } else {
    console.log('[apply-overrides] android:allowBackup already "false" — no change');
  }

  applyNativePlugins();
  applyReleaseHardening();
}

main();
