package org.example.runtimeprobe;

import android.app.Activity;
import android.os.Bundle;
import android.os.SystemClock;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.text.TextUtils;
import android.util.Log;
import android.widget.Toast;

public final class MainActivity extends Activity {
    public int lifecycleState;
    public int createCount;
    public int startCount;
    public int resumeCount;
    public int pauseCount;
    public int stopCount;
    public int destroyCount;
    public String builtText;
    public String observedPackage;
    public String observedLocalClass;
    public String observedTitle;
    public String bundleText;
    public int bundleNumber;
    public boolean bundleFlag;
    public String intentAction;
    public int intentNumber;
    public int colorValue;
    public int textChecks;
    public int logScore;
    public Context applicationContext;
    public long wideValue;
    public long instanceWide;
    public static long staticWide;
    public double doubleValue;
    public long clockValue;
    public long bundleLong;
    public int wideChecks;
    public long constant32;

    private static long addWide(long left, long right) { return left + right; }
    private static long rangeWide(int a, long b, int c, long d, int e) { return a + b + c + d + e; }
    private static double addDouble(double left, double right) { return left + right; }
    private static long divideAndRemainder(long left, long right) { return left / right + left % right; }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setTitle("RuntimeProbe DEX");
        StringBuilder builder = new StringBuilder("value=");
        builder.append(41).append(true).append('!');
        builtText = builder.toString().trim().concat("");
        observedPackage = getPackageName();
        observedLocalClass = getLocalClassName();
        observedTitle = getTitle().toString();
        applicationContext = getApplicationContext();
        textChecks = TextUtils.isEmpty(builtText) ? 0 : TextUtils.getTrimmedLength("  x  ");

        Bundle bundle = new Bundle(4);
        bundle.putString("text", null);
        bundle.putString("text", "bundle");
        bundle.putInt("number", 7);
        bundle.putBoolean("flag", true);
        bundleText = bundle.getString("text", "missing");
        bundleNumber = bundle.getInt("number", -1);
        bundleFlag = bundle.getBoolean("flag", false);
        long seed = 0x1122334455667788L;
        constant32 = 123456789L;
        long moved = seed;
        instanceWide = moved;
        staticWide = addWide(instanceWide, 5L);
        long[] longs = new long[2];
        longs[0] = staticWide;
        longs[1] = divideAndRemainder(Long.MIN_VALUE, -1L);
        double[] doubles = new double[3];
        doubles[0] = addDouble(1.5d, 2.25d);
        doubles[1] = 0.0d / 0.0d;
        doubles[2] = -0.0d;
        wideValue = rangeWide(1, longs[0], 2, (longs[1] >>> 63) + (longs[0] << 3), 3);
        doubleValue = doubles[0] % 2.0d;
        wideChecks = (doubles[1] != doubles[1] ? 1 : 0) + (longs[1] == Long.MIN_VALUE ? 2 : 0);
        bundle.putLong("wide", wideValue);
        bundleLong = bundle.getLong("wide", -1L) + bundle.getLong("missing", 7_000_000_000L);
        clockValue = SystemClock.uptimeMillis() + SystemClock.elapsedRealtime() + SystemClock.elapsedRealtimeNanos();

        Intent intent = getIntent();
        intent.putExtra("number", 9).putExtra("text", "intent");
        intentAction = intent.getAction();
        intentNumber = intent.getIntExtra("number", -1);
        colorValue = Color.argb(300, -1, 128, 7);

        logScore = Log.v("RuntimeProbe", "verbose")
                + Log.d("RuntimeProbe", "debug")
                + Log.i("RuntimeProbe", builtText)
                + Log.w("RuntimeProbe", "warning")
                + Log.e("RuntimeProbe", "error")
                + Log.wtf("RuntimeProbe", "assert");
        Toast.makeText(this, builtText, Toast.LENGTH_LONG).show();
        createCount++;
        lifecycleState = 1;
    }

    @Override
    protected void onStart() {
        super.onStart();
        startCount++;
        lifecycleState = lifecycleState * 10 + 2;
    }

    @Override
    protected void onResume() {
        super.onResume();
        resumeCount++;
        lifecycleState = lifecycleState * 10 + 3;
    }

    @Override
    protected void onPause() {
        super.onPause();
        pauseCount++;
        lifecycleState = lifecycleState * 10 + 4;
    }

    @Override
    protected void onStop() {
        super.onStop();
        stopCount++;
        lifecycleState = lifecycleState * 10 + 5;
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        destroyCount++;
        lifecycleState = lifecycleState * 10 + 6;
    }
}
