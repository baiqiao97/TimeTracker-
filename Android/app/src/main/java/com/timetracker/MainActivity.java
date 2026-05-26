package com.timetracker;

import android.app.AppOpsManager;
import android.content.Context;
import android.content.Intent;
import android.os.Bundle;
import android.provider.Settings;
import android.view.Menu;
import android.view.MenuItem;
import android.view.View;
import android.view.ViewGroup;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.BaseAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ListView;
import android.widget.Spinner;
import android.widget.TextView;
import android.widget.Toast;

import androidx.appcompat.app.AlertDialog;
import androidx.appcompat.app.AppCompatActivity;

import com.timetracker.database.AppDatabase;
import com.timetracker.model.Activity;
import com.timetracker.model.AppUsageSummary;
import com.timetracker.utils.AppSettings;
import com.timetracker.utils.DataSyncUtils;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Calendar;
import java.util.Date;
import java.util.List;
import java.util.Locale;

public class MainActivity extends AppCompatActivity {

    private TextView tvTotalUsage;
    private ListView lvTopApps;
    private Button btnDaily, btnWeekly, btnMonthly;
    private Button btnPause;

    private String currentRange = "daily";
    private boolean isPaused = false;

    private final TopAppsAdapter adapter = new TopAppsAdapter();

    // Activity mode
    private LinearLayout activityBar;
    private Spinner spActivity;
    private Button btnAddActivityMain;
    private List<Activity> activityList = new ArrayList<>();

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        tvTotalUsage = findViewById(R.id.tv_total_usage);
        lvTopApps = findViewById(R.id.lv_top_apps);
        btnDaily = findViewById(R.id.btn_daily);
        btnWeekly = findViewById(R.id.btn_weekly);
        btnMonthly = findViewById(R.id.btn_monthly);
        btnPause = findViewById(R.id.btn_pause);
        Button btnTodo = findViewById(R.id.btn_todo);
        Button btnSchedule = findViewById(R.id.btn_schedule);
        Button btnChart = findViewById(R.id.btn_chart);
        Button btnCategories = findViewById(R.id.btn_categories);
        Button btnSettings = findViewById(R.id.btn_settings);

        // Activity mode UI
        activityBar = findViewById(R.id.activityBar);
        spActivity = findViewById(R.id.spActivity);
        btnAddActivityMain = findViewById(R.id.btnAddActivityMain);

        lvTopApps.setAdapter(adapter);

        // 日期范围按钮
        btnDaily.setOnClickListener(v -> switchRange("daily"));
        btnWeekly.setOnClickListener(v -> switchRange("weekly"));
        btnMonthly.setOnClickListener(v -> switchRange("monthly"));

