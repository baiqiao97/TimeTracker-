package com.timetracker.model;

import androidx.room.Entity;
import androidx.room.PrimaryKey;

@Entity(tableName = "devices")
public class Device {
    @PrimaryKey
    private String deviceId;
    private String deviceName;
    private String platform;
    private String lastSync;

    public Device() {}

    public Device(String deviceId, String deviceName, String platform, String lastSync) {
        this.deviceId = deviceId;
        this.deviceName = deviceName;
        this.platform = platform;
        this.lastSync = lastSync;
    }

    public String getDeviceId() {
        return deviceId;
    }

    public void setDeviceId(String deviceId) {
        this.deviceId = deviceId;
    }

    public String getDeviceName() {
        return deviceName;
    }

    public void setDeviceName(String deviceName) {
        this.deviceName = deviceName;
    }

    public String getPlatform() {
        return platform;
    }

    public void setPlatform(String platform) {
        this.platform = platform;
    }

    public String getLastSync() {
        return lastSync;
    }

    public void setLastSync(String lastSync) {
        this.lastSync = lastSync;
    }
}