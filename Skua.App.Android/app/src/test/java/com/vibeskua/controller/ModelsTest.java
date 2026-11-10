package com.vibeskua.controller;

import org.junit.Test;
import static org.junit.Assert.*;

public class ModelsTest {

    @Test
    public void testBotStatusParsing() {
        String json = "{\n" +
                "  \"logged_in\": true,\n" +
                "  \"playing\": true,\n" +
                "  \"username\": \"Hero\",\n" +
                "  \"level\": 100,\n" +
                "  \"gold\": 5000000,\n" +
                "  \"coins\": 2000,\n" +
                "  \"hp\": 3500,\n" +
                "  \"max_hp\": 3500,\n" +
                "  \"mp\": 2000,\n" +
                "  \"max_mp\": 2000,\n" +
                "  \"current_class\": \"ArchMage\",\n" +
                "  \"class_rank\": 10,\n" +
                "  \"in_combat\": true,\n" +
                "  \"alive\": true,\n" +
                "  \"afk\": false,\n" +
                "  \"map\": \"battleon-1234\",\n" +
                "  \"room_id\": 1234,\n" +
                "  \"cell\": \"Enter\",\n" +
                "  \"pad\": \"Spawn\",\n" +
                "  \"server\": \"Artix\",\n" +
                "  \"has_target\": true,\n" +
                "  \"target_name\": \"Frogzard\",\n" +
                "  \"target_hp\": 1200,\n" +
                "  \"target_max_hp\": 1200,\n" +
                "  \"script_running\": true,\n" +
                "  \"script_paused\": false,\n" +
                "  \"loaded_script\": \"CoreBots.cs\",\n" +
                "  \"kills\": 42,\n" +
                "  \"deaths\": 1,\n" +
                "  \"drops\": 10,\n" +
                "  \"quests_completed\": 5,\n" +
                "  \"relogins\": 2,\n" +
                "  \"inventory_count\": 45,\n" +
                "  \"uptime\": \"01:23:45\",\n" +
                "  \"active_quests\": [{\n" +
                "    \"id\": 1234,\n" +
                "    \"name\": \"Slay the Beast\",\n" +
                "    \"can_complete\": true,\n" +
                "    \"requirements\": [\n" +
                "      {\"id\": 50, \"name\": \"Beast Fur\", \"required\": 10, \"current\": 10, \"is_temp\": true}\n" +
                "    ]\n" +
                "  }],\n" +
                "  \"current_drops\": [\"Unidentified 13\", \"Dark Crystal Shard\"]\n" +
                "}";

        Models.BotStatus s = Models.BotStatus.fromJson(json);

        assertTrue(s.loggedIn);
        assertTrue(s.playing);
        assertEquals("Hero", s.username);
        assertEquals(100, s.level);
        assertEquals(5000000, s.gold);
        assertEquals(2000, s.coins);
        assertEquals(3500, s.hp);
        assertEquals(3500, s.maxHp);
        assertEquals("ArchMage", s.currentClass);
        assertEquals(10, s.classRank);
        assertTrue(s.inCombat);
        assertTrue(s.alive);
        assertFalse(s.afk);
        assertEquals("battleon-1234", s.map);
        assertEquals(1234, s.roomId);
        assertEquals("Artix", s.server);
        assertTrue(s.hasTarget);
        assertEquals("Frogzard", s.targetName);
        assertEquals(1200, s.targetHp);
        assertEquals(1200, s.targetMaxHp);
        assertTrue(s.scriptRunning);
        assertFalse(s.scriptPaused);
        assertEquals("CoreBots.cs", s.loadedScript);
        assertEquals(42, s.kills);
        assertEquals(1, s.deaths);
        assertEquals(10, s.drops);
        assertEquals(5, s.questsCompleted);
        assertEquals(2, s.relogins);
        assertEquals(45, s.inventoryCount);
        assertEquals("01:23:45", s.uptime);

        assertEquals(1, s.activeQuests.size());
        assertEquals(1234, s.activeQuests.get(0).id);
        assertEquals("Slay the Beast", s.activeQuests.get(0).name);
        assertTrue(s.activeQuests.get(0).canComplete);
        assertEquals(1, s.activeQuests.get(0).requirements.size());
        assertEquals("Beast Fur", s.activeQuests.get(0).requirements.get(0).name);
        assertTrue(s.activeQuests.get(0).requirements.get(0).isSatisfied());
        assertTrue(s.activeQuests.get(0).requirements.get(0).isTemp);

        assertEquals(2, s.currentDrops.size());
        assertEquals("Unidentified 13", s.currentDrops.get(0));
        assertEquals("Dark Crystal Shard", s.currentDrops.get(1));
    }

    @Test
    public void testEmptyJsonHandling() {
        Models.BotStatus s = Models.BotStatus.fromJson("");
        assertFalse(s.loggedIn);
        assertEquals("00:00:00", s.uptime);

        Models.BotStatus s2 = Models.BotStatus.fromJson(null);
        assertFalse(s2.loggedIn);

        Models.BotStatus s3 = Models.BotStatus.fromJson("{invalid json}");
        assertFalse(s3.loggedIn);
    }

    @Test
    public void testScriptItem() {
        Models.ScriptItem item = new Models.ScriptItem("Nulgath/CoreFarm", "CoreFarm.cs", "Nulgath/CoreFarm.cs", "Nulgath");
        assertEquals("Nulgath/CoreFarm", item.name);
        assertEquals("CoreFarm.cs", item.fileName);
        assertEquals("Nulgath/CoreFarm.cs", item.path);
        assertEquals("Nulgath", item.category);

        Models.ScriptItem itemNull = new Models.ScriptItem(null, null, null, null);
        assertEquals("", itemNull.name);
        assertEquals("", itemNull.fileName);
        assertEquals("", itemNull.path);
        assertEquals("Root", itemNull.category);
    }

