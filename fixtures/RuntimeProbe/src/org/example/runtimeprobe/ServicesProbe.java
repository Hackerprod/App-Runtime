package org.example.runtimeprobe;

import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;

public final class ServicesProbe {
    public static int deniedClipboard(Context context) {
        try { context.getSystemService(Context.CLIPBOARD_SERVICE); return -1; }
        catch (SecurityException expected) { return 1; }
    }
    public static int deniedConnectivity(Context context) {
        try { context.getSystemService(Context.CONNECTIVITY_SERVICE); return -1; }
        catch (SecurityException expected) { return 2; }
    }
    public static int deniedWrite(Context context) {
        try { ClipboardManager manager = (ClipboardManager)context.getSystemService(Context.CLIPBOARD_SERVICE); manager.setPrimaryClip(ClipData.newPlainText("label", "secret")); return -1; }
        catch (SecurityException expected) { return 21; }
    }
    public static int deniedRead(Context context) {
        try { ClipboardManager manager = (ClipboardManager)context.getSystemService(Context.CLIPBOARD_SERVICE); manager.hasPrimaryClip(); return -1; }
        catch (SecurityException expected) { return 22; }
    }
    public static int unknown(Context context) { return context.getSystemService("unknown-service") == null ? 3 : -1; }
    public static int clipboardUnavailable(Context context) { return context.getSystemService(Context.CLIPBOARD_SERVICE) == null ? 20 : -1; }
    public static int clipQuota() { ClipData.newPlainText("a", "a"); ClipData.newPlainText("b", "b"); return -1; }
    public static int hasClipboard(Context context) { ClipboardManager manager = (ClipboardManager)context.getSystemService(Context.CLIPBOARD_SERVICE); return manager.hasPrimaryClip() ? 1 : 0; }
    public static int clipboard(Context context) {
        ClipboardManager first = (ClipboardManager)context.getSystemService(Context.CLIPBOARD_SERVICE);
        ClipboardManager second = (ClipboardManager)context.getSystemService(Context.CLIPBOARD_SERVICE);
        if (first == null || first != second) return -1;
        ClipData clip = ClipData.newPlainText("guest-label", "guest-text");
        first.setPrimaryClip(clip);
        if (!first.hasPrimaryClip()) return -2;
        ClipData read = first.getPrimaryClip();
        if (read == null || read.getItemCount() != 1) return -3;
        if (!"guest-text".equals(read.getItemAt(0).coerceToText(context).toString())) return -4;
        first.clearPrimaryClip();
        return first.hasPrimaryClip() ? -5 : 4;
    }
    public static int connectivity(Context context) {
        ConnectivityManager manager = (ConnectivityManager)context.getSystemService(Context.CONNECTIVITY_SERVICE);
        Network network = manager.getActiveNetwork();
        if (network == null) return 5;
        NetworkCapabilities caps = manager.getNetworkCapabilities(network);
        if (caps == null) return -1;
        int score = caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) ? 1 : 0;
        score += caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED) ? 2 : 0;
        score += caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) ? 4 : 0;
        score += caps.hasCapability(9999) ? 100 : 0;
        score += caps.hasTransport(9999) ? 100 : 0;
        score += manager.isActiveNetworkMetered() ? 0 : 8;
        return score;
    }
    public static int stale(Context context) {
        ConnectivityManager manager = (ConnectivityManager)context.getSystemService(Context.CONNECTIVITY_SERVICE);
        Network network = manager.getActiveNetwork();
        return manager.getNetworkCapabilities(network) == null ? 6 : -1;
    }
}
