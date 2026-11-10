package com.vibeskua.controller;

import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.recyclerview.widget.RecyclerView;

import java.util.ArrayList;
import java.util.List;

public class ScriptPickerAdapter extends RecyclerView.Adapter<ScriptPickerAdapter.ViewHolder> {

    public interface OnScriptPickListener {
        void onScriptPicked(Models.ScriptItem script);
    }

    private final List<Models.ScriptItem> allScripts = new ArrayList<>();
    private final List<Models.ScriptItem> filteredScripts = new ArrayList<>();
    private final OnScriptPickListener listener;
    private String actionLabel = "QUEUE";
    private String currentCategory = "ALL";
    private String currentQuery = "";

    public ScriptPickerAdapter(OnScriptPickListener listener) {
        this.listener = listener;
    }

    public void setActionLabel(String label) {
        this.actionLabel = (label != null && !label.isEmpty()) ? label : "QUEUE";
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
        this.currentCategory = (category != null && !category.isEmpty()) ? category.toUpperCase() : "ALL";
        applyFilter();
    }

    public void filter(String query) {
        this.currentQuery = query != null ? query.trim() : "";
        applyFilter();
    }

    private void applyFilter() {
        filteredScripts.clear();
        for (Models.ScriptItem s : allScripts) {
            // Category check
            if (!"ALL".equalsIgnoreCase(currentCategory)) {
                String cat = s.category != null ? s.category.toUpperCase() : "";
                if (!cat.contains(currentCategory)) {
                    continue;
                }
            }
            // Query check
            if (!currentQuery.isEmpty()) {
                String q = currentQuery.toLowerCase();
                String name = (s.name != null ? s.name : "").toLowerCase();
                String path = (s.path != null ? s.path : "").toLowerCase();
                String tags = (s.tags != null ? s.tags : "").toLowerCase();
                if (!name.contains(q) && !path.contains(q) && !tags.contains(q)) {
                    continue;
                }
            }
            filteredScripts.add(s);
        }
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

        holder.btnAction.setText(actionLabel);
        holder.btnAction.setOnClickListener(v -> {
            if (listener != null) {
                listener.onScriptPicked(script);
            }
        });
        holder.itemView.setOnClickListener(v -> {
            if (listener != null) {
                listener.onScriptPicked(script);
            }
        });
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
