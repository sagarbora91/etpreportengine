package com.saagartraders.bcc;

import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.database.sqlite.SQLiteCantOpenDatabaseException;
import android.database.sqlite.SQLiteDatabase;
import android.database.sqlite.SQLiteDatabaseCorruptException;
import android.database.sqlite.SQLiteDiskIOException;
import android.database.sqlite.SQLiteException;
import android.database.sqlite.SQLiteFullException;
import android.database.sqlite.SQLiteOpenHelper;
import android.database.sqlite.SQLiteReadOnlyDatabaseException;
import android.os.StatFs;

import com.getcapacitor.JSArray;
import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.nio.charset.StandardCharsets;

/*
 * Incremental durable store for the synchronous JavaScript localStorage facade.
 *
 * The native db never receives plaintext business keys or values:
 * JavaScript sends a SHA-256 key id and a per-record AES-GCM envelope. Writes
 * are bounded and transactional, so changing one app key never exports or
 * rewrites the full multi-year db.
 */
@CapacitorPlugin(name = "SaagarNativeStore")
public class SaagarNativeStorePlugin extends Plugin {
    private static final int CONTRACT_VERSION = 1;
    private static final int MAX_BATCH_OPS = 64;
    private static final int MAX_BATCH_BYTES = 16 * 1024 * 1024;
    private static final int DEFAULT_PAGE_ROWS = 32;
    private static final int MAX_PAGE_ROWS = 64;
    private static final int DEFAULT_PAGE_BYTES = 2 * 1024 * 1024;
    private static final int MAX_PAGE_BYTES = 8 * 1024 * 1024;
    private static final int DEFAULT_RECORD_CHUNK_CHARS = 256 * 1024;
    private static final int MAX_RECORD_CHUNK_CHARS = 512 * 1024;
    private static final int MAX_INLINE_CURSOR_PAYLOAD_CHARS = 512 * 1024;
    private StoreDb helper;

    @Override
    public void load() {
        helper = new StoreDb(getContext());
        helper.setWriteAheadLoggingEnabled(true);
    }

    @PluginMethod
    public void status(PluginCall call) {
        JSObject out = null;
        Throwable failure = null;
        try {
            SQLiteDatabase db = db();
            String integrity = quickCheck(db);
            out = new JSObject();
            out.put("contractVersion", CONTRACT_VERSION);
            out.put("available", true);
            out.put("schemaVersion", db.getVersion());
            out.put("rows", rowCount(db));
            out.put("stagedRows", rowCount(db, "kv_stage"));
            out.put("migrated", "1".equals(meta(db, "migrated")));
            out.put("integrity", "ok".equalsIgnoreCase(integrity) ? "ok" : "failed");
            out.put("storage", storageSnapshot());
        } catch (Throwable t) {
            failure = t;
        }
        if (failure != null) {
            rejectNative(call, "status", failure);
            return;
        }
        call.resolve(out);
    }

    @PluginMethod
    public void storageInfo(PluginCall call) {
        call.resolve(storageSnapshot());
    }

    @PluginMethod
    public void beginMigration(PluginCall call) {
        SQLiteDatabase db = null;
        boolean transactionStarted = false;
        JSObject out = null;
        Throwable failure = null;
        try {
            db = db();
            db.beginTransaction();
            transactionStarted = true;
            db.delete("kv_stage", null, null);
            putMeta(db, "expected_rows", "");
            db.setTransactionSuccessful();
            out = new JSObject();
            out.put("ready", true);
        } catch (Throwable t) {
            failure = t;
        } finally {
            failure = finishTransaction(db, transactionStarted, failure);
        }
        if (failure != null) {
            rejectNative(call, "beginMigration", failure);
            return;
        }
        call.resolve(out);
    }

