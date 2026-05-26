package com.timetracker.utils;

import android.content.Context;
import android.util.Log;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.reflect.TypeToken;
import com.timetracker.database.AppDatabase;
import com.timetracker.model.TimeRecord;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.lang.reflect.Type;
import java.net.HttpURLConnection;
import java.net.URL;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Calendar;
import java.util.Date;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

public class ServerSyncClient {
    private static final String TAG = "ServerSyncClient";
    private static final int CONNECT_TIMEOUT = 10000;
    private static final int READ_TIMEOUT = 10000;
    private static final SimpleDateFormat DATE_FORMAT =
            new SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.US);

    private static final Gson GSON = new GsonBuilder()
            .setDateFormat("yyyy-MM-dd HH:mm:ss")
            .create();

    private static String getServerUrl(Context ctx) {
        String url = AppSettings.getServerUrl(ctx);
        if (url.isEmpty()) {
            url = "http://localhost:" + AppSettings.getServerPort(ctx);
        }
        return url.endsWith("/") ? url.substring(0, url.length() - 1) : url;
    }

    public static class AuthResult {
        public boolean ok;
        public String error;
        public String token;

        AuthResult(boolean ok, String error, String token) {
            this.ok = ok;
            this.error = error;
            this.token = token;
        }
    }

    public static AuthResult register(Context ctx, String username, String password) {
        HttpURLConnection conn = null;
        try {
            String json = GSON.toJson(new AuthRequest(username, password));
            conn = post(ctx, "/api/auth/register", json);
            String body = readResponse(conn);

            if (conn.getResponseCode() != 200) {
                AuthError err = GSON.fromJson(body, AuthError.class);
                return new AuthResult(false, err != null ? err.error : "Unknown error", null);
            }

            AuthResponse resp = GSON.fromJson(body, AuthResponse.class);
            if (resp != null && resp.token != null) {
                AppSettings.setAuthToken(ctx, resp.token);
            }
            return new AuthResult(true, null, resp != null ? resp.token : null);
        } catch (Exception e) {
            Log.e(TAG, "Register error", e);
            return new AuthResult(false, e.getMessage(), null);
        } finally {
            if (conn != null) conn.disconnect();
        }
    }

    public static AuthResult login(Context ctx, String username, String password) {
        HttpURLConnection conn = null;
        try {
            String json = GSON.toJson(new AuthRequest(username, password));
            conn = post(ctx, "/api/auth/login", json);
            String body = readResponse(conn);

            if (conn.getResponseCode() != 200) {
                AuthError err = GSON.fromJson(body, AuthError.class);
                return new AuthResult(false,
                        err != null ? err.error : "Login failed", null);
            }

            AuthResponse resp = GSON.fromJson(body, AuthResponse.class);
            if (resp != null && resp.token != null) {
                AppSettings.setAuthToken(ctx, resp.token);
            }
            return new AuthResult(true, null, resp != null ? resp.token : null);
        } catch (Exception e) {
            Log.e(TAG, "Login error", e);
            return new AuthResult(false, e.getMessage(), null);
        } finally {
            if (conn != null) conn.disconnect();
        }
    }

    public static boolean sync(Context ctx) {
        String token = AppSettings.getAuthToken(ctx);
        if (token.isEmpty()) return false;

        HttpURLConnection dlConn = null;
        HttpURLConnection ulConn = null;
        try {
            AppDatabase db = AppDatabase.getInstance(ctx);

            long lastSync = AppSettings.getLastSyncTime(ctx);
            String dlUrl = "/api/sync/download?limit=10000";
            if (lastSync > 0) {
                dlUrl += "&since=" + DATE_FORMAT.format(new Date(lastSync));
            }

            dlConn = get(ctx, dlUrl, token);
            String dlBody = readResponse(dlConn);

            if (dlConn.getResponseCode() == 200 && !dlBody.isEmpty()) {
                Type listType = new TypeToken<List<TimeRecord>>(){}.getType();
                List<TimeRecord> records = GSON.fromJson(dlBody, listType);

                if (records != null && !records.isEmpty()) {
                    List<TimeRecord> existingRecords = db.timeRecordDao()
                            .getRecordsByDateRange(new Date(0), new Date());
                    Set<String> existing = new HashSet<>();
                    Calendar cal = Calendar.getInstance();
                    for (TimeRecord r : existingRecords) {
                        cal.setTime(r.getDate());
                        existing.add(r.getPackageName() + "|"
                                + String.format(Locale.US, "%04d%02d%02d",
                                    cal.get(Calendar.YEAR),
                                    cal.get(Calendar.MONTH) + 1,
                                    cal.get(Calendar.DAY_OF_MONTH))
                                + "|" + r.getDeviceId());
                    }

                    List<TimeRecord> toAdd = new ArrayList<>();
                    for (TimeRecord r : records) {
                        cal.setTime(r.getDate());
                        String key = r.getPackageName() + "|"
                                + String.format(Locale.US, "%04d%02d%02d",
                                    cal.get(Calendar.YEAR),
                                    cal.get(Calendar.MONTH) + 1,
                                    cal.get(Calendar.DAY_OF_MONTH))
                                + "|" + r.getDeviceId();
                        if (!existing.contains(key)) {
                            toAdd.add(r);
                        }
                    }

                    if (!toAdd.isEmpty()) {
                        db.timeRecordDao().insertAll(toAdd);
                        Log.d(TAG, "Downloaded: " + toAdd.size() + " records");
                    }
                }
            }

            List<TimeRecord> local = db.timeRecordDao().getRecordsByDateRange(
                    new Date(lastSync), new Date());
            String deviceId = android.provider.Settings.Secure.getString(
                    ctx.getContentResolver(),
                    android.provider.Settings.Secure.ANDROID_ID);
            List<TimeRecord> mine = new ArrayList<>();
            for (TimeRecord r : local) {
                if (r.getDeviceId() != null && r.getDeviceId().contains(deviceId)) {
                    mine.add(r);
                }
            }

            if (!mine.isEmpty()) {
                String json = GSON.toJson(mine);
                ulConn = post(ctx, "/api/sync/upload", json);
                ulConn.getResponseCode();
            }

            AppSettings.setLastSyncTime(ctx, System.currentTimeMillis());
            Log.d(TAG, "Sync completed");
            return true;
        } catch (Exception e) {
            Log.e(TAG, "Sync error", e);
            return false;
        } finally {
            if (dlConn != null) dlConn.disconnect();
            if (ulConn != null) ulConn.disconnect();
        }
    }

    private static HttpURLConnection get(Context ctx, String path, String token)
            throws Exception {
        URL url = new URL(getServerUrl(ctx) + path);
        HttpURLConnection conn = (HttpURLConnection) url.openConnection();
        conn.setConnectTimeout(CONNECT_TIMEOUT);
        conn.setReadTimeout(READ_TIMEOUT);
        conn.setRequestProperty("Authorization", "Bearer " + token);
        return conn;
    }

    private static HttpURLConnection post(Context ctx, String path, String json)
            throws Exception {
        URL url = new URL(getServerUrl(ctx) + path);
        HttpURLConnection conn = (HttpURLConnection) url.openConnection();
        conn.setConnectTimeout(CONNECT_TIMEOUT);
        conn.setReadTimeout(READ_TIMEOUT);
        conn.setRequestMethod("POST");
        conn.setRequestProperty("Content-Type", "application/json");
        conn.setDoOutput(true);

        String token = AppSettings.getAuthToken(ctx);
        if (!token.isEmpty()) {
            conn.setRequestProperty("Authorization", "Bearer " + token);
        }

        try (OutputStream os = conn.getOutputStream()) {
            os.write(json.getBytes("UTF-8"));
        }
        return conn;
    }

    private static String readResponse(HttpURLConnection conn) throws Exception {
        try (BufferedReader reader = new BufferedReader(
                new InputStreamReader(conn.getInputStream(), "UTF-8"))) {
            StringBuilder sb = new StringBuilder();
            String line;
            while ((line = reader.readLine()) != null) {
                sb.append(line);
            }
            return sb.toString();
        }
    }

    private static class AuthRequest {
        String username;
        String password;
        AuthRequest(String u, String p) { username = u; password = p; }
    }

    private static class AuthResponse {
        String token;
        int userId;
    }

    private static class AuthError {
        String error;
    }
}
