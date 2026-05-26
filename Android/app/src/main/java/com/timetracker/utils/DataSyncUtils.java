package com.timetracker.utils;

import android.content.Context;
import android.os.Environment;
import android.util.Log;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.timetracker.database.AppDatabase;
import com.timetracker.model.TimeRecord;

import java.io.File;
import java.io.FileReader;
import java.io.FileWriter;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Calendar;
import java.util.Date;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

public class DataSyncUtils {
    private static final String TAG = "DataSyncUtils";
    private static final String EXPORT_DIR = "TimeTracker";
    private static final String EXPORT_FILE = "time_tracker_data.json";

    /**
     * 获取导出目录 - 优先使用应用私有目录，兼容 scoped storage
     */
    private static File getExportDir(Context context) {
        // API 29+ 使用应用专属外部存储目录，无需额外权限
        File externalDir = context.getExternalFilesDir(null);
        if (externalDir != null) {
            File dir = new File(externalDir, EXPORT_DIR);
            if (!dir.exists()) {
                dir.mkdirs();
            }
            return dir;
        }
        // 回退方案：使用传统外部存储
        File fallbackDir = new File(Environment.getExternalStorageDirectory(), EXPORT_DIR);
        if (!fallbackDir.exists()) {
            fallbackDir.mkdirs();
        }
        return fallbackDir;
    }

    public static boolean exportData(Context context) {
        try {
            AppDatabase db = AppDatabase.getInstance(context);
            List<TimeRecord> records = db.timeRecordDao().getRecordsByDateRange(
                    new Date(0), new Date(System.currentTimeMillis())
            );

            File exportDir = getExportDir(context);
            File exportFile = new File(exportDir, EXPORT_FILE);

            Gson gson = new GsonBuilder()
                    .setDateFormat("yyyy-MM-dd HH:mm:ss")
                    .setPrettyPrinting()
                    .create();
            String json = gson.toJson(records);

            FileWriter writer = new FileWriter(exportFile);
            writer.write(json);
            writer.close();

            Log.d(TAG, "Data exported successfully to: " + exportFile.getAbsolutePath());
            return true;
        } catch (IOException e) {
            Log.e(TAG, "Error exporting data: " + e.getMessage());
            return false;
        }
    }

    public static boolean importData(Context context) {
        try {
            File exportDir = getExportDir(context);
            File exportFile = new File(exportDir, EXPORT_FILE);
            if (!exportFile.exists()) {
                Log.e(TAG, "Export file not found: " + exportFile.getAbsolutePath());
                return false;
            }

            // 反序列化JSON数据
            Gson gson = new GsonBuilder()
                    .setDateFormat("yyyy-MM-dd HH:mm:ss")
                    .create();
            FileReader reader = new FileReader(exportFile);
            TimeRecord[] recordsArray = gson.fromJson(reader, TimeRecord[].class);
            reader.close();

            if (recordsArray == null || recordsArray.length == 0) {
                Log.e(TAG, "No records found in export file");
                return false;
            }

            // 去重导入：用 HashSet 实现 O(1) 查找（key = packageName|dayOfYear|year|deviceId）
            AppDatabase db = AppDatabase.getInstance(context);
            List<TimeRecord> existingRecords = db.timeRecordDao().getRecordsByDateRange(
                    new Date(0), new Date(System.currentTimeMillis()));

            // 构建已有记录的去重集合
            Set<String> existingKeys = new HashSet<>();
            Calendar cal = Calendar.getInstance();
            for (TimeRecord existing : existingRecords) {
                cal.setTime(existing.getDate());
                String key = existing.getPackageName() + "|"
                        + String.format(Locale.US, "%04d%02d%02d",
                            cal.get(Calendar.YEAR),
                            cal.get(Calendar.MONTH) + 1,
                            cal.get(Calendar.DAY_OF_MONTH))
                        + "|" + existing.getDeviceId();
                existingKeys.add(key);
            }

            List<TimeRecord> toInsert = new ArrayList<>();
            for (TimeRecord record : recordsArray) {
                cal.setTime(record.getDate());
                String key = record.getPackageName() + "|"
                        + String.format(Locale.US, "%04d%02d%02d",
                            cal.get(Calendar.YEAR),
                            cal.get(Calendar.MONTH) + 1,
                            cal.get(Calendar.DAY_OF_MONTH))
                        + "|" + record.getDeviceId();
                if (!existingKeys.contains(key)) {
                    toInsert.add(record);
                }
            }

            // 执行批量插入
            int inserted = toInsert.size();
            int skipped = recordsArray.length - inserted;
            if (inserted > 0) {
                db.timeRecordDao().insertAll(toInsert);
            }
            Log.d(TAG, "Data imported: " + inserted + " records, skipped " + skipped + " duplicates");
            return true;
        } catch (IOException e) {
            Log.e(TAG, "Error importing data: " + e.getMessage());
            return false;
        }
    }

    public static boolean syncData(Context context) {
        try {
            boolean exportSuccess = exportData(context);
            if (!exportSuccess) {
                Log.e(TAG, "Failed to export local data for sync");
                return false;
            }

            boolean importSuccess = importData(context);
            if (importSuccess) {
                Log.d(TAG, "Data synced successfully (local)");
            } else {
                Log.w(TAG, "No new data to import (may have no other device data)");
            }

            if (!AppSettings.getServerUrl(context).isEmpty()
                    || !AppSettings.getAuthToken(context).isEmpty()) {
                ServerSyncClient.sync(context);
            }

            return true;
        } catch (Exception e) {
            Log.e(TAG, "Error syncing data: " + e.getMessage());
            return false;
        }
    }
}