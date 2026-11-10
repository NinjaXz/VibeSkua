package com.vibeskua.controller;

import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.recyclerview.widget.RecyclerView;

import java.util.ArrayList;
import java.util.List;

public class TempItemsAdapter extends RecyclerView.Adapter<TempItemsAdapter.ViewHolder> {

    public interface OnFilterCountListener {
        void onCountUpdated(int visibleCount, int totalCount);
    }

    private final List<Models.TempItem> allItems = new ArrayList<>();
    private final List<Models.TempItem> filteredItems = new ArrayList<>();
    private OnFilterCountListener countListener;
    private String currentQuery = "";

    public void setOnFilterCountListener(OnFilterCountListener l) {
        this.countListener = l;
    }

    public void setItems(List<Models.TempItem> items) {
        allItems.clear();
        if (items != null) {
            allItems.addAll(items);
        }
        filter(currentQuery);
    }

    public void filter(String query) {
        currentQuery = query != null ? query.trim().toLowerCase() : "";
        filteredItems.clear();
        if (currentQuery.isEmpty()) {
            filteredItems.addAll(allItems);
        } else {
            for (Models.TempItem it : allItems) {
                if (it.name.toLowerCase().contains(currentQuery)) {
                    filteredItems.add(it);
                }
            }
        }
        notifyDataSetChanged();
        if (countListener != null) {
            countListener.onCountUpdated(filteredItems.size(), allItems.size());
        }
    }

    @NonNull
    @Override
    public ViewHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
        View view = LayoutInflater.from(parent.getContext()).inflate(R.layout.item_temp, parent, false);
        return new ViewHolder(view);
    }

    @Override
    public void onBindViewHolder(@NonNull ViewHolder holder, int position) {
        Models.TempItem item = filteredItems.get(position);
        holder.tvName.setText(item.name);
        String details = "Qty: " + item.quantity + (item.maxQuantity > 1 ? " / " + item.maxQuantity : "");
        holder.tvDetails.setText(details);
    }

    @Override
    public int getItemCount() {
        return filteredItems.size();
    }

    public static class ViewHolder extends RecyclerView.ViewHolder {
        final TextView tvName;
        final TextView tvDetails;

        public ViewHolder(@NonNull View itemView) {
            super(itemView);
            tvName = itemView.findViewById(R.id.tempItemName);
            tvDetails = itemView.findViewById(R.id.tempItemDetails);
        }
    }
}
