package com.vibeskua.controller;

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
import java.util.List;

public class ScriptsAdapter extends RecyclerView.Adapter<ScriptsAdapter.ViewHolder> {

    public interface OnScriptClickListener {
        void onRunScript(Models.ScriptItem script);
        void onLoadScript(Models.ScriptItem script);
    }

    public static final int SORT_NAME_ASC = 0;
    public static final int SORT_NAME_DESC = 1;
    public static final int SORT_CATEGORY = 2;
    public static final int SORT_DOWNLOADED = 3;
    public static final int SORT_DATE_DESC = 4;
    public static final int SORT_DATE_ASC = 5;

    private final List<Models.ScriptItem> allScripts = new ArrayList<>();
    private final List<Models.ScriptItem> displayedScripts = new ArrayList<>();
    private final OnScriptClickListener listener;
    private String selectedCategory = "All";
    private String currentQuery = "";
    private int currentSortMode = SORT_NAME_ASC;

    public ScriptsAdapter(OnScriptClickListener listener) {
        this.listener = listener;
    }

    public void setScripts(List<Models.ScriptItem> scripts) {
        allScripts.clear();
        if (scripts != null) {
            allScripts.addAll(scripts);
        }
        applyFilter();
    }

    public void setCategory(String category) {
        this.selectedCategory = (category != null && !category.isEmpty()) ? category : "All";
        applyFilter();
    }

    public void filter(String query) {
        this.currentQuery = query != null ? query.trim() : "";
        applyFilter();
    }

    public void cycleSortMode() {
        currentSortMode = (currentSortMode + 1) % 6;
        applyFilter();
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
            case SORT_DATE_DESC: return "SORT: DATE (NEWEST)";
            case SORT_DATE_ASC: return "SORT: DATE (OLDEST)";
            case SORT_NAME_ASC:
            default: return "SORT: A-Z";
        }
    }

    private void applyFilter() {
        displayedScripts.clear();
        String q = currentQuery.toLowerCase();
        boolean filterCategory = !selectedCategory.equalsIgnoreCase("All");

        for (Models.ScriptItem s : allScripts) {
            if (filterCategory) {
                boolean catMatch = s.category.equalsIgnoreCase(selectedCategory);
                if (!catMatch) {
                    String p = ("/" + (s.path != null && !s.path.isEmpty() ? s.path : s.fileName)).replace('\\', '/').toLowerCase();
                    if (selectedCategory.equalsIgnoreCase("Classes")) {
                        catMatch = s.category.equalsIgnoreCase("Class")
                                || p.contains("/classes/")
                                || p.contains("/class/")
                                || p.endsWith("class.cs")
                                || p.endsWith("classes.cs");
                    } else if (selectedCategory.equalsIgnoreCase("Rep")) {
                        catMatch = s.category.equalsIgnoreCase("Reputation")
                                || p.contains("/rep/")
                                || p.contains("/reputation/")
                                || p.endsWith("rep.cs")
                                || p.endsWith("reputation.cs");
                    }
                }
                if (!catMatch) continue;
            }

            if (q.isEmpty()
                    || s.name.toLowerCase().contains(q)
                    || s.fileName.toLowerCase().contains(q)
                    || s.path.toLowerCase().contains(q)
                    || s.category.toLowerCase().contains(q)
                    || (s.description != null && s.description.toLowerCase().contains(q))
                    || (s.tags != null && s.tags.toLowerCase().contains(q))) {
                displayedScripts.add(s);
            }
        }

        Collections.sort(displayedScripts, (a, b) -> {
            switch (currentSortMode) {
                case SORT_NAME_DESC:
                    return b.name.compareToIgnoreCase(a.name);
                case SORT_CATEGORY: {
                    int c = a.category.compareToIgnoreCase(b.category);
                    return c != 0 ? c : a.name.compareToIgnoreCase(b.name);
                }
                case SORT_DOWNLOADED: {
                    int d = Boolean.compare(b.isDownloaded, a.isDownloaded);
                    return d != 0 ? d : a.name.compareToIgnoreCase(b.name);
                }
                case SORT_DATE_DESC: {
                    int d = Long.compare(b.lastModified, a.lastModified);
                    return d != 0 ? d : a.name.compareToIgnoreCase(b.name);
                }
                case SORT_DATE_ASC: {
                    int d = Long.compare(a.lastModified, b.lastModified);
                    return d != 0 ? d : a.name.compareToIgnoreCase(b.name);
                }
                case SORT_NAME_ASC:
                default:
                    return a.name.compareToIgnoreCase(b.name);
            }
        });

        notifyDataSetChanged();
    }

    @NonNull
    @Override
    public ViewHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
        View v = LayoutInflater.from(parent.getContext()).inflate(R.layout.item_script, parent, false);
        return new ViewHolder(v);
    }

    @Override
    public void onBindViewHolder(@NonNull ViewHolder holder, int position) {
        Models.ScriptItem s = displayedScripts.get(position);
        holder.tvName.setText(s.name);
        holder.tvCategory.setText(s.category);

        holder.tvStatusBadge.setText(s.isDownloaded ? "Downloaded" : "Cloud");
        holder.tvStatusBadge.setTextColor(ContextCompat.getColor(holder.itemView.getContext(),
                s.isDownloaded ? R.color.accent : R.color.text_muted));

        if (s.lastModified > 0) {
            holder.tvDate.setText(s.getFormattedDate());
            holder.tvDate.setVisibility(View.VISIBLE);
        } else {
            holder.tvDate.setVisibility(View.GONE);
        }

        String sizeText = s.getFormattedSize();
        String pathInfo = !s.path.isEmpty() ? s.path : s.fileName;
        if (!sizeText.isEmpty()) {
            pathInfo += " | " + sizeText;
        }
        holder.tvPathAndSize.setText(pathInfo);

        if (s.description != null && !s.description.trim().isEmpty()) {
            holder.tvDescription.setText(s.description.trim());
            holder.tvDescription.setVisibility(View.VISIBLE);
        } else {
            holder.tvDescription.setVisibility(View.GONE);
        }

        if (s.tags != null && !s.tags.trim().isEmpty()) {
            holder.tvTags.setText(s.tags.trim());
            holder.tvTags.setVisibility(View.VISIBLE);
        } else {
            holder.tvTags.setVisibility(View.GONE);
        }

        holder.btnRun.setOnClickListener(v -> {
            if (listener != null) listener.onRunScript(s);
        });
        holder.itemView.setOnClickListener(v -> {
            if (listener != null) listener.onLoadScript(s);
        });
    }

    @Override
    public int getItemCount() {
        return displayedScripts.size();
    }

    public static class ViewHolder extends RecyclerView.ViewHolder {
        TextView tvName, tvCategory, tvStatusBadge, tvDate, tvPathAndSize, tvDescription, tvTags;
        Button btnRun;

        public ViewHolder(@NonNull View itemView) {
            super(itemView);
            tvName = itemView.findViewById(R.id.scriptName);
            tvCategory = itemView.findViewById(R.id.scriptCategory);
            tvStatusBadge = itemView.findViewById(R.id.scriptStatusBadge);
            tvDate = itemView.findViewById(R.id.scriptDate);
            tvPathAndSize = itemView.findViewById(R.id.scriptPathAndSize);
            tvDescription = itemView.findViewById(R.id.scriptDescription);
            tvTags = itemView.findViewById(R.id.scriptTags);
            btnRun = itemView.findViewById(R.id.btnRunScript);
        }
    }
}
