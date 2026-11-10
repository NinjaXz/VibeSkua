package com.vibeskua.controller;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

public class Models {

    public static class ScriptItem {
        public String name;
        public String fileName;
        public String path;
        public String category;
        public String description;
        public String tags;
        public boolean isDownloaded;
        public long size;
        public long lastModified;

        public ScriptItem(String name, String fileName, String path, String category) {
            this(name, fileName, path, category, "", "", false, 0, 0);
        }

        public ScriptItem(String name, String fileName, String path, String category, String description, String tags, boolean isDownloaded, long size) {
            this(name, fileName, path, category, description, tags, isDownloaded, size, 0);
        }

        public ScriptItem(String name, String fileName, String path, String category, String description, String tags, boolean isDownloaded, long size, long lastModified) {
            this.name = name != null ? name : "";
            this.fileName = fileName != null ? fileName : "";
            this.path = path != null ? path : "";
            this.category = (category != null && !category.isEmpty()) ? category : "Root";
            this.description = description != null ? description : "";
            this.tags = tags != null ? tags : "";
            this.isDownloaded = isDownloaded;
            this.size = size;
            this.lastModified = lastModified;
        }

        public String getFormattedSize() {
            if (size <= 0) return "";
            if (size < 1024) return size + " B";
            if (size < 1024 * 1024) return String.format("%.1f KB", size / 1024.0);
            return String.format("%.1f MB", size / (1024.0 * 1024.0));
        }

        public String getFormattedDate() {
            if (lastModified <= 0) return "";
            try {
                return new java.text.SimpleDateFormat("yyyy-MM-dd", java.util.Locale.US).format(new java.util.Date(lastModified));
            } catch (Exception e) {
                return "";
            }
        }
    }

    public static class ChatMessage {
        public String type = "zone";
        public String sender = "";
        public String message = "";
        public String timestamp = "";

        public ChatMessage(String type, String sender, String message, String timestamp) {
            this.type = type != null ? type : "zone";
            this.sender = sender != null ? sender : "";
            this.message = message != null ? message : "";
            this.timestamp = timestamp != null ? timestamp : "";
        }
    }

    public static class QuestRequirement {
        public int id;
        public String name;
        public int required;
        public int current;
        public boolean isTemp;

        public QuestRequirement(int id, String name, int required, int current, boolean isTemp) {
            this.id = id;
            this.name = name != null ? name : "";
            this.required = required;
            this.current = current;
            this.isTemp = isTemp;
        }

        public boolean isSatisfied() {
            return current >= required;
        }

        public String getDisplayText() {
            String suffix = isTemp ? " (Temp)" : "";
            return "- " + name + ": " + current + " / " + required + suffix;
        }
    }

    public static class QuestItem {
        public int id;
        public String name;
        public boolean canComplete;
        public List<QuestRequirement> requirements = new ArrayList<>();

        public QuestItem(int id, String name) {
            this(id, name, false, null);
        }

        public QuestItem(int id, String name, boolean canComplete, List<QuestRequirement> requirements) {
            this.id = id;
            this.name = name != null ? name : "";
            this.canComplete = canComplete;
            if (requirements != null) {
                this.requirements.addAll(requirements);
            }
        }
    }

    public static class TempItem {
        public int id;
        public String name;
        public int quantity;
        public int maxQuantity;

        public TempItem(int id, String name, int quantity, int maxQuantity) {
            this.id = id;
            this.name = name != null ? name : "";
            this.quantity = quantity;
            this.maxQuantity = maxQuantity;
        }
    }

    public static class InventoryItem {
        public int id;
        public int charId;
        public String name;
        public int quantity;
        public int maxQuantity;
        public boolean ac;
        public boolean equipped;
        public String category;
        public boolean upgrade;

        public InventoryItem(int id, int charId, String name, int quantity, int maxQuantity, boolean ac, boolean equipped, String category, boolean upgrade) {
            this.id = id;
            this.charId = charId;
            this.name = name != null ? name : "";
            this.quantity = quantity;
            this.maxQuantity = maxQuantity;
            this.ac = ac;
            this.equipped = equipped;
            this.category = category != null ? category : "Item";
            this.upgrade = upgrade;
        }

