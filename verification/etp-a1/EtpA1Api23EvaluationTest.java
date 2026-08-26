package com.saagartraders.bcc;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;
import androidx.test.ext.junit.rules.ActivityScenarioRule;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import java.io.InputStream;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;
import org.json.JSONObject;
import org.junit.Assume;
import org.junit.Rule;
import org.junit.Test;
import org.junit.runner.RunWith;
@RunWith(AndroidJUnit4.class)
public final class EtpA1Api23EvaluationTest {
  @Rule public ActivityScenarioRule<MainActivity> activityRule = new ActivityScenarioRule<>(MainActivity.class);
  private String evaluate(String source) throws Exception {
    CountDownLatch latch=new CountDownLatch(1); AtomicReference<String> result=new AtomicReference<>();
    activityRule.getScenario().onActivity(activity -> activity.getBridge().getWebView().evaluateJavascript(source,value -> {result.set(value);latch.countDown();}));
    assertTrue("ETP-A1 WebView evaluation timed out",latch.await(35,TimeUnit.SECONDS)); return result.get();
  }
  @Test public void candidateParsesBoundedSyntheticWorkbookOnApi23() throws Exception {
    AtomicReference<Boolean> staged=new AtomicReference<>(false);
    activityRule.getScenario().onActivity(activity -> {try(InputStream ignored=activity.getAssets().open("public/__etp_eval/index.html")){staged.set(true);}catch(Exception ignored){staged.set(false);}if(staged.get())activity.getBridge().getWebView().loadUrl("https://localhost/__etp_eval/index.html");});
    Assume.assumeTrue("ETP-A1 generated assets are not staged",staged.get()); String raw="null";
    for(int attempt=0;attempt<120&&"null".equals(raw);attempt++){Thread.sleep(250);raw=evaluate("window.__etpA1&&window.__etpA1.state==='done'?JSON.stringify(window.__etpA1):null");}
    assertTrue("ETP-A1 result missing",!"null".equals(raw)); Object decoded=new org.json.JSONTokener(raw).nextValue(); JSONObject result=decoded instanceof JSONObject?(JSONObject)decoded:new JSONObject(String.valueOf(decoded));
    assertEquals("XLSX_EVAL_OK",result.getString("code")); assertEquals(5000,result.getInt("rows")); assertEquals(4,result.getInt("columns")); assertTrue(result.getBoolean("leadingZeroPreserved")); assertTrue(result.getInt("elapsedMs")<=30000); assertTrue(result.getInt("maxHeartbeatGapMs")<=250);
    System.out.println("ETP_A1_API23_EVIDENCE "+result);
  }
}
