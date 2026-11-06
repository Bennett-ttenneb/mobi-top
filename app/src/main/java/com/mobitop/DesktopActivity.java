package com.mobitop;

import android.Manifest;
import android.app.Activity;
import android.app.ActivityOptions;
import android.app.DownloadManager;
import android.app.Notification;
import android.app.PendingIntent;
import android.content.ActivityNotFoundException;
import android.content.BroadcastReceiver;
import android.content.ComponentName;
import android.content.ContentUris;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import android.database.ContentObserver;
import android.database.Cursor;
import android.graphics.Bitmap;
import android.graphics.ImageDecoder;
import android.graphics.Point;
import android.graphics.Rect;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.media.MediaMetadata;
import android.media.session.MediaController;
import android.media.session.MediaSessionManager;
import android.media.session.PlaybackState;
import android.net.Uri;
import android.os.BatteryManager;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.os.Handler;
import android.os.Looper;
import android.os.StatFs;
import android.provider.MediaStore;
import android.provider.Settings;
import android.service.notification.StatusBarNotification;
import android.text.Editable;
import android.text.TextUtils;
import android.text.TextWatcher;
import android.view.Display;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.inputmethod.EditorInfo;
import android.widget.BaseAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.GridView;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.PopupMenu;
import android.widget.ScrollView;
import android.widget.TextClock;
import android.widget.TextView;
import android.widget.Toast;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/** The desktop shown on the monitor: wallpaper, widgets, taskbar with pinned apps, start menu. */
public class DesktopActivity extends Activity {
    static class App {
        ComponentName cn; String label; Drawable icon;
    }

    private static final int PICK_WALLPAPER = 1;
    private static final int WHITE = 0xFFFFFFFF, GREY = 0xFF9AA0A6;

    private final List<App> apps = new ArrayList<>();
    private final List<App> shown = new ArrayList<>();
    private final Handler main = new Handler(Looper.getMainLooper());
    private SharedPreferences prefs;
    private ImageView wallpaper;
    private LinearLayout pinBar, menu, webRow, notifPanel, notifList, files, media;
    private TextView bell, mediaTitle, system;
    private Button playPause;
    private EditText search;
    private BaseAdapter adapter;
    private MediaController controller;
    private MediaSessionManager sessions;
    private int cascade;

    private final BroadcastReceiver battery = new BroadcastReceiver() {
        @Override public void onReceive(Context c, Intent i) { updateSystem(i); }
    };
    private final ContentObserver downloads = new ContentObserver(main) {
        @Override public void onChange(boolean self) { loadFiles(); }
    };
    private final MediaController.Callback mediaCallback = new MediaController.Callback() {
        @Override public void onPlaybackStateChanged(PlaybackState s) { updateMedia(); }
        @Override public void onMetadataChanged(MediaMetadata m) { updateMedia(); }
        @Override public void onSessionDestroyed() { setController(null); }
    };
    private final MediaSessionManager.OnActiveSessionsChangedListener sessionsChanged = this::pickController;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        prefs = getSharedPreferences("desk", MODE_PRIVATE);

        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(0xFF0F2027);
        wallpaper = new ImageView(this);
        wallpaper.setScaleType(ImageView.ScaleType.CENTER_CROP);
        root.addView(wallpaper, new FrameLayout.LayoutParams(-1, -1));
        root.setOnClickListener(v -> closePanels());
        root.setOnLongClickListener(v -> { wallpaperMenu(v); return true; });

        ScrollView widgets = new ScrollView(this); // at high zoom the widgets are taller than the screen
        widgets.setVerticalScrollBarEnabled(false);
        widgets.addView(buildWidgets());
        root.addView(widgets, widgetParams());
        root.addView(buildTaskbar(), new FrameLayout.LayoutParams(-1, dp(48), Gravity.BOTTOM));
        root.addView(buildMenu(), panelParams(480, 560, Gravity.START));
        root.addView(buildNotifPanel(), panelParams(380, 520, Gravity.END));
        setContentView(root);