        public InventoryItem(int id, String name, int quantity, int maxQuantity, boolean ac, boolean equipped, String category) {
            this(id, 0, name, quantity, maxQuantity, ac, equipped, category, false);
        }

        public boolean isEquippable() {
            if (category == null) return false;
            String c = category.trim().toLowerCase();
            return c.contains("sword") || c.contains("axe") || c.contains("dagger") ||
                   c.contains("gun") || c.contains("bow") || c.contains("mace") ||
                   c.contains("polearm") || c.contains("staff") || c.contains("wand") ||
                   c.contains("class") || c.contains("armor") || c.contains("helm") ||
                   c.contains("cape") || c.contains("pet") || c.contains("amulet") ||
                   c.contains("ground") || c.contains("weapon");
        }

        public String getGroupCategory() {
            if (category == null) return "Misc";
            String c = category.trim().toLowerCase();
            if (c.contains("class")) return "Classes";
            if (c.contains("armor")) return "Armors";
            if (c.contains("helm")) return "Helms";
            if (c.contains("cape")) return "Capes";
            if (c.contains("pet")) return "Pets";
            if (c.contains("sword") || c.contains("axe") || c.contains("dagger") ||
                c.contains("gun") || c.contains("bow") || c.contains("mace") ||
                c.contains("polearm") || c.contains("staff") || c.contains("wand") ||
                c.contains("weapon") || c.contains("scythe") || c.contains("gauntlet") ||
                c.contains("whip")) return "Weapons";
            if (c.contains("enhancement") || c.contains("item") || c.contains("resource") ||
                c.contains("misc") || c.contains("quest")) return "Misc";
            return category;
        }
    }

    public static class BotStatus {
        public boolean loggedIn;
        public boolean playing;
        public String username = "";
        public int level;
        public int gold;
        public int coins;
        public int hp;
        public int maxHp;
        public int mp;
        public int maxMp;
        public String currentClass = "";
        public int classRank;
        public boolean inCombat;
        public boolean alive;
        public boolean afk;
        public String map = "";
        public int roomId;
        public String cell = "";
        public String pad = "";
        public String server = "";
        public boolean hasTarget;
        public String targetName = "";
        public int targetHp;
        public int targetMaxHp;
        public boolean scriptRunning;
        public boolean scriptPaused;
        public String scriptStatus = "";
        public String loadedScript = "";
        public String loadedScriptPath = "";
        public String scriptLastLog = "";
        public List<String> scriptLogs = new ArrayList<>();
        public int kills;
        public int deaths;
        public int drops;
        public int questsCompleted;
        public int relogins;
        public int inventoryCount;
        public int inventoryUsed;
        public int inventoryMax;
        public int bankCount;
        public int bankUsed;
        public int bankMax;
        public boolean bankLoaded;
        public String uptime = "00:00:00";
        public List<QuestItem> activeQuests = new ArrayList<>();
        public List<String> currentDrops = new ArrayList<>();
        public List<TempItem> tempItems = new ArrayList<>();
        public List<ChatMessage> recentChat = new ArrayList<>();

        // Bot Options
        public boolean lagKiller;
        public boolean autoRelogin;
        public boolean infiniteRange;
        public boolean headlessMode;
        public boolean streamerMode;

