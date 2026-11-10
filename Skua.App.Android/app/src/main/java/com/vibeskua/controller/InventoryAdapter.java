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
import java.util.Comparator;
import java.util.List;

public class InventoryAdapter extends RecyclerView.Adapter<InventoryAdapter.ViewHolder> {

    public static final int SORT_AZ = 0;
    public static final int SORT_ZA = 1;
    public static final int SORT_QTY = 2;
    public static final int SORT_EQUIPPED = 3;
    public static final int SORT_AC = 4;

    public interface OnItemActionListener {
        void onAction(Models.InventoryItem item, boolean isBank);
        default void onEquip(Models.InventoryItem item) {}
    }

    public interface OnFilterCountListener {
        void onCountUpdated(int visibleCount, int totalCount);
    }

    private final List<Models.InventoryItem> allItems = new ArrayList<>();
    private final List<Models.InventoryItem> filteredItems = new ArrayList<>();
    private final OnItemActionListener listener;
    private OnFilterCountListener filterCountListener;
    private boolean isBankView = false;
    private String currentQuery = "";
    private String currentCategory = "All";
    private int currentSortMode = SORT_AZ;

    public InventoryAdapter(OnItemActionListener listener) {
        this.listener = listener;
    }

    public void setOnFilterCountListener(OnFilterCountListener l) {
        this.filterCountListener = l;
    }

    public void setItems(List<Models.InventoryItem> items, boolean isBank) {
        this.isBankView = isBank;
        this.allItems.clear();
        if (items != null) {
            this.allItems.addAll(items);
        }
        filter(currentQuery, currentCategory);
    }

    public int getSortMode() {
        return currentSortMode;
    }

    public int cycleSortMode() {
        currentSortMode = (currentSortMode + 1) % 5;
        filter(currentQuery, currentCategory);
        return currentSortMode;
    }

    public void setSortMode(int mode) {
        this.currentSortMode = mode;
        filter(currentQuery, currentCategory);
    }

    public String getSortLabel() {
        switch (currentSortMode) {
            case SORT_AZ: return "Sort: A-Z";
            case SORT_ZA: return "Sort: Z-A";
            case SORT_QTY: return "Sort: Qty High";
            case SORT_EQUIPPED: return "Sort: Equip";
            case SORT_AC: return "Sort: AC";
            default: return "Sort";
        }
    }

    public void filter(String query) {
        filter(query, this.currentCategory);
    }

    public void setCategory(String category) {
        this.currentCategory = (category != null && !category.isEmpty()) ? category : "All";
        filter(currentQuery, currentCategory);
    }

    public void filter(String query, String category) {
        this.currentQuery = query != null ? query.trim().toLowerCase() : "";
        this.currentCategory = (category != null && !category.isEmpty()) ? category : "All";
        this.filteredItems.clear();

        for (Models.InventoryItem item : allItems) {
            if (item == null) continue;

            // Category filter
            if (!matchesCategory(item, currentCategory)) {
                continue;
            }

            // Search query filter
            if (!currentQuery.isEmpty()) {
                String name = item.name != null ? item.name.toLowerCase() : "";
                String cat = item.category != null ? item.category.toLowerCase() : "";
                if (!name.contains(currentQuery) && !cat.contains(currentQuery)) {
                    continue;
                }
            }

            this.filteredItems.add(item);
        }

        // Apply sorting
        sortFilteredItems();

        notifyDataSetChanged();

        if (filterCountListener != null) {
            filterCountListener.onCountUpdated(filteredItems.size(), allItems.size());
        }
    }

    private void sortFilteredItems() {
        Collections.sort(filteredItems, new Comparator<Models.InventoryItem>() {
            @Override
            public int compare(Models.InventoryItem a, Models.InventoryItem b) {
                String nameA = a.name != null ? a.name : "";
                String nameB = b.name != null ? b.name : "";
                switch (currentSortMode) {
                    case SORT_ZA:
                        return nameB.compareToIgnoreCase(nameA);
                    case SORT_QTY:
                        int diffQty = Integer.compare(b.quantity, a.quantity);
                        return diffQty != 0 ? diffQty : nameA.compareToIgnoreCase(nameB);
                    case SORT_EQUIPPED:
                        int eqA = a.equipped ? 1 : 0;
                        int eqB = b.equipped ? 1 : 0;
                        int diffEq = Integer.compare(eqB, eqA);
                        return diffEq != 0 ? diffEq : nameA.compareToIgnoreCase(nameB);
                    case SORT_AC:
                        int acA = a.ac ? 1 : 0;
                        int acB = b.ac ? 1 : 0;
                        int diffAc = Integer.compare(acB, acA);
                        return diffAc != 0 ? diffAc : nameA.compareToIgnoreCase(nameB);
                    case SORT_AZ:
                    default:
                        return nameA.compareToIgnoreCase(nameB);
                }
            }
        });
    }

