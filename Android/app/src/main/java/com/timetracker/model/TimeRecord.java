package com.timetracker.model;

import androidx.room.Entity;
import androidx.room.PrimaryKey;

import java.util.Date;

@Entity(tableName = "time_records")
public class TimeRecord {
    @PrimaryKey(autoGenerate = true)
    private int id;
    private String packageName;
    private String appName;
    private long usageTime;
    private Date date;
    private String deviceId;
    private Integer categoryId;
    private boolean isForeground;
    private Integer activityId;

    public TimeRecord(String packageName, String appName, long usageTime, Date date, String deviceId, Integer categoryId, boolean isForeground, Integer activityId) {
        this.packageName = packageName;
        this.appName = appName;
        this.usageTime = usageTime;
        this.date = date;
        this.deviceId = deviceId;
        this.categoryId = categoryId;
        this.isForeground = isForeground;
        this.activityId = activityId;
    }

    // Room 兼容：无参构造函数
    public TimeRecord() {}

    public int getId() {
        return id;
    }

    public void setId(int id) {
        this.id = id;
    }

    public String getPackageName() {
        return packageName;
    }

    public void setPackageName(String packageName) {
        this.packageName = packageName;
    }

    public String getAppName() {
        return appName;
    }

    public void setAppName(String appName) {
        this.appName = appName;
    }

    public long getUsageTime() {
        return usageTime;
    }

    public void setUsageTime(long usageTime) {
        this.usageTime = usageTime;
    }

    public Date getDate() {
        return date;
    }

    public void setDate(Date date) {
        this.date = date;
    }

    public String getDeviceId() {
        return deviceId;
    }

    public void setDeviceId(String deviceId) {
        this.deviceId = deviceId;
    }

    public Integer getCategoryId() {
        return categoryId;
    }

    public void setCategoryId(Integer categoryId) {
        this.categoryId = categoryId;
    }

    public boolean isForeground() {
        return isForeground;
    }

    public void setForeground(boolean foreground) {
        isForeground = foreground;
    }

    public Integer getActivityId() { return activityId; }
    public void setActivityId(Integer activityId) { this.activityId = activityId; }
}