        public static BotStatus fromJson(String jsonStr) {
            BotStatus s = new BotStatus();
            if (jsonStr == null || jsonStr.trim().isEmpty()) {
                return s;
            }

            try {
                JSONObject obj = new JSONObject(jsonStr);
                s.loggedIn = obj.optBoolean("logged_in", false);
                s.playing = obj.optBoolean("playing", false);
                s.username = obj.optString("username", "Unknown");
                s.level = obj.optInt("level", 0);
                s.gold = obj.optInt("gold", 0);
                s.coins = obj.optInt("coins", 0);
                s.hp = obj.optInt("hp", 0);
                s.maxHp = obj.optInt("max_hp", 0);
                s.mp = obj.optInt("mp", 0);
                s.maxMp = obj.optInt("max_mp", 0);
                s.currentClass = obj.optString("current_class", "None");
                s.classRank = obj.optInt("class_rank", 0);
                s.inCombat = obj.optBoolean("in_combat", false);
                s.alive = obj.optBoolean("alive", true);
                s.afk = obj.optBoolean("afk", false);
                s.map = obj.optString("map", "Unknown");
                s.roomId = obj.optInt("room_id", 0);
                s.cell = obj.optString("cell", "Enter");
                s.pad = obj.optString("pad", "Spawn");
                s.server = obj.optString("server", "Unknown");
                s.hasTarget = obj.optBoolean("has_target", false);
                s.targetName = obj.optString("target_name", "");
                s.targetHp = obj.optInt("target_hp", 0);
                s.targetMaxHp = obj.optInt("target_max_hp", 0);
                s.scriptRunning = obj.optBoolean("script_running", false);
                s.scriptPaused = obj.optBoolean("script_paused", false);
                s.scriptStatus = obj.optString("script_status", "");
                s.loadedScript = obj.optString("loaded_script", "");
                s.loadedScriptPath = obj.optString("loaded_script_path", "");
                s.scriptLastLog = obj.optString("script_last_log", "");
                JSONArray logsArr = obj.optJSONArray("script_logs");
                if (logsArr != null) {
                    for (int i = 0; i < logsArr.length(); i++) {
                        String l = logsArr.optString(i, null);
                        if (l != null && !l.trim().isEmpty()) {
                            s.scriptLogs.add(l);
                        }
                    }
                }
                s.kills = obj.optInt("kills", 0);
                s.deaths = obj.optInt("deaths", 0);
                s.drops = obj.optInt("drops", 0);
                s.questsCompleted = obj.optInt("quests_completed", 0);
                s.relogins = obj.optInt("relogins", 0);
                s.inventoryCount = obj.optInt("inventory_count", 0);
                s.inventoryUsed = obj.optInt("inventory_used", s.inventoryCount);
                s.inventoryMax = obj.optInt("inventory_max", 0);
                s.bankCount = obj.optInt("bank_count", 0);
                s.bankUsed = obj.optInt("bank_used", 0);
                s.bankMax = obj.optInt("bank_max", 0);
                s.bankLoaded = obj.optBoolean("bank_loaded", false);
                s.uptime = obj.optString("uptime", "00:00:00");

                JSONArray questsArr = obj.optJSONArray("active_quests");
                if (questsArr != null) {
                    for (int i = 0; i < questsArr.length(); i++) {
                        JSONObject q = questsArr.optJSONObject(i);
                        if (q != null) {
                            int qId = q.optInt("id", 0);
                            String qName = q.optString("name", "");
                            boolean canComp = q.optBoolean("can_complete", false);
                            List<QuestRequirement> reqs = new ArrayList<>();
                            JSONArray reqsArr = q.optJSONArray("requirements");
                            if (reqsArr != null) {
                                for (int r = 0; r < reqsArr.length(); r++) {
                                    JSONObject ro = reqsArr.optJSONObject(r);
                                    if (ro != null) {
                                        reqs.add(new QuestRequirement(
                                                ro.optInt("id", 0),
                                                ro.optString("name", ""),
                                                ro.optInt("required", 1),
                                                ro.optInt("current", 0),
                                                ro.optBoolean("is_temp", false)
                                        ));
                                    }
                                }
                            }
                            s.activeQuests.add(new QuestItem(qId, qName, canComp, reqs));
                        }
                    }
                }

                JSONArray dropsArr = obj.optJSONArray("current_drops");
                if (dropsArr != null) {
                    for (int i = 0; i < dropsArr.length(); i++) {
                        String dropName = dropsArr.optString(i);
                        if (dropName != null && !dropName.isEmpty()) {
                            s.currentDrops.add(dropName);
                        }
                    }
                }

                JSONArray tempArr = obj.optJSONArray("temp_items");
                if (tempArr != null) {
                    for (int i = 0; i < tempArr.length(); i++) {
                        JSONObject t = tempArr.optJSONObject(i);
                        if (t != null) {
                            s.tempItems.add(new TempItem(
                                    t.optInt("id", 0),
                                    t.optString("name", ""),
                                    t.optInt("quantity", 1),
                                    t.optInt("max_quantity", 1)
                            ));
                        }
                    }
                }

                JSONArray chatArr = obj.optJSONArray("recent_chat");
                if (chatArr != null) {
                    for (int i = 0; i < chatArr.length(); i++) {
                        JSONObject c = chatArr.optJSONObject(i);
                        if (c != null) {
                            s.recentChat.add(new ChatMessage(
                                    c.optString("type", "zone"),
                                    c.optString("sender", "World"),
                                    c.optString("message", ""),
                                    c.optString("timestamp", "")
                            ));
                        }
                    }
                }

                JSONObject opt = obj.optJSONObject("options");
                if (opt != null) {
                    s.lagKiller = opt.optBoolean("lag_killer", false);
                    s.autoRelogin = opt.optBoolean("auto_relogin", false);
                    s.infiniteRange = opt.optBoolean("infinite_range", false);
                    s.headlessMode = opt.optBoolean("headless_mode", false);
                    s.streamerMode = opt.optBoolean("streamer_mode", false);
                }
            } catch (Exception ignored) {
            }

            return s;
        }
    }