    @PluginMethod
    public void finishMigration(PluginCall call) {
        Integer expectedValue = call.getInt("expectedRows");
        if (expectedValue == null || expectedValue < 0) {
            rejectArgument(call, "finishMigration");
            return;
        }
        final int expected = expectedValue;
        SQLiteDatabase db = null;
        boolean transactionStarted = false;
        JSObject out = null;
        Throwable failure = null;
        try {
            db = db();
            db.beginTransaction();
            transactionStarted = true;
            long actual = rowCount(db, "kv_stage");
            if (actual != expected) {
                throw new NativeStoreFailure("ROW_COUNT_MISMATCH", true);
            }
            if (!"ok".equalsIgnoreCase(quickCheck(db))) {
                throw new NativeStoreFailure("INTEGRITY_FAILED", false);
            }
            db.delete("kv", null, null);
            db.execSQL("INSERT INTO kv(key_id,payload,updated_seq) SELECT key_id,payload,updated_seq FROM kv_stage");
            if (rowCount(db) != expected) {
                throw new NativeStoreFailure("ROW_COUNT_MISMATCH", true);
            }
            db.delete("kv_stage", null, null);
            putMeta(db, "expected_rows", String.valueOf(expected));
            putMeta(db, "migrated", "1");
            db.setTransactionSuccessful();
            out = new JSObject();
            out.put("migrated", true);
            out.put("rows", actual);
        } catch (Throwable t) {
            failure = t;
        } finally {
            failure = finishTransaction(db, transactionStarted, failure);
        }
        if (failure != null) {
            rejectNative(call, "finishMigration", failure);
            return;
        }
        call.resolve(out);
    }

    @PluginMethod
    public void readPage(PluginCall call) {
        String after = call.getString("afterKeyId");
        if (after == null) {
            if (call.getData().has("afterKeyId")) {
                rejectArgument(call, "readPage");
                return;
            }
            after = "";
        }
        if (!after.isEmpty() && !after.matches("[a-f0-9]{64}")) {
            rejectArgument(call, "readPage");
            return;
        }
        Integer limitValue = call.getInt("limit");
        if (limitValue == null && call.getData().has("limit")) {
            rejectArgument(call, "readPage");
            return;
        }
        Integer maxBytesValue = call.getInt("maxBytes");
        if (maxBytesValue == null && call.getData().has("maxBytes")) {
            rejectArgument(call, "readPage");
            return;
        }
        int limit = limitValue == null ? DEFAULT_PAGE_ROWS : limitValue;
        int maxBytes = maxBytesValue == null ? DEFAULT_PAGE_BYTES : maxBytesValue;
        if (limit < 1 || limit > MAX_PAGE_ROWS || maxBytes < 64 * 1024 || maxBytes > MAX_PAGE_BYTES) {
            rejectArgument(call, "readPage");
            return;
        }
        int maxInlineChars = Math.min(maxBytes, MAX_INLINE_CURSOR_PAYLOAD_CHARS);

        JSArray rows = new JSArray();
        String last = after;
        int used = 0;
        boolean hasMore = false;
        JSObject out = null;
        Throwable failure = null;
        String sql = "SELECT key_id,CASE WHEN length(payload)<=CAST(? AS INTEGER) THEN payload ELSE NULL END,updated_seq,length(payload) "
            + "FROM kv WHERE key_id>? ORDER BY key_id LIMIT ?";
        try {
            SQLiteDatabase db = db();
            try (Cursor cursor = db.rawQuery(sql, new String[] {
                String.valueOf(maxInlineChars), after, String.valueOf(limit + 1)
            })) {
                while (cursor.moveToNext()) {
                    String id = cursor.getString(0);
                    int payloadChars = cursor.getInt(3);
                    if (payloadChars > maxInlineChars) {
                        if (rows.length() == 0) {
                            JSObject oversized = new JSObject();
                            oversized.put("keyId", id);
                            oversized.put("seq", cursor.getLong(2));
                            oversized.put("chars", payloadChars);
                            out = new JSObject();
                            out.put("rows", rows);
                            out.put("afterKeyId", after);
                            out.put("done", false);
                            out.put("bytes", 0);
                            out.put("oversized", oversized);
                        } else {
                            hasMore = true;
                        }
                        break;
                    }
                    String payload = cursor.getString(1);
                    if (payload == null) throw new NativeStoreFailure("DB_READ_FAILED", true);
                    int bytes = payload == null ? 0 : payload.getBytes(StandardCharsets.UTF_8).length;
                    if (rows.length() >= limit || (rows.length() > 0 && used + bytes > maxBytes)) {
                        hasMore = true;
                        break;
                    }
                    JSObject row = new JSObject();
                    row.put("keyId", id);
                    row.put("payload", payload);
                    row.put("seq", cursor.getLong(2));
                    rows.put(row);
                    last = id;
                    used += bytes;
                }
                if (!hasMore && cursor.moveToNext()) {
                    hasMore = true;
                }
            }
            if (out == null) {
                out = new JSObject();
                out.put("rows", rows);
                out.put("afterKeyId", last);
                out.put("done", !hasMore);
                out.put("bytes", used);
            }
        } catch (Throwable t) {
            failure = t;
        }
        if (failure != null) {
            rejectNative(call, "readPage", failure);
            return;
        }
        call.resolve(out);
    }

