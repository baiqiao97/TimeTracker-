package com.timetracker;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.app.usage.UsageEvents;
import android.app.usage.UsageStatsManager;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.PowerManager;
import android.provider.Settings;
import android.util.Log;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Calendar;
import java.util.Date;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

import com.timetracker.database.AppDatabase;
import com.timetracker.model.Category;
import com.timetracker.model.Device;
import com.timetracker.model.TimeRecord;
import com.timetracker.model.User;

public class TrackingService extends Service {

    private static final String CHANNEL_ID = "TimeTrackerChannel";
    private static final int NOTIFICATION_ID = 1;
    private static final String TAG = "TimeTracker";

    private Handler handler;
    private Runnable trackingRunnable;
    private UsageStatsManager usageStatsManager;
    /** 包名 -> 分类ID 的映射缓存 */
    private final Map<String, Integer> packageCategoryMap = new HashMap<>();
    /** 分类映射是否已加载 */
    private volatile boolean categoryMapLoaded = false;

    // 追踪状态
    private String lastForegroundPackage = "";
    private String currentAppName = "";
    private long lastEventTime = 0;
    private long lastCheckTime = 0;
    private boolean isPaused = false;
    private NotificationManager notificationManager;

    /** 分类加载完成前缓冲的记录，加载后批量写入 */
    private final List<PendingRecord> pendingRecords = new ArrayList<>();

    // 修复：使用固定大小线程池替代 new Thread()，避免高频率创建大量线程
    private final ExecutorService dbExecutor = Executors.newFixedThreadPool(2);

    private static final Map<String, String> DEFAULT_PACKAGE_CATEGORIES = new HashMap<>();
    static {
        DEFAULT_PACKAGE_CATEGORIES.put("com.android.chrome", "浏览器");
        DEFAULT_PACKAGE_CATEGORIES.put("com.google.android.youtube", "视频");
        DEFAULT_PACKAGE_CATEGORIES.put("com.whatsapp", "社交媒体");
        DEFAULT_PACKAGE_CATEGORIES.put("com.facebook.katana", "社交媒体");
        DEFAULT_PACKAGE_CATEGORIES.put("com.instagram.android", "社交媒体");
        DEFAULT_PACKAGE_CATEGORIES.put("com.microsoft.office.word", "办公");
        DEFAULT_PACKAGE_CATEGORIES.put("com.microsoft.office.excel", "办公");
        DEFAULT_PACKAGE_CATEGORIES.put("com.microsoft.office.powerpoint", "办公");
        DEFAULT_PACKAGE_CATEGORIES.put("com.tencent.mm", "通讯");
        DEFAULT_PACKAGE_CATEGORIES.put("com.tencent.mobileqq", "通讯");
    }

    @Override
    public void onCreate() {
        super.onCreate();
        createNotificationChannel();
        notificationManager = getSystemService(NotificationManager.class);
        startForeground(NOTIFICATION_ID, createNotification("初始化中..."));

        usageStatsManager = (UsageStatsManager) getSystemService(Context.USAGE_STATS_SERVICE);
        lastCheckTime = System.currentTimeMillis();

        // 注册设备 + 自动清理旧数据
        registerDeviceAndCleanup();

        // 异步加载分类映射
        loadCategoryMappings();

        handler = new Handler(Looper.getMainLooper());
        trackingRunnable = () -> {
            if (!isPaused) {
                trackAppUsage();
            }
            int interval = isDeviceIdle() ? 300000 : 30000; // 30秒活跃 / 5分钟空闲
            handler.postDelayed(trackingRunnable, interval);
        };
        // 延迟启动追踪，等待分类映射加载完成
        handler.postDelayed(trackingRunnable, 2000);
    }