    @Test
    public void testInventoryItem() {
        Models.InventoryItem item = new Models.InventoryItem(1001, "Void Highlord", 1, 1, true, true, "Class");
        assertEquals(1001, item.id);
        assertEquals("Void Highlord", item.name);
        assertEquals(1, item.quantity);
        assertEquals(1, item.maxQuantity);
        assertTrue(item.ac);
        assertTrue(item.equipped);
        assertEquals("Class", item.category);
    }

    @Test
    public void testQuestItem() {
        Models.QuestItem q = new Models.QuestItem(5432, "Nulgath Larvae");
        assertEquals(5432, q.id);
        assertEquals("Nulgath Larvae", q.name);
        assertFalse(q.canComplete);
        assertTrue(q.requirements.isEmpty());

        Models.QuestRequirement req1 = new Models.QuestRequirement(101, "Mana Golem Defeated", 5, 5, true);
        assertTrue(req1.isSatisfied());
        assertTrue(req1.isTemp);
        assertEquals("- Mana Golem Defeated: 5 / 5 (Temp)", req1.getDisplayText());

        Models.QuestRequirement req2 = new Models.QuestRequirement(102, "Diamond of Nulgath", 13, 8, false);
        assertFalse(req2.isSatisfied());
        assertFalse(req2.isTemp);
        assertEquals("- Diamond of Nulgath: 8 / 13", req2.getDisplayText());

        java.util.List<Models.QuestRequirement> reqList = new java.util.ArrayList<>();
        reqList.add(req1);
        reqList.add(req2);
        Models.QuestItem qFull = new Models.QuestItem(5432, "Nulgath Larvae", true, reqList);
        assertTrue(qFull.canComplete);
        assertEquals(2, qFull.requirements.size());
    }

    @Test
    public void testTempItem() {
        Models.TempItem t = new Models.TempItem(888, "Defeated Makai", 25, 50);
        assertEquals(888, t.id);
        assertEquals("Defeated Makai", t.name);
        assertEquals(25, t.quantity);
        assertEquals(50, t.maxQuantity);
    }

    @Test
    public void testInventoryItemCategoriesAndEquip() {
        Models.InventoryItem weapon = new Models.InventoryItem(1, 10, "Caladbolg", 1, 1, true, false, "Sword", false);
        assertEquals("Weapons", weapon.getGroupCategory());
        assertTrue(weapon.isEquippable());

        Models.InventoryItem armor = new Models.InventoryItem(2, 11, "Naval Commander", 1, 1, true, false, "Class", false);
        assertEquals("Classes", armor.getGroupCategory());
        assertTrue(armor.isEquippable());

        Models.InventoryItem misc = new Models.InventoryItem(3, 12, "Relic of Chaos", 99, 99, false, false, "Item", false);
        assertEquals("Misc", misc.getGroupCategory());
        assertFalse(misc.isEquippable());
    }

    @Test
    public void testBotStatusTempItemsAndBank() {
        String json = "{\n" +
                "  \"logged_in\": true,\n" +
                "  \"inventory_count\": 179,\n" +
                "  \"inventory_used\": 179,\n" +
                "  \"inventory_max\": 200,\n" +
                "  \"bank_count\": 450,\n" +
                "  \"bank_used\": 450,\n" +
                "  \"bank_max\": 1500,\n" +
                "  \"bank_loaded\": true,\n" +
                "  \"temp_items\": [\n" +
                "    {\"id\": 101, \"name\": \"Escherion's Helm (Temp)\", \"quantity\": 1, \"max_quantity\": 1}\n" +
                "  ]\n" +
                "}";

        Models.BotStatus s = Models.BotStatus.fromJson(json);
        assertEquals(179, s.inventoryCount);
        assertEquals(179, s.inventoryUsed);
        assertEquals(200, s.inventoryMax);
        assertEquals(450, s.bankCount);
        assertEquals(450, s.bankUsed);
        assertEquals(1500, s.bankMax);
        assertTrue(s.bankLoaded);
        assertEquals(1, s.tempItems.size());
        assertEquals("Escherion's Helm (Temp)", s.tempItems.get(0).name);
    }

    @Test
    public void testScriptExecutionDetailsParsing() {
        String json = "{\n" +
                "  \"logged_in\": true,\n" +
                "  \"script_running\": true,\n" +
                "  \"script_paused\": false,\n" +
                "  \"script_status\": \"RUNNING\",\n" +
                "  \"loaded_script\": \"VHL.cs\",\n" +
                "  \"loaded_script_path\": \"Nation/VHL.cs\",\n" +
                "  \"script_last_log\": \"[12:34:56] Farming Dark Crystal Shard (23/50)\",\n" +
                "  \"script_logs\": [\n" +
                "    \"[12:30:00] Script started\",\n" +
                "    \"[12:32:00] Completed prerequisite quest\",\n" +
                "    \"[12:34:56] Farming Dark Crystal Shard (23/50)\"\n" +
                "  ]\n" +
                "}";

        Models.BotStatus s = Models.BotStatus.fromJson(json);
        assertTrue(s.scriptRunning);
        assertFalse(s.scriptPaused);
        assertEquals("RUNNING", s.scriptStatus);
        assertEquals("VHL.cs", s.loadedScript);
        assertEquals("Nation/VHL.cs", s.loadedScriptPath);
        assertEquals("[12:34:56] Farming Dark Crystal Shard (23/50)", s.scriptLastLog);
        assertEquals(3, s.scriptLogs.size());
        assertEquals("[12:30:00] Script started", s.scriptLogs.get(0));
        assertEquals("[12:34:56] Farming Dark Crystal Shard (23/50)", s.scriptLogs.get(2));
    }
}