    @PluginMethod
    public void readRecordChunk(PluginCall call) {
        String keyId = call.getString("keyId");
        Integer offsetValue = call.getInt("offset");
        Integer limitValue = call.getInt("limit");
        if (keyId == null || !keyId.matches("[a-f0-9]{64}") || offsetValue == null
            || offsetValue < 0 || offsetValue > MAX_BATCH_BYTES
            || (limitValue != null && (limitValue < 64 * 1024 || limitValue > MAX_RECORD_CHUNK_CHARS))) {
            rejectArgument(call, "readRecordChunk");
            return;
        }
        int offset = offsetValue;
        int limit = limitValue == null ? DEFAULT_RECORD_CHUNK_CHARS : limitValue;
        JSObject out = null;
        Throwable failure = null;
        try {
            SQLiteDatabase db = db();
            try (Cursor cursor = db.rawQuery(
                "SELECT substr(payload,?,?),length(payload),updated_seq FROM kv WHERE key_id=?",
                new String[] { String.valueOf(offset + 1), String.valueOf(limit), keyId })) {
                if (!cursor.moveToFirst()) throw new NativeStoreFailure("DB_READ_FAILED", true);
                String chunk = cursor.getString(0);
                int totalChars = cursor.getInt(1);
                if (totalChars < 1 || totalChars > MAX_BATCH_BYTES) {
                    throw new NativeStoreFailure("DB_READ_FAILED", true);
                }
                int nextOffset = offset + (chunk == null ? 0 : chunk.length());
                if (offset >= totalChars || nextOffset <= offset || nextOffset > totalChars || nextOffset - offset > limit) {
                    throw new NativeStoreFailure("DB_READ_FAILED", true);
                }
                out = new JSObject();
                out.put("keyId", keyId);
                out.put("chunk", chunk);
                out.put("offset", offset);
                out.put("nextOffset", nextOffset);
                out.put("totalChars", totalChars);
                out.put("seq", cursor.getLong(2));
                out.put("done", nextOffset == totalChars);
            }
        } catch (Throwable t) {
            failure = t;
        }
        if (failure != null) {
            rejectNative(call, "readRecordChunk", failure);
            return;
        }
        call.resolve(out);
    }

    @PluginMethod
    public void applyBatch(PluginCall call) {
        JSArray input = call.getArray("ops");
        if (input == null && call.getData().has("ops")) {
            rejectArgument(call, "applyBatch");
            return;
        }
        if (input == null) {
            input = new JSArray();
        }
        Boolean clearValue = call.getBoolean("clear");
        if (clearValue == null && call.getData().has("clear")) {
            rejectArgument(call, "applyBatch");
            return;
        }
        Boolean stageValue = call.getBoolean("stage");
        if (stageValue == null && call.getData().has("stage")) {
            rejectArgument(call, "applyBatch");
            return;
        }
        boolean clear = clearValue != null && clearValue;
        boolean stage = stageValue != null && stageValue;
        String table = stage ? "kv_stage" : "kv";
        JSONArray ops = input;
        try {
            validateBatch(ops);
        } catch (Throwable t) {
            rejectNative(call, "applyBatch", t);
            return;
        }

        SQLiteDatabase db = null;
        boolean transactionStarted = false;
        int changed = 0;
        JSObject out = null;
        Throwable failure = null;
        try {
            db = db();
            db.beginTransaction();
            transactionStarted = true;
            if (clear) {
                db.delete(table, null, null);
            }
            for (int i = 0; i < ops.length(); i++) {
                JSONObject op = ops.getJSONObject(i);
                String type = op.getString("type");
                String keyId = op.getString("keyId");
                long seq = ((Number) op.get("seq")).longValue();
                if ("remove".equals(type)) {
                    changed += db.delete(table, "key_id=?", new String[] { keyId });
                } else {
                    ContentValues values = new ContentValues();
                    values.put("key_id", keyId);
                    values.put("payload", op.getString("payload"));
                    values.put("updated_seq", seq);
                    long rowId = db.insertWithOnConflict(table, null, values, SQLiteDatabase.CONFLICT_REPLACE);
                    if (rowId == -1) {
                        throw new NativeStoreFailure("DB_IO_FAILED", true);
                    }
                    changed++;
                }
            }
            db.setTransactionSuccessful();
            out = new JSObject();
            out.put("ok", true);
            out.put("changed", changed);
            out.put("rows", clear ? rowCount(db, table) : -1);
        } catch (Throwable t) {
            failure = t;
        } finally {
            failure = finishTransaction(db, transactionStarted, failure);
        }
        if (failure != null) {
            rejectNative(call, "applyBatch", failure);
            return;
        }
        call.resolve(out);
    }

