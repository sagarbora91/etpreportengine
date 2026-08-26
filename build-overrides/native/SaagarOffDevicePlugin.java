package com.saagartraders.bcc;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.content.ContentResolver;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.UriPermission;
import android.database.Cursor;
import android.net.Uri;
import android.provider.DocumentsContract;

import androidx.activity.result.ActivityResult;

import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.ActivityCallback;
import com.getcapacitor.annotation.CapacitorPlugin;

import java.io.File;
import java.io.FileInputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.security.MessageDigest;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Calendar;
import java.util.Collections;
import java.util.Comparator;
import java.util.Date;
import java.util.List;
import java.util.Locale;

/* BKP-03: provider-neutral encrypted off-device backup delivery through Android's
   Storage Access Framework. The owner selects a Drive/OneDrive/document-provider
   folder once; the persisted URI stays in native SharedPreferences and JS sees
   only a SHA-256 destination id. Built-in phone/download/media providers are
   rejected because they do not survive device loss. */
@CapacitorPlugin(name = "SaagarOffDevice")
public class SaagarOffDevicePlugin extends Plugin {
    private static final String PREFS = "saagar_offdevice_v1";
    private static final String URI_KEY = "tree_uri";
    private static final String ID_KEY = "destination_id";
    private static final String LABEL_KEY = "destination_label";
    private static final String BACKUP_DIR = "SaagarBCC-Backups";
    private static final String MIME = "application/vnd.saagar.backup+json";

