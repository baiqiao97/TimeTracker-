package com.timetracker.utils;

import android.content.Context;
import android.content.SharedPreferences;

public class AppSettings {
    private static final String PREFS_NAME = "timetracker_settings";
    private static final String KEY_MODE = "trackingMode";
    private static final String KEY_ACTIVITY_ID = "currentActivityId";

    public static boolean isActivityMode(Context ctx) {
        return "activity".equals(getPrefs(ctx).getString(KEY_MODE, "simple"));
    }

    public static void setTrackingMode(Context ctx, String mode) {
        getPrefs(ctx).edit().putString(KEY_MODE, mode).apply();
    }

    public static int getCurrentActivityId(Context ctx) {
        return getPrefs(ctx).getInt(KEY_ACTIVITY_ID, -1);
    }

    public static void setCurrentActivityId(Context ctx, int id) {
        getPrefs(ctx).edit().putInt(KEY_ACTIVITY_ID, id).apply();
    }

    private static SharedPreferences getPrefs(Context ctx) {
        return ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
    }
}
