package com.timetracker.database;

import androidx.room.Database;
import androidx.room.Room;
import androidx.room.RoomDatabase;
import androidx.room.TypeConverters;
import androidx.room.migration.Migration;
import androidx.sqlite.db.SupportSQLiteDatabase;

import android.content.Context;

import com.timetracker.dao.ActivityDao;
import com.timetracker.dao.CategoryDao;
import com.timetracker.dao.DeviceDao;
import com.timetracker.dao.TimeRecordDao;
import com.timetracker.dao.UserDao;
import com.timetracker.model.Activity;
import com.timetracker.model.Category;
import com.timetracker.model.Device;
import com.timetracker.model.TimeRecord;
import com.timetracker.model.User;

@Database(entities = {TimeRecord.class, User.class, Device.class, Category.class, Activity.class}, version = 3)
@TypeConverters({DateConverter.class})
public abstract class AppDatabase extends RoomDatabase {
    private static volatile AppDatabase instance;

    public abstract TimeRecordDao timeRecordDao();
    public abstract CategoryDao categoryDao();
    public abstract DeviceDao deviceDao();
    public abstract UserDao userDao();
    public abstract ActivityDao activityDao();

    private static final Migration MIGRATION_1_2 = new Migration(1, 2) {
        @Override
        public void migrate(SupportSQLiteDatabase database) {
            database.execSQL("CREATE TABLE IF NOT EXISTS categories (" +
                    "id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                    "name TEXT NOT NULL, " +
                    "color TEXT DEFAULT '#3498db', " +
                    "description TEXT DEFAULT '')");
        }
    };

    private static final Migration MIGRATION_2_3 = new Migration(2, 3) {
        @Override
        public void migrate(SupportSQLiteDatabase database) {
            database.execSQL("ALTER TABLE time_records ADD COLUMN activityId INTEGER DEFAULT NULL");
            database.execSQL("CREATE TABLE IF NOT EXISTS activities (" +
                    "id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                    "name TEXT NOT NULL, " +
                    "color TEXT DEFAULT '#6c5ce7', " +
                    "icon TEXT DEFAULT '📌')");
        }
    };

    public static AppDatabase getInstance(Context context) {
        if (instance == null) {
            synchronized (AppDatabase.class) {
                if (instance == null) {
                    instance = Room.databaseBuilder(context.getApplicationContext(),
                            AppDatabase.class, "time_tracker_db")
                            .addMigrations(MIGRATION_1_2, MIGRATION_2_3)
                            .fallbackToDestructiveMigration()
                            .build();
                }
            }
        }
        return instance;
    }
}
