package com.timetracker.utils;

import android.content.Context;
import android.content.SharedPreferences;

public class AppSettings {
    private static final String PREFS_NAME = "timetracker_settings";
    private static final String KEY_MODE = "trackingMode";
    private static final String KEY_ACTIVITY_ID = "currentActivityId";
    private static final String KEY_SERVER_URL = "serverUrl";
    private static final String KEY_AUTH_TOKEN = "authToken";
    private static final String KEY_AUTO_SYNC = "autoSync";
    private static final String KEY_HOST_SERVER = "hostServer";
    private static final String KEY_SERVER_PORT = "serverPort";
    private static final String KEY_LAST_SYNC_TIME = "lastSyncTime";

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

    public static String getServerUrl(Context ctx) {
        return getPrefs(ctx).getString(KEY_SERVER_URL, "");
    }

    public static void setServerUrl(Context ctx, String url) {
        getPrefs(ctx).edit().putString(KEY_SERVER_URL, url).apply();
    }

    public static String getAuthToken(Context ctx) {
        return getPrefs(ctx).getString(KEY_AUTH_TOKEN, "");
    }

    public static void setAuthToken(Context ctx, String token) {
        getPrefs(ctx).edit().putString(KEY_AUTH_TOKEN, token).apply();
    }

    public static boolean isAutoSync(Context ctx) {
        return getPrefs(ctx).getBoolean(KEY_AUTO_SYNC, false);
    }

    public static void setAutoSync(Context ctx, boolean enabled) {
        getPrefs(ctx).edit().putBoolean(KEY_AUTO_SYNC, enabled).apply();
    }

    public static boolean isHostServer(Context ctx) {
        return getPrefs(ctx).getBoolean(KEY_HOST_SERVER, false);
    }

    public static long getLastSyncTime(Context ctx) {
        return getPrefs(ctx).getLong(KEY_LAST_SYNC_TIME, 0);
    }

    public static void setLastSyncTime(Context ctx, long time) {
        getPrefs(ctx).edit().putLong(KEY_LAST_SYNC_TIME, time).apply();
    }

    public static int getServerPort(Context ctx) {
        return getPrefs(ctx).getInt(KEY_SERVER_PORT, 5080);
    }

    private static SharedPreferences getPrefs(Context ctx) {
        return ctx.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
    }
}
