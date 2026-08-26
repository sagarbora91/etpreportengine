package com.saagartraders.bcc;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotEquals;
import static org.junit.Assert.assertTrue;

import android.content.Context;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;
import android.webkit.WebView;
import androidx.test.ext.junit.rules.ActivityScenarioRule;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;
import org.json.JSONObject;
import org.junit.Rule;
import org.junit.Test;
import org.junit.runner.RunWith;

/** Canonical emulator/runtime evidence only. This does not constitute physical-device acceptance. */
@RunWith(AndroidJUnit4.class)
public final class EtpNativeApi23EvidenceTest {
  private static final String SCOPE="WLMHW|2025-26|2026-04-01..2026-04-30";
  private static final String GEN="api23.native.001";
  private static final String CANARY="ETP_PLAINTEXT_CANARY_8f0c37d1";
  private static final String ALIAS="saagar_etp_fact_aes256_v1";
  @Rule public ActivityScenarioRule<MainActivity> activityRule=new ActivityScenarioRule<>(MainActivity.class);

  private String evaluate(String source)throws Exception{CountDownLatch latch=new CountDownLatch(1);AtomicReference<String> result=new AtomicReference<>();activityRule.getScenario().onActivity(activity->{WebView webView=activity.getBridge().getWebView();webView.evaluateJavascript(source,value->{result.set(value);latch.countDown();});});assertTrue("ETP native WebView evaluation timed out",latch.await(20,TimeUnit.SECONDS));return result.get();}
  private void awaitPlugin()throws Exception{String ready="false";for(int i=0;i<40&&!"true".equals(ready);i++){ready=evaluate("!!(window.Capacitor&&Capacitor.Plugins&&Capacitor.Plugins.SaagarEtpStore)");if(!"true".equals(ready))Thread.sleep(250);}assertEquals("true",ready);}
  private JSONObject awaitResult(String key)throws Exception{String raw="null";for(int i=0;i<100&&"null".equals(raw);i++){Thread.sleep(100);raw=evaluate("window['"+key+"']?JSON.stringify(window['"+key+"']):null");}assertNotEquals("native evidence result missing", "null",raw);Object decoded=new org.json.JSONTokener(raw).nextValue();return decoded instanceof JSONObject?(JSONObject)decoded:new JSONObject(String.valueOf(decoded));}
  private void start(String key,String promiseExpression)throws Exception{evaluate("window['"+key+"']=null;Promise.resolve().then(function(){return "+promiseExpression+";}).then(function(x){window['"+key+"']=x;},function(e){window['"+key+"']={ok:false,code:String(e&&e.code||''),message:String(e&&e.message||'')};})");}
  private String q(String value){return JSONObject.quote(value);}
  private String base(){return "var p=Capacitor.Plugins.SaagarEtpStore,cv=1,scope="+q(SCOPE)+",gen="+q(GEN)+";";}
  private String manifest(){return "{scopeKey:scope,generationId:gen,reports:[{reportId:'R003',sourceSha256:'1111111111111111111111111111111111111111111111111111111111111111',headerSignatureSha256:'5555555555555555555555555555555555555555555555555555555555555555',rowCount:2},{reportId:'R013',sourceSha256:'2222222222222222222222222222222222222222222222222222222222222222',headerSignatureSha256:'6666666666666666666666666666666666666666666666666666666666666666',rowCount:0},{reportId:'R022',sourceSha256:'3333333333333333333333333333333333333333333333333333333333333333',headerSignatureSha256:'7777777777777777777777777777777777777777777777777777777777777777',rowCount:0},{reportId:'R025',sourceSha256:'4444444444444444444444444444444444444444444444444444444444444444',headerSignatureSha256:'8888888888888888888888888888888888888888888888888888888888888888',rowCount:0}]};";}
  private SQLiteDatabase database(){Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();return SQLiteDatabase.openDatabase(context.getDatabasePath("saagar-etp.db").getPath(),null,SQLiteDatabase.OPEN_READWRITE);}
  private boolean fileContains(File file,String needle)throws Exception{if(!file.exists())return false;byte[] target=needle.getBytes(StandardCharsets.UTF_8);try(FileInputStream in=new FileInputStream(file);ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] buffer=new byte[8192];for(int n;(n=in.read(buffer))!=-1;)out.write(buffer,0,n);byte[] data=out.toByteArray();outer:for(int i=0;i<=data.length-target.length;i++){for(int j=0;j<target.length;j++)if(data[i+j]!=target[j])continue outer;return true;}return false;}}

  @Test public void keystoreRoundTripUniqueIvTamperAndResetAreFailClosed()throws Exception{
    awaitPlugin();start("__etpSetup","(function(){"+base()+"var m="+manifest()+"return p.resetStore({contractVersion:cv}).then(function(){return p.beginStage({contractVersion:cv,scopeKey:scope,generationId:gen});}).then(function(){return p.appendStageChunk({contractVersion:cv,scopeKey:scope,generationId:gen,reportId:'R003',chunkIndex:0,rows:[{transaction_id:"+q(CANARY)+",amount:1}]});}).then(function(){return p.appendStageChunk({contractVersion:cv,scopeKey:scope,generationId:gen,reportId:'R003',chunkIndex:1,rows:[{transaction_id:'SECOND',amount:2}]});}).then(function(){return p.finishStage({contractVersion:cv,scopeKey:scope,generationId:gen,manifest:m});}).then(function(){return p.publishStage({contractVersion:cv,scopeKey:scope,generationId:gen,manifest:m});}).then(function(){return p.readFacts({contractVersion:cv,scopeKey:scope,generationId:gen,reportId:'R003',fields:['transaction_id','amount'],cursorChunkIndex:0,cursorRowOffset:0,limit:2});}).then(function(page){return {ok:true,rowCount:page.rows.length};});})()");
    JSONObject setup=awaitResult("__etpSetup");assertTrue(setup.toString(),setup.getBoolean("ok"));assertEquals(2,setup.getInt("rowCount"));
    Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();File dbFile=context.getDatabasePath("saagar-etp.db");List<String> envelopes=new ArrayList<>();try(SQLiteDatabase db=database();Cursor c=db.rawQuery("SELECT payload_envelope FROM stage_chunk WHERE scope_key=? AND generation_id=? AND report_id='R003' ORDER BY chunk_index",new String[]{SCOPE,GEN})){while(c.moveToNext())envelopes.add(c.getString(0));}assertEquals(2,envelopes.size());String iv0=envelopes.get(0).split("\\.",-1)[1],iv1=envelopes.get(1).split("\\.",-1)[1];assertNotEquals("AES-GCM IVs must be unique",iv0,iv1);
    for(String suffix:new String[]{"","-wal","-shm","-journal"})assertFalse("plaintext canary leaked to "+suffix,fileContains(new File(dbFile.getPath()+suffix),CANARY));
    try(SQLiteDatabase db=database()){db.execSQL("UPDATE stage_chunk SET payload_envelope=substr(payload_envelope,1,length(payload_envelope)-1)||CASE substr(payload_envelope,-1) WHEN 'A' THEN 'B' ELSE 'A' END WHERE scope_key=? AND generation_id=? AND report_id='R003' AND chunk_index=1",new Object[]{SCOPE,GEN});}
    start("__etpTamper","(function(){"+base()+"return p.readFacts({contractVersion:cv,scopeKey:scope,generationId:gen,reportId:'R003',fields:['transaction_id'],cursorChunkIndex:1,cursorRowOffset:0,limit:1}).then(function(){return {ok:true};});})()");JSONObject tamper=awaitResult("__etpTamper");assertFalse(tamper.toString(),tamper.getBoolean("ok"));assertEquals("INTEGRITY_FAILED",tamper.getString("code"));
    start("__etpReset","(function(){"+base()+"return p.resetStore({contractVersion:cv}).then(function(x){return {ok:x.ok===true,state:x.state};});})()");JSONObject reset=awaitResult("__etpReset");assertTrue(reset.toString(),reset.getBoolean("ok"));KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);assertFalse("ETP alias survived reset",store.containsAlias(ALIAS));assertFalse("ETP database survived reset",dbFile.exists());
    System.out.println("ETP_NATIVE_API23_EVIDENCE roundtrip=true uniqueIv=true nonFirstChunkTamper=INTEGRITY_FAILED plaintextCanaryAbsent=true aliasReset=true sdk="+android.os.Build.VERSION.SDK_INT);
  }

  @Test public void incompleteStageSurvivesActivityRecreationWithoutPublication()throws Exception{
    awaitPlugin();String stageGen="api23.stage.recovery";start("__etpStage","(function(){var p=Capacitor.Plugins.SaagarEtpStore,cv=1,scope="+q(SCOPE)+",gen="+q(stageGen)+";return p.resetStore({contractVersion:cv}).then(function(){return p.beginStage({contractVersion:cv,scopeKey:scope,generationId:gen});}).then(function(){return p.appendStageChunk({contractVersion:cv,scopeKey:scope,generationId:gen,reportId:'R003',chunkIndex:0,rows:[{transaction_id:'STAGED_ONLY',amount:7}]});}).then(function(){return {ok:true};});})()");assertTrue(awaitResult("__etpStage").getBoolean("ok"));activityRule.getScenario().recreate();awaitPlugin();start("__etpRecovery","Capacitor.Plugins.SaagarEtpStore.readStatus({contractVersion:1,scopeKey:"+q(SCOPE)+"})");JSONObject status=awaitResult("__etpRecovery");assertTrue(status.toString(),status.getBoolean("ok"));assertEquals("STAGING",status.getString("state"));assertTrue(status.isNull("activeGenerationId"));System.out.println("ETP_NATIVE_STAGE_RECOVERY_EVIDENCE state=STAGING activeGeneration=null sdk="+android.os.Build.VERSION.SDK_INT);
  }
}
