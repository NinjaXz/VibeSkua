package com.vibeskua.controller;

import android.os.Handler;
import android.os.Looper;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.core.content.ContextCompat;
import androidx.recyclerview.widget.RecyclerView;

import java.util.ArrayList;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

public class ScriptPickerAdapter extends RecyclerView.Adapter<ScriptPickerAdapter.ViewHolder> {

    public interface OnScriptPickListener {
        void onScriptPicked(Models.ScriptItem script);
    }

    public static final int SORT_NAME_ASC = 0;
    public static final int SORT_NAME_DESC = 1;
    public static final int SORT_CATEGORY = 2;
    public static final int SORT_DOWNLOADED = 3;
    public static final int SORT_DATE_DESC = 4;
    public static final int SORT_DATE_ASC = 5;

    private final List<Models.ScriptItem> allScripts = new ArrayList<>();
    private final List<Models.ScriptItem> filteredScripts = new ArrayList<>();
    private final Set<String> sessionQueuedKeys = new HashSet<>();
    private final OnScriptPickListener listener;
    private String actionLabel = "QUEUE";
    private String currentCategory = "ALL";
    private String currentQuery = "";
    private int currentSortMode = SORT_NAME_ASC;
    private boolean multiQueueMode = false;

    public ScriptPickerAdapter(OnScriptPickListener listener) {
        this.listener = listener;
    }

    public void setActionLabel(String label) {
        this.actionLabel = (label != null && !label.isEmpty()) ? label : "QUEUE";
        notifyDataSetChanged();
    }

    public void setMultiQueueMode(boolean enabled) {
        this.multiQueueMode = enabled;
        notifyDataSetChanged();
    }

    public void markQueued(Models.ScriptItem script) {
        if (script == null) return;
        String key = (script.path != null && !script.path.isEmpty()) ? script.path : script.fileName;
        sessionQueuedKeys.add(key.toLowerCase());
        notifyDataSetChanged();
    }

    public void clearQueuedState() {
        sessionQueuedKeys.clear();
        notifyDataSetChanged();
    }

    public void setScripts(List<Models.ScriptItem> scripts) {
        allScripts.clear();
        if (scripts != null) {
            allScripts.addAll(scripts);
        }
        applyFilter();
    }

    public void setCategory(String category) {
        this.currentCategory = (category != null && !category.isEmpty()) ? category : "ALL";
        applyFilter();
    }

    public void filter(String query) {
        this.currentQuery = query != null ? query.trim() : "";
        applyFilter();
    }

    public int cycleSortMode() {
        currentSortMode = (currentSortMode + 1) % 6;
        applyFilter();
        return currentSortMode;
    }

    public void setSortMode(int mode) {
        this.currentSortMode = mode;
        applyFilter();
    }

    public String getSortLabel() {
        switch (currentSortMode) {
            case SORT_NAME_DESC: return "SORT: Z-A";
            case SORT_CATEGORY: return "SORT: CATEGORY";
            case SORT_DOWNLOADED: return "SORT: LOCAL FIRST";
            case SORT_DATE_DESC: return "SORT: DATE (NEW)";
            case SORT_DATE_ASC: return "SORT: DATE (OLD)";
            case SORT_NAME_ASC:
            default: return "SORT: A-Z";
        }
    }

