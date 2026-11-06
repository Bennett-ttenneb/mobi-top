package com.mobitop;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/** The laptop script tells us which empty screen to open the next app on; each one becomes its own laptop window. */
public class SpareReceiver extends BroadcastReceiver {
    static volatile int display = -1;

    @Override
    public void onReceive(Context c, Intent i) { display = i.getIntExtra("display", -1); }
}
