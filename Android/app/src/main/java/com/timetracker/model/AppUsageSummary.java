package com.timetracker.model;

/**
 * 应用使用统计汇总 POJO，用于 DAO 聚合查询（含分类信息）
 */
public class AppUsageSummary {
    public String packageName;
    public String appName;
    public long totalUsage;
    public String categoryName;
    public String categoryColor;
}