        // 暂停/恢复
        btnPause.setOnClickListener(v -> {
            Intent intent = new Intent(this, TrackingService.class);
            if (isPaused) {
                intent.setAction("RESUME");
                btnPause.setText("⏸ 暂停");
                btnPause.setTextColor(0xFFf59e0b);
                btnPause.setBackgroundColor(0xFFFFFBEB);
                isPaused = false;
            } else {
                intent.setAction("PAUSE");
                btnPause.setText("▶ 恢复");
                btnPause.setTextColor(0xFF10b981);
                btnPause.setBackgroundColor(0xFFECFDF5);
                isPaused = true;
            }
            if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
                startForegroundService(intent);
            } else {
                startService(intent);
            }
        });

        btnTodo.setOnClickListener(v -> {
            startActivity(new Intent(this, TodoActivity.class));
        });

        btnSchedule.setOnClickListener(v -> {
            startActivity(new Intent(this, ScheduleActivity.class));
        });

        btnChart.setOnClickListener(v -> {
            Intent intent = new Intent(this, StatsActivity.class);
            intent.putExtra("range", currentRange);
            startActivity(intent);
        });

        btnCategories.setOnClickListener(v -> {
            Intent intent = new Intent(this, CategoryManageActivity.class);
            startActivity(intent);
        });

        // Settings button
        btnSettings.setOnClickListener(v -> {
            startActivity(new Intent(this, SettingsActivity.class));
        });

        // Activity mode selector
        refreshActivityBar();
        spActivity.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(AdapterView<?> parent, View view, int pos, long id) {
                if (pos < activityList.size()) {
                    AppSettings.setCurrentActivityId(MainActivity.this, activityList.get(pos).getId());
                }
            }
            @Override public void onNothingSelected(AdapterView<?> parent) {}
        });
        btnAddActivityMain.setOnClickListener(v -> showAddActivityDialog());

        // 检查权限并启动追踪
        if (!hasUsageStatsPermission()) {
            requestUsageStatsPermission();
        } else {
            startTrackingService();
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        // 刷新数据显示（不再弹出重复 Toast）
        loadStats();
    }

    private void refreshActivityBar() {
        boolean active = AppSettings.isActivityMode(this);
        activityBar.setVisibility(active ? View.VISIBLE : View.GONE);
        if (!active) return;

        new Thread(() -> {
            AppDatabase db = AppDatabase.getInstance(this);
            activityList = db.activityDao().getAll();
            List<String> names = new ArrayList<>();
            for (Activity a : activityList) names.add(a.toString());
            int selectedId = AppSettings.getCurrentActivityId(this);
            int selIdx = 0;
            for (int i = 0; i < activityList.size(); i++) {
                if (activityList.get(i).getId() == selectedId) { selIdx = i; break; }
            }
            final int finalIdx = selIdx;
            runOnUiThread(() -> {
                ArrayAdapter<String> adapter = new ArrayAdapter<>(this,
                    android.R.layout.simple_spinner_item, names);
                adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
                spActivity.setAdapter(adapter);
                if (finalIdx < activityList.size()) spActivity.setSelection(finalIdx);
            });
        }).start();
    }

    private void showAddActivityDialog() {
        AlertDialog.Builder builder = new AlertDialog.Builder(this);
        builder.setTitle("新建活动");
        final EditText input = new EditText(this);
        input.setHint("活动名称");
        input.setPadding(32, 16, 32, 16);
        builder.setView(input);
        builder.setPositiveButton("添加", (dialog, which) -> {
            String name = input.getText().toString().trim();
            if (!name.isEmpty()) {
                new Thread(() -> {
                    AppDatabase.getInstance(this).activityDao().insert(new Activity(name, "#6c5ce7", "📌"));
                    runOnUiThread(this::refreshActivityBar);
                }).start();
            }
        });
        builder.setNegativeButton("取消", null);
        builder.show();
    }

    private void switchRange(String range) {
        currentRange = range;
        btnDaily.setSelected(range.equals("daily"));
        btnWeekly.setSelected(range.equals("weekly"));
        btnMonthly.setSelected(range.equals("monthly"));
        // Update text color
        int activeTc = 0xFFffffff, inactiveTc = 0xFF6b7280;
        btnDaily.setTextColor(range.equals("daily") ? activeTc : inactiveTc);
        btnWeekly.setTextColor(range.equals("weekly") ? activeTc : inactiveTc);
        btnMonthly.setTextColor(range.equals("monthly") ? activeTc : inactiveTc);
        loadStats();
    }

    private void loadStats() {
        new Thread(() -> {
            try {
                AppDatabase db = AppDatabase.getInstance(this);
                Calendar cal = Calendar.getInstance();
                cal.set(Calendar.HOUR_OF_DAY, 23);
                cal.set(Calendar.MINUTE, 59);
                cal.set(Calendar.SECOND, 59);
                cal.set(Calendar.MILLISECOND, 999);
                Date endDate = cal.getTime();

                Date startDate;
                switch (currentRange) {
                    case "weekly":
                        cal.add(Calendar.DAY_OF_YEAR, -7);
                        startDate = cal.getTime();
                        break;
                    case "monthly":
                        cal.add(Calendar.MONTH, -1);
                        startDate = cal.getTime();
                        break;
                    default: // daily
                        cal.set(Calendar.HOUR_OF_DAY, 0);
                        cal.set(Calendar.MINUTE, 0);
                        cal.set(Calendar.SECOND, 0);
                        cal.set(Calendar.MILLISECOND, 0);
                        startDate = cal.getTime();
                        break;
                }

                long totalMs = db.timeRecordDao().getTotalUsageTime(startDate, endDate);
                List<AppUsageSummary> topApps = db.timeRecordDao().getTopApps(startDate, endDate);

                runOnUiThread(() -> {
                    // 更新总时长
                    long totalMinutes = totalMs / 1000 / 60;
                    long hours = totalMinutes / 60;
                    long minutes = totalMinutes % 60;
                    tvTotalUsage.setText(String.format(Locale.getDefault(), "%d 小时 %d 分钟", hours, minutes));

                    // 更新应用列表
                    adapter.setData(topApps != null ? topApps : new ArrayList<>());
                });
            } catch (Exception e) {
                runOnUiThread(() -> Toast.makeText(this, "加载数据失败: " + e.getMessage(), Toast.LENGTH_SHORT).show());
            }
        }).start();
    }

    @Override
    public boolean onCreateOptionsMenu(Menu menu) {
        getMenuInflater().inflate(R.menu.menu_main, menu);
        return true;
    }

    @Override
    public boolean onOptionsItemSelected(MenuItem item) {
        int id = item.getItemId();

        if (id == R.id.action_settings) {
            startActivity(new Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS));
            return true;
        } else if (id == R.id.action_export) {
            runBackgroundTask(() -> DataSyncUtils.exportData(this), "Data exported", "Export failed");
            return true;
        } else if (id == R.id.action_import) {
            runBackgroundTask(() -> {
                boolean result = DataSyncUtils.importData(this);
                if (result) loadStats(); // 刷新列表
                return result;
            }, "Data imported", "Import failed");
            return true;
        } else if (id == R.id.action_sync) {
            Toast.makeText(this, "Syncing data...", Toast.LENGTH_SHORT).show();
            runBackgroundTask(() -> {
                boolean result = DataSyncUtils.syncData(this);
                if (result) loadStats(); // 刷新列表
                return result;
            }, "Data synced", "Sync failed");
            return true;
        }

        return super.onOptionsItemSelected(item);
    }

    private void runBackgroundTask(TaskRunner task, String successMsg, String failMsg) {
        new Thread(() -> {
            boolean success = task.run();
            runOnUiThread(() -> {
                Toast.makeText(this, success ? successMsg : failMsg, Toast.LENGTH_SHORT).show();
            });
        }).start();
    }

    private boolean hasUsageStatsPermission() {
        AppOpsManager appOps = (AppOpsManager) getSystemService(Context.APP_OPS_SERVICE);
        int mode = appOps.checkOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS,
                android.os.Process.myUid(), getPackageName());
        return mode == AppOpsManager.MODE_ALLOWED;
    }

    private void requestUsageStatsPermission() {
        startActivity(new Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS));
    }

    private void startTrackingService() {
        Intent serviceIntent = new Intent(this, TrackingService.class);
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
            startForegroundService(serviceIntent);
        } else {
            startService(serviceIntent);
        }
    }

    // --- Adapter ---

    private class TopAppsAdapter extends BaseAdapter {
        private List<AppUsageSummary> data = new ArrayList<>();

        void setData(List<AppUsageSummary> newData) {
            data = newData;
            notifyDataSetChanged();
        }

        @Override
        public int getCount() { return data.size(); }

        @Override
        public Object getItem(int i) { return data.get(i); }

        @Override
        public long getItemId(int i) { return i; }

        @Override
        public View getView(int i, View convertView, ViewGroup parent) {
            ViewHolder holder;
            if (convertView == null) {
                convertView = getLayoutInflater().inflate(R.layout.item_app_usage, parent, false);
                holder = new ViewHolder();
                holder.rankView = convertView.findViewById(R.id.tv_rank);
                holder.nameView = convertView.findViewById(R.id.tv_app_name);
                holder.timeView = convertView.findViewById(R.id.tv_usage_time);
                holder.catColorView = convertView.findViewById(R.id.v_category_dot);
                holder.catNameView = convertView.findViewById(R.id.tv_category);
                convertView.setTag(holder);
            } else {
                holder = (ViewHolder) convertView.getTag();
            }

            AppUsageSummary item = data.get(i);
            holder.rankView.setText(String.valueOf(i + 1));
            holder.nameView.setText(item.appName);
            long minutes = item.totalUsage / 1000 / 60;
            holder.timeView.setText(String.format(Locale.getDefault(),
                    "%dh %dm", minutes / 60, minutes % 60));

            // 显示分类颜色和名称
            String catName = item.categoryName != null ? item.categoryName : "未分类";
            holder.catNameView.setText(catName);
            try {
                int color = item.categoryColor != null
                        ? android.graphics.Color.parseColor(item.categoryColor)
                        : 0xFF888888;
                holder.catColorView.setBackgroundColor(color);
                // 前三名特殊高亮
                if (i < 3) {
                    holder.rankView.getBackground().setTint(color);
                }
            } catch (Exception e) {
                holder.catColorView.setBackgroundColor(0xFF888888);
            }

            return convertView;
        }

        class ViewHolder {
            TextView rankView, nameView, timeView, catNameView;
            View catColorView;
        }
    }

    interface TaskRunner {
        boolean run();
    }
}
