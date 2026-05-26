package com.timetracker.dao;

import androidx.room.Dao;
import androidx.room.Insert;
import androidx.room.Query;

import com.timetracker.model.Activity;

import java.util.List;

@Dao
public interface ActivityDao {
    @Query("SELECT * FROM activities ORDER BY id")
    List<Activity> getAll();

    @Insert
    void insert(Activity activity);

    @Query("DELETE FROM activities WHERE id = :id")
    void delete(int id);
}
