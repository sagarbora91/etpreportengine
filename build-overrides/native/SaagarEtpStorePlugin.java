package com.saagartraders.bcc;

import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.database.sqlite.*;
import com.getcapacitor.*;
import com.getcapacitor.annotation.CapacitorPlugin;
import org.json.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.KeyStore;
import java.util.HashSet;
import android.util.Base64;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.AEADBadTagException;
import javax.crypto.spec.GCMParameterSpec;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.security.keystore.KeyPermanentlyInvalidatedException;

/* Separate native ETP store with no operational-store coupling and no report-specific columns. */
@CapacitorPlugin(name="SaagarEtpStore")
public class SaagarEtpStorePlugin extends Plugin {
  static final int CONTRACT_VERSION=1, MAX_ROWS=500, MAX_BYTES=512*1024, MAX_READ_ROWS=200, MAX_READ_FIELDS=64;
  static final String[] REPORTS={"R003","R013","R022","R025"};
  static final String ETP_KEY_ALIAS="saagar_etp_fact_aes256_v1";
  private EtpDb helper;
  @Override public void load(){ helper=new EtpDb(getContext()); helper.setWriteAheadLoggingEnabled(true); }

  @PluginMethod public synchronized void beginStage(PluginCall call){
    String scope=scope(call,"beginStage"), gen=token(call,"generationId","beginStage"); if(scope==null||gen==null)return;
    SQLiteDatabase db=null;
    try{ db=db(); begin(db);
      db.delete("generation","scope_key=? AND state IN ('STAGING','SEALED')",new String[]{scope});
      if(exists(db,"SELECT 1 FROM generation WHERE scope_key=? AND generation_id=?",scope,gen)) fail("GENERATION_EXISTS",false);
      ContentValues v=new ContentValues(); v.put("scope_key",scope);v.put("generation_id",gen);v.put("state","STAGING");v.put("created_at",System.currentTimeMillis());
      if(db.insert("generation",null,v)==-1)fail("DB_IO_FAILED",true); commit(db); ok(call,"state","STAGING");
    }catch(Throwable t){rollback(db);reject(call,"beginStage",t);}
  }

  @PluginMethod public synchronized void appendStageChunk(PluginCall call){
    String scope=scope(call,"appendStageChunk"),gen=token(call,"generationId","appendStageChunk"),report=report(call,"appendStageChunk");
    Integer index=call.getInt("chunkIndex"); JSArray rows=call.getArray("rows"); if(scope==null||gen==null||report==null)return;
    if(index==null||index<0||index>=4096||rows==null||rows.length()<1||rows.length()>MAX_ROWS){bad(call,"appendStageChunk");return;}
    String payload=rows.toString(); if(bytes(payload)>MAX_BYTES){bad(call,"appendStageChunk");return;} String digest=sha256(payload);
    String aad="1|"+scope+"|"+gen+"|"+report+"|"+index+"|"+rows.length()+"|"+digest;
    String envelope;try{envelope=encryptEnvelope(payload,aad);}catch(Throwable t){reject(call,"appendStageChunk",new Failure("KEY_UNAVAILABLE",false));return;} SQLiteDatabase db=null;
    try{db=db();begin(db); requireState(db,scope,gen,"STAGING"); ContentValues v=new ContentValues();v.put("scope_key",scope);v.put("generation_id",gen);v.put("report_id",report);v.put("chunk_index",index);v.put("row_count",rows.length());v.put("digest",digest);v.put("payload_envelope",envelope);
      if(db.insertWithOnConflict("stage_chunk",null,v,SQLiteDatabase.CONFLICT_IGNORE)==-1){try(Cursor c=db.rawQuery("SELECT digest FROM stage_chunk WHERE scope_key=? AND generation_id=? AND report_id=? AND chunk_index=?",new String[]{scope,gen,report,String.valueOf(index)})){if(!c.moveToFirst()||!digest.equals(c.getString(0)))fail("CHUNK_CONFLICT",false);}}
      commit(db);JSObject out=new JSObject();out.put("ok",true);out.put("digest",digest);out.put("rowCount",rows.length());call.resolve(out);
    }catch(Throwable t){rollback(db);reject(call,"appendStageChunk",t);}
  }

