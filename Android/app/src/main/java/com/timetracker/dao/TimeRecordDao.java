package com.timetracker.dao;

import androidx.room.Dao;
import androidx.room.Insert;
import androidx.room.Query;

import com.timetracker.model.AppUsageSummary;
import com.timetracker.model.TimeRecord;

import java.util.Date;
import java.util.List;

@Dao
public interface TimeRecordDao {
    @Insert
    void insert(TimeRecord timeRecord);

    @Insert
    void insertAll(List<TimeRecord> records);

    @Query("SELECT * FROM time_records WHERE date BETWEEN :startDate AND :endDate ORDER BY date DESC")
    List<TimeRecord> getRecordsByDateRange(Date startDate, Date endDate);

    @Query("SELECT tr.packageName, tr.appName, SUM(tr.usageTime) as totalUsage, c.name as categoryName, c.color as categoryColor " +
            "FROM time_records tr LEFT JOIN categories c ON tr.categoryId = c.id " +
            "WHERE tr.date BETWEEN :startDate AND :endDate " +
            "GROUP BY tr.packageName ORDER BY totalUsage DESC")
    List<AppUsageSummary> getTopApps(Date startDate, Date endDate);

    @Query("SELECT SUM(usageTime) FROM time_records WHERE date BETWEEN :startDate AND :endDate")
    long getTotalUsageTime(Date startDate, Date endDate);

    @Query("DELETE FROM time_records WHERE date < :cutoffDate")
    void deleteOldRecords(Date cutoffDate);
}