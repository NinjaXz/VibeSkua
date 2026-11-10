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

public class DropsAdapter extends RecyclerView.Adapter<DropsAdapter.ViewHolder> {

    public interface OnDropClickListener {
        void onPickup(String dropName);
    }

    private final List<String> drops = new ArrayList<>();
    private final OnDropClickListener listener;

    public DropsAdapter(OnDropClickListener listener) {
        this.listener = listener;
    }

    public void setDrops(List<String> newDrops) {
        drops.clear();
        if (newDrops != null) {
            drops.addAll(newDrops);
        }
        notifyDataSetChanged();
    }

    @NonNull
    @Override
    public ViewHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
        View v = LayoutInflater.from(parent.getContext()).inflate(R.layout.item_drop, parent, false);
        return new ViewHolder(v);
    }

    @Override
    public void onBindViewHolder(@NonNull ViewHolder holder, int position) {
        String d = drops.get(position);
        holder.tvName.setText(d);
        holder.btnPickup.setOnClickListener(v -> {
            if (listener != null) listener.onPickup(d);
        });
    }

    @Override
    public int getItemCount() {
        return drops.size();
    }

    public static class ViewHolder extends RecyclerView.ViewHolder {
        TextView tvName;
        Button btnPickup;

        public ViewHolder(@NonNull View itemView) {
            super(itemView);
            tvName = itemView.findViewById(R.id.dropName);
            btnPickup = itemView.findViewById(R.id.btnPickupDrop);
        }
    }
}
