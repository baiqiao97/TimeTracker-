package com.timetracker;

import android.os.Bundle;
import android.view.View;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ListView;
import android.widget.RadioGroup;
import android.widget.Toast;

import androidx.appcompat.app.AppCompatActivity;

import com.timetracker.database.AppDatabase;
import com.timetracker.model.Activity;
import com.timetracker.utils.AppSettings;

import java.util.ArrayList;
import java.util.List;

public class SettingsActivity extends AppCompatActivity {

    private RadioGroup rgMode;
    private EditText etActivityName;
    private Button btnAddActivity;
    private ListView lvActivities;
    private View activitySection;

    private AppDatabase db;
    private List<Activity> activities = new ArrayList<>();
    private ArrayAdapter<String> adapter;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_settings);

        db = AppDatabase.getInstance(this);
        rgMode = findViewById(R.id.rgTrackingMode);
        etActivityName = findViewById(R.id.etActivityName);
        btnAddActivity = findViewById(R.id.btnAddActivity);
        lvActivities = findViewById(R.id.lvActivities);
        activitySection = findViewById(R.id.activitySection);
        Button btnSave = findViewById(R.id.btnSaveSettings);

        // Load current settings
        if (AppSettings.isActivityMode(this)) {
            rgMode.check(R.id.rbActivity);
            activitySection.setVisibility(View.VISIBLE);
            loadActivities();
        } else {
            rgMode.check(R.id.rbSimple);
        }

        rgMode.setOnCheckedChangeListener((group, checkedId) -> {
            if (checkedId == R.id.rbActivity) {
                activitySection.setVisibility(View.VISIBLE);
                loadActivities();
            } else {
                activitySection.setVisibility(View.GONE);
            }
        });

        btnAddActivity.setOnClickListener(v -> {
            String name = etActivityName.getText().toString().trim();
            if (name.isEmpty()) { Toast.makeText(this, "请输入名称", Toast.LENGTH_SHORT).show(); return; }
            new Thread(() -> {
                db.activityDao().insert(new Activity(name, "#6c5ce7", "📌"));
                runOnUiThread(() -> {
                    etActivityName.setText("");
                    loadActivities();
                });
            }).start();
        });

        lvActivities.setOnItemLongClickListener((parent, view, position, id) -> {
            Activity act = activities.get(position);
            new Thread(() -> {
                db.activityDao().delete(act.getId());
                runOnUiThread(this::loadActivities);
            }).start();
            return true;
        });

        btnSave.setOnClickListener(v -> {
            String mode = rgMode.getCheckedRadioButtonId() == R.id.rbActivity ? "activity" : "simple";
            AppSettings.setTrackingMode(this, mode);
            Toast.makeText(this, "设置已保存", Toast.LENGTH_SHORT).show();
            finish();
        });

        // Init default activities if none
        new Thread(() -> {
            List<Activity> list = db.activityDao().getAll();
            if (list.isEmpty()) {
                db.activityDao().insert(new Activity("学习", "#6c5ce7", "📚"));
                db.activityDao().insert(new Activity("工作", "#10b981", "💼"));
                db.activityDao().insert(new Activity("娱乐", "#f59e0b", "🎮"));
                db.activityDao().insert(new Activity("社交", "#3b82f6", "💬"));
            }
        }).start();
    }

    private void loadActivities() {
        new Thread(() -> {
            activities = db.activityDao().getAll();
            List<String> names = new ArrayList<>();
            for (Activity a : activities) names.add(a.toString() + "  (长按删除)");
            runOnUiThread(() -> {
                adapter = new ArrayAdapter<>(this, android.R.layout.simple_list_item_1, names);
                lvActivities.setAdapter(adapter);
            });
        }).start();
    }
}