    private boolean matchesCategory(Models.InventoryItem item, String cat) {
        if ("All".equalsIgnoreCase(cat)) return true;
        if ("AC Only".equalsIgnoreCase(cat)) return item.ac;
        if ("Equipped".equalsIgnoreCase(cat)) return item.equipped;
        String group = item.getGroupCategory();
        return group.equalsIgnoreCase(cat);
    }

    @NonNull
    @Override
    public ViewHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
        View view = LayoutInflater.from(parent.getContext()).inflate(R.layout.item_inventory, parent, false);
        return new ViewHolder(view);
    }

    @Override
    public void onBindViewHolder(@NonNull ViewHolder holder, int position) {
        Models.InventoryItem item = filteredItems.get(position);
        holder.tvItemName.setText(item.name);

        String details = "Qty: " + item.quantity + (item.maxQuantity > 1 ? " / " + item.maxQuantity : "") + " | " + item.category;
        holder.tvItemDetails.setText(details);

        holder.tvBadgeAc.setVisibility(item.ac ? View.VISIBLE : View.GONE);
        holder.tvBadgeEquipped.setVisibility(item.equipped ? View.VISIBLE : View.GONE);
        if (holder.tvBadgeUpgrade != null) {
            holder.tvBadgeUpgrade.setVisibility(item.upgrade ? View.VISIBLE : View.GONE);
        }

        holder.btnItemAction.setText(isBankView ? "To Inv" : "To Bank");
        holder.btnItemAction.setOnClickListener(v -> {
            if (listener != null) {
                listener.onAction(item, isBankView);
            }
        });

        if (holder.btnEquip != null) {
            if (isBankView || !item.isEquippable()) {
                holder.btnEquip.setVisibility(View.GONE);
            } else {
                holder.btnEquip.setVisibility(View.VISIBLE);
                if (item.equipped) {
                    holder.btnEquip.setText("Equipped");
                    holder.btnEquip.setEnabled(false);
                    holder.btnEquip.setBackgroundResource(R.drawable.bg_box);
                    holder.btnEquip.setTextColor(ContextCompat.getColor(holder.itemView.getContext(), R.color.text_muted));
                } else {
                    holder.btnEquip.setText("Equip");
                    holder.btnEquip.setEnabled(true);
                    holder.btnEquip.setBackgroundResource(R.drawable.btn_green);
                    holder.btnEquip.setTextColor(ContextCompat.getColor(holder.itemView.getContext(), R.color.white));
                    holder.btnEquip.setOnClickListener(v -> {
                        if (listener != null) {
                            listener.onEquip(item);
                        }
                    });
                }
            }
        }
    }

    @Override
    public int getItemCount() {
        return filteredItems.size();
    }

    public static class ViewHolder extends RecyclerView.ViewHolder {
        final TextView tvItemName;
        final TextView tvBadgeAc;
        final TextView tvBadgeUpgrade;
        final TextView tvBadgeEquipped;
        final TextView tvItemDetails;
        final Button btnEquip;
        final Button btnItemAction;

        public ViewHolder(@NonNull View itemView) {
            super(itemView);
            tvItemName = itemView.findViewById(R.id.tvItemName);
            tvBadgeAc = itemView.findViewById(R.id.tvBadgeAc);
            tvBadgeUpgrade = itemView.findViewById(R.id.tvBadgeUpgrade);
            tvBadgeEquipped = itemView.findViewById(R.id.tvBadgeEquipped);
            tvItemDetails = itemView.findViewById(R.id.tvItemDetails);
            btnEquip = itemView.findViewById(R.id.btnEquip);
            btnItemAction = itemView.findViewById(R.id.btnItemAction);
        }
    }
}
