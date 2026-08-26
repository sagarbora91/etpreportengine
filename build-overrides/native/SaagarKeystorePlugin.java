package com.saagartraders.bcc;

import android.os.Build;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyInfo;
import android.security.keystore.KeyProperties;
import android.security.keystore.StrongBoxUnavailableException;
import android.util.Base64;

import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

import java.security.KeyStore;
import java.util.Arrays;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.SecretKeyFactory;
import javax.crypto.spec.GCMParameterSpec;

/* R0-W2 W2-S2a — hardware Keystore custody for the DB data-key (DEK).
   Wraps/unwraps a 32-byte DEK with a non-exportable AES-GCM KEK in AndroidKeyStore.
   The KEK never leaves the TEE/StrongBox; only a wrapped blob is stored in app-private
   DATA. Boot unwraps headlessly (NO user-auth binding — would deadlock cold-boot).
   Fail policy lives in JS (getDEK): this plugin only reports truth + rejects with a code. */
@CapacitorPlugin(name = "SaagarKeystore")
public class SaagarKeystorePlugin extends Plugin {
    private static final String KS_PROVIDER  = "AndroidKeyStore";
    private static final String ALIAS        = "saagar_dek_kek_v1";
    private static final String XFORM        = "AES/GCM/NoPadding";
    private static final int    GCM_TAG_BITS = 128;
    private static final int    IV_LEN       = 12;
    private static final byte[] MAGIC        = new byte[] { 'S','K','W','1' };
    private static final byte    VER         = 0x01;

    private boolean lastStrongBox = false;

