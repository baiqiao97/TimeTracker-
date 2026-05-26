# TimeTracker ProGuard Rules

# Room
-keep class com.timetracker.model.** { *; }
-keep class com.timetracker.dao.** { *; }

# Gson
-keepattributes Signature
-keepattributes *Annotation*
-keep class com.google.gson.** { *; }

# MPAndroidChart
-keep class com.github.mikephil.charting.** { *; }

# General
-keepattributes SourceFile,LineNumberTable
