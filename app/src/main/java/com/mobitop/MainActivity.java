package com.mobitop;

import android.app.Activity;
import android.app.ActivityOptions;
import android.content.Intent;
import android.hardware.display.DisplayManager;
import android.os.Bundle;
import android.provider.Settings;
import android.view.Display;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

/** Phone-side screen: pick a connected monitor and start the desktop on it. */
public class MainActivity extends Activity implements DisplayManager.DisplayListener {
    private DisplayManager dm;
    private LinearLayout list;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        // Opened on a second screen (e.g. the laptop via scrcpy): go straight to the desktop
        int here = getWindowManager().getDefaultDisplay().getDisplayId();
        if (here != Display.DEFAULT_DISPLAY) { startDesktop(here); finish(); return; }
        dm = getSystemService(DisplayManager.class);
        list = new LinearLayout(this);
        list.setOrientation(LinearLayout.VERTICAL);
        int p = dp(16);
        list.setPadding(p, p, p, p);
        ScrollView sv = new ScrollView(this);
        sv.addView(list);
        setContentView(sv);
        refresh();
    }

    @Override protected void onResume() { super.onResume(); dm.registerDisplayListener(this, null); refresh(); }
    @Override protected void onPause() { super.onPause(); dm.unregisterDisplayListener(this); }
    @Override public void onDisplayAdded(int id) { refresh(); }
    @Override public void onDisplayRemoved(int id) { refresh(); }
    @Override public void onDisplayChanged(int id) { }

    private void refresh() {
        list.removeAllViews();
        TextView title = new TextView(this);
        title.setText("Mobi-Top");
        title.setTextSize(28);
        list.addView(title);

        boolean any = false;
        for (Display d : dm.getDisplays()) {
            if (d.getDisplayId() == Display.DEFAULT_DISPLAY) continue;
            any = true;
            Button btn = new Button(this);
            btn.setText("Start desktop on " + d.getName());
            int id = d.getDisplayId();
            btn.setOnClickListener(v -> startDesktop(id));
            list.addView(btn);
        }
        if (!any) list.addView(text("No external display found. Connect a monitor (USB-C to HDMI) or use "
                + "Developer options → Simulate secondary displays to try it."));

        Button here = new Button(this);
        here.setText("Preview desktop on this phone");
        here.setOnClickListener(v -> startDesktop(Display.DEFAULT_DISPLAY));
        list.addView(here);

        list.addView(text("\nOne-time setup so apps open in movable windows:\n"
                + "1. Settings → About phone → tap Build number 7 times.\n"
                + "2. Developer options → turn on \"Enable freeform windows\" and "
                + "\"Force desktop mode\" (or \"Enable desktop mode on secondary displays\").\n"
                + "3. Also turn on \"Force activities to be resizable\".\n"
                + "4. Restart the phone."));
        Button dev = new Button(this);
        dev.setText("Open Developer options");
        dev.setOnClickListener(v -> {
            try { startActivity(new Intent(Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS)); }
            catch (Exception e) { startActivity(new Intent(Settings.ACTION_DEVICE_INFO_SETTINGS)); }
        });
        list.addView(dev);
    }

    private void startDesktop(int displayId) {
        Intent i = new Intent(this, DesktopActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK); // reuse the open desktop instead of stacking copies
        ActivityOptions o = ActivityOptions.makeBasic().setLaunchDisplayId(displayId);
        // In desktop mode the desktop itself would open as a small window; make it cover the screen
        Display d = getSystemService(DisplayManager.class).getDisplay(displayId);
        if (d != null && displayId != Display.DEFAULT_DISPLAY) {
            android.graphics.Point s = new android.graphics.Point();
            d.getRealSize(s);
            o.setLaunchBounds(new android.graphics.Rect(0, 0, s.x, s.y));
        }
        startActivity(i, o.toBundle());
    }

    private TextView text(String s) {
        TextView t = new TextView(this);
        t.setText(s);
        t.setTextSize(15);
        return t;
    }

    private int dp(int v) { return Math.round(v * getResources().getDisplayMetrics().density); }
}