    /* available() -> {available, backing, apiLevel, reason}. Never rejects; JS decides policy. */
    @PluginMethod
    public void available(PluginCall call) {
        JSObject r = new JSObject();
        r.put("apiLevel", Build.VERSION.SDK_INT);
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.M) {
            r.put("available", false); r.put("backing", "none"); r.put("reason", "api<23");
            call.resolve(r); return;
        }
        try {
            KeyStore ks = KeyStore.getInstance(KS_PROVIDER); ks.load(null);
            r.put("available", true);
            if (ks.containsAlias(ALIAS)) {                     // READ-ONLY: never mint here (adversarial P1 fold —
                SecretKey k = (SecretKey) ks.getKey(ALIAS, null);   // a probe must not provision or overwrite the KEK)
                r.put("backing", k != null ? backingOf(k) : "orphaned");
            } else {
                r.put("backing", "uninitialized");              // minted lazily on the first real wrapKey
            }
            r.put("reason", "ok");
        } catch (Throwable t) {
            r.put("available", false); r.put("backing", "none");
            r.put("reason", "ks_error:" + t.getClass().getSimpleName());
        }
        call.resolve(r);
    }

    /* wrapKey({data: b64 raw 32-byte DEK}) -> {wrapped: b64(SKW1|ver|iv|ct), backing} */
    @PluginMethod
    public void wrapKey(PluginCall call) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.M) { call.reject("api<23", "E_NO_KEYSTORE"); return; }
        String dataB64 = call.getString("data");
        if (dataB64 == null) { call.reject("missing data", "E_ARGS"); return; }
        byte[] dek = null;
        try {
            dek = Base64.decode(dataB64, Base64.NO_WRAP);
            SecretKey kek = getOrCreateKek();
            Cipher c = Cipher.getInstance(XFORM);
            c.init(Cipher.ENCRYPT_MODE, kek);        // Keystore owns the IV (RandomizedEncryptionRequired)
            byte[] iv = c.getIV();                    // read AFTER init
            byte[] ct = c.doFinal(dek);
            byte[] out = new byte[MAGIC.length + 1 + iv.length + ct.length];
            int o = 0;
            System.arraycopy(MAGIC, 0, out, o, MAGIC.length); o += MAGIC.length;
            out[o++] = VER;
            System.arraycopy(iv, 0, out, o, iv.length); o += iv.length;
            System.arraycopy(ct, 0, out, o, ct.length);
            JSObject r = new JSObject();
            r.put("wrapped", Base64.encodeToString(out, Base64.NO_WRAP));
            r.put("backing", backingOf(kek));
            call.resolve(r);
        } catch (Throwable t) {
            call.reject("wrap failed: " + t.getMessage(), "E_WRAP");
        } finally {
            if (dek != null) Arrays.fill(dek, (byte) 0);
        }
    }

    /* unwrapKey({wrapped: b64}) -> {data: b64 raw 32-byte DEK}. Rejects distinctly on orphaned KEK. */
    @PluginMethod
    public void unwrapKey(PluginCall call) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.M) { call.reject("api<23", "E_NO_KEYSTORE"); return; }
        String wrappedB64 = call.getString("wrapped");
        if (wrappedB64 == null) { call.reject("missing wrapped", "E_ARGS"); return; }
        byte[] dek = null;
        try {
            byte[] blob = Base64.decode(wrappedB64, Base64.NO_WRAP);
            int hdr = MAGIC.length + 1;
            if (blob.length <= hdr + IV_LEN) { call.reject("blob too short", "E_FORMAT"); return; }
            for (int i = 0; i < MAGIC.length; i++)
                if (blob[i] != MAGIC[i]) { call.reject("bad magic", "E_FORMAT"); return; }
            if (blob[MAGIC.length] != VER) { call.reject("bad version", "E_VERSION"); return; }
            byte[] iv = Arrays.copyOfRange(blob, hdr, hdr + IV_LEN);
            byte[] ct = Arrays.copyOfRange(blob, hdr + IV_LEN, blob.length);
            KeyStore ks = KeyStore.getInstance(KS_PROVIDER); ks.load(null);
            SecretKey kek = (SecretKey) ks.getKey(ALIAS, null);
            if (kek == null) { call.reject("kek alias missing", "E_ORPHAN"); return; } // KEK evaporated
            Cipher c = Cipher.getInstance(XFORM);
            c.init(Cipher.DECRYPT_MODE, kek, new GCMParameterSpec(GCM_TAG_BITS, iv));
            dek = c.doFinal(ct);                      // KeyPermanentlyInvalidated / AEADBadTag land here
            JSObject r = new JSObject();
            r.put("data", Base64.encodeToString(dek, Base64.NO_WRAP));
            call.resolve(r);
        } catch (android.security.keystore.KeyPermanentlyInvalidatedException e) {
            call.reject("kek invalidated", "E_ORPHAN");
        } catch (Throwable t) {
            call.reject("unwrap failed: " + t.getMessage(), "E_UNWRAP");
        } finally {
            if (dek != null) Arrays.fill(dek, (byte) 0);
        }
    }

    /* ---- internals ---- */
    private SecretKey getOrCreateKek() throws Exception {
        KeyStore ks = KeyStore.getInstance(KS_PROVIDER); ks.load(null);
        if (ks.containsAlias(ALIAS)) {
            SecretKey k = (SecretKey) ks.getKey(ALIAS, null);
            if (k != null) return k;
            // Adversarial P1 fold: alias present but key unreadable (OEM keystore restart / partial corruption).
            // Do NOT fall through to mintKek() — generateKey() on an existing alias REPLACES it, destroying the KEK
            // and orphaning every DEK ever wrapped under it. Signal orphan instead (JS fails open to plaintext).
            throw new IllegalStateException("E_ORPHAN kek alias present but unreadable");
        }
        return mintKek();
    }

    private SecretKey mintKek() throws Exception {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            try { SecretKey k = generate(true); lastStrongBox = true; return k; }
            catch (StrongBoxUnavailableException ignored) { /* fall through */ }
            catch (Throwable t) { /* OEMs throw plain exceptions for StrongBox absence — fall through */ }
        }
        SecretKey k = generate(false); lastStrongBox = false; return k;
    }

    private SecretKey generate(boolean strongBox) throws Exception {
        KeyGenerator kg = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, KS_PROVIDER);
        KeyGenParameterSpec.Builder b = new KeyGenParameterSpec.Builder(
                ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setKeySize(256)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                // NO setUserAuthenticationRequired(true): boot unwraps headless before any PIN (OD-K3).
                .setRandomizedEncryptionRequired(true);
        if (strongBox && Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) b.setIsStrongBoxBacked(true);
        kg.init(b.build());
        return kg.generateKey();
    }

    private String backingOf(SecretKey key) {
        try {
            SecretKeyFactory f = SecretKeyFactory.getInstance(key.getAlgorithm(), KS_PROVIDER);
            KeyInfo info = (KeyInfo) f.getKeySpec(key, KeyInfo.class);
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                int sec = info.getSecurityLevel();
                if (sec == KeyProperties.SECURITY_LEVEL_STRONGBOX) return "strongbox";
                if (sec == KeyProperties.SECURITY_LEVEL_TRUSTED_ENVIRONMENT) return "tee";
                return "software";
            }
            if (info.isInsideSecureHardware()) return "secure-hardware"; // <31: cannot reliably split strongbox vs tee — report honestly (adversarial P2 fold: lastStrongBox is stale for a reused key)
            return "software";
        } catch (Throwable t) { return "software"; } // unknown -> claim the weaker tier (honest)
    }
}