  @PluginMethod public synchronized void finishStage(PluginCall call){
    String scope=scope(call,"finishStage"),gen=token(call,"generationId","finishStage");JSObject manifest=call.getObject("manifest");if(scope==null||gen==null)return;
    if(!validManifest(manifest,scope,gen)){bad(call,"finishStage");return;} String text=manifest.toString();if(bytes(text)>256*1024){bad(call,"finishStage");return;} SQLiteDatabase db=null;
    try{db=db();begin(db);requireState(db,scope,gen,"STAGING");JSONArray reports=manifest.getJSONArray("reports");long expected=0;
      for(int i=0;i<reports.length();i++){JSONObject r=reports.getJSONObject(i);String id=r.getString("reportId");long rows=r.getLong("rowCount");expected+=rows;if(scalar(db,"SELECT COALESCE(SUM(row_count),0) FROM stage_chunk WHERE scope_key=? AND generation_id=? AND report_id=?",scope,gen,id)!=rows)fail("GENERATION_INCOMPLETE",true);}
      if(scalar(db,"SELECT COALESCE(SUM(row_count),0) FROM stage_chunk WHERE scope_key=? AND generation_id=?",scope,gen)!=expected)fail("GENERATION_INCOMPLETE",true);long chunks=scalar(db,"SELECT COUNT(*) FROM stage_chunk WHERE scope_key=? AND generation_id=?",scope,gen);String manifestDigest=sha256(text);String sealAad="1|SEAL|"+scope+"|"+gen+"|"+manifestDigest+"|"+expected+"|"+chunks;String seal=encryptEnvelope("SEALED",sealAad);
      ContentValues v=new ContentValues();v.put("state","SEALED");v.put("manifest",text);v.put("manifest_digest",manifestDigest);v.put("row_count",expected);v.put("chunk_count",chunks);v.put("seal_envelope",seal);v.put("sealed_at",System.currentTimeMillis());db.update("generation",v,"scope_key=? AND generation_id=?",new String[]{scope,gen});commit(db);ok(call,"state","SEALED");
    }catch(Throwable t){rollback(db);reject(call,"finishStage",t);}
  }

  @PluginMethod public synchronized void publishStage(PluginCall call){
    String scope=scope(call,"publishStage"),gen=token(call,"generationId","publishStage");JSObject manifest=call.getObject("manifest");if(scope==null||gen==null)return;if(!validManifest(manifest,scope,gen)){bad(call,"publishStage");return;}SQLiteDatabase db=null;
    try{db=db();begin(db);requireState(db,scope,gen,"SEALED");try(Cursor c=db.rawQuery("SELECT manifest FROM generation WHERE scope_key=? AND generation_id=?",new String[]{scope,gen})){if(!c.moveToFirst()||!manifest.toString().equals(c.getString(0)))fail("MANIFEST_MISMATCH",false);}
      ContentValues p=new ContentValues();p.put("scope_key",scope);p.put("active_generation_id",gen);p.put("restore_fence",0);db.insertWithOnConflict("scope_pointer",null,p,SQLiteDatabase.CONFLICT_REPLACE);
      ContentValues v=new ContentValues();v.put("state","PUBLISHED");v.put("published_at",System.currentTimeMillis());db.update("generation",v,"scope_key=? AND generation_id=? AND state='SEALED'",new String[]{scope,gen});db.execSQL("DELETE FROM generation WHERE scope_key=? AND generation_id<>? AND generation_id NOT IN (SELECT generation_id FROM generation WHERE scope_key=? AND generation_id<>? AND state='PUBLISHED' ORDER BY published_at DESC LIMIT 1)",new Object[]{scope,gen,scope,gen});commit(db);JSObject out=new JSObject();out.put("ok",true);out.put("state","ACCEPTED");out.put("activeGenerationId",gen);call.resolve(out);
    }catch(Throwable t){rollback(db);reject(call,"publishStage",t);}
  }

