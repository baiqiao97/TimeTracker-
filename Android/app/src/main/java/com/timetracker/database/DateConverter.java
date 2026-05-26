package com.timetracker.database;

import androidx.room.TypeConverter;

import java.util.Date;

/**
 * Room 类型转换器：Date <-> Long (timestamp)
 */
public class DateConverter {
    @TypeConverter
    public static Date fromTimestamp(Long value) {
        return value == null ? null : new Date(value);
    }

    @TypeConverter
    public static Long dateToTimestamp(Date date) {
        return date == null ? null : date.getTime();
    }
}
