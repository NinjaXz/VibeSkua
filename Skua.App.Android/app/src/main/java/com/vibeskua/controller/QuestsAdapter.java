package com.vibeskua.controller;

import android.graphics.Color;
import android.text.SpannableStringBuilder;
import android.text.Spanned;
import android.text.style.ForegroundColorSpan;
import android.text.style.StyleSpan;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.recyclerview.widget.RecyclerView;

import java.util.ArrayList;
import java.util.List;

public class QuestsAdapter extends RecyclerView.Adapter<QuestsAdapter.ViewHolder> {

    public interface OnTurnInClickListener {
        void onTurnIn(Models.QuestItem quest);
    }

    private final List<Models.QuestItem> quests = new ArrayList<>();
    private final OnTurnInClickListener turnInListener;

    public QuestsAdapter() {
        this(null);
    }

    public QuestsAdapter(OnTurnInClickListener turnInListener) {
        this.turnInListener = turnInListener;
    }

    public void setQuests(List<Models.QuestItem> newQuests) {
        quests.clear();
        if (newQuests != null) {
            quests.addAll(newQuests);
        }
        notifyDataSetChanged();
    }

    @NonNull
    @Override
    public ViewHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
        View v = LayoutInflater.from(parent.getContext()).inflate(R.layout.item_quest, parent, false);
        return new ViewHolder(v);
    }

    @Override
    public void onBindViewHolder(@NonNull ViewHolder holder, int position) {
        Models.QuestItem q = quests.get(position);
        holder.tvName.setText(q.name);
        holder.tvId.setText("ID: " + q.id);

        if (holder.btnTurnIn != null) {
            if (q.canComplete) {
                holder.btnTurnIn.setVisibility(View.VISIBLE);
                holder.btnTurnIn.setOnClickListener(v -> {
                    if (turnInListener != null) {
                        turnInListener.onTurnIn(q);
                    }
                });
            } else {
                holder.btnTurnIn.setVisibility(View.GONE);
            }
        }

        if (holder.tvRequirements != null) {
            if (q.requirements != null && !q.requirements.isEmpty()) {
                SpannableStringBuilder ssb = new SpannableStringBuilder();
                for (int i = 0; i < q.requirements.size(); i++) {
                    Models.QuestRequirement req = q.requirements.get(i);
                    if (i > 0) ssb.append("\n");

                    ssb.append("- ").append(req.name).append(": ");

                    int qtyStart = ssb.length();
                    String qtyText = req.current + " / " + req.required;
                    ssb.append(qtyText);
                    int qtyEnd = ssb.length();

                    int qtyColor = req.isSatisfied() ? Color.parseColor("#34D399") : Color.parseColor("#FBBF24");
                    ssb.setSpan(new ForegroundColorSpan(qtyColor), qtyStart, qtyEnd, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
                    ssb.setSpan(new StyleSpan(android.graphics.Typeface.BOLD), qtyStart, qtyEnd, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);

                    if (req.isTemp) {
                        int tempStart = ssb.length();
                        ssb.append(" (Temp)");
                        int tempEnd = ssb.length();
                        ssb.setSpan(new ForegroundColorSpan(Color.parseColor("#29B6F6")), tempStart, tempEnd, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
                    }
                }
                holder.tvRequirements.setText(ssb);
                holder.tvRequirements.setVisibility(View.VISIBLE);
            } else {
                holder.tvRequirements.setVisibility(View.GONE);
            }
        }
    }

    @Override
    public int getItemCount() {
        return quests.size();
    }

    public static class ViewHolder extends RecyclerView.ViewHolder {
        final TextView tvName;
        final TextView tvId;
        final TextView tvRequirements;
        final Button btnTurnIn;

        public ViewHolder(@NonNull View itemView) {
            super(itemView);
            tvName = itemView.findViewById(R.id.questName);
            tvId = itemView.findViewById(R.id.questId);
            tvRequirements = itemView.findViewById(R.id.tvQuestRequirements);
            btnTurnIn = itemView.findViewById(R.id.btnTurnInQuest);
        }
    }
}