  @PluginMethod public synchronized void readStatus(PluginCall call){String scope=scope(call,"readStatus");if(scope==null)return;try{SQLiteDatabase db=db();if(!"ok".equalsIgnoreCase(check(db)))fail("INTEGRITY_FAILED",false);String active=null;boolean fence=false;try(Cursor c=db.rawQuery("SELECT active_generation_id,restore_fence FROM scope_pointer WHERE scope_key=?",new String[]{scope})){if(c.moveToFirst()){active=c.isNull(0)?null:c.getString(0);fence=c.getInt(1)==1;}}if(active!=null&&!fence)authenticateGenerationSeal(db,scope,active);String state=fence?"REIMPORT_REQUIRED":active!=null?"ACCEPTED":exists(db,"SELECT 1 FROM generation WHERE scope_key=? AND state IN ('STAGING','SEALED')",scope)?"STAGING":"EMPTY";JSObject out=new JSObject();out.put("ok",true);out.put("state",state);out.put("activeGenerationId",active==null?JSONObject.NULL:active);out.put("restoreFence",fence);call.resolve(out);}catch(Throwable t){reject(call,"readStatus",t);}}
  @PluginMethod public synchronized void readFacts(PluginCall call){
    String scope=scope(call,"readFacts"),gen=token(call,"generationId","readFacts"),report=report(call,"readFacts");JSArray requested=call.getArray("fields");Integer chunk=call.getInt("cursorChunkIndex"),offset=call.getInt("cursorRowOffset"),limit=call.getInt("limit");if(scope==null||gen==null||report==null)return;
    if(requested==null||requested.length()<1||requested.length()>MAX_READ_FIELDS||chunk==null||chunk<0||chunk>=4096||offset==null||offset<0||offset>=MAX_ROWS||limit==null||limit<1||limit>MAX_READ_ROWS){bad(call,"readFacts");return;}
    HashSet<String> fields=new HashSet<>();try{for(int i=0;i<requested.length();i++){String field=requested.getString(i);if(!safeField(field)||!fields.add(field)){bad(call,"readFacts");return;}}}catch(Throwable t){bad(call,"readFacts");return;}
    try{SQLiteDatabase db=db();if(!"ok".equalsIgnoreCase(check(db)))fail("INTEGRITY_FAILED",false);String active=null;boolean fence=false;try(Cursor c=db.rawQuery("SELECT active_generation_id,restore_fence FROM scope_pointer WHERE scope_key=?",new String[]{scope})){if(c.moveToFirst()){active=c.isNull(0)?null:c.getString(0);fence=c.getInt(1)==1;}}if(fence)fail("RESTORE_FENCED",false);if(active==null||!gen.equals(active))fail("STALE_GENERATION",false);authenticateGenerationSeal(db,scope,gen);
      JSArray rows=new JSArray();int nextChunk=chunk,nextOffset=offset,consumedChunks=0;boolean more=false;
      while(rows.length()<limit&&consumedChunks<2){try(Cursor c=db.rawQuery("SELECT chunk_index,row_count,digest,payload_envelope FROM stage_chunk WHERE scope_key=? AND generation_id=? AND report_id=? AND chunk_index>=? ORDER BY chunk_index LIMIT 1",new String[]{scope,gen,report,String.valueOf(nextChunk)})){if(!c.moveToFirst()){more=false;break;}int actualChunk=c.getInt(0),rowCount=c.getInt(1);String digest=c.getString(2),envelope=c.getString(3);if(actualChunk!=nextChunk)nextOffset=0;String aad="1|"+scope+"|"+gen+"|"+report+"|"+actualChunk+"|"+rowCount+"|"+digest;byte[] plain=null;try{plain=decryptEnvelopeBytes(envelope,aad);if(!digest.equals(sha256(plain)))throw new AEADBadTagException("digest");JSONArray source=new JSONArray(new String(plain,StandardCharsets.UTF_8));if(source.length()!=rowCount)throw new AEADBadTagException("rowCount");for(int i=nextOffset;i<source.length()&&rows.length()<limit;i++){JSONObject input=source.getJSONObject(i),output=new JSONObject();for(String field:fields)output.put(field,input.has(field)?input.get(field):JSONObject.NULL);rows.put(output);nextOffset=i+1;}if(nextOffset>=source.length()){nextChunk=actualChunk+1;nextOffset=0;}else{nextChunk=actualChunk;}more=exists(db,"SELECT 1 FROM stage_chunk WHERE scope_key=? AND generation_id=? AND report_id=? AND chunk_index>=?",scope,gen,report,String.valueOf(nextChunk))&&(nextOffset>0||exists(db,"SELECT 1 FROM stage_chunk WHERE scope_key=? AND generation_id=? AND report_id=? AND chunk_index>?",scope,gen,report,String.valueOf(actualChunk)));}finally{if(plain!=null)java.util.Arrays.fill(plain,(byte)0);}}consumedChunks++;}
      JSObject out=new JSObject();out.put("ok",true);out.put("scopeKey",scope);out.put("generationId",gen);out.put("reportId",report);out.put("rows",rows);out.put("hasMore",more);out.put("nextChunkIndex",nextChunk);out.put("nextRowOffset",nextOffset);call.resolve(out);
    }catch(Throwable t){reject(call,"readFacts",t);}
  }
  @PluginMethod public synchronized void fenceAfterRestore(PluginCall call){String scope=scope(call,"fenceAfterRestore");if(scope==null)return;SQLiteDatabase db=null;try{db=db();begin(db);db.delete("stage_chunk","scope_key=?",new String[]{scope});db.delete("generation","scope_key=?",new String[]{scope});ContentValues p=new ContentValues();p.put("scope_key",scope);p.putNull("active_generation_id");p.put("restore_fence",1);db.insertWithOnConflict("scope_pointer",null,p,SQLiteDatabase.CONFLICT_REPLACE);commit(db);ok(call,"state","REIMPORT_REQUIRED");}catch(Throwable t){rollback(db);reject(call,"fenceAfterRestore",t);}}
  @PluginMethod public synchronized void resetScope(PluginCall call){String scope=scope(call,"resetScope");if(scope==null)return;SQLiteDatabase db=null;try{db=db();begin(db);db.delete("stage_chunk","scope_key=?",new String[]{scope});db.delete("generation","scope_key=?",new String[]{scope});db.delete("scope_pointer","scope_key=?",new String[]{scope});commit(db);ok(call,"state","EMPTY");}catch(Throwable t){rollback(db);reject(call,"resetScope",t);}}
  @PluginMethod public synchronized void resetStore(PluginCall call){try{if(call.getInt("contractVersion")==null||call.getInt("contractVersion")!=1){bad(call,"resetStore");return;}if(helper!=null){helper.close();helper=null;}Context c=getContext();java.io.File f=c.getDatabasePath(EtpDb.NAME);for(String suffix:new String[]{"","-wal","-shm","-journal"}){java.io.File target=new java.io.File(f.getPath()+suffix);if(target.exists()&&!target.delete())fail("RESET_FAILED",true);}KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);if(store.containsAlias(ETP_KEY_ALIAS))store.deleteEntry(ETP_KEY_ALIAS);if(store.containsAlias(ETP_KEY_ALIAS))fail("RESET_FAILED",false);ok(call,"state","EMPTY");}catch(Throwable t){reject(call,"resetStore",t);}}

  private SQLiteDatabase db(){if(helper==null){helper=new EtpDb(getContext());helper.setWriteAheadLoggingEnabled(true);}return helper.getWritableDatabase();}
  private void begin(SQLiteDatabase db){if(!"ok".equalsIgnoreCase(check(db)))fail("INTEGRITY_FAILED",false);db.beginTransaction();}
  private void commit(SQLiteDatabase db){db.setTransactionSuccessful();db.endTransaction();}
  private void rollback(SQLiteDatabase db){if(db!=null&&db.inTransaction())try{db.endTransaction();}catch(Throwable ignored){}}
  private String check(SQLiteDatabase db){try(Cursor c=db.rawQuery("PRAGMA quick_check(1)",null)){return c.moveToFirst()?c.getString(0):"failed";}}
  private boolean exists(SQLiteDatabase db,String sql,String...args){try(Cursor c=db.rawQuery(sql,args)){return c.moveToFirst();}}
  private long scalar(SQLiteDatabase db,String sql,String...args){try(Cursor c=db.rawQuery(sql,args)){return c.moveToFirst()?c.getLong(0):0;}}
  private boolean fenced(SQLiteDatabase db,String scope){return scalar(db,"SELECT COUNT(*) FROM scope_pointer WHERE scope_key=? AND restore_fence=1",scope)>0;}
  private void requireState(SQLiteDatabase db,String s,String g,String state){if(!exists(db,"SELECT 1 FROM generation WHERE scope_key=? AND generation_id=? AND state=?",s,g,state))fail("INVALID_GENERATION_STATE",false);}
  private String scope(PluginCall c,String op){Integer cv=c.getInt("contractVersion");String v=c.getString("scopeKey");if(cv==null||cv!=1||v==null||!v.matches("(WLMHW|HEMW)\\|\\d{4}-\\d{2}\\|\\d{4}-\\d{2}-\\d{2}\\.\\.\\d{4}-\\d{2}-\\d{2}")){bad(c,op);return null;}return v;}
  private String token(PluginCall c,String key,String op){String v=c.getString(key);if(v==null||v.length()>96||!v.matches("[A-Za-z0-9._:-]+")){bad(c,op);return null;}return v;}
  private String report(PluginCall c,String op){String v=c.getString("reportId");for(String x:REPORTS)if(x.equals(v))return v;bad(c,op);return null;}
  private boolean safeField(String v){return v!=null&&v.matches("[a-z][a-z0-9_]{0,63}")&&!v.matches("(?i).*(^|_)(workbook|worksheet|filename|file_label|file_path|source_name|source_bytes|blob|base64|customer|consumer|mobile|phone|email|address|name|aadhaar|pan|dob)($|_).*");}
  private boolean validManifest(JSObject m,String s,String g){try{if(m==null||!s.equals(m.getString("scopeKey"))||!g.equals(m.getString("generationId")))return false;JSONArray a=m.getJSONArray("reports");if(a.length()!=4)return false;HashSet<String> seen=new HashSet<>();for(int i=0;i<4;i++){JSONObject r=a.getJSONObject(i);String id=r.getString("reportId");boolean known=false;for(String x:REPORTS)if(x.equals(id))known=true;if(!known||!seen.add(id)||!r.getString("sourceSha256").matches("[a-f0-9]{64}")||!r.getString("headerSignatureSha256").matches("[a-f0-9]{64}")){return false;}long n=r.getLong("rowCount");if(n<0||n>250000)return false;}return true;}catch(Throwable t){return false;}}
  private int bytes(String s){return s.getBytes(StandardCharsets.UTF_8).length;}
  private String sha256(String s){try{byte[] h=MessageDigest.getInstance("SHA-256").digest(s.getBytes(StandardCharsets.UTF_8));StringBuilder b=new StringBuilder();for(byte x:h)b.append(String.format("%02x",x&255));return b.toString();}catch(Throwable t){throw new Failure("ETP_STORE_UNAVAILABLE",false);}}
  private SecretKey etpKey() throws Exception {KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);java.security.Key existing=store.getKey(ETP_KEY_ALIAS,null);if(existing instanceof SecretKey)return(SecretKey)existing;KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");generator.init(new KeyGenParameterSpec.Builder(ETP_KEY_ALIAS,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setKeySize(256).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).setRandomizedEncryptionRequired(true).build());return generator.generateKey();}
  private String encryptEnvelope(String plaintext,String aad) throws Exception {byte[] plain=plaintext.getBytes(StandardCharsets.UTF_8),opened=null;try{Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,etpKey());byte[] iv=cipher.getIV();if(iv==null||iv.length!=12)throw new AEADBadTagException("iv");cipher.updateAAD(aad.getBytes(StandardCharsets.UTF_8));byte[] sealed=cipher.doFinal(plain);String envelope="ETP1."+Base64.encodeToString(iv,Base64.NO_WRAP|Base64.NO_PADDING)+"."+Base64.encodeToString(sealed,Base64.NO_WRAP|Base64.NO_PADDING);opened=decryptEnvelopeBytes(envelope,aad);if(!MessageDigest.isEqual(plain,opened))throw new AEADBadTagException("verification");return envelope;}finally{java.util.Arrays.fill(plain,(byte)0);if(opened!=null)java.util.Arrays.fill(opened,(byte)0);}}
  private byte[] decryptEnvelopeBytes(String envelope,String aad) throws Exception {String[] parts=envelope.split("\\.",-1);if(parts.length!=3||!"ETP1".equals(parts[0]))throw new AEADBadTagException("envelope");byte[] iv=Base64.decode(parts[1],Base64.NO_WRAP);if(iv.length!=12)throw new AEADBadTagException("iv");byte[] sealed=Base64.decode(parts[2],Base64.NO_WRAP);Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,existingEtpKey(),new GCMParameterSpec(128,iv));cipher.updateAAD(aad.getBytes(StandardCharsets.UTF_8));return cipher.doFinal(sealed);}
  private SecretKey existingEtpKey() throws Exception {KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);java.security.Key key=store.getKey(ETP_KEY_ALIAS,null);if(!(key instanceof SecretKey))throw new Failure("KEY_UNAVAILABLE",false);return(SecretKey)key;}
  private void authenticateGenerationSeal(SQLiteDatabase db,String scope,String generation) throws Exception {try(Cursor c=db.rawQuery("SELECT manifest_digest,row_count,chunk_count,seal_envelope FROM generation WHERE scope_key=? AND generation_id=? AND state='PUBLISHED'",new String[]{scope,generation})){if(!c.moveToFirst())throw new AEADBadTagException("seal");String aad="1|SEAL|"+scope+"|"+generation+"|"+c.getString(0)+"|"+c.getLong(1)+"|"+c.getLong(2);byte[] opened=null;try{opened=decryptEnvelopeBytes(c.getString(3),aad);if(!MessageDigest.isEqual(opened,"SEALED".getBytes(StandardCharsets.UTF_8)))throw new AEADBadTagException("seal");}finally{if(opened!=null)java.util.Arrays.fill(opened,(byte)0);}}}
  private String sha256(byte[] data){try{byte[] h=MessageDigest.getInstance("SHA-256").digest(data);StringBuilder b=new StringBuilder();for(byte x:h)b.append(String.format("%02x",x&255));return b.toString();}catch(Throwable t){throw new Failure("ETP_STORE_UNAVAILABLE",false);}}
  private void ok(PluginCall c,String key,String value){JSObject o=new JSObject();o.put("ok",true);o.put(key,value);c.resolve(o);}
  private void bad(PluginCall c,String op){reject(c,op,new Failure("INVALID_ARGUMENT",false));}
  private static void fail(String r,boolean retry){throw new Failure(r,retry);}
  private void reject(PluginCall c,String op,Throwable t){String r=reason(t);JSObject d=new JSObject();d.put("contractVersion",1);d.put("reason",r);d.put("operation",op);d.put("retryable",t instanceof Failure&&((Failure)t).retryable);c.reject("ETP storage request failed.",r,d);}
  private String reason(Throwable t){if(t instanceof Failure)return((Failure)t).reason;if(t instanceof AEADBadTagException)return"INTEGRITY_FAILED";if(t instanceof KeyPermanentlyInvalidatedException)return"KEY_UNAVAILABLE";if(t instanceof SQLiteDatabaseCorruptException)return"INTEGRITY_FAILED";if(t instanceof SQLiteFullException)return"NO_SPACE";if(t instanceof SQLiteCantOpenDatabaseException)return"DB_OPEN_FAILED";if(t instanceof SQLiteReadOnlyDatabaseException)return"DB_READ_ONLY";if(t instanceof SQLiteException)return"DB_IO_FAILED";return"ETP_STORE_UNAVAILABLE";}
  static class Failure extends RuntimeException{final String reason;final boolean retryable;Failure(String r,boolean x){reason=r;retryable=x;}}
  static class EtpDb extends SQLiteOpenHelper{static final String NAME="saagar-etp.db";EtpDb(Context c){super(c,NAME,null,1);}@Override public void onConfigure(SQLiteDatabase db){db.setForeignKeyConstraintsEnabled(true);db.rawQuery("PRAGMA secure_delete=ON",null).close();}@Override public void onCreate(SQLiteDatabase db){db.execSQL("CREATE TABLE generation(scope_key TEXT NOT NULL,generation_id TEXT NOT NULL,state TEXT NOT NULL,manifest TEXT,manifest_digest TEXT,row_count INTEGER,chunk_count INTEGER,seal_envelope TEXT,created_at INTEGER NOT NULL,sealed_at INTEGER,published_at INTEGER,PRIMARY KEY(scope_key,generation_id))");db.execSQL("CREATE TABLE stage_chunk(scope_key TEXT NOT NULL,generation_id TEXT NOT NULL,report_id TEXT NOT NULL,chunk_index INTEGER NOT NULL,row_count INTEGER NOT NULL,digest TEXT NOT NULL,payload_envelope TEXT NOT NULL,PRIMARY KEY(scope_key,generation_id,report_id,chunk_index),FOREIGN KEY(scope_key,generation_id) REFERENCES generation(scope_key,generation_id) ON DELETE CASCADE)");db.execSQL("CREATE TABLE scope_pointer(scope_key TEXT PRIMARY KEY,active_generation_id TEXT,restore_fence INTEGER NOT NULL DEFAULT 0)");}@Override public void onUpgrade(SQLiteDatabase db,int o,int n){fail("SCHEMA_UNSUPPORTED",false);}}
}
