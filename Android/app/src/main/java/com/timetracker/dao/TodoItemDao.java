package com.timetracker.dao;

import androidx.room.Dao;
import androidx.room.Insert;
import androidx.room.Query;

import com.timetracker.model.TodoItem;

import java.util.List;

@Dao
public interface TodoItemDao {
    @Insert
    long insert(TodoItem item);

    @Insert
    void insertAll(List<TodoItem> items);

    @Query("SELECT * FROM todo_items ORDER BY is_completed ASC, priority DESC, due_date ASC, created_at DESC")
    List<TodoItem> getAll();

    @Query("SELECT * FROM todo_items WHERE is_completed = 0 ORDER BY priority DESC, due_date ASC, created_at DESC")
    List<TodoItem> getPending();

    @Query("UPDATE todo_items SET is_completed=:completed, completed_at=:completedAt WHERE id=:id")
    void toggleCompleted(int id, boolean completed, String completedAt);

    @Query("DELETE FROM todo_items WHERE id=:id")
    void deleteById(int id);

    @Query("UPDATE todo_items SET title=:title, description=:desc, priority=:pri, due_date=:due WHERE id=:id")
    void update(int id, String title, String desc, int pri, String due);
}
