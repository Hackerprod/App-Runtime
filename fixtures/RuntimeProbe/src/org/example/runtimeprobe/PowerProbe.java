package org.example.runtimeprobe;

import android.content.Context;
import android.os.BatteryManager;
import android.os.PowerManager;

public final class PowerProbe {
    private PowerProbe() {}

    public static long sample(Context context) {
        BatteryManager battery = (BatteryManager) context.getSystemService("batterymanager");
        PowerManager power = (PowerManager) context.getSystemService("power");
        int capacity = battery.getIntProperty(BatteryManager.BATTERY_PROPERTY_CAPACITY);
        long energy = battery.getLongProperty(BatteryManager.BATTERY_PROPERTY_ENERGY_COUNTER);
        int flags = (battery.isCharging() ? 1 : 0) | (power.isPowerSaveMode() ? 2 : 0);
        return energy + capacity + flags;
    }
}
