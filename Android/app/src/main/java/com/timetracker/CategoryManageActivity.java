package com.timetracker;

import android.os.Bundle;
import android.view.View;
import android.view.ViewGroup;
import android.widget.BaseAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ListView;
import android.widget.TextView;
import android.widget.Toast;

import androidx.appcompat.app.AlertDialog;
import androidx.appcompat.app.AppCompatActivity;

import com.timetracker.database.AppDatabase;
import com.timetracker.model.Category;

import java.util.ArrayList;
import java.util.List;

public class CategoryManageActivity extends AppCompatActivity {

    private ListView lvCategories;
    private EditText etName, etColor, etDesc;
    private final CategoryAdapter adapter = new CategoryAdapter();
    private List<Category> categories = new ArrayList<>();

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_category_manage);

        lvCategories = findViewById(R.id.lv_categories);
        etName = findViewById(R.id.et_category_name);
        etColor = findViewById(R.id.et_category_color);
        etDesc = findViewById(R.id.et_category_desc);
        Button btnAdd = findViewById(R.id.btn_add_category);

        lvCategories.setAdapter(adapter);
        btnAdd.setOnClickListener(v -> addCategory());
        loadCategories();
    }

    @Override
    protected void onResume() {
        super.onResume();
        loadCategories();
    }

    private void loadCategories() {
        new Thread(() -> {
            AppDatabase db = AppDatabase.getInstance(this);
            List<Category> list = db.categoryDao().getAllCategories();
            runOnUiThread(() -> {
                categories = list != null ? list : new ArrayList<>();
                adapter.notifyDataSetChanged();
            });
        }).start();
    }

    private void addCategory() {
        String name = etName.getText().toString().trim();
        String color = etColor.getText().toString().trim();
        String desc = etDesc.getText().toString().trim();

        if (name.isEmpty()) {
            Toast.makeText(this, "请输入分类名称", Toast.LENGTH_SHORT).show();
            return;
        }
        if (color.isEmpty()) color = "#3498db";

        final String finalColor = color;
        final String finalDesc = desc;
        new Thread(() -> {
            AppDatabase db = AppDatabase.getInstance(this);
            db.categoryDao().insert(new Category(name, finalColor, finalDesc));
            runOnUiThread(() -> {
                etName.setText("");
                etColor.setText("");
                etDesc.setText("");
                loadCategories();
                Toast.makeText(this, "分类已添加", Toast.LENGTH_SHORT).show();
            });
        }).start();
    }

    private void deleteCategory(Category category) {
        new AlertDialog.Builder(this)
                .setTitle("删除分类")
                .setMessage("确定要删除 \"" + category.getName() + "\" 吗？")
                .setPositiveButton("删除", (d, w) -> {
                    new Thread(() -> {
                        AppDatabase db = AppDatabase.getInstance(this);
                        db.categoryDao().delete(category);
                        runOnUiThread(() -> {
                            loadCategories();
                            Toast.makeText(this, "已删除", Toast.LENGTH_SHORT).show();
                        });
                    }).start();
                })
                .setNegativeButton("取消", null)
                .show();
    }

    private class CategoryAdapter extends BaseAdapter {
        @Override
        public int getCount() { return categories.size(); }

        @Override
        public Object getItem(int i) { return categories.get(i); }

        @Override
        public long getItemId(int i) { return categories.get(i).getId(); }

        @Override
        public View getView(int i, View convertView, ViewGroup parent) {
            ViewHolder holder;
            if (convertView == null) {
                convertView = getLayoutInflater().inflate(R.layout.item_category, parent, false);
                holder = new ViewHolder();
                holder.colorView = convertView.findViewById(R.id.view_category_color);
                holder.nameView = convertView.findViewById(R.id.tv_category_name);
                holder.descView = convertView.findViewById(R.id.tv_category_desc);
                holder.deleteBtn = convertView.findViewById(R.id.btn_delete_category);
                convertView.setTag(holder);
            } else {
                holder = (ViewHolder) convertView.getTag();
            }

            Category cat = categories.get(i);
            holder.nameView.setText(cat.getName());
            holder.descView.setText(cat.getDescription());
            try {
                holder.colorView.setBackgroundColor(android.graphics.Color.parseColor(cat.getColor()));
            } catch (Exception e) {
                holder.colorView.setBackgroundColor(0xFF3498db);
            }
            holder.deleteBtn.setOnClickListener(v -> deleteCategory(cat));

            return convertView;
        }

        class ViewHolder {
            View colorView;
            TextView nameView, descView;
            Button deleteBtn;
        }
    }
}
