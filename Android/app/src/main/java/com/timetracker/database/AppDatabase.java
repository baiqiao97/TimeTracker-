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
import com.timetracker.dao.ScheduleDao;
import com.timetracker.dao.TimeRecordDao;
import com.timetracker.dao.TodoItemDao;
import com.timetracker.dao.UserDao;
import com.timetracker.model.Activity;
import com.timetracker.model.Category;
import com.timetracker.model.Device;
import com.timetracker.model.Schedule;
import com.timetracker.model.TimeRecord;
import com.timetracker.model.TodoItem;
import com.timetracker.model.User;

@Database(entities = {TimeRecord.class, User.class, Device.class, Category.class, Activity.class, TodoItem.class, Schedule.class}, version = 5)
@TypeConverters({DateConverter.class})
public abstract class AppDatabase extends RoomDatabase {
    private static volatile AppDatabase instance;

    public abstract TimeRecordDao timeRecordDao();
    public abstract CategoryDao categoryDao();
    public abstract DeviceDao deviceDao();
    public abstract UserDao userDao();
    public abstract ActivityDao activityDao();
    public abstract TodoItemDao todoItemDao();
    public abstract ScheduleDao scheduleDao();

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

    private static final Migration MIGRATION_3_4 = new Migration(3, 4) {
        @Override
        public void migrate(SupportSQLiteDatabase database) {
            database.execSQL("CREATE TABLE IF NOT EXISTS todo_items (" +
                    "id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                    "title TEXT NOT NULL, " +
                    "description TEXT DEFAULT '', " +
                    "is_completed INTEGER NOT NULL DEFAULT 0, " +
                    "priority INTEGER NOT NULL DEFAULT 0, " +
                    "due_date TEXT DEFAULT '', " +
                    "created_at TEXT DEFAULT '', " +
                    "completed_at TEXT DEFAULT NULL, " +
                    "device_id TEXT DEFAULT '', " +
                    "user_id INTEGER DEFAULT NULL)");
            database.execSQL("CREATE TABLE IF NOT EXISTS schedules (" +
                    "id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                    "title TEXT NOT NULL, " +
                    "description TEXT DEFAULT '', " +
                    "start_time TEXT DEFAULT '', " +
                    "end_time TEXT DEFAULT '', " +
                    "is_all_day INTEGER NOT NULL DEFAULT 0, " +
                    "color TEXT DEFAULT '#3498db', " +
                    "created_at TEXT DEFAULT '', " +
                    "device_id TEXT DEFAULT '', " +
                    "user_id INTEGER DEFAULT NULL)");
        }
    };

    private static final Migration MIGRATION_4_5 = new Migration(4, 5) {
        @Override
        public void migrate(SupportSQLiteDatabase database) {
            // 将旧的 users(name, device_id) 迁移到新的 users(username, password, token, expires_at, created_at)
            database.execSQL("CREATE TABLE IF NOT EXISTS users_new (" +
                    "id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                    "username TEXT UNIQUE NOT NULL, " +
                    "password TEXT NOT NULL, " +
                    "token TEXT, " +
                    "expires_at TEXT, " +
                    "created_at TEXT)");
            // 迁移旧数据：name → username
            database.execSQL("INSERT OR IGNORE INTO users_new(username, password, created_at) " +
                    "SELECT name, '', datetime('now') FROM users");
            database.execSQL("DROP TABLE users");
            database.execSQL("ALTER TABLE users_new RENAME TO users");
        }
    };

    public static AppDatabase getInstance(Context context) {
        if (instance == null) {
            synchronized (AppDatabase.class) {
                if (instance == null) {
                    instance = Room.databaseBuilder(context.getApplicationContext(),
                            AppDatabase.class, "time_tracker_db")
                            .addMigrations(MIGRATION_1_2, MIGRATION_2_3, MIGRATION_3_4, MIGRATION_4_5)
                            .fallbackToDestructiveMigration()
                            .build();
                }
            }
        }
        return instance;
    }
}