    /**
     * 注册当前设备信息到数据库，并清理超过90天的旧记录
     */
    private void registerDeviceAndCleanup() {
        new Thread(() -> {
            try {
                AppDatabase db = AppDatabase.getInstance(getApplicationContext());
                String deviceId = getDeviceId();

                // 注册设备
                Device device = db.deviceDao().getDevice(deviceId);
                if (device == null) {
                    String deviceName = Build.MODEL;
                    db.deviceDao().insertOrUpdate(new Device(deviceId, deviceName, "Android",
                            new SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.US).format(new Date())));

                    // 不再自动创建默认用户，用户通过注册/登录创建
                    Log.d(TAG, "Device registered: " + deviceName + " (" + deviceId + ")");
                } else {
                    // 更新最后同步时间
                    db.deviceDao().updateLastSync(deviceId,
                            new SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.US).format(new Date()));
                }

                // 自动清理超过90天的旧记录
                Calendar cal = Calendar.getInstance();
                cal.add(Calendar.DAY_OF_YEAR, -90);
                db.timeRecordDao().deleteOldRecords(cal.getTime());
                Log.d(TAG, "Cleaned records older than 90 days");
            } catch (Exception e) {
                Log.e(TAG, "Error registering device", e);
            }
        }).start();
    }

    /**
     * 异步加载分类映射。完成后设置 categoryMapLoaded = true
     */
    private void loadCategoryMappings() {
        new Thread(() -> {
            try {
                AppDatabase db = AppDatabase.getInstance(getApplicationContext());
                List<Category> categories = db.categoryDao().getAllCategories();

                // 如果没有分类，插入默认分类
                if (categories.isEmpty()) {
                    db.categoryDao().insert(new Category("浏览器", "#3498db", "网页浏览"));
                    db.categoryDao().insert(new Category("社交媒体", "#e4405f", "社交应用"));
                    db.categoryDao().insert(new Category("游戏", "#e74c3c", "游戏娱乐"));
                    db.categoryDao().insert(new Category("办公", "#9b59b6", "办公应用"));
                    db.categoryDao().insert(new Category("工具", "#2ecc71", "实用工具"));
                    db.categoryDao().insert(new Category("通讯", "#1abc9c", "聊天通讯"));
                    db.categoryDao().insert(new Category("视频", "#ff5722", "视频播放"));
                    db.categoryDao().insert(new Category("其他", "#f39c12", "其他应用"));
                    categories = db.categoryDao().getAllCategories();
                }

                // 构建包名 -> categoryId 映射
                synchronized (packageCategoryMap) {
                    for (Category cat : categories) {
                        for (Map.Entry<String, String> entry : DEFAULT_PACKAGE_CATEGORIES.entrySet()) {
                            if (cat.getName().equals(entry.getValue())) {
                                packageCategoryMap.put(entry.getKey(), cat.getId());
                            }
                        }
                    }
                }

                categoryMapLoaded = true;
                Log.d(TAG, "Category mappings loaded: " + packageCategoryMap.size() + " entries");

                // 🔴 回填启动期的缓冲记录
                flushPendingRecords();
            } catch (Exception e) {
                Log.e(TAG, "Error loading categories", e);
                categoryMapLoaded = true;
                flushPendingRecords(); // 即使失败也回填
            }
        }).start();
    }

    /**
     * 将启动期缓冲的记录写入数据库
     */
    private void flushPendingRecords() {
        List<PendingRecord> toFlush;
        synchronized (pendingRecords) {
            toFlush = new ArrayList<>(pendingRecords);
            pendingRecords.clear();
        }
        for (PendingRecord pr : toFlush) {
            doInsert(pr.packageName, pr.appName, pr.usageTimeMs, pr.isForeground);
        }
        if (!toFlush.isEmpty()) {
            Log.d(TAG, "Flushed " + toFlush.size() + " buffered records");
        }
    }

    public void addPackageCategoryMapping(String packageName, int categoryId) {
        synchronized (packageCategoryMap) {
            packageCategoryMap.put(packageName, categoryId);
        }
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent != null) {
            String action = intent.getAction();
            if ("PAUSE".equals(action)) {
                isPaused = true;
                updateNotification("追踪已暂停");
                Log.d(TAG, "Tracking paused");
            } else if ("RESUME".equals(action)) {
                isPaused = false;
                lastCheckTime = System.currentTimeMillis(); // 重置检查时间避免计入暂停期
                updateNotification("追踪已恢复");
                Log.d(TAG, "Tracking resumed");
            }
        }
        return START_STICKY;
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    @Override
    public void onDestroy() {
        super.onDestroy();
        if (handler != null && trackingRunnable != null) {
            handler.removeCallbacks(trackingRunnable);
        }
        // 修复：关闭线程池
        dbExecutor.shutdown();
    }

    private void createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel channel = new NotificationChannel(CHANNEL_ID,
                    "TimeTracker Service",
                    NotificationManager.IMPORTANCE_LOW);
            NotificationManager manager = getSystemService(NotificationManager.class);
            if (manager != null) {
                manager.createNotificationChannel(channel);
            }
        }
    }

    private Notification createNotification(String content) {
        int flags = PendingIntent.FLAG_UPDATE_CURRENT;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            flags |= PendingIntent.FLAG_IMMUTABLE;
        }
        Intent notificationIntent = new Intent(this, MainActivity.class);
        PendingIntent pendingIntent = PendingIntent.getActivity(this, 0,
                notificationIntent, flags);

        Intent pauseIntent = new Intent(this, TrackingService.class);
        pauseIntent.setAction("PAUSE");
        PendingIntent pausePending = PendingIntent.getService(this, 1,
                pauseIntent, flags);

        Intent resumeIntent = new Intent(this, TrackingService.class);
        resumeIntent.setAction("RESUME");
        PendingIntent resumePending = PendingIntent.getService(this, 2,
                resumeIntent, flags);

        Notification.Builder builder;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            builder = new Notification.Builder(this, CHANNEL_ID);
        } else {
            builder = new Notification.Builder(this);
        }
        builder.setSmallIcon(android.R.drawable.ic_menu_manage)
                .setContentTitle("TimeTracker")
                .setContentText(content)
                .setContentIntent(pendingIntent)
                .setPriority(Notification.PRIORITY_LOW);

        if (isPaused) {
            builder.addAction(android.R.drawable.ic_media_play, "恢复追踪", resumePending);
        } else {
            builder.addAction(android.R.drawable.ic_media_pause, "暂停追踪", pausePending);
        }

        return builder.build();
    }

    /**
     * 更新通知文本（实时显示当前追踪的应用）
     */
    private void updateNotification(String text) {
        if (notificationManager != null) {
            notificationManager.notify(NOTIFICATION_ID, createNotification(text));
        }
    }

    /**
     * 使用 UsageEvents API 获取最近的前台活动，精确追踪应用切换
     * 当无事件时回退到 UsageStats 查询当前前台包名
     */
    private void trackAppUsage() {
        try {
            long currentTime = System.currentTimeMillis();
            long queryStart = lastCheckTime;

            UsageEvents usageEvents = usageStatsManager.queryEvents(queryStart, currentTime);
            if (usageEvents == null) {
                // queryEvents 返回 null 时回退
                fallbackTrack(queryStart, currentTime);
                lastCheckTime = currentTime;
                return;
            }

            String currentForegroundPackage = null;
            long currentForegroundStart = 0;
            boolean hasEvents = false;

            UsageEvents.Event event = new UsageEvents.Event();
            while (usageEvents.hasNextEvent()) {
                hasEvents = true;
                usageEvents.getNextEvent(event);

                // MOVE_TO_FOREGROUND / ACTIVITY_RESUMED 表示应用进入前台
                if (event.getEventType() == UsageEvents.Event.MOVE_TO_FOREGROUND
                        || event.getEventType() == UsageEvents.Event.ACTIVITY_RESUMED) {
                    if (currentForegroundPackage != null && !currentForegroundPackage.isEmpty()) {
                        long usageDuration = event.getTimeStamp() - currentForegroundStart;
                        if (usageDuration > 0) {
                            saveUsageRecord(currentForegroundPackage, usageDuration, true);
                        }
                    }
                    currentForegroundPackage = event.getPackageName();
                    currentForegroundStart = event.getTimeStamp();
                }
                // ACTIVITY_PAUSED / ACTIVITY_STOPPED 表示应用退出前台
                else if (event.getEventType() == UsageEvents.Event.ACTIVITY_PAUSED
                        || event.getEventType() == UsageEvents.Event.ACTIVITY_STOPPED) {
                    if (currentForegroundPackage != null && !currentForegroundPackage.isEmpty()
                            && event.getPackageName().equals(currentForegroundPackage)) {
                        long usageDuration = event.getTimeStamp() - currentForegroundStart;
                        if (usageDuration > 0) {
                            saveUsageRecord(currentForegroundPackage, usageDuration, true);
                        }
                        currentForegroundPackage = null;
                    }
                }
            }

            if (!hasEvents && !lastForegroundPackage.isEmpty()) {
                // 🔴 修复：无事件时（用户一直在同一App），用上次记录的包名补偿时间
                long usageDuration = currentTime - queryStart;
                if (usageDuration > 0) {
                    saveUsageRecord(lastForegroundPackage, usageDuration, true);
                }
            } else if (currentForegroundPackage != null && !currentForegroundPackage.isEmpty()) {
                // 有事件且当前仍在运行 → 记录最后一段
                long usageDuration = currentTime - Math.max(currentForegroundStart, queryStart);
                if (usageDuration > 0) {
                    saveUsageRecord(currentForegroundPackage, usageDuration, true);
                }
                lastForegroundPackage = currentForegroundPackage;
            }

            lastCheckTime = currentTime;
        } catch (Exception e) {
            Log.e(TAG, "Error tracking app usage", e);
        }
    }

    /**
     * 回退方案：UsageEvents 不可用时，用 UsageStats 获取最后活跃应用
     */
    private void fallbackTrack(long queryStart, long currentTime) {
        if (lastForegroundPackage.isEmpty()) return;
        long usageDuration = currentTime - queryStart;
        if (usageDuration > 0 && usageDuration < 300000) { // 最多记录5分钟
            saveUsageRecord(lastForegroundPackage, usageDuration, true);
        }
    }

    /**
     * 保存使用记录到数据库。分类未加载时缓冲到队列，加载后自动回填。
     */
    private void saveUsageRecord(String packageName, long usageTimeMs, boolean isForeground) {
        final String appName = getAppNameFromPackage(packageName);

        // 实时更新通知栏显示当前应用
        if (!appName.equals(currentAppName)) {
            currentAppName = appName;
            updateNotification("当前: " + appName);
        }

        if (!categoryMapLoaded) {
            // 🔴 修复：启动期先缓冲，分类加载完成后自动回填
            synchronized (pendingRecords) {
                pendingRecords.add(new PendingRecord(packageName, appName, usageTimeMs, isForeground));
            }
            Log.d(TAG, "Buffered (categories not loaded): " + appName + " - " + usageTimeMs + "ms");
            return;
        }

        doInsert(packageName, appName, usageTimeMs, isForeground);
    }

    private void doInsert(String packageName, String appName, long usageTimeMs, boolean isForeground) {
        dbExecutor.execute(() -> {
            try {
                AppDatabase db = AppDatabase.getInstance(getApplicationContext());
                String deviceId = getDeviceId();
                Integer categoryId;
                synchronized (packageCategoryMap) {
                    categoryId = packageCategoryMap.get(packageName);
                }
                TimeRecord record = new TimeRecord(
                        packageName,
                        appName,
                        usageTimeMs,
                        new Date(),
                        deviceId,
                        categoryId,
                        isForeground,
                        null
                );
                // Activity mode: attach current activity id
                if (com.timetracker.utils.AppSettings.isActivityMode(getApplicationContext())) {
                    int aid = com.timetracker.utils.AppSettings.getCurrentActivityId(getApplicationContext());
                    if (aid > 0) record.setActivityId(aid);
                }
                db.timeRecordDao().insert(record);

                Log.d(TAG, String.format("Recorded: %s (%s) - %dms, category=%d",
                        appName, packageName, usageTimeMs, categoryId != null ? categoryId : -1));
            } catch (Exception e) {
                Log.e(TAG, "Error inserting record", e);
            }
        });
    }

    private String getAppNameFromPackage(String packageName) {
        try {
            return getPackageManager().getApplicationLabel(
                    getPackageManager().getApplicationInfo(packageName, 0)).toString();
        } catch (PackageManager.NameNotFoundException e) {
            return packageName;
        }
    }

    private String getDeviceId() {
        return android.provider.Settings.Secure.getString(
                getContentResolver(), android.provider.Settings.Secure.ANDROID_ID);
    }

    private boolean isDeviceIdle() {
        PowerManager powerManager = (PowerManager) getSystemService(Context.POWER_SERVICE);
        return powerManager != null && !powerManager.isInteractive();
    }

    /** 启动期缓冲记录 */
    private static class PendingRecord {
        final String packageName;
        final String appName;
        final long usageTimeMs;
        final boolean isForeground;

        PendingRecord(String packageName, String appName, long usageTimeMs, boolean isForeground) {
            this.packageName = packageName;
            this.appName = appName;
            this.usageTimeMs = usageTimeMs;
            this.isForeground = isForeground;
        }
    }
}
