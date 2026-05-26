package com.timetracker.dao;

import androidx.room.Dao;
import androidx.room.Insert;
import androidx.room.OnConflictStrategy;
import androidx.room.Query;
import androidx.room.Update;

import com.timetracker.model.Device;

@Dao
public interface DeviceDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    void insertOrUpdate(Device device);

    @Query("SELECT * FROM devices WHERE deviceId = :deviceId LIMIT 1")
    Device getDevice(String deviceId);

    @Query("UPDATE devices SET lastSync = :lastSync WHERE deviceId = :deviceId")
    void updateLastSync(String deviceId, String lastSync);
}
