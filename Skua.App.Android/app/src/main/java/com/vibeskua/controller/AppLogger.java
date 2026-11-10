package com.vibeskua.controller;

import android.util.Log;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.CopyOnWriteArrayList;

/**
 * App-wide activity and error logger.
 * Captures all actions, network calls, errors, and system events in-memory with a dedicated viewer.
 */
public class AppLogger {

    public static final String TAG = "VibeSkua";
    private static final int MAX_LOG_ENTRIES = 500;

    public static class LogEntry {
        public final long timestamp;
        public final String level;
        public final String tag;
        public final String message;

        public LogEntry(String level, String tag, String message) {
            this.timestamp = System.currentTimeMillis();
            this.level = level != null ? level : "INFO";
            this.tag = tag != null ? tag : TAG;
            this.message = message != null ? message : "";
        }

        public String getFormattedTime() {
            return new SimpleDateFormat("HH:mm:ss", Locale.US).format(new Date(timestamp));
        }

        public String getFormatted() {
            return String.format("[%s] [%s] [%s] %s", getFormattedTime(), level, tag, message);
        }

        public boolean isError() {
            return "ERROR".equalsIgnoreCase(level);
        }

        public boolean isWarn() {
            return "WARN".equalsIgnoreCase(level);
        }
    }

    private static final List<LogEntry> entries = new CopyOnWriteArrayList<>();

    public static void i(String tag, String message) {
        addEntry("INFO", tag, message);
        Log.i(tag, message);
    }

    public static void w(String tag, String message) {
        addEntry("WARN", tag, message);
        Log.w(tag, message);
    }

    public static void e(String tag, String message) {
        addEntry("ERROR", tag, message);
        Log.e(tag, message);
    }

    public static void e(String tag, String message, Throwable t) {
        String msg = message + (t != null ? "\n" + Log.getStackTraceString(t) : "");
        addEntry("ERROR", tag, msg);
        Log.e(tag, msg, t);
    }

    private static synchronized void addEntry(String level, String tag, String message) {
        if (entries.size() >= MAX_LOG_ENTRIES) {
            entries.remove(0);
        }
        entries.add(new LogEntry(level, tag, message));
    }

    public static List<LogEntry> getEntries() {
        return new ArrayList<>(entries);
    }

    public static String getFullLogText() {
        StringBuilder sb = new StringBuilder();
        for (LogEntry e : entries) {
            sb.append(e.getFormatted()).append("\n");
        }
        return sb.toString();
    }

    public static void clear() {
        entries.clear();
        addEntry("INFO", TAG, "Logs cleared by user.");
    }
}