    public static class TabAccount {
        public int pid;
        public String username = "";
        public boolean isActive;
        public String map = "";
        public String cell = "";
        public String pad = "";
        public String server = "";
        public int hp;
        public int maxHp;
        public int mp;
        public int maxMp;
        public boolean scriptRunning;
        public String scriptName = "";

        public static TabAccount fromJson(JSONObject obj) {
            return fromJson(obj, 1);
        }

        public static TabAccount fromJson(JSONObject obj, int tabIndex) {
            TabAccount t = new TabAccount();
            if (obj != null) {
                t.pid = obj.optInt("pid", 0);
                String rawName = obj.optString("username", "");
                if (rawName == null || rawName.trim().isEmpty() || rawName.startsWith("Tab ")) {
                    t.username = "Skua " + tabIndex;
                } else {
                    t.username = rawName.trim();
                }
                t.isActive = obj.optBoolean("is_active", false);
                t.map = obj.optString("map", "");
                t.cell = obj.optString("cell", "");
                t.pad = obj.optString("pad", "");
                t.server = obj.optString("server", "");
                t.hp = obj.optInt("hp", 0);
                t.maxHp = obj.optInt("max_hp", 0);
                t.mp = obj.optInt("mp", 0);
                t.maxMp = obj.optInt("max_mp", 0);
                t.scriptRunning = obj.optBoolean("script_running", false);
                t.scriptName = obj.optString("script_name", "");
            }
            return t;
        }
    }

    public static class ManagedAccount {
        public String username = "";
        public String displayName = "";
        public List<String> tags = new ArrayList<>();

        public static ManagedAccount fromJson(JSONObject obj) {
            ManagedAccount a = new ManagedAccount();
            if (obj != null) {
                a.username = obj.optString("username", "");
                a.displayName = obj.optString("display_name", a.username);
                JSONArray tagsArr = obj.optJSONArray("tags");
                if (tagsArr != null) {
                    for (int i = 0; i < tagsArr.length(); i++) {
                        a.tags.add(tagsArr.optString(i, ""));
                    }
                }
            }
            return a;
        }
    }

    public static class ManagedGroup {
        public String name = "";
        public List<String> accounts = new ArrayList<>();

        public static ManagedGroup fromJson(JSONObject obj) {
            ManagedGroup g = new ManagedGroup();
            if (obj != null) {
                g.name = obj.optString("name", "");
                JSONArray accArr = obj.optJSONArray("accounts");
                if (accArr != null) {
                    for (int i = 0; i < accArr.length(); i++) {
                        g.accounts.add(accArr.optString(i, ""));
                    }
                }
            }
            return g;
        }
    }
}