        applyWallpaper();
        registerReceiver(battery, new IntentFilter(Intent.ACTION_BATTERY_CHANGED));
        getContentResolver().registerContentObserver(MediaStore.Downloads.EXTERNAL_CONTENT_URI, true, downloads);
        sessions = getSystemService(MediaSessionManager.class);
        NotifService.onChange = this::notificationsChanged;
        // After an update or force-stop Android leaves the listener unbound until asked
        if (NotifService.instance == null && hasNotifAccess()) {
            android.service.notification.NotificationListenerService.requestRebind(new ComponentName(this, NotifService.class));
        }
        notificationsChanged();
    }

    @Override
    protected void onResume() {
        super.onResume();
        loadApps(); // picks up apps installed while we were away
        loadFiles(); // and permission granted in Settings
    }

    @Override
    public void onWindowFocusChanged(boolean focused) {
        // Permissions are granted in a Settings window on top, which doesn't pause us; re-check on return
        if (focused) { loadFiles(); notificationsChanged(); }
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        unregisterReceiver(battery);
        getContentResolver().unregisterContentObserver(downloads);
        if (NotifService.onChange != null) NotifService.onChange = null;
        try { sessions.removeOnActiveSessionsChangedListener(sessionsChanged); } catch (Exception ignored) { }
        setController(null);
    }

    @Override
    public void onBackPressed() {
        if (menu.getVisibility() == View.VISIBLE || notifPanel.getVisibility() == View.VISIBLE) closePanels();
        else super.onBackPressed();
    }

    @Override
    public boolean onKeyDown(int code, KeyEvent e) {
        if (code == KeyEvent.KEYCODE_F7) { toggleMenu(); return true; }
        return super.onKeyDown(code, e);
    }

    // ---------- Layout ----------

    private View buildTaskbar() {
        LinearLayout bar = new LinearLayout(this);
        bar.setBackgroundColor(0xE0101418);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        Button start = new Button(this);
        start.setText("⊞");
        start.setTextSize(20);
        start.setContentDescription("Start menu (F7)");
        start.setOnClickListener(v -> toggleMenu());
        pinBar = new LinearLayout(this);
        pinBar.setGravity(Gravity.CENTER_VERTICAL);

        media = new LinearLayout(this);
        media.setGravity(Gravity.CENTER_VERTICAL);
        media.setVisibility(View.GONE);
        mediaTitle = text("", 13, WHITE);
        mediaTitle.setMaxWidth(dp(220));
        mediaTitle.setSingleLine(true);
        mediaTitle.setEllipsize(TextUtils.TruncateAt.END);
        media.addView(mediaTitle);
        media.addView(barButton("⏮", "Previous", v -> { if (controller != null) controller.getTransportControls().skipToPrevious(); }));
        playPause = barButton("⏯", "Play or pause", v -> togglePlay());
        media.addView(playPause);
        media.addView(barButton("⏭", "Next", v -> { if (controller != null) controller.getTransportControls().skipToNext(); }));

        bell = text("🔔", 15, WHITE);
        bell.setPadding(dp(12), 0, dp(12), 0);
        bell.setGravity(Gravity.CENTER);
        bell.setContentDescription("Notifications");
        bell.setOnClickListener(v -> toggleNotifs());

        TextClock clock = new TextClock(this);
        clock.setTextColor(WHITE);
        clock.setPadding(dp(12), 0, dp(12), 0);
        bar.addView(start, new LinearLayout.LayoutParams(dp(56), -1));
        bar.addView(pinBar, new LinearLayout.LayoutParams(0, -1, 1));
        bar.addView(media);
        bar.addView(bell, new LinearLayout.LayoutParams(-2, -1));
        bar.addView(clock);
        return bar;
    }

    private View buildMenu() {
        menu = panel();
        search = new EditText(this);
        search.setHint("Search apps or the web");
        search.setSingleLine(true);
        search.setImeOptions(EditorInfo.IME_ACTION_GO);
        search.setTextColor(WHITE);
        search.setHintTextColor(GREY);
        search.addTextChangedListener(new TextWatcher() {
            public void beforeTextChanged(CharSequence s, int a, int c, int d) { }
            public void onTextChanged(CharSequence s, int a, int c, int d) { filter(s.toString()); }
            public void afterTextChanged(Editable e) { }
        });
        // Enter opens the top app, or searches the web when no app matches
        search.setOnEditorActionListener((v, id, e) -> {
            if (e != null && e.getAction() != KeyEvent.ACTION_DOWN) return true;
            if (!shown.isEmpty()) launch(shown.get(0));
            else webSearch("https://www.google.com/search?q=");
            return true;
        });
        webRow = new LinearLayout(this);
        webRow.setVisibility(View.GONE);
        webRow.addView(webButton("Google", "https://www.google.com/search?q="));
        webRow.addView(webButton("YouTube", "https://www.youtube.com/results?search_query="));
        webRow.addView(webButton("Perplexity", "https://www.perplexity.ai/search?q="));

        GridView grid = new GridView(this);
        grid.setNumColumns(GridView.AUTO_FIT);
        grid.setColumnWidth(dp(88));
        grid.setVerticalSpacing(dp(8));
        adapter = new AppAdapter();
        grid.setAdapter(adapter);
        grid.setOnItemClickListener((p, v, pos, id) -> launch(shown.get(pos)));
        grid.setOnItemLongClickListener((p, v, pos, id) -> { togglePin(shown.get(pos)); return true; });
        menu.addView(search);
        menu.addView(webRow);
        menu.addView(grid, new LinearLayout.LayoutParams(-1, 0, 1));
        return menu;
    }

    private View buildNotifPanel() {
        notifPanel = panel();
        notifPanel.addView(text("Notifications", 16, WHITE));
        notifList = new LinearLayout(this);
        notifList.setOrientation(LinearLayout.VERTICAL);
        ScrollView sv = new ScrollView(this);
        sv.addView(notifList);
        notifPanel.addView(sv, new LinearLayout.LayoutParams(-1, 0, 1));
        return notifPanel;
    }

    private View buildWidgets() {
        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);

        LinearLayout clockCard = card(col, null);
        TextClock time = new TextClock(this);
        time.setFormat12Hour("h:mm");
        time.setFormat24Hour("HH:mm");
        time.setTextSize(44);
        time.setTextColor(WHITE);
        TextClock date = new TextClock(this);
        date.setFormat12Hour("EEEE, d MMMM");
        date.setFormat24Hour("EEEE, d MMMM");
        date.setTextColor(GREY);
        clockCard.addView(time);
        clockCard.addView(date);

        system = text("", 13, WHITE);
        card(col, "Phone").addView(system);

        EditText notes = new EditText(this);
        notes.setHint("Type a note or to-do…");
        notes.setHintTextColor(GREY);
        notes.setTextColor(WHITE);
        notes.setTextSize(14);
        notes.setGravity(Gravity.TOP);
        notes.setMinLines(3);
        notes.setMaxLines(6);
        notes.setBackground(null);
        notes.setText(prefs.getString("notes", ""));
        notes.addTextChangedListener(new TextWatcher() {
            public void beforeTextChanged(CharSequence s, int a, int c, int d) { }
            public void onTextChanged(CharSequence s, int a, int c, int d) { }
            public void afterTextChanged(Editable e) { prefs.edit().putString("notes", e.toString()).apply(); }
        });
        card(col, "Notes").addView(notes);

        files = card(col, "Downloads");
        files.getChildAt(0).setOnClickListener(v -> launchIntent(new Intent(DownloadManager.ACTION_VIEW_DOWNLOADS)));
        return col;
    }

    private FrameLayout.LayoutParams widgetParams() {
        FrameLayout.LayoutParams p = new FrameLayout.LayoutParams(dp(300), -2, Gravity.TOP | Gravity.END);
        p.setMargins(0, dp(24), dp(24), dp(60));
        return p;
    }

    private FrameLayout.LayoutParams panelParams(int w, int h, int side) {
        // At high zoom a fixed-height panel can be taller than the screen; keep its top on screen
        int room = getResources().getDisplayMetrics().heightPixels - dp(48 + 16);
        FrameLayout.LayoutParams p = new FrameLayout.LayoutParams(dp(w), Math.min(dp(h), room), Gravity.BOTTOM | side);
        p.bottomMargin = dp(48);
        return p;
    }

    private LinearLayout panel() {
        LinearLayout p = new LinearLayout(this);
        p.setOrientation(LinearLayout.VERTICAL);
        p.setBackgroundColor(0xFF202428);
        p.setPadding(dp(12), dp(12), dp(12), dp(12));
        p.setClickable(true); // swallow taps so they don't close the panel
        p.setVisibility(View.GONE);
        return p;
    }

    private LinearLayout card(LinearLayout parent, String title) {
        LinearLayout c = new LinearLayout(this);
        c.setOrientation(LinearLayout.VERTICAL);
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(0xA0101418);
        bg.setCornerRadius(dp(16));
        c.setBackground(bg);
        c.setPadding(dp(16), dp(12), dp(16), dp(12));
        c.setClickable(true);
        if (title != null) c.addView(text(title, 12, GREY));
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(-1, -2);
        p.bottomMargin = dp(12);
        parent.addView(c, p);
        return c;
    }

    private TextView text(String s, int sp, int color) {
        TextView t = new TextView(this);
        t.setText(s);
        t.setTextSize(sp);
        t.setTextColor(color);
        return t;
    }

    private Button barButton(String label, String desc, View.OnClickListener l) {
        Button b = new Button(this);
        b.setText(label);
        b.setContentDescription(desc);
        b.setBackground(null);
        b.setTextColor(WHITE);
        b.setMinWidth(0);
        b.setMinimumWidth(0);
        b.setOnClickListener(l);
        return b;
    }

    private Button webButton(String label, String url) {
        Button b = barButton(label, "Search " + label, v -> webSearch(url));
        b.setTextColor(0xFF8AB4F8);
        return b;
    }

    // ---------- Start menu ----------

    private void toggleMenu() {
        boolean open = menu.getVisibility() != View.VISIBLE;
        closePanels();
        if (open) { menu.setVisibility(View.VISIBLE); search.setText(""); search.requestFocus(); }
    }

    private void closePanels() {
        menu.setVisibility(View.GONE);
        notifPanel.setVisibility(View.GONE);
    }

    private void loadApps() {
        PackageManager pm = getPackageManager();
        Intent i = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER);
        apps.clear();
        for (ResolveInfo ri : pm.queryIntentActivities(i, 0)) {
            if (ri.activityInfo.packageName.equals(getPackageName())) continue;
            App a = new App();
            a.cn = new ComponentName(ri.activityInfo.packageName, ri.activityInfo.name);
            a.label = ri.loadLabel(pm).toString();
            a.icon = ri.loadIcon(pm);
            apps.add(a);
        }
        apps.sort((x, y) -> x.label.compareToIgnoreCase(y.label));
        filter(search.getText().toString());
        renderPins();
    }

    private void filter(String q) {
        shown.clear();
        for (App a : apps) if (a.label.toLowerCase().contains(q.toLowerCase())) shown.add(a);
        adapter.notifyDataSetChanged();
        webRow.setVisibility(q.trim().isEmpty() ? View.GONE : View.VISIBLE);
    }

    private void webSearch(String base) {
        String q = search.getText().toString().trim();
        if (q.isEmpty()) return;
        launchIntent(new Intent(Intent.ACTION_VIEW, Uri.parse(base + Uri.encode(q))));
    }

    private void togglePin(App a) {
        Set<String> pins = new HashSet<>(prefs.getStringSet("pins", new HashSet<>()));
        String key = a.cn.flattenToString();
        boolean added = pins.add(key);
        if (!added) pins.remove(key);
        prefs.edit().putStringSet("pins", pins).apply();
        Toast.makeText(this, (added ? "Pinned " : "Unpinned ") + a.label, Toast.LENGTH_SHORT).show();
        renderPins();
    }

    private void renderPins() {
        pinBar.removeAllViews();
        Set<String> pins = prefs.getStringSet("pins", new HashSet<>());
        for (App a : apps) {
            if (!pins.contains(a.cn.flattenToString())) continue;
            ImageView iv = new ImageView(this);
            iv.setImageDrawable(a.icon);
            iv.setContentDescription(a.label);
            iv.setPadding(dp(6), dp(6), dp(6), dp(6));
            iv.setOnClickListener(v -> launch(a));
            iv.setOnLongClickListener(v -> { togglePin(a); return true; });
            pinBar.addView(iv, new LinearLayout.LayoutParams(dp(48), dp(48)));
        }
    }

    // ---------- Opening things in a window on this screen ----------

    // Xiaomi apps keep their phone layout in any window; these Google versions adapt to the window size
    private static final String[][] GOOGLE_VERSIONS = {
            {"com.miui.calculator", "com.google.android.calculator", "Google Calculator"},
            {"com.miui.notes", "com.google.android.keep", "Google Keep"},
            {"com.mi.android.globalFileexplorer", "com.google.android.apps.nbu.files", "Files by Google"},
            {"com.miui.gallery", "com.google.android.apps.photos", "Google Photos"},
            {"com.android.deskclock", "com.google.android.deskclock", "Google Clock"},
            {"com.xiaomi.calendar", "com.google.android.calendar", "Google Calendar"},
            {"com.android.mms", "com.google.android.apps.messaging", "Google Messages"},
    };

    private void launch(App a) {
        String pkg = a.cn.getPackageName();
        for (String[] g : GOOGLE_VERSIONS) {
            if (!g[0].equals(pkg) || prefs.getBoolean("keep:" + pkg, false)) continue;
            Intent google = getPackageManager().getLaunchIntentForPackage(g[1]);
            new android.app.AlertDialog.Builder(this)
                    .setTitle("Try " + g[2] + "?")
                    .setMessage(a.label + " always shows its phone layout. " + g[2]
                            + " fills laptop windows properly." + (google == null ? " It's free on the Play Store." : ""))
                    .setPositiveButton(google != null ? "Open " + g[2] : "Get " + g[2], (d, w) -> {
                        if (google != null) launchIntent(google);
                        else launchIntent(new Intent(Intent.ACTION_VIEW, Uri.parse("market://details?id=" + g[1])));
                    })
                    .setNegativeButton("Open " + a.label, (d, w) -> {
                        prefs.edit().putBoolean("keep:" + pkg, true).apply(); // don't ask about this app again
                        open(a);
                    })
                    .show();
            return;
        }
        open(a);
    }

    private void open(App a) {
        launchIntent(new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER).setComponent(a.cn));
    }

    private void launchIntent(Intent i) {
        closePanels();
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        // The laptop script reads this to title the app's window ("Play Store", not "Vending")
        ResolveInfo ri = getPackageManager().resolveActivity(i, 0);
        if (ri != null) {
            android.util.Log.i("MobiTop", "title " + ri.activityInfo.packageName + "="
                    + ri.activityInfo.applicationInfo.loadLabel(getPackageManager()));
        }
        // shortcut: an app already running on the phone may jump there instead of opening here; Android decides, not us
        try {
            startActivity(i, windowOptions().toBundle());
        } catch (SecurityException e) {
            startActivity(i); // some displays refuse third-party launches; open it normally instead
        } catch (ActivityNotFoundException e) {
            Toast.makeText(this, "No app can open this", Toast.LENGTH_SHORT).show();
        }
    }

    private ActivityOptions windowOptions() {
        int spare = SpareReceiver.display;
        if (spare != -1 && getSystemService(android.hardware.display.DisplayManager.class).getDisplay(spare) != null) {
            SpareReceiver.display = -1; // one app per laptop window; the script sends a fresh screen
            ActivityOptions o = ActivityOptions.makeBasic().setLaunchDisplayId(spare);
            try { // hidden API; full screen there means HyperOS skips its phone-style floating frame
                ActivityOptions.class.getMethod("setLaunchWindowingMode", int.class).invoke(o, 1);
            } catch (Exception ignored) { }
            return o;
        }
        Display d = getWindowManager().getDefaultDisplay(); // the display this activity is on
        Point size = new Point();
        d.getRealSize(size);
        int[] r = windowBounds(size.x, size.y - dp(48), cascade++, dp(32));
        return ActivityOptions.makeBasic()
                .setLaunchDisplayId(d.getDisplayId())
                .setLaunchBounds(new Rect(r[0], r[1], r[2], r[3]));
    }

    /** Cascading window rect {left, top, right, bottom} that always fits inside the usable area. */
    static int[] windowBounds(int sw, int sh, int n, int step) {
        int w = sw * 3 / 5, h = sh * 7 / 10;
        int x = sw / 8 + (n % 6) * step, y = sh / 12 + (n % 6) * step;
        x = Math.max(0, Math.min(x, sw - w));
        y = Math.max(0, Math.min(y, sh - h));
        return new int[]{x, y, x + w, y + h};
    }

    // ---------- Notifications and media ----------

    private boolean hasNotifAccess() {
        String flat = Settings.Secure.getString(getContentResolver(), "enabled_notification_listeners");
        return flat != null && flat.contains(new ComponentName(this, NotifService.class).flattenToString());
    }

    private void notificationsChanged() {
        int n = NotifService.list().size();
        bell.setText(n > 0 ? "🔔 " + n : "🔔");
        if (notifPanel.getVisibility() == View.VISIBLE) renderNotifs();
        pickController(null);
    }

    private void toggleNotifs() {
        boolean open = notifPanel.getVisibility() != View.VISIBLE;
        closePanels();
        if (!open) return;
        if (!hasNotifAccess()) {
            Toast.makeText(this, "Allow notification access for Mobi-Top", Toast.LENGTH_LONG).show();
            launchIntent(new Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS));
            return;
        }
        notifPanel.setVisibility(View.VISIBLE);
        renderNotifs();
    }

    private void renderNotifs() {
        notifList.removeAllViews();
        List<StatusBarNotification> list = NotifService.list();
        if (list.isEmpty()) notifList.addView(text("\nNo notifications", 14, GREY));
        PackageManager pm = getPackageManager();
        for (StatusBarNotification s : list) {
            Bundle x = s.getNotification().extras;
            String app;
            try { app = pm.getApplicationLabel(pm.getApplicationInfo(s.getPackageName(), 0)).toString(); }
            catch (PackageManager.NameNotFoundException e) { app = s.getPackageName(); }
            CharSequence title = x.getCharSequence(Notification.EXTRA_TITLE);
            CharSequence body = x.getCharSequence(Notification.EXTRA_TEXT);
            LinearLayout row = new LinearLayout(this);
            row.setOrientation(LinearLayout.VERTICAL);
            row.setPadding(0, dp(10), 0, dp(10));
            row.addView(text(app, 11, GREY));
            if (title != null) row.addView(text(title.toString(), 14, WHITE));
            if (body != null) {
                TextView t = text(body.toString(), 13, 0xFFCCCCCC);
                t.setMaxLines(3);
                t.setEllipsize(TextUtils.TruncateAt.END);
                row.addView(t);
            }
            PendingIntent pi = s.getNotification().contentIntent;
            row.setOnClickListener(v -> openNotification(pi));
            notifList.addView(row);
        }
    }

    private void openNotification(PendingIntent pi) {
        closePanels();
        if (pi == null) return;
        ActivityOptions o = windowOptions();
        if (Build.VERSION.SDK_INT >= 34) {
            o.setPendingIntentBackgroundActivityStartMode(ActivityOptions.MODE_BACKGROUND_ACTIVITY_START_ALLOWED);
        }
        try { pi.send(this, 0, null, null, null, null, o.toBundle()); }
        catch (PendingIntent.CanceledException ignored) { } // notification's app already closed it
    }

    /** Uses the first active media session; needs notification access, so quietly does nothing without it. */
    private void pickController(List<MediaController> list) {
        if (!hasNotifAccess()) return;
        ComponentName me = new ComponentName(this, NotifService.class);
        try {
            if (list == null) {
                sessions.removeOnActiveSessionsChangedListener(sessionsChanged);
                sessions.addOnActiveSessionsChangedListener(sessionsChanged, me);
                list = sessions.getActiveSessions(me);
            }
        } catch (SecurityException e) { return; } // access granted but listener not bound yet
        setController(list.isEmpty() ? null : list.get(0));
    }

    private void setController(MediaController c) {
        if (controller != null) controller.unregisterCallback(mediaCallback);
        controller = c;
        if (c != null) c.registerCallback(mediaCallback, main);
        updateMedia();
    }

    private void updateMedia() {
        MediaMetadata m = controller == null ? null : controller.getMetadata();
        if (m == null) { media.setVisibility(View.GONE); return; }
        String title = m.getString(MediaMetadata.METADATA_KEY_TITLE), artist = m.getString(MediaMetadata.METADATA_KEY_ARTIST);
        mediaTitle.setText(artist == null ? title : title + " · " + artist);
        playPause.setText(isPlaying() ? "⏸" : "▶");
        media.setVisibility(View.VISIBLE);
    }

    private boolean isPlaying() {
        PlaybackState s = controller == null ? null : controller.getPlaybackState();
        return s != null && s.getState() == PlaybackState.STATE_PLAYING;
    }

    private void togglePlay() {
        if (controller == null) return;
        if (isPlaying()) controller.getTransportControls().pause();
        else controller.getTransportControls().play();
    }

    // ---------- Widgets ----------

    private void updateSystem(Intent bat) {
        int pct = bat.getIntExtra(BatteryManager.EXTRA_LEVEL, 0) * 100 / Math.max(1, bat.getIntExtra(BatteryManager.EXTRA_SCALE, 100));
        boolean charging = bat.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0) != 0;
        StatFs fs = new StatFs(Environment.getDataDirectory().getPath());
        double gb = 1024.0 * 1024 * 1024;
        system.setText(String.format("Battery %d%%%s\nStorage %.0f GB free of %.0f GB",
                pct, charging ? " (charging)" : "", fs.getAvailableBytes() / gb, fs.getTotalBytes() / gb));
    }

    private boolean hasFileAccess() {
        if (Build.VERSION.SDK_INT >= 30) return Environment.isExternalStorageManager();
        return checkSelfPermission(Manifest.permission.READ_EXTERNAL_STORAGE) == PackageManager.PERMISSION_GRANTED;
    }

    private void loadFiles() {
        while (files.getChildCount() > 1) files.removeViewAt(1); // keep the title
        if (!hasFileAccess()) {
            Button allow = barButton("Allow file access", "Allow file access", v -> {
                if (Build.VERSION.SDK_INT >= 30) launchIntent(new Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,
                        Uri.parse("package:" + getPackageName())));
                else requestPermissions(new String[]{Manifest.permission.READ_EXTERNAL_STORAGE}, 2);
            });
            allow.setTextColor(0xFF8AB4F8);
            files.addView(allow);
            return;
        }
        Uri base = MediaStore.Downloads.EXTERNAL_CONTENT_URI;
        String[] cols = {MediaStore.MediaColumns._ID, MediaStore.MediaColumns.DISPLAY_NAME, MediaStore.MediaColumns.MIME_TYPE};
        try (Cursor c = getContentResolver().query(base, cols, null, null, MediaStore.MediaColumns.DATE_MODIFIED + " DESC")) {
            int count = 0;
            while (c != null && c.moveToNext() && count++ < 8) {
                Uri uri = ContentUris.withAppendedId(base, c.getLong(0));
                String mime = c.getString(2);
                TextView t = text("📄 " + c.getString(1), 13, WHITE);
                t.setSingleLine(true);
                t.setEllipsize(TextUtils.TruncateAt.MIDDLE);
                t.setPadding(0, dp(5), 0, dp(5));
                t.setOnClickListener(v -> launchIntent(new Intent(Intent.ACTION_VIEW)
                        .setDataAndType(uri, mime).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)));
                files.addView(t);
            }
            if (count == 0) files.addView(text("Nothing downloaded yet", 13, GREY));
        }
    }

    @Override
    public void onRequestPermissionsResult(int code, String[] perms, int[] results) { loadFiles(); }

    // ---------- Wallpaper ----------

    private void wallpaperMenu(View anchor) {
        PopupMenu m = new PopupMenu(this, anchor, Gravity.CENTER);
        m.getMenu().add("Change wallpaper");
        m.getMenu().add("Default wallpaper");
        m.setOnMenuItemClickListener(item -> {
            if (item.getTitle().toString().startsWith("Change")) {
                startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT)
                        .addCategory(Intent.CATEGORY_OPENABLE).setType("image/*"), PICK_WALLPAPER);
            } else {
                prefs.edit().remove("wallpaper").apply();
                applyWallpaper();
            }
            return true;
        });
        m.show();
    }

    @Override
    protected void onActivityResult(int code, int result, Intent data) {
        if (code != PICK_WALLPAPER || result != RESULT_OK || data == null || data.getData() == null) return;
        Uri uri = data.getData();
        getContentResolver().takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION); // survives restarts
        prefs.edit().putString("wallpaper", uri.toString()).apply();
        applyWallpaper();
    }

    private void applyWallpaper() {
        String s = prefs.getString("wallpaper", null);
        if (s != null) {
            try {
                // Decode at roughly screen size so a 50 MP photo doesn't run the phone out of memory
                Bitmap bmp = ImageDecoder.decodeBitmap(ImageDecoder.createSource(getContentResolver(), Uri.parse(s)),
                        (dec, info, src) -> dec.setTargetSampleSize(Math.max(1, info.getSize().getWidth() / 2560)));
                wallpaper.setImageBitmap(bmp);
                return;
            } catch (Exception e) {
                prefs.edit().remove("wallpaper").apply(); // image deleted or access lost
            }
        }
        wallpaper.setImageResource(R.drawable.wallpaper_mountains);
    }

    private int dp(int v) { return Math.round(v * getResources().getDisplayMetrics().density); }

    private class AppAdapter extends BaseAdapter {
        public int getCount() { return shown.size(); }
        public Object getItem(int p) { return shown.get(p); }
        public long getItemId(int p) { return p; }

        public View getView(int p, View cv, ViewGroup parent) {
            LinearLayout cell = (LinearLayout) cv;
            if (cell == null) {
                cell = new LinearLayout(DesktopActivity.this);
                cell.setOrientation(LinearLayout.VERTICAL);
                cell.setGravity(Gravity.CENTER_HORIZONTAL);
                cell.addView(new ImageView(DesktopActivity.this), new LinearLayout.LayoutParams(dp(48), dp(48)));
                TextView t = text("", 12, WHITE);
                t.setSingleLine(true);
                t.setEllipsize(TextUtils.TruncateAt.END);
                t.setGravity(Gravity.CENTER);
                cell.addView(t);
            }
            App a = shown.get(p);
            ((ImageView) cell.getChildAt(0)).setImageDrawable(a.icon);
            ((TextView) cell.getChildAt(1)).setText(a.label);
            return cell;
        }
    }
}