    @PluginMethod
    public void reset(PluginCall call) {
        SQLiteDatabase db = null;
        boolean transactionStarted = false;
        JSObject out = null;
        Throwable failure = null;
        try {
            db = db();
            db.beginTransaction();
            transactionStarted = true;
            db.delete("kv", null, null);
            db.delete("kv_stage", null, null);
            db.delete("meta", null, null);
            db.setTransactionSuccessful();
            out = new JSObject();
            out.put("cleared", true);
        } catch (Throwable t) {
            failure = t;
        } finally {
            failure = finishTransaction(db, transactionStarted, failure);
        }
        if (failure != null) {
            rejectNative(call, "reset", failure);
            return;
        }
        call.resolve(out);
    }
    private SQLiteDatabase db() {
        if (helper == null) {
            helper = new StoreDb(getContext());
            helper.setWriteAheadLoggingEnabled(true);
        }
        return helper.getWritableDatabase();
    }

    private long rowCount(SQLiteDatabase db) {
        return rowCount(db, "kv");
    }

    private long rowCount(SQLiteDatabase db, String table) {
        if (!("kv".equals(table) || "kv_stage".equals(table))) throw new IllegalArgumentException("invalid-table");
        try (Cursor cursor = db.rawQuery("SELECT COUNT(*) FROM " + table, null)) {
            return cursor.moveToFirst() ? cursor.getLong(0) : 0;
        }
    }

    private String quickCheck(SQLiteDatabase db) {
        try (Cursor cursor = db.rawQuery("PRAGMA quick_check(1)", null)) {
            return cursor.moveToFirst() ? cursor.getString(0) : "missing";
        }
    }

    private String meta(SQLiteDatabase db, String key) {
        try (Cursor cursor = db.rawQuery("SELECT value FROM meta WHERE key=?", new String[] { key })) {
            return cursor.moveToFirst() ? cursor.getString(0) : "";
        }
    }

    private void putMeta(SQLiteDatabase db, String key, String value) {
        ContentValues values = new ContentValues();
        values.put("key", key);
        values.put("value", value);
        db.insertWithOnConflict("meta", null, values, SQLiteDatabase.CONFLICT_REPLACE);
    }

    private Throwable finishTransaction(SQLiteDatabase db, boolean transactionStarted, Throwable failure) {
        if (!transactionStarted || db == null) {
            return failure;
        }
        try {
            db.endTransaction();
        } catch (Throwable endFailure) {
            if (failure == null) {
                return endFailure;
            }
        }
        return failure;
    }

