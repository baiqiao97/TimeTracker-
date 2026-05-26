package com.timetracker;

import android.os.Bundle;
import android.widget.Toast;

import androidx.appcompat.app.AppCompatActivity;

import com.github.mikephil.charting.charts.BarChart;
import com.github.mikephil.charting.charts.PieChart;
import com.github.mikephil.charting.components.XAxis;
import com.github.mikephil.charting.data.BarData;
import com.github.mikephil.charting.data.BarDataSet;
import com.github.mikephil.charting.data.BarEntry;
import com.github.mikephil.charting.data.PieData;
import com.github.mikephil.charting.data.PieDataSet;
import com.github.mikephil.charting.data.PieEntry;
import com.github.mikephil.charting.formatter.IndexAxisValueFormatter;
import com.github.mikephil.charting.utils.ColorTemplate;
import com.timetracker.database.AppDatabase;
import com.timetracker.model.AppUsageSummary;
import com.timetracker.model.TimeRecord;

import java.util.ArrayList;
import java.util.Calendar;
import java.util.Date;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

public class StatsActivity extends AppCompatActivity {

    private BarChart barChart;
    private PieChart pieChart;
    private String range = "daily";

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_stats);

        barChart = findViewById(R.id.barChart);
        pieChart = findViewById(R.id.pieChart);

        if (barChart == null || pieChart == null) {
            Toast.makeText(this, "Chart initialization failed", Toast.LENGTH_SHORT).show();
            finish();
            return;
        }

        // 从 Intent 获取统计范围
        range = getIntent().getStringExtra("range");
        if (range == null) range = "daily";

        loadStatsByRange();
        loadTopApps();
    }

    private void loadStatsByRange() {
        new Thread(() -> {
            try {
                AppDatabase db = AppDatabase.getInstance(this);
                Calendar calendar = Calendar.getInstance();

                Date endDate;
                Date startDate;

                switch (range) {
                    case "weekly":
                        calendar.set(Calendar.HOUR_OF_DAY, 23);
                        calendar.set(Calendar.MINUTE, 59);
                        calendar.set(Calendar.SECOND, 59);
                        endDate = calendar.getTime();
                        calendar.add(Calendar.DAY_OF_YEAR, -7);
                        startDate = calendar.getTime();
                        break;
                    case "monthly":
                        calendar.set(Calendar.HOUR_OF_DAY, 23);
                        calendar.set(Calendar.MINUTE, 59);
                        calendar.set(Calendar.SECOND, 59);
                        endDate = calendar.getTime();
                        calendar.add(Calendar.MONTH, -1);
                        startDate = calendar.getTime();
                        break;
                    default: // daily
                        calendar.set(Calendar.HOUR_OF_DAY, 23);
                        calendar.set(Calendar.MINUTE, 59);
                        calendar.set(Calendar.SECOND, 59);
                        endDate = calendar.getTime();
                        calendar.set(Calendar.HOUR_OF_DAY, 0);
                        calendar.set(Calendar.MINUTE, 0);
                        calendar.set(Calendar.SECOND, 0);
                        startDate = calendar.getTime();
                        break;
                }

                List<TimeRecord> records = db.timeRecordDao().getRecordsByDateRange(startDate, endDate);
                runOnUiThread(() -> {
                    if (records == null || records.isEmpty()) {
                        Toast.makeText(this, "No data available", Toast.LENGTH_SHORT).show();
                        return;
                    }
                    displayStats(records);
                });
            } catch (Exception e) {
                runOnUiThread(() ->
                        Toast.makeText(this, "Error loading data: " + e.getMessage(), Toast.LENGTH_SHORT).show());
            }
        }).start();
    }

    private void loadTopApps() {
        new Thread(() -> {
            try {
                AppDatabase db = AppDatabase.getInstance(this);
                Calendar calendar = Calendar.getInstance();
                calendar.set(Calendar.HOUR_OF_DAY, 0);
                calendar.set(Calendar.MINUTE, 0);
                calendar.set(Calendar.SECOND, 0);
                calendar.set(Calendar.MILLISECOND, 0);
                Date startDate = calendar.getTime();
                calendar.add(Calendar.DAY_OF_YEAR, 1);
                Date endDate = calendar.getTime();

                List<AppUsageSummary> topApps = db.timeRecordDao().getTopApps(startDate, endDate);
                runOnUiThread(() -> {
                    if (topApps == null || topApps.isEmpty()) {
                        Toast.makeText(this, "No app usage data available", Toast.LENGTH_SHORT).show();
                        return;
                    }
                    displayTopApps(topApps);
                });
            } catch (Exception e) {
                runOnUiThread(() ->
                        Toast.makeText(this, "Error loading data: " + e.getMessage(), Toast.LENGTH_SHORT).show());
            }
        }).start();
    }

    private void displayStats(List<TimeRecord> records) {
        // 按应用名称聚合使用时间
        Map<String, Long> aggregated = new HashMap<>();
        for (TimeRecord record : records) {
            String app = record.getAppName() != null ? record.getAppName() : "Unknown";
            aggregated.merge(app, record.getUsageTime(), Long::sum);
        }

        // 按使用时间降序排列
        List<Map.Entry<String, Long>> sorted = new ArrayList<>(aggregated.entrySet());
        sorted.sort((a, b) -> Long.compare(b.getValue(), a.getValue()));

        List<BarEntry> entries = new ArrayList<>();
        List<String> labels = new ArrayList<>();

        int count = 0;
        for (Map.Entry<String, Long> entry : sorted) {
            if (count >= 15) break; // 最多显示15个应用
            float minutes = entry.getValue() / 1000f / 60f;
            entries.add(new BarEntry(count, minutes));
            labels.add(entry.getKey());
            count++;
        }

        String rangeLabel;
        switch (range) {
            case "weekly": rangeLabel = "Weekly"; break;
            case "monthly": rangeLabel = "Monthly"; break;
            default: rangeLabel = "Daily"; break;
        }

        BarDataSet dataSet = new BarDataSet(entries, rangeLabel + " App Usage (minutes)");
        dataSet.setColors(ColorTemplate.MATERIAL_COLORS);
        dataSet.setValueTextSize(10f);

        BarData barData = new BarData(dataSet);
        barChart.setData(barData);
        barChart.getDescription().setText(rangeLabel + " App Usage (Top " + count + ")");
        barChart.getDescription().setTextSize(12f);

        // 设置 X 轴标签
        XAxis xAxis = barChart.getXAxis();
        xAxis.setValueFormatter(new IndexAxisValueFormatter(labels));
        xAxis.setGranularity(1f);
        xAxis.setLabelRotationAngle(-45f);

        barChart.animateY(500);
        barChart.invalidate();
    }

    private void displayTopApps(List<AppUsageSummary> topApps) {
        List<PieEntry> entries = new ArrayList<>();

        for (AppUsageSummary app : topApps) {
            // 使用浮点除法保留精度
            float minutes = app.totalUsage / 1000f / 60f;
            entries.add(new PieEntry(minutes, app.appName));
        }

        PieDataSet dataSet = new PieDataSet(entries, "Top Apps");
        dataSet.setColors(ColorTemplate.MATERIAL_COLORS);
        dataSet.setValueTextSize(12f);

        PieData pieData = new PieData(dataSet);
        pieChart.setData(pieData);
        pieChart.getDescription().setText("App Usage Distribution");
        pieChart.getDescription().setTextSize(12f);
        pieChart.setDrawEntryLabels(true);
        pieChart.setUsePercentValues(true);

        pieChart.animateY(500);
        pieChart.invalidate();
    }
}
