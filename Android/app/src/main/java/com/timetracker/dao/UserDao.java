package com.timetracker.dao;

import androidx.room.Dao;
import androidx.room.Insert;
import androidx.room.OnConflictStrategy;
import androidx.room.Query;

import com.timetracker.model.User;

@Dao
public interface UserDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    void insertOrUpdate(User user);

    @Query("SELECT * FROM users WHERE deviceId = :deviceId LIMIT 1")
    User getUserByDevice(String deviceId);
}