    @PluginMethod
    public void chooseFolder(PluginCall call) {
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION |
                Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION | Intent.FLAG_GRANT_PREFIX_URI_PERMISSION);
        startActivityForResult(call, intent, "folderResult");
    }

    @ActivityCallback
    @SuppressLint("WrongConstant") // Flags are masked to the two persisted URI-grant bits below.
    private void folderResult(PluginCall call, ActivityResult result) {
        if (result.getResultCode() != Activity.RESULT_OK || result.getData() == null || result.getData().getData() == null) {
            call.reject("Folder selection cancelled", "E_CANCELLED");
            return;
        }
        Uri tree = result.getData().getData();
        try {
            if (!isOffDeviceAuthority(tree.getAuthority())) {
                call.reject("Choose Google Drive, OneDrive, or another off-device document provider", "E_ON_DEVICE_PROVIDER");
                return;
            }
            int flags = result.getData().getFlags() & (Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
            getContext().getContentResolver().takePersistableUriPermission(tree, flags);
            if (!hasGrant(tree)) {
                call.reject("The selected folder permission did not persist", "E_PERMISSION");
                return;
            }
            String id = sha256Text(tree.toString());
            String label = safeLabel(displayName(tree));
            prefs().edit().putString(URI_KEY, tree.toString()).putString(ID_KEY, id).putString(LABEL_KEY, label).apply();
            JSObject out = new JSObject();
            out.put("configured", true);
            out.put("destinationId", id);
            out.put("label", label);
            out.put("provider", safeLabel(tree.getAuthority()));
            call.resolve(out);
        } catch (Throwable t) {
            call.reject("Folder setup failed: " + safeMessage(t), "E_FOLDER_SETUP");
        }
    }

    @PluginMethod
    public void status(PluginCall call) {
        JSObject out = new JSObject();
        try {
            String raw = prefs().getString(URI_KEY, "");
            if (raw.isEmpty()) { out.put("configured", false); call.resolve(out); return; }
            Uri tree = Uri.parse(raw);
            boolean granted = hasGrant(tree) && isOffDeviceAuthority(tree.getAuthority());
            out.put("configured", granted);
            out.put("destinationId", granted ? prefs().getString(ID_KEY, "") : "");
            out.put("label", granted ? prefs().getString(LABEL_KEY, "Off-device folder") : "");
            out.put("provider", granted ? safeLabel(tree.getAuthority()) : "");
            if (!granted) out.put("reason", "permission-missing");
            call.resolve(out);
        } catch (Throwable t) {
            call.reject("Folder status failed", "E_STATUS");
        }
    }

    @PluginMethod
    public void clearFolder(PluginCall call) {
        try {
            String raw = prefs().getString(URI_KEY, "");
            if (!raw.isEmpty()) {
                try { getContext().getContentResolver().releasePersistableUriPermission(Uri.parse(raw), Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION); }
                catch (Throwable ignored) {}
            }
            prefs().edit().clear().apply();
            JSObject out = new JSObject(); out.put("cleared", true); call.resolve(out);
        } catch (Throwable t) { call.reject("Folder permission could not be cleared", "E_CLEAR"); }
    }

    @PluginMethod
    public void copyFromCache(PluginCall call) {
        String path = call.getString("path");
        String date = call.getString("date");
        if (path == null || !path.matches("[A-Za-z0-9._-]+\\.sccbak") || date == null || !date.matches("\\d{4}-\\d{2}-\\d{2}")) {
            call.reject("Invalid backup file or date", "E_ARGS"); return;
        }
        try { call.resolve(copyNow(path, date)); }
        catch (Throwable t) { call.reject("Off-device copy failed: " + safeMessage(t), "E_COPY"); }
    }

    private JSObject copyNow(String cacheName, String dateText) throws Exception {
        String raw = prefs().getString(URI_KEY, "");
        if (raw.isEmpty()) throw new IllegalStateException("folder-not-configured");
        Uri tree = Uri.parse(raw);
        if (!hasGrant(tree) || !isOffDeviceAuthority(tree.getAuthority())) throw new SecurityException("folder-permission-missing");
        File cache = getContext().getCacheDir();
        File source = new File(cache, cacheName).getCanonicalFile();
        if (!source.getParentFile().equals(cache.getCanonicalFile()) || !source.isFile()) throw new SecurityException("invalid-cache-source");
        Uri folder = ensureDirectory(tree, BACKUP_DIR);
        Date date = parseDate(dateText);
        Calendar cal = Calendar.getInstance(); cal.setTime(date); cal.setFirstDayOfWeek(Calendar.MONDAY); cal.setMinimalDaysInFirstWeek(4);
        String daily = "backup-" + dateText + ".sccbak";
        String weekly = String.format(Locale.US, "week-%04d-W%02d.sccbak", isoWeekYear(cal), cal.get(Calendar.WEEK_OF_YEAR));
        String monthly = "month-" + dateText.substring(0, 7) + ".sccbak";
        String expected = sha256File(source);
        String actual = writeVerified(folder, daily, source, expected);
        writeVerified(folder, "latest.sccbak", source, expected);
        writeVerified(folder, weekly, source, expected);
        writeVerified(folder, monthly, source, expected);
        prune(folder, "backup-\\d{4}-\\d{2}-\\d{2}\\.sccbak", 7);
        prune(folder, "week-\\d{4}-W\\d{2}\\.sccbak", 5);
        prune(folder, "month-\\d{4}-\\d{2}\\.sccbak", 12);
        JSObject out = new JSObject();
        out.put("verified", expected.equals(actual));
        out.put("sha256", actual);
        out.put("size", source.length());
        out.put("dailyFile", daily);
        out.put("destinationId", prefs().getString(ID_KEY, ""));
        out.put("label", prefs().getString(LABEL_KEY, "Off-device folder"));
        return out;
    }

    private static int isoWeekYear(Calendar cal) {
        int year = cal.get(Calendar.YEAR);
        int month = cal.get(Calendar.MONTH);
        int week = cal.get(Calendar.WEEK_OF_YEAR);
        if (month == Calendar.JANUARY && week >= 52) return year - 1;
        if (month == Calendar.DECEMBER && week == 1) return year + 1;
        return year;
    }
    private String writeVerified(Uri folder, String name, File source, String expected) throws Exception {
        ContentResolver cr = getContext().getContentResolver();
        String partialName = name + ".partial";
        deleteNamed(folder, partialName);
        Uri partial = DocumentsContract.createDocument(cr, folder, MIME, partialName);
        if (partial == null) throw new IllegalStateException("provider-create-failed");
        writeFile(partial, source);
        if (!expected.equals(sha256Uri(partial))) { try { DocumentsContract.deleteDocument(cr, partial); } catch (Throwable ignored) {} throw new IllegalStateException("write-verification-failed"); }
        Uri finalUri = null;
        try {
            deleteNamed(folder, name);
            finalUri = DocumentsContract.renameDocument(cr, partial, name);
        } catch (Throwable ignored) {}
        if (finalUri == null) {
            Uri target = findChild(folder, name);
            if (target == null) target = DocumentsContract.createDocument(cr, folder, MIME, name);
            if (target == null) throw new IllegalStateException("provider-target-create-failed");
            writeFile(target, source);
            finalUri = target;
            try { DocumentsContract.deleteDocument(cr, partial); } catch (Throwable ignored) {}
        }
        String actual = sha256Uri(finalUri);
        if (!expected.equals(actual)) throw new IllegalStateException("readback-hash-mismatch");
        return actual;
    }

    private void writeFile(Uri target, File source) throws Exception {
        ContentResolver cr = getContext().getContentResolver();
        try (InputStream in = new FileInputStream(source); OutputStream out = cr.openOutputStream(target, "wt")) {
            if (out == null) throw new IllegalStateException("provider-output-unavailable");
            byte[] buffer = new byte[65536]; int n;
            while ((n = in.read(buffer)) >= 0) if (n > 0) out.write(buffer, 0, n);
            out.flush();
        }
    }

    private Uri ensureDirectory(Uri tree, String name) throws Exception {
        ContentResolver cr = getContext().getContentResolver();
        Uri root = DocumentsContract.buildDocumentUriUsingTree(tree, DocumentsContract.getTreeDocumentId(tree));
        Uri existing = findChild(root, name);
        if (existing != null) return existing;
        Uri made = DocumentsContract.createDocument(cr, root, DocumentsContract.Document.MIME_TYPE_DIR, name);
        if (made == null) throw new IllegalStateException("backup-folder-create-failed");
        return made;
    }

    private Uri findChild(Uri parent, String name) throws Exception {
        ContentResolver cr = getContext().getContentResolver();
        Uri children = DocumentsContract.buildChildDocumentsUriUsingTree(parent, DocumentsContract.getDocumentId(parent));
        String[] cols = { DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME };
        try (Cursor c = cr.query(children, cols, null, null, null)) {
            if (c == null) return null;
            while (c.moveToNext()) if (name.equals(c.getString(1))) return DocumentsContract.buildDocumentUriUsingTree(parent, c.getString(0));
        }
        return null;
    }

    private void deleteNamed(Uri folder, String name) throws Exception {
        Uri child = findChild(folder, name);
        if (child != null) DocumentsContract.deleteDocument(getContext().getContentResolver(), child);
    }

    private void prune(Uri folder, String pattern, int keep) throws Exception {
        ContentResolver cr = getContext().getContentResolver();
        Uri children = DocumentsContract.buildChildDocumentsUriUsingTree(folder, DocumentsContract.getDocumentId(folder));
        String[] cols = { DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME };
        List<Doc> matches = new ArrayList<>();
        try (Cursor c = cr.query(children, cols, null, null, null)) {
            if (c != null) while (c.moveToNext()) {
                String name = c.getString(1);
                if (name != null && name.matches(pattern)) matches.add(new Doc(name, DocumentsContract.buildDocumentUriUsingTree(folder, c.getString(0))));
            }
        }
        Collections.sort(matches, new Comparator<Doc>() { public int compare(Doc a, Doc b) { return b.name.compareTo(a.name); } });
        for (int i = keep; i < matches.size(); i++) DocumentsContract.deleteDocument(cr, matches.get(i).uri);
    }

    private String sha256File(File file) throws Exception {
        try (InputStream in = new FileInputStream(file)) { return digest(in); }
    }
    private String sha256Uri(Uri uri) throws Exception {
        InputStream in = getContext().getContentResolver().openInputStream(uri);
        if (in == null) throw new IllegalStateException("provider-readback-unavailable");
        try (InputStream closeable = in) { return digest(closeable); }
    }
    private String digest(InputStream in) throws Exception {
        MessageDigest md = MessageDigest.getInstance("SHA-256");
        byte[] buffer = new byte[65536]; int n;
        while ((n = in.read(buffer)) >= 0) if (n > 0) md.update(buffer, 0, n);
        StringBuilder out = new StringBuilder();
        for (byte b : md.digest()) out.append(String.format(Locale.US, "%02x", b & 0xff));
        return out.toString();
    }
    private String sha256Text(String value) throws Exception {
        MessageDigest md = MessageDigest.getInstance("SHA-256");
        md.update(value.getBytes("UTF-8"));
        StringBuilder out = new StringBuilder();
        for (byte b : md.digest()) out.append(String.format(Locale.US, "%02x", b & 0xff));
        return out.toString();
    }

    private boolean hasGrant(Uri tree) {
        for (UriPermission p : getContext().getContentResolver().getPersistedUriPermissions()) {
            if (tree.equals(p.getUri()) && p.isReadPermission() && p.isWritePermission()) return true;
        }
        return false;
    }
    private boolean isOffDeviceAuthority(String authority) {
        if (authority == null || authority.trim().isEmpty()) return false;
        return !authority.equals("com.android.externalstorage.documents") &&
               !authority.equals("com.android.providers.downloads.documents") &&
               !authority.equals("com.android.providers.media.documents");
    }
    private String displayName(Uri tree) {
        try {
            Uri doc = DocumentsContract.buildDocumentUriUsingTree(tree, DocumentsContract.getTreeDocumentId(tree));
            try (Cursor c = getContext().getContentResolver().query(doc, new String[] { DocumentsContract.Document.COLUMN_DISPLAY_NAME }, null, null, null)) {
                if (c != null && c.moveToFirst()) return c.getString(0);
            }
        } catch (Throwable ignored) {}
        return "Off-device folder";
    }
    private Date parseDate(String value) throws Exception {
        SimpleDateFormat fmt = new SimpleDateFormat("yyyy-MM-dd", Locale.US); fmt.setLenient(false); return fmt.parse(value);
    }
    private SharedPreferences prefs() { return getContext().getSharedPreferences(PREFS, Context.MODE_PRIVATE); }
    private String safeLabel(String value) { return value == null ? "" : value.replaceAll("[\\p{Cntrl}]", " ").trim().substring(0, Math.min(100, value.replaceAll("[\\p{Cntrl}]", " ").trim().length())); }
    private String safeMessage(Throwable t) { String m = t == null ? "unknown" : t.getMessage(); return safeLabel(m == null ? t.getClass().getSimpleName() : m); }
    private static class Doc { final String name; final Uri uri; Doc(String name, Uri uri) { this.name = name; this.uri = uri; } }
}
