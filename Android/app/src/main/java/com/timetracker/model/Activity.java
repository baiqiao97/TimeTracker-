package com.timetracker.model;

import androidx.room.Entity;
import androidx.room.PrimaryKey;

@Entity(tableName = "activities")
public class Activity {
    @PrimaryKey(autoGenerate = true)
    private int id;
    private String name;
    private String color;
    private String icon;

    public Activity(String name, String color, String icon) {
        this.name = name;
        this.color = color;
        this.icon = icon;
    }

    public int getId() { return id; }
    public void setId(int id) { this.id = id; }
    public String getName() { return name; }
    public void setName(String name) { this.name = name; }
    public String getColor() { return color; }
    public void setColor(String color) { this.color = color; }
    public String getIcon() { return icon; }
    public void setIcon(String icon) { this.icon = icon; }

    @Override
    public String toString() { return icon + " " + name; }
}
