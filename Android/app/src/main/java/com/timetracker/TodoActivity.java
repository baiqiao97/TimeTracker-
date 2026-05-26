package com.timetracker;

import android.os.Bundle;
import android.view.View;
import android.view.ViewGroup;
import android.widget.BaseAdapter;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.CompoundButton;
import android.widget.EditText;
import android.widget.ListView;
import android.widget.Spinner;
import android.widget.TextView;
import android.widget.Toast;

import androidx.appcompat.app.AppCompatActivity;

import com.timetracker.database.AppDatabase;
import com.timetracker.model.TodoItem;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;

public class TodoActivity extends AppCompatActivity {

    private EditText etTitle;
    private Spinner spPriority;
    private ListView lvTodos;
    private final TodoAdapter adapter = new TodoAdapter();
    private List<TodoItem> todos = new ArrayList<>();
    private String filterMode = "all";
    private AppDatabase db;
    private SimpleDateFormat sdf = new SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.US);

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_todo);

        db = AppDatabase.getInstance(this);
        etTitle = findViewById(R.id.etTodoTitle);
        spPriority = findViewById(R.id.spPriority);
        lvTodos = findViewById(R.id.lvTodos);

        lvTodos.setAdapter(adapter);
        findViewById(R.id.btnAddTodo).setOnClickListener(v -> addTodo());
        findViewById(R.id.btnFilterAll).setOnClickListener(v -> { filterMode = "all"; loadTodos(); });
        findViewById(R.id.btnFilterPending).setOnClickListener(v -> { filterMode = "pending"; loadTodos(); });
        findViewById(R.id.btnFilterDone).setOnClickListener(v -> { filterMode = "done"; loadTodos(); });

        loadTodos();
    }

    private void loadTodos() {
        new Thread(() -> {
            List<TodoItem> list;
            if ("pending".equals(filterMode)) list = db.todoItemDao().getPending();
            else list = db.todoItemDao().getAll();
            if ("done".equals(filterMode)) {
                List<TodoItem> filtered = new ArrayList<>();
                for (TodoItem t : list) if (t.isCompleted()) filtered.add(t);
                list = filtered;
            } else if ("pending".equals(filterMode)) {
                List<TodoItem> filtered = new ArrayList<>();
                for (TodoItem t : list) if (!t.isCompleted()) filtered.add(t);
                list = filtered;
            }
            todos = list;
            runOnUiThread(() -> adapter.notifyDataSetChanged());
        }).start();
    }

    private void addTodo() {
        String title = etTitle.getText().toString().trim();
        if (title.isEmpty()) { Toast.makeText(this, "请输入标题", Toast.LENGTH_SHORT).show(); return; }
        int priority = 2 - spPriority.getSelectedItemPosition();
        String now = sdf.format(new Date());
        String deviceId = android.provider.Settings.Secure.getString(
                getContentResolver(), android.provider.Settings.Secure.ANDROID_ID);
        new Thread(() -> {
            db.todoItemDao().insert(new TodoItem(title, "", priority, null, now, deviceId));
            runOnUiThread(() -> { etTitle.setText(""); loadTodos(); });
        }).start();
    }

    private void toggleTodo(TodoItem item) {
        boolean newState = !item.isCompleted();
        String completedAt = newState ? sdf.format(new Date()) : null;
        new Thread(() -> {
            db.todoItemDao().toggleCompleted(item.getId(), newState, completedAt);
            runOnUiThread(this::loadTodos);
        }).start();
    }

    private void deleteTodo(TodoItem item) {
        new Thread(() -> {
            db.todoItemDao().deleteById(item.getId());
            runOnUiThread(this::loadTodos);
        }).start();
    }

    private class TodoAdapter extends BaseAdapter {
        @Override public int getCount() { return todos.size(); }
        @Override public Object getItem(int i) { return todos.get(i); }
        @Override public long getItemId(int i) { return todos.get(i).getId(); }

        @Override
        public View getView(int i, View convertView, ViewGroup parent) {
            if (convertView == null)
                convertView = getLayoutInflater().inflate(R.layout.item_todo, parent, false);

            TodoItem item = todos.get(i);
            CheckBox cb = convertView.findViewById(R.id.cbDone);
            TextView tvTitle = convertView.findViewById(R.id.tvTitle);
            TextView tvPriority = convertView.findViewById(R.id.tvPriority);
            TextView tvDueDate = convertView.findViewById(R.id.tvDueDate);
            Button btnDel = convertView.findViewById(R.id.btnDelete);

            cb.setOnCheckedChangeListener(null);
            cb.setChecked(item.isCompleted());
            tvTitle.setText(item.getTitle());
            if (item.isCompleted()) {
                tvTitle.setAlpha(0.5f);
                tvTitle.getPaint().setStrikeThruText(true);
            } else {
                tvTitle.setAlpha(1f);
                tvTitle.getPaint().setStrikeThruText(false);
            }

            String[] priLabels = {"🔴 高", "🟡 中", "🟢 低"};
            int[] priColors = {0xFFef4444, 0xFFf59e0b, 0xFF10b981};
            int idx = Math.max(0, Math.min(2, 2 - item.getPriority()));
            tvPriority.setText(priLabels[idx]);
            tvPriority.setTextColor(priColors[idx]);
            tvPriority.setBackgroundColor((priColors[idx] & 0x00FFFFFF) | 0x20000000);

            tvDueDate.setText(item.getDueDate() != null ? item.getDueDate().substring(0, 10) : "");
            tvDueDate.setVisibility(item.getDueDate() != null ? View.VISIBLE : View.GONE);

            cb.setOnCheckedChangeListener((btn, checked) -> toggleTodo(item));
            btnDel.setOnClickListener(v -> deleteTodo(item));

            return convertView;
        }
    }
}