    private void validateBatch(JSONArray ops) {
        if (ops.length() > MAX_BATCH_OPS) {
            throw new NativeStoreFailure("INVALID_ARGUMENT", false);
        }
        long bytes = 0;
        try {
            for (int i = 0; i < ops.length(); i++) {
                JSONObject op = ops.getJSONObject(i);
                Object typeValue = op.opt("type");
                Object keyValue = op.opt("keyId");
                Object seqValue = op.opt("seq");
                if (!(typeValue instanceof String) || !(keyValue instanceof String) || !(seqValue instanceof Number)) {
                    throw new NativeStoreFailure("INVALID_ARGUMENT", false);
                }
                String type = (String) typeValue;
                String keyId = (String) keyValue;
                if (!("set".equals(type) || "remove".equals(type)) || !keyId.matches("[a-f0-9]{64}")) {
                    throw new NativeStoreFailure("INVALID_ARGUMENT", false);
                }
                double seqNumber = ((Number) seqValue).doubleValue();
                if (Double.isNaN(seqNumber) || Double.isInfinite(seqNumber) || seqNumber < 0 || seqNumber > Long.MAX_VALUE || seqNumber != Math.rint(seqNumber)) {
                    throw new NativeStoreFailure("INVALID_ARGUMENT", false);
                }
                bytes += utf8Length(keyId);
                if ("set".equals(type)) {
                    Object payloadValue = op.opt("payload");
                    if (!(payloadValue instanceof String) || ((String) payloadValue).isEmpty()) {
                        throw new NativeStoreFailure("INVALID_ARGUMENT", false);
                    }
                    bytes += utf8Length((String) payloadValue);
                }
                if (bytes > MAX_BATCH_BYTES) {
                    throw new NativeStoreFailure("INVALID_ARGUMENT", false);
                }
            }
        } catch (NativeStoreFailure failure) {
            throw failure;
        } catch (Throwable ignored) {
            throw new NativeStoreFailure("INVALID_ARGUMENT", false);
        }
    }

    private void rejectArgument(PluginCall call, String operation) {
        rejectNative(call, operation, new NativeStoreFailure("INVALID_ARGUMENT", false));
    }

    private void rejectNative(PluginCall call, String operation, Throwable failure) {
        String reason = reasonFor(operation, failure);
        boolean retryable = retryableFor(reason, failure);
        JSObject data = new JSObject();
        data.put("contractVersion", CONTRACT_VERSION);
        data.put("reason", reason);
        data.put("operation", operation);
        data.put("retryable", retryable);
        data.put("storage", storageSnapshot());
        call.reject(publicMessage(reason), reason, data);
    }

    private String reasonFor(String operation, Throwable failure) {
        if (failure instanceof NativeStoreFailure) {
            return ((NativeStoreFailure) failure).reason;
        }
        if (failure instanceof SQLiteDatabaseCorruptException) {
            return "INTEGRITY_FAILED";
        }
        if (failure instanceof SQLiteFullException) {
            return "NO_SPACE";
        }
        if (failure instanceof SQLiteCantOpenDatabaseException) {
            return "DB_OPEN_FAILED";
        }
        if (failure instanceof SQLiteDiskIOException) {
            return "DB_IO_FAILED";
        }
        if (failure instanceof SQLiteReadOnlyDatabaseException) {
            return "DB_READ_ONLY";
        }
        if (failure instanceof SQLiteException) {
            return ("readPage".equals(operation) || "readRecordChunk".equals(operation)) ? "DB_READ_FAILED" : "DB_IO_FAILED";
        }
        if (failure instanceof IllegalArgumentException) {
            return "INVALID_ARGUMENT";
        }
        if ("readPage".equals(operation) || "readRecordChunk".equals(operation)) {
            return "DB_READ_FAILED";
        }
        return "STORE_UNAVAILABLE";
    }

    private boolean retryableFor(String reason, Throwable failure) {
        if (failure instanceof NativeStoreFailure) {
            return ((NativeStoreFailure) failure).retryable;
        }
        return "NO_SPACE".equals(reason)
            || "DB_OPEN_FAILED".equals(reason)
            || "DB_IO_FAILED".equals(reason)
            || "DB_READ_FAILED".equals(reason)
            || "ROW_COUNT_MISMATCH".equals(reason)
            || "MIGRATION_INCOMPLETE".equals(reason)
            || "STORE_UNAVAILABLE".equals(reason);
    }

    private String publicMessage(String reason) {
        if ("NO_SPACE".equals(reason)) return "Device storage does not have enough free space.";
        if ("DB_OPEN_FAILED".equals(reason)) return "Secure storage could not be opened.";
        if ("INTEGRITY_FAILED".equals(reason)) return "Secure storage integrity verification failed.";
        if ("DB_IO_FAILED".equals(reason)) return "Secure storage input/output failed.";
        if ("DB_READ_FAILED".equals(reason)) return "Secure storage could not be read.";
        if ("DB_READ_ONLY".equals(reason)) return "Secure storage is read-only.";
        if ("SCHEMA_UNSUPPORTED".equals(reason)) return "This secure storage version is not supported.";
        if ("ROW_COUNT_MISMATCH".equals(reason)) return "Secure storage migration verification failed.";
        if ("MIGRATION_INCOMPLETE".equals(reason)) return "Secure storage migration is incomplete.";
        if ("INVALID_ARGUMENT".equals(reason)) return "The secure storage request was invalid.";
        return "Secure storage is unavailable.";
    }