    private void applyFilter() {
        filteredScripts.clear();
        String q = currentQuery.toLowerCase();
        boolean filterCategory = !currentCategory.equalsIgnoreCase("ALL");

        for (Models.ScriptItem s : allScripts) {
            if (s == null) continue;

            if (filterCategory) {
                boolean catMatch = s.category != null && s.category.equalsIgnoreCase(currentCategory);
                if (!catMatch) {
                    String p = ("/" + (s.path != null && !s.path.isEmpty() ? s.path : s.fileName)).replace('\\', '/').toLowerCase();
                    if (currentCategory.equalsIgnoreCase("Classes")) {
                        catMatch = (s.category != null && s.category.equalsIgnoreCase("Class"))
                                || p.contains("/classes/")
                                || p.contains("/class/")
                                || p.endsWith("class.cs")
                                || p.endsWith("classes.cs");
                    } else if (currentCategory.equalsIgnoreCase("Rep")) {
                        catMatch = (s.category != null && s.category.equalsIgnoreCase("Reputation"))
                                || p.contains("/rep/")
                                || p.contains("/reputation/")
                                || p.endsWith("rep.cs")
                                || p.endsWith("reputation.cs");
                    } else if (currentCategory.equalsIgnoreCase("Local")) {
                        catMatch = s.isDownloaded;
                    } else {
                        String catLow = currentCategory.toLowerCase();
                        catMatch = (s.category != null && s.category.toLowerCase().contains(catLow))
                                || p.contains("/" + catLow + "/");
                    }
                }
                if (!catMatch) continue;
            }

            if (q.isEmpty()
                    || (s.name != null && s.name.toLowerCase().contains(q))
                    || (s.fileName != null && s.fileName.toLowerCase().contains(q))
                    || (s.path != null && s.path.toLowerCase().contains(q))
                    || (s.category != null && s.category.toLowerCase().contains(q))
                    || (s.description != null && s.description.toLowerCase().contains(q))
                    || (s.tags != null && s.tags.toLowerCase().contains(q))) {
                filteredScripts.add(s);
            }
        }

        Collections.sort(filteredScripts, (a, b) -> {
            String nameA = a.name != null ? a.name : "";
            String nameB = b.name != null ? b.name : "";
            String catA = a.category != null ? a.category : "";
            String catB = b.category != null ? b.category : "";

            switch (currentSortMode) {
                case SORT_NAME_DESC:
                    return nameB.compareToIgnoreCase(nameA);
                case SORT_CATEGORY:
                    int c = catA.compareToIgnoreCase(catB);
                    return c != 0 ? c : nameA.compareToIgnoreCase(nameB);
                case SORT_DOWNLOADED:
                    if (a.isDownloaded != b.isDownloaded) {
                        return a.isDownloaded ? -1 : 1;
                    }
                    return nameA.compareToIgnoreCase(nameB);
                case SORT_DATE_DESC:
                    return Long.compare(b.lastModified, a.lastModified);
                case SORT_DATE_ASC:
                    return Long.compare(a.lastModified, b.lastModified);
                case SORT_NAME_ASC:
                default:
                    return nameA.compareToIgnoreCase(nameB);
            }
        });

        notifyDataSetChanged();
    }

    public int getFilteredCount() {
        return filteredScripts.size();
    }

    @NonNull
    @Override
    public ViewHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
        View view = LayoutInflater.from(parent.getContext())
                .inflate(R.layout.item_picker_script, parent, false);
        return new ViewHolder(view);
    }

    @Override
    public void onBindViewHolder(@NonNull ViewHolder holder, int position) {
        Models.ScriptItem script = filteredScripts.get(position);
        holder.tvName.setText(script.name != null && !script.name.isEmpty() ? script.name : script.fileName);

        String info = (script.category != null ? script.category : "Script") + " | " +
                (script.path != null ? script.path : "");
        holder.tvCategory.setText(info);

        String key = ((script.path != null && !script.path.isEmpty()) ? script.path : script.fileName).toLowerCase();
        boolean isRecentlyQueued = multiQueueMode && sessionQueuedKeys.contains(key);

        if (isRecentlyQueued) {
            holder.btnAction.setText("QUEUED ✓");
            holder.btnAction.setBackgroundResource(R.drawable.btn_green);
            holder.btnAction.setTextColor(ContextCompat.getColor(holder.itemView.getContext(), R.color.white));
        } else {
            holder.btnAction.setText(actionLabel);
            holder.btnAction.setBackgroundResource(R.drawable.btn_primary);
            holder.btnAction.setTextColor(ContextCompat.getColor(holder.itemView.getContext(), R.color.white));
        }

        View.OnClickListener clickListener = v -> {
            if (listener != null) {
                if (multiQueueMode) {
                    sessionQueuedKeys.add(key);
                    holder.btnAction.setText("QUEUED ✓");
                    holder.btnAction.setBackgroundResource(R.drawable.btn_green);
                    holder.btnAction.setTextColor(ContextCompat.getColor(holder.itemView.getContext(), R.color.white));
                    new Handler(Looper.getMainLooper()).postDelayed(() -> {
                        if (holder.getAdapterPosition() == position) {
                            holder.btnAction.setText("+ QUEUE");
                            holder.btnAction.setBackgroundResource(R.drawable.bg_box);
                            holder.btnAction.setTextColor(ContextCompat.getColor(holder.itemView.getContext(), R.color.text_muted));
                        }
                    }, 1200);
                }
                listener.onScriptPicked(script);
            }
        };

        holder.btnAction.setOnClickListener(clickListener);
        holder.itemView.setOnClickListener(clickListener);
    }

    @Override
    public int getItemCount() {
        return filteredScripts.size();
    }

    static class ViewHolder extends RecyclerView.ViewHolder {
        TextView tvName;
        TextView tvCategory;
        Button btnAction;

        ViewHolder(@NonNull View itemView) {
            super(itemView);
            tvName = itemView.findViewById(R.id.tvPickerScriptName);
            tvCategory = itemView.findViewById(R.id.tvPickerScriptCategory);
            btnAction = itemView.findViewById(R.id.btnPickerScriptAction);
        }
    }
}
