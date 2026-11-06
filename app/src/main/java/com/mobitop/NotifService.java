package com.mobitop;

import android.app.Notification;
import android.service.notification.NotificationListenerService;
import android.service.notification.StatusBarNotification;

import java.util.ArrayList;
import java.util.List;

/** Gives the desktop the phone's notifications once the user grants notification access. */
public class NotifService extends NotificationListenerService {
    static NotifService instance;
    static Runnable onChange; // set by the desktop; callbacks arrive on the main thread

    @Override public void onListenerConnected() { instance = this; changed(); }
    @Override public void onListenerDisconnected() { instance = null; changed(); }
    @Override public void onNotificationPosted(StatusBarNotification sbn) { changed(); }
    @Override public void onNotificationRemoved(StatusBarNotification sbn) { changed(); }

    private static void changed() { if (onChange != null) onChange.run(); }

    /** Notifications worth showing: skips ongoing ones (music, downloads) and group summaries. */
    static List<StatusBarNotification> list() {
        List<StatusBarNotification> out = new ArrayList<>();
        if (instance == null) return out;
        StatusBarNotification[] all;
        try { all = instance.getActiveNotifications(); } catch (SecurityException e) { return out; }
        if (all == null) return out;
        for (StatusBarNotification s : all) {
            if (!s.isClearable() || (s.getNotification().flags & Notification.FLAG_GROUP_SUMMARY) != 0) continue;
            out.add(s);
        }
        return out;
    }
}
