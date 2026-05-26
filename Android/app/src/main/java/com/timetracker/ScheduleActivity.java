package com.timetracker;

import android.graphics.Color;
import android.os.Bundle;
import android.view.View;
import android.view.ViewGroup;
import android.widget.BaseAdapter;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.DatePicker;
import android.widget.EditText;
import android.widget.ListView;
import android.widget.Spinner;
import android.widget.TextView;
import android.widget.Toast;

import androidx.appcompat.app.AppCompatActivity;

import com.timetracker.database.AppDatabase;
import com.timetracker.model.Schedule;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Calendar;
import java.util.Date;
import java.util.List;
import java.util.Locale;

public class ScheduleActivity extends AppCompatActivity {

    private EditText etTitle, etTime;
    private DatePicker dpDate;
    private CheckBox chkAllDay;
    private Spinner spColor;
    private ListView lvSchedules;
    private final ScheduleAdapter adapter = new ScheduleAdapter();
    private List<Schedule> schedules = new ArrayList<>();
    private AppDatabase db;
    private SimpleDateFormat sdf = new SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.US);
    private SimpleDateFormat dateFmt = new SimpleDateFormat("yyyy-MM-dd", Locale.US);
    private static final String[] COLORS = {"#6c5ce7","#10b981","#f59e0b","#ef4444","#3b82f6"};

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_schedule);

        db = AppDatabase.getInstance(this);
        etTitle = findViewById(R.id.etScheduleTitle);
        etTime = findViewById(R.id.etScheduleTime);
        dpDate = findViewById(R.id.dpScheduleDate);
        chkAllDay = findViewById(R.id.chkAllDay);
        spColor = findViewById(R.id.spColor);
        lvSchedules = findViewById(R.id.lvSchedules);

        lvSchedules.setAdapter(adapter);
        findViewById(R.id.btnAddSchedule).setOnClickListener(v -> addSchedule());
        loadSchedules();
    }

    private void loadSchedules() {
        new Thread(() -> {
            schedules = db.scheduleDao().getAll();
            runOnUiThread(() -> adapter.notifyDataSetChanged());
        }).start();
    }

    private void addSchedule() {
        String title = etTitle.getText().toString().trim();
        if (title.isEmpty()) { Toast.makeText(this, "请输入标题", Toast.LENGTH_SHORT).show(); return; }

        Calendar cal = Calendar.getInstance();
        cal.set(dpDate.getYear(), dpDate.getMonth(), dpDate.getDayOfMonth());
        String dateStr = dateFmt.format(cal.getTime());

        boolean allDay = chkAllDay.isChecked();
        String color = COLORS[spColor.getSelectedItemPosition()];
        String startTimeStr, endTimeStr;

        if (allDay) {
            startTimeStr = dateStr + " 00:00:00";
            endTimeStr = dateStr + " 23:59:59";
        } else {
            String timeInput = etTime.getText().toString().trim();
            String[] parts = timeInput.split("-");
            startTimeStr = dateStr + " " + parts[0].trim() + ":00";
            endTimeStr = parts.length > 1 ? dateStr + " " + parts[1].trim() + ":00" : null;
        }

        String now = sdf.format(new Date());
        String deviceId = android.provider.Settings.Secure.getString(
                getContentResolver(), android.provider.Settings.Secure.ANDROID_ID);

        new Thread(() -> {
            db.scheduleDao().insert(new Schedule(title, "", startTimeStr, endTimeStr, allDay, color, now, deviceId));
            runOnUiThread(() -> { etTitle.setText(""); etTime.setText("08:00 - 10:00"); loadSchedules(); });
        }).start();
    }

    private void deleteSchedule(Schedule item) {
        new Thread(() -> {
            db.scheduleDao().deleteById(item.getId());
            runOnUiThread(this::loadSchedules);
        }).start();
    }

    private class ScheduleAdapter extends BaseAdapter {
        @Override public int getCount() { return schedules.size(); }
        @Override public Object getItem(int i) { return schedules.get(i); }
        @Override public long getItemId(int i) { return schedules.get(i).getId(); }

        @Override
        public View getView(int i, View convertView, ViewGroup parent) {
            if (convertView == null)
                convertView = getLayoutInflater().inflate(R.layout.item_schedule, parent, false);

            Schedule item = schedules.get(i);
            View colorBar = convertView.findViewById(R.id.vColorBar);
            TextView tvTitle = convertView.findViewById(R.id.tvTitle);
            TextView tvTime = convertView.findViewById(R.id.tvTime);
            TextView tvDesc = convertView.findViewById(R.id.tvDesc);
            Button btnDel = convertView.findViewById(R.id.btnDelete);

            try { colorBar.setBackgroundColor(Color.parseColor(item.getColor())); }
            catch (Exception ex) { colorBar.setBackgroundColor(0xFF6c5ce7); }

            tvTitle.setText(item.getTitle());
            String timeText = item.isAllDay() ? "全天" : item.getStartTime().substring(11, 16)
                    + (item.getEndTime() != null ? " - " + item.getEndTime().substring(11, 16) : "");
            tvTime.setText(item.getStartTime().substring(0, 10) + "  " + timeText);
            tvDesc.setText(item.getDescription());
            tvDesc.setVisibility(item.getDescription() != null && !item.getDescription().isEmpty() ? View.VISIBLE : View.GONE);

            btnDel.setOnClickListener(v -> deleteSchedule(item));
            return convertView;
        }
    }
}
