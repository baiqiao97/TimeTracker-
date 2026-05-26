package com.timetracker.dao;

import androidx.room.Dao;
import androidx.room.Insert;
import androidx.room.Query;

import com.timetracker.model.Schedule;

import java.util.List;

@Dao
public interface ScheduleDao {
    @Insert
    long insert(Schedule schedule);

    @Insert
    void insertAll(List<Schedule> schedules);

    @Query("SELECT * FROM schedules ORDER BY start_time ASC")
    List<Schedule> getAll();

    @Query("SELECT * FROM schedules WHERE start_time >= :from AND start_time <= :to ORDER BY start_time ASC")
    List<Schedule> getByDateRange(String from, String to);

    @Query("DELETE FROM schedules WHERE id=:id")
    void deleteById(int id);

    @Query("UPDATE schedules SET title=:title, description=:desc, start_time=:st, end_time=:et, is_all_day=:ia, color=:c WHERE id=:id")
    void update(int id, String title, String desc, String st, String et, boolean ia, String c);
}