    private JSObject storageSnapshot() {
        long totalBytes = 0;
        long availableBytes = 0;
        long freeBytes = 0;
        File databaseFile = null;
        try {
            Context context = getContext();
            databaseFile = context.getDatabasePath(StoreDb.NAME);
            File volume = databaseFile.getParentFile();
            if (volume == null) {
                volume = context.getFilesDir();
            }
            StatFs stats = new StatFs(volume.getAbsolutePath());
            totalBytes = stats.getTotalBytes();
            availableBytes = stats.getAvailableBytes();
            freeBytes = stats.getFreeBytes();
        } catch (Throwable ignored) {
            // Diagnostics must never hide the underlying storage error.
        }
        long databaseBytes = fileBytes(databaseFile);
        long walBytes = sidecarBytes(databaseFile, "-wal");
        long shmBytes = sidecarBytes(databaseFile, "-shm");
        long journalBytes = sidecarBytes(databaseFile, "-journal");
        long nativeStoreBytes = saturatingAdd(databaseBytes, walBytes, shmBytes, journalBytes);
        JSObject storage = new JSObject();
        storage.put("totalBytes", totalBytes);
        storage.put("availableBytes", availableBytes);
        storage.put("freeBytes", freeBytes);
        storage.put("databaseBytes", databaseBytes);
        storage.put("walBytes", walBytes);
        storage.put("shmBytes", shmBytes);
        storage.put("journalBytes", journalBytes);
        storage.put("nativeStoreBytes", nativeStoreBytes);
        return storage;
    }

    private long fileBytes(File file) {
        try {
            return file != null && file.isFile() ? Math.max(0, file.length()) : 0;
        } catch (Throwable ignored) {
            return 0;
        }
    }

    private long sidecarBytes(File databaseFile, String suffix) {
        if (databaseFile == null) {
            return 0;
        }
        return fileBytes(new File(databaseFile.getPath() + suffix));
    }

    private long saturatingAdd(long... values) {
        long total = 0;
        for (long value : values) {
            if (value > Long.MAX_VALUE - total) {
                return Long.MAX_VALUE;
            }
            total += Math.max(0, value);
        }
        return total;
    }

    private int utf8Length(String value) {
        return value == null ? 0 : value.getBytes(StandardCharsets.UTF_8).length;
    }

    private static final class NativeStoreFailure extends RuntimeException {
        private final String reason;
        private final boolean retryable;

        NativeStoreFailure(String reason, boolean retryable) {
            super(reason);
            this.reason = reason;
            this.retryable = retryable;
        }
    }
    private static final class StoreDb extends SQLiteOpenHelper {
        private static final String NAME = "saagar-native-kv.db";
        private static final int VERSION = 2;

        StoreDb(Context context) {
            super(context, NAME, null, VERSION);
        }

        @Override
        public void onConfigure(SQLiteDatabase db) {
            super.onConfigure(db);
            db.setForeignKeyConstraintsEnabled(true);
            db.rawQuery("PRAGMA secure_delete=ON", null).close();
        }

        @Override
        public void onCreate(SQLiteDatabase db) {
            db.execSQL("CREATE TABLE kv (key_id TEXT PRIMARY KEY NOT NULL, payload TEXT NOT NULL, updated_seq INTEGER NOT NULL)");
            db.execSQL("CREATE TABLE kv_stage (key_id TEXT PRIMARY KEY NOT NULL, payload TEXT NOT NULL, updated_seq INTEGER NOT NULL)");
            db.execSQL("CREATE TABLE meta (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL)");
        }

        @Override
        public void onUpgrade(SQLiteDatabase db, int oldVersion, int newVersion) {
            if (oldVersion < 2) {
                db.execSQL("CREATE TABLE IF NOT EXISTS kv_stage (key_id TEXT PRIMARY KEY NOT NULL, payload TEXT NOT NULL, updated_seq INTEGER NOT NULL)");
                return;
            }
            throw new NativeStoreFailure("SCHEMA_UNSUPPORTED", false);
        }
    }
}
