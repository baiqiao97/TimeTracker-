package com.timetracker.model;

import androidx.room.ColumnInfo;
import androidx.room.Entity;
import androidx.room.PrimaryKey;

@Entity(tableName = "todo_items")
public class TodoItem {
    @PrimaryKey(autoGenerate = true)
    private int id;
    private String title;
    private String description;
    @ColumnInfo(name = "is_completed")
    private boolean isCompleted;
    private int priority;
    @ColumnInfo(name = "due_date")
    private String dueDate;
    @ColumnInfo(name = "created_at")
    private String createdAt;
    @ColumnInfo(name = "completed_at")
    private String completedAt;
    @ColumnInfo(name = "device_id")
    private String deviceId;
    @ColumnInfo(name = "user_id")
    private Integer userId;

    public TodoItem(String title, String description, int priority, String dueDate,
                    String createdAt, String deviceId) {
        this.title = title;
        this.description = description;
        this.isCompleted = false;
        this.priority = priority;
        this.dueDate = dueDate;
        this.createdAt = createdAt;
        this.completedAt = null;
        this.deviceId = deviceId;
        this.userId = null;
    }

    public int getId() { return id; }
    public void setId(int id) { this.id = id; }
    public String getTitle() { return title; }
    public void setTitle(String title) { this.title = title; }
    public String getDescription() { return description; }
    public void setDescription(String description) { this.description = description; }
    public boolean isCompleted() { return isCompleted; }
    public void setCompleted(boolean completed) { isCompleted = completed; }
    public int getPriority() { return priority; }
    public void setPriority(int priority) { this.priority = priority; }
    public String getDueDate() { return dueDate; }
    public void setDueDate(String dueDate) { this.dueDate = dueDate; }
    public String getCreatedAt() { return createdAt; }
    public void setCreatedAt(String createdAt) { this.createdAt = createdAt; }
    public String getCompletedAt() { return completedAt; }
    public void setCompletedAt(String completedAt) { this.completedAt = completedAt; }
    public String getDeviceId() { return deviceId; }
    public void setDeviceId(String deviceId) { this.deviceId = deviceId; }
    public Integer getUserId() { return userId; }
    public void setUserId(Integer userId) { this.userId = userId; }
}
