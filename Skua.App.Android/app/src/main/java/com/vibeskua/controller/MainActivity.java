package com.vibeskua.controller;

import android.app.AlertDialog;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.text.Editable;
import android.text.TextWatcher;
import android.view.KeyEvent;
import android.view.View;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputMethodManager;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.ImageButton;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;
import android.widget.Toast;

import android.graphics.BlurMaskFilter;
import android.graphics.RenderEffect;
import android.graphics.Shader;
import androidx.appcompat.widget.PopupMenu;

import android.Manifest;
import android.content.pm.PackageManager;
import androidx.appcompat.app.AppCompatActivity;
import androidx.core.app.ActivityCompat;
import androidx.core.app.NotificationCompat;
import androidx.core.app.NotificationManagerCompat;
import androidx.core.content.ContextCompat;
import androidx.recyclerview.widget.LinearLayoutManager;
import androidx.recyclerview.widget.RecyclerView;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

public class MainActivity extends AppCompatActivity {

    private static final String PREFS_NAME = "SkuaPrefs";
    private static final String KEY_HOST = "host";
    private static final String KEY_PORT = "port";

    private SharedPreferences prefs;
    private String currentHost = "";
    private int currentPort = 8088;
    private int pollIntervalMs = 1000;
    private boolean autoReconnect = true;

    private final Handler pollHandler = new Handler(Looper.getMainLooper());
    private Runnable pollRunnable;
    private final Handler backgroundPollHandler = new Handler(Looper.getMainLooper());
    private Runnable backgroundPollRunnable;
    private boolean isConnected = false;

    // Multi-Account & Army State
    private int currentSelectedPid = 0;
    private boolean isBroadcastActive = false;
    private final List<Models.TabAccount> runningTabs = new ArrayList<>();
    private final List<Models.ManagedAccount> savedAccounts = new ArrayList<>();
    private final List<Models.ManagedGroup> savedGroups = new ArrayList<>();
    private final Map<Integer, Boolean> lastScriptRunning = new HashMap<>();
    private final Map<Integer, Integer> lastHp = new HashMap<>();
    private final Map<Integer, Boolean> lastLoggedIn = new HashMap<>();
    private final Map<Integer, Integer> lastDropsCount = new HashMap<>();

    // Header & Connection
    private TextView tvConnectionStatus;
    private View panelConnection;
    private EditText editHost, editPort;
    private Button btnConnect, btnScanLan, btnToggleHostPanel;

    // Top App Bar views
    private View btnAccountSelector;
    private ImageView ivTopLeaderCrown;
    private View viewTopStatusDot;
    private TextView tvActiveAccountName, tvAccountStatusDot;
    private ImageButton btnToggleStreamer, btnFocusPc, btnOpenSettings;
    private boolean isStreamerMode = false;
    private int consecutivePollFailures = 0;
    private Models.BotStatus lastBotStatus = null;
    private static final String KEY_STREAMER_MODE = "streamer_mode";

    // Bottom Navigation Bar views
    private View navBtnDashboard, navBtnArmy, navBtnScripts, navBtnControls, navBtnVault;
    private LinearLayout pillNavDashboard, pillNavArmy, pillNavScripts, pillNavControls, pillNavVault;
    private ImageView ivNavDashboard, ivNavArmy, ivNavScripts, ivNavControls, ivNavVault;
    private TextView tvNavDashboardText, tvNavArmyText, tvNavScriptsText, tvNavControlsText, tvNavVaultText;

    // Quick-Switcher Strip views
    private LinearLayout layoutAccountChips;
    private TextView tvNoTabsWarning;
    private Button btnQuickLaunch, btnBroadcastToggle;

    // Tabs
    private Button tabBtnDashboard, tabBtnFleet, tabBtnScripts, tabBtnControls, tabBtnDrops, tabBtnInventory;
    private View tabDashboard, tabFleet, tabScripts, tabControls, tabDrops, tabInventory;

    // Army Deck views
    private TextView tvFleetSummaryCount, tvFleetBroadcastIndicator;
    private Button btnFleetStartAll, btnFleetStopAll, btnFleetLoadScriptAll, btnFleetHeadlessAll, btnFleetReloginAll, btnReloadFleet;
    private LinearLayout layoutFleetRoster, layoutSavedGroups, layoutSavedAccounts;
    private TextView tvEmptyFleet, tvNoSavedGroups, tvNoSavedAccounts;
    private Spinner spinnerNotifyAccounts;
    private CheckBox chkNotifyMuteAll, chkNotifyScriptStopped, chkNotifyPlayerDeath, chkNotifyDisconnect, chkNotifyItemDrop;
    private Button btnTestNotification;
    private boolean isFleetHeadlessEnabled = false;

    // Army Script Scheduler Card
    private View cardArmyScheduler;
    private TextView tvSchedulerStatus;
    private Button btnSchedulerStart, btnSchedulerStop, btnSchedulerQueueScript, btnSchedulerClear;
    private LinearLayout layoutSchedulerQueue;
    private TextView tvEmptySchedulerQueue;
    private final List<Models.ScriptItem> schedulerQueue = new ArrayList<>();
    private boolean isArmySchedulerRunning = false;

    // Army Queue (Manager Squads) Card
    private View cardArmyQueue;
    private TextView tvArmyQueueBadge, tvArmyQueueStatus;
    private Button btnArmyQueueStart, btnArmyQueuePause, btnArmyQueueStop, btnArmyQueueRefresh;
    private LinearLayout layoutArmyQueueSquads;
    private TextView tvEmptyArmyQueue;
    private boolean isArmyQueueRunning = false;
    private boolean isArmyQueuePaused = false;

    // Script Picker Modes
    private static final int SCRIPT_PICKER_MODE_ARMY_LOAD = 0;
    private static final int SCRIPT_PICKER_MODE_SCHEDULER_QUEUE = 1;
    private static final int SCRIPT_PICKER_MODE_ARMY_QUEUE_STEP = 2;
    private final List<Models.ScriptItem> allCachedScripts = new ArrayList<>();

    // Dashboard views
    private TextView tvPlayerName, tvPlayerClass, tvHp, tvMp;
    private ProgressBar progressBarHp, progressBarMp;
    private TextView tvScriptStatus, tvScriptName, tvScriptPath, tvScriptCurrentStep, tvScriptLocation, tvScriptQuests, tvScriptLogs;
    private View containerScriptProgress, containerScriptLogs;
    private Button btnStartScript, btnPauseScript, btnStopScript;
    private TextView tvTargetName, tvTargetHp;
    private Button btnAttackTarget, btnClearTarget;
    private TextView tvStatKills, tvStatDrops, tvStatQuests, tvStatUptime, tvStatGold, tvStatAcs, tvStatMap;

    // Scripts view
    private EditText editScriptSearch;
    private Button btnUpdateScripts, btnSortScripts;
    private TextView tvRosterCountBadge;
    private RecyclerView recyclerScripts;
    private ScriptsAdapter scriptsAdapter;
    private Button btnCatAll, btnCatArmy, btnCatClasses, btnCatDailies, btnCatEvil, btnCatFarm;
    private Button btnCatGood, btnCatLegion, btnCatLocal, btnCatNation, btnCatOther, btnCatRep;
    private Button btnCatSeasonal, btnCatStory, btnCatUltras;
    private final List<Button> categoryButtons = new ArrayList<>();

    // Controls views
    private Button btnSkill1, btnSkill2, btnSkill3, btnSkill4, btnSkill5, btnRest, btnOpenBank, btnRelog;
    private EditText editCell, editPad, editGoto, editJoinMap, editChat;
    private Button btnJump, btnGoto, btnJoinMap, btnSendChat;
    private LinearLayout layoutChatFeed;
    private TextView tvChatFeed, tvChatCount;
    private ScrollView scrollChat;
    private Button btnChatChanZone, btnChatChanParty, btnChatChanGuild, btnChatChanWhisper;
    private final List<Models.ChatMessage> receivedChatMessages = new ArrayList<>();
    private final Set<String> seenChatMessageSignatures = new HashSet<>();
    private Button btnToggleLagKiller, btnToggleAutoRelogin, btnToggleInfiniteRange;
    private Button btnToggleHeadlessMode, btnToggleStreamerMode;

    // Vault Unified Hub Subtabs (Bag, Bank, Quests, Drops)
    private static final int VAULT_SUBTAB_BAG = 0;
    private static final int VAULT_SUBTAB_BANK = 1;
    private static final int VAULT_SUBTAB_QUESTS = 2;
    private static final int VAULT_SUBTAB_DROPS = 3;
    private int currentVaultSubTab = VAULT_SUBTAB_BAG;
    private View containerInventoryHub;

    // Drops & Quests views
    private static final int DROPS_SUBTAB_QUESTS = 0;
    private static final int DROPS_SUBTAB_TEMP = 1;
    private static final int DROPS_SUBTAB_DROPS = 2;
    private int currentDropsSubTab = DROPS_SUBTAB_QUESTS;

    private Button subTabBtnQuests, subTabBtnTempDrops, subTabBtnDrops, btnReloadQuests;
    private View containerQuests, containerTempDrops, containerScreenDrops;
    private Button btnPickupAll, btnReloadDrops;
    private TextView tvDropsHeader, tvTempDropsHeader, tvQuestsHeader;
    private RecyclerView recyclerDrops, recyclerTempDrops, recyclerQuests;
    private DropsAdapter dropsAdapter;
    private TempItemsAdapter tempDropsAdapter;
    private QuestsAdapter questsAdapter;

    // Inventory & Bank views
    private static final int SUBTAB_INVENTORY = 0;
    private static final int SUBTAB_BANK = 1;
    private int currentInventorySubTab = SUBTAB_INVENTORY;

    private Button subTabBtnInventory, subTabBtnBank, btnOpenBankInv, btnInvSort;
    private View scrollInvCategories;
    private Button btnInvCatAll, btnInvCatWeapons, btnInvCatClasses, btnInvCatArmors, btnInvCatHelms;
    private Button btnInvCatCapes, btnInvCatPets, btnInvCatMisc, btnInvCatAc, btnInvCatEquipped;
    private final List<Button> invCategoryButtons = new ArrayList<>();
    private EditText editInventorySearch;
    private TextView tvInventoryCount;
    private RecyclerView recyclerInventory;
    private InventoryAdapter inventoryAdapter;
    private final List<Models.InventoryItem> cachedInventory = new ArrayList<>();
    private final List<Models.InventoryItem> cachedBank = new ArrayList<>();
    private final List<Models.TempItem> cachedTempItems = new ArrayList<>();
    private int invUsedSlots = 0, invMaxSlots = 0;
    private int bankUsedSlots = 0, bankMaxSlots = 0;
    private boolean isBankLoaded = false;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        prefs = getSharedPreferences(PREFS_NAME, MODE_PRIVATE);
        currentHost = prefs.getString(KEY_HOST, "");
        currentPort = prefs.getInt(KEY_PORT, 8088);
        isStreamerMode = prefs.getBoolean(KEY_STREAMER_MODE, false);

        AppLogger.i("App", "VibeSkua Remote started. Saved host: '" + currentHost + "'");

        initViews();
        setupListeners();
        setupAdapters();
        createNotificationChannel();

        editHost.setText(currentHost != null ? currentHost : "");
        editPort.setText(String.valueOf(currentPort));

        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.TIRAMISU) {
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
                ActivityCompat.requestPermissions(this, new String[]{Manifest.permission.POST_NOTIFICATIONS}, 101);
            }
        }

        // Attempt initial connection or auto-scan LAN if no IP configured
        if (!currentHost.isEmpty()) {
            connectToServer(false);
        } else {
            performLanScan();
        }

        // Set initial tab & subtab
        switchMainPage(0);
        switchVaultSubTab(VAULT_SUBTAB_BAG);
    }

    private void initViews() {
        tvConnectionStatus = findViewById(R.id.tvConnectionStatus);
        panelConnection = findViewById(R.id.panelConnection);
        editHost = findViewById(R.id.editHost);
        editPort = findViewById(R.id.editPort);
        btnConnect = findViewById(R.id.btnConnect);
        btnScanLan = findViewById(R.id.btnScanLan);
        btnToggleHostPanel = findViewById(R.id.btnToggleHostPanel);

        // Top App Bar
        btnAccountSelector = findViewById(R.id.btnAccountSelector);
        ivTopLeaderCrown = findViewById(R.id.ivTopLeaderCrown);
        viewTopStatusDot = findViewById(R.id.viewTopStatusDot);
        tvActiveAccountName = findViewById(R.id.tvActiveAccountName);
        tvAccountStatusDot = findViewById(R.id.tvAccountStatusDot);
        btnToggleStreamer = findViewById(R.id.btnToggleStreamer);
        btnOpenSettings = findViewById(R.id.btnOpenSettings);
        updateStreamerButtonState();

        // Bottom Navigation Bar
        navBtnDashboard = findViewById(R.id.navBtnDashboard);
        navBtnArmy = findViewById(R.id.navBtnArmy);
        navBtnScripts = findViewById(R.id.navBtnScripts);
        navBtnControls = findViewById(R.id.navBtnControls);
        navBtnVault = findViewById(R.id.navBtnVault);
        pillNavDashboard = findViewById(R.id.pillNavDashboard);
        pillNavArmy = findViewById(R.id.pillNavArmy);
        pillNavScripts = findViewById(R.id.pillNavScripts);
        pillNavControls = findViewById(R.id.pillNavControls);
        pillNavVault = findViewById(R.id.pillNavVault);
        ivNavDashboard = findViewById(R.id.ivNavDashboard);
        ivNavArmy = findViewById(R.id.ivNavArmy);
        ivNavScripts = findViewById(R.id.ivNavScripts);
        ivNavControls = findViewById(R.id.ivNavControls);
        ivNavVault = findViewById(R.id.ivNavVault);
        tvNavDashboardText = findViewById(R.id.tvNavDashboardText);
        tvNavArmyText = findViewById(R.id.tvNavArmyText);
        tvNavScriptsText = findViewById(R.id.tvNavScriptsText);
        tvNavControlsText = findViewById(R.id.tvNavControlsText);
        tvNavVaultText = findViewById(R.id.tvNavVaultText);

        tabBtnDashboard = findViewById(R.id.tabBtnDashboard);
        tabBtnFleet = findViewById(R.id.tabBtnFleet);
        tabBtnScripts = findViewById(R.id.tabBtnScripts);
        tabBtnControls = findViewById(R.id.tabBtnControls);
        tabBtnDrops = findViewById(R.id.tabBtnDrops);
        tabBtnInventory = findViewById(R.id.tabBtnInventory);

        tabDashboard = findViewById(R.id.tabDashboard);
        tabFleet = findViewById(R.id.tabFleet);
        tabScripts = findViewById(R.id.tabScripts);
        tabControls = findViewById(R.id.tabControls);
        tabDrops = findViewById(R.id.tabDrops);
        tabInventory = findViewById(R.id.tabInventory);

        containerInventoryHub = findViewById(R.id.containerInventoryHub);

        // Quick-Switcher Strip
        layoutAccountChips = findViewById(R.id.layoutAccountChips);
        tvNoTabsWarning = findViewById(R.id.tvNoTabsWarning);
        btnQuickLaunch = findViewById(R.id.btnQuickLaunch);
        btnFocusPc = findViewById(R.id.btnFocusPc);
        btnBroadcastToggle = findViewById(R.id.btnBroadcastToggle);

        // Army Command Deck
        tvFleetSummaryCount = findViewById(R.id.tvFleetSummaryCount);
        tvFleetBroadcastIndicator = findViewById(R.id.tvFleetBroadcastIndicator);
        btnFleetStartAll = findViewById(R.id.btnFleetStartAll);
        btnFleetStopAll = findViewById(R.id.btnFleetStopAll);
        btnFleetLoadScriptAll = findViewById(R.id.btnFleetLoadScriptAll);
        btnFleetHeadlessAll = findViewById(R.id.btnFleetHeadlessAll);
        btnFleetReloginAll = findViewById(R.id.btnFleetReloginAll);
        btnReloadFleet = findViewById(R.id.btnReloadFleet);
        layoutFleetRoster = findViewById(R.id.layoutFleetRoster);
        layoutSavedGroups = findViewById(R.id.layoutSavedGroups);
        layoutSavedAccounts = findViewById(R.id.layoutSavedAccounts);
        tvEmptyFleet = findViewById(R.id.tvEmptyFleet);
        tvNoSavedGroups = findViewById(R.id.tvNoSavedGroups);
        tvNoSavedAccounts = findViewById(R.id.tvNoSavedAccounts);
        spinnerNotifyAccounts = findViewById(R.id.spinnerNotifyAccounts);
        chkNotifyMuteAll = findViewById(R.id.chkNotifyMuteAll);
        chkNotifyScriptStopped = findViewById(R.id.chkNotifyScriptStopped);
        chkNotifyPlayerDeath = findViewById(R.id.chkNotifyPlayerDeath);
        chkNotifyDisconnect = findViewById(R.id.chkNotifyDisconnect);
        chkNotifyItemDrop = findViewById(R.id.chkNotifyItemDrop);
        btnTestNotification = findViewById(R.id.btnTestNotification);

        // Army Script Scheduler Card
        cardArmyScheduler = findViewById(R.id.cardArmyScheduler);
        tvSchedulerStatus = findViewById(R.id.tvSchedulerStatus);
        btnSchedulerStart = findViewById(R.id.btnSchedulerStart);
        btnSchedulerStop = findViewById(R.id.btnSchedulerStop);
        btnSchedulerQueueScript = findViewById(R.id.btnSchedulerQueueScript);
        btnSchedulerClear = findViewById(R.id.btnSchedulerClear);
        layoutSchedulerQueue = findViewById(R.id.layoutSchedulerQueue);
        tvEmptySchedulerQueue = findViewById(R.id.tvEmptySchedulerQueue);

        // Army Queue (Manager Squads) Card
        cardArmyQueue = findViewById(R.id.cardArmyQueue);
        tvArmyQueueBadge = findViewById(R.id.tvArmyQueueBadge);
        tvArmyQueueStatus = findViewById(R.id.tvArmyQueueStatus);
        btnArmyQueueStart = findViewById(R.id.btnArmyQueueStart);
        btnArmyQueuePause = findViewById(R.id.btnArmyQueuePause);
        btnArmyQueueStop = findViewById(R.id.btnArmyQueueStop);
        btnArmyQueueRefresh = findViewById(R.id.btnArmyQueueRefresh);
        layoutArmyQueueSquads = findViewById(R.id.layoutArmyQueueSquads);
        tvEmptyArmyQueue = findViewById(R.id.tvEmptyArmyQueue);

        tvPlayerName = findViewById(R.id.tvPlayerName);
        tvPlayerClass = findViewById(R.id.tvPlayerClass);
        tvHp = findViewById(R.id.tvHp);
        tvMp = findViewById(R.id.tvMp);
        progressBarHp = findViewById(R.id.progressBarHp);
        progressBarMp = findViewById(R.id.progressBarMp);

        tvScriptStatus = findViewById(R.id.tvScriptStatus);
        tvScriptName = findViewById(R.id.tvScriptName);
        tvScriptPath = findViewById(R.id.tvScriptPath);
        containerScriptProgress = findViewById(R.id.containerScriptProgress);
        tvScriptCurrentStep = findViewById(R.id.tvScriptCurrentStep);
        tvScriptLocation = findViewById(R.id.tvScriptLocation);
        tvScriptQuests = findViewById(R.id.tvScriptQuests);
        containerScriptLogs = findViewById(R.id.containerScriptLogs);
        tvScriptLogs = findViewById(R.id.tvScriptLogs);
        btnStartScript = findViewById(R.id.btnStartScript);
        btnPauseScript = findViewById(R.id.btnPauseScript);
        btnStopScript = findViewById(R.id.btnStopScript);

        tvTargetName = findViewById(R.id.tvTargetName);
        tvTargetHp = findViewById(R.id.tvTargetHp);
        btnAttackTarget = findViewById(R.id.btnAttackTarget);
        btnClearTarget = findViewById(R.id.btnClearTarget);

        tvStatKills = findViewById(R.id.tvStatKills);
        tvStatDrops = findViewById(R.id.tvStatDrops);
        tvStatQuests = findViewById(R.id.tvStatQuests);
        tvStatUptime = findViewById(R.id.tvStatUptime);
        tvStatGold = findViewById(R.id.tvStatGold);
        tvStatAcs = findViewById(R.id.tvStatAcs);
        tvStatMap = findViewById(R.id.tvStatMap);

        editScriptSearch = findViewById(R.id.editScriptSearch);
        btnUpdateScripts = findViewById(R.id.btnUpdateScripts);
        btnSortScripts = findViewById(R.id.btnSortScripts);
        tvRosterCountBadge = findViewById(R.id.tvRosterCountBadge);
        recyclerScripts = findViewById(R.id.recyclerScripts);

        btnCatAll = findViewById(R.id.btnCatAll);
        btnCatArmy = findViewById(R.id.btnCatArmy);
        btnCatClasses = findViewById(R.id.btnCatClasses);
        btnCatDailies = findViewById(R.id.btnCatDailies);
        btnCatEvil = findViewById(R.id.btnCatEvil);
        btnCatFarm = findViewById(R.id.btnCatFarm);
        btnCatGood = findViewById(R.id.btnCatGood);
        btnCatLegion = findViewById(R.id.btnCatLegion);
        btnCatLocal = findViewById(R.id.btnCatLocal);
        btnCatNation = findViewById(R.id.btnCatNation);
        btnCatOther = findViewById(R.id.btnCatOther);
        btnCatRep = findViewById(R.id.btnCatRep);
        btnCatSeasonal = findViewById(R.id.btnCatSeasonal);
        btnCatStory = findViewById(R.id.btnCatStory);
        btnCatUltras = findViewById(R.id.btnCatUltras);

        categoryButtons.clear();
        categoryButtons.add(btnCatAll);
        categoryButtons.add(btnCatArmy);
        categoryButtons.add(btnCatClasses);
        categoryButtons.add(btnCatDailies);
        categoryButtons.add(btnCatEvil);
        categoryButtons.add(btnCatFarm);
        categoryButtons.add(btnCatGood);
        categoryButtons.add(btnCatLegion);
        categoryButtons.add(btnCatLocal);
        categoryButtons.add(btnCatNation);
        categoryButtons.add(btnCatOther);
        categoryButtons.add(btnCatRep);
        categoryButtons.add(btnCatSeasonal);
        categoryButtons.add(btnCatStory);
        categoryButtons.add(btnCatUltras);

        btnSkill1 = findViewById(R.id.btnSkill1);
        btnSkill2 = findViewById(R.id.btnSkill2);
        btnSkill3 = findViewById(R.id.btnSkill3);
        btnSkill4 = findViewById(R.id.btnSkill4);
        btnSkill5 = findViewById(R.id.btnSkill5);
        btnRest = findViewById(R.id.btnRest);
        btnOpenBank = findViewById(R.id.btnOpenBank);
        btnRelog = findViewById(R.id.btnRelog);

        editCell = findViewById(R.id.editCell);
        editPad = findViewById(R.id.editPad);
        btnJump = findViewById(R.id.btnJump);

        editGoto = findViewById(R.id.editGoto);
        btnGoto = findViewById(R.id.btnGoto);

        editJoinMap = findViewById(R.id.editJoinMap);
        btnJoinMap = findViewById(R.id.btnJoinMap);

        btnToggleLagKiller = findViewById(R.id.btnToggleLagKiller);
        btnToggleAutoRelogin = findViewById(R.id.btnToggleAutoRelogin);
        btnToggleInfiniteRange = findViewById(R.id.btnToggleInfiniteRange);
        btnToggleHeadlessMode = findViewById(R.id.btnToggleHeadlessMode);
        btnToggleStreamerMode = findViewById(R.id.btnToggleStreamerMode);

        editChat = findViewById(R.id.editChat);
        btnSendChat = findViewById(R.id.btnSendChat);
        layoutChatFeed = findViewById(R.id.layoutChatFeed);
        tvChatFeed = findViewById(R.id.tvChatFeed);
        tvChatCount = findViewById(R.id.tvChatCount);
        scrollChat = findViewById(R.id.scrollChat);
        btnChatChanZone = findViewById(R.id.btnChatChanZone);
        btnChatChanParty = findViewById(R.id.btnChatChanParty);
        btnChatChanGuild = findViewById(R.id.btnChatChanGuild);
        btnChatChanWhisper = findViewById(R.id.btnChatChanWhisper);

        subTabBtnQuests = findViewById(R.id.subTabBtnQuests);
        subTabBtnTempDrops = findViewById(R.id.subTabBtnTempDrops);
        subTabBtnDrops = findViewById(R.id.subTabBtnDrops);
        btnReloadQuests = findViewById(R.id.btnReloadQuests);

        containerQuests = findViewById(R.id.containerQuests);
        containerTempDrops = findViewById(R.id.containerTempDrops);
        containerScreenDrops = findViewById(R.id.containerScreenDrops);

        btnPickupAll = findViewById(R.id.btnPickupAll);
        btnReloadDrops = findViewById(R.id.btnReloadDrops);

        tvDropsHeader = findViewById(R.id.tvDropsHeader);
        tvTempDropsHeader = findViewById(R.id.tvTempDropsHeader);
        tvQuestsHeader = findViewById(R.id.tvQuestsHeader);

        recyclerDrops = findViewById(R.id.recyclerDrops);
        recyclerTempDrops = findViewById(R.id.recyclerTempDrops);
        recyclerQuests = findViewById(R.id.recyclerQuests);

        subTabBtnInventory = findViewById(R.id.subTabBtnInventory);
        subTabBtnBank = findViewById(R.id.subTabBtnBank);
        btnOpenBankInv = findViewById(R.id.btnOpenBankInv);
        btnInvSort = findViewById(R.id.btnInvSort);

        scrollInvCategories = findViewById(R.id.scrollInvCategories);
        btnInvCatAll = findViewById(R.id.btnInvCatAll);
        btnInvCatWeapons = findViewById(R.id.btnInvCatWeapons);
        btnInvCatClasses = findViewById(R.id.btnInvCatClasses);
        btnInvCatArmors = findViewById(R.id.btnInvCatArmors);
        btnInvCatHelms = findViewById(R.id.btnInvCatHelms);
        btnInvCatCapes = findViewById(R.id.btnInvCatCapes);
        btnInvCatPets = findViewById(R.id.btnInvCatPets);
        btnInvCatMisc = findViewById(R.id.btnInvCatMisc);
        btnInvCatAc = findViewById(R.id.btnInvCatAc);
        btnInvCatEquipped = findViewById(R.id.btnInvCatEquipped);

        invCategoryButtons.clear();
        invCategoryButtons.add(btnInvCatAll);
        invCategoryButtons.add(btnInvCatWeapons);
        invCategoryButtons.add(btnInvCatClasses);
        invCategoryButtons.add(btnInvCatArmors);
        invCategoryButtons.add(btnInvCatHelms);
        invCategoryButtons.add(btnInvCatCapes);
        invCategoryButtons.add(btnInvCatPets);
        invCategoryButtons.add(btnInvCatMisc);
        invCategoryButtons.add(btnInvCatAc);
        invCategoryButtons.add(btnInvCatEquipped);

        editInventorySearch = findViewById(R.id.editInventorySearch);
        tvInventoryCount = findViewById(R.id.tvInventoryCount);
        recyclerInventory = findViewById(R.id.recyclerInventory);
    }

    private void setupListeners() {
        if (btnAccountSelector != null) {
            btnAccountSelector.setOnClickListener(v -> showAccountSwitcherDialog());
        }
        if (btnOpenSettings != null) {
            btnOpenSettings.setOnClickListener(v -> showConnectionSettingsDialog());
        }
        if (btnFocusPc != null) {
            btnFocusPc.setOnClickListener(v -> focusSelectedTabPc());
        }
        if (btnToggleStreamer != null) {
            updateStreamerButtonState();
            btnToggleStreamer.setOnClickListener(v -> toggleStreamerMode());
        }

        // Bottom Navigation Bar listeners
        if (navBtnDashboard != null) navBtnDashboard.setOnClickListener(v -> switchMainPage(0));
        if (navBtnArmy != null) navBtnArmy.setOnClickListener(v -> switchMainPage(1));
        if (navBtnScripts != null) navBtnScripts.setOnClickListener(v -> switchMainPage(2));
        if (navBtnControls != null) navBtnControls.setOnClickListener(v -> switchMainPage(3));
        if (navBtnVault != null) navBtnVault.setOnClickListener(v -> switchMainPage(4));

        btnToggleHostPanel.setOnClickListener(v -> {
            panelConnection.setVisibility(panelConnection.getVisibility() == View.VISIBLE ? View.GONE : View.VISIBLE);
        });

        btnConnect.setOnClickListener(v -> connectToServer(true));
        btnScanLan.setOnClickListener(v -> performLanScan());

        tabBtnDashboard.setOnClickListener(v -> switchTab(0));
        tabBtnFleet.setOnClickListener(v -> switchTab(1));
        tabBtnScripts.setOnClickListener(v -> switchTab(2));
        tabBtnControls.setOnClickListener(v -> switchTab(3));
        tabBtnDrops.setOnClickListener(v -> switchTab(4));
        tabBtnInventory.setOnClickListener(v -> switchTab(5));

        // Quick-Switcher listeners
        btnBroadcastToggle.setOnClickListener(v -> toggleBroadcast());
        btnFocusPc.setOnClickListener(v -> focusSelectedTabPc());
        btnQuickLaunch.setOnClickListener(v -> showQuickLaunchDialog());

        // Army Command Deck batch listeners
        btnReloadFleet.setOnClickListener(v -> {
            fetchTabs();
            fetchSavedAccounts();
            pollArmyQueue();
            Toast.makeText(this, "Refreshed army", Toast.LENGTH_SHORT).show();
        });
        btnFleetStartAll.setOnClickListener(v -> sendPost("/api/fleet/start-all", "{}", "Started all scripts"));
        btnFleetStopAll.setOnClickListener(v -> sendPost("/api/fleet/stop-all", "{}", "Stopped all scripts"));
        btnFleetLoadScriptAll.setOnClickListener(v -> showScriptPickerDialog(SCRIPT_PICKER_MODE_ARMY_LOAD, 0));
        btnFleetHeadlessAll.setOnClickListener(v -> toggleFleetHeadless());
        btnFleetReloginAll.setOnClickListener(v -> sendPost("/api/fleet/relogin-all", "{}", "Relogged all accounts"));

        // Army Script Scheduler listeners
        if (btnSchedulerStart != null) {
            btnSchedulerStart.setOnClickListener(v -> startArmyScheduler());
        }
        if (btnSchedulerStop != null) {
            btnSchedulerStop.setOnClickListener(v -> stopArmyScheduler());
        }
        if (btnSchedulerQueueScript != null) {
            btnSchedulerQueueScript.setOnClickListener(v -> showScriptPickerDialog(SCRIPT_PICKER_MODE_SCHEDULER_QUEUE, 0));
        }
        if (btnSchedulerClear != null) {
            btnSchedulerClear.setOnClickListener(v -> clearArmySchedulerQueue());
        }

        // Army Queue (Manager Squads) listeners
        if (btnArmyQueueStart != null) {
            btnArmyQueueStart.setOnClickListener(v -> startArmyQueue());
        }
        if (btnArmyQueuePause != null) {
            btnArmyQueuePause.setOnClickListener(v -> pauseResumeArmyQueue());
        }
        if (btnArmyQueueStop != null) {
            btnArmyQueueStop.setOnClickListener(v -> stopArmyQueue());
        }
        if (btnArmyQueueRefresh != null) {
            btnArmyQueueRefresh.setOnClickListener(v -> {
                pollArmyQueue();
                Toast.makeText(this, "Refreshed squad queue", Toast.LENGTH_SHORT).show();
            });
        }

        // Test Notification listener
        if (btnTestNotification != null) {
            btnTestNotification.setOnClickListener(v -> triggerTestNotification());
        }

        btnStartScript.setOnClickListener(v -> sendPost("/api/scripts/start", "{}", "Started script"));
        btnPauseScript.setOnClickListener(v -> sendPost("/api/scripts/pause", "{}", "Toggled pause"));
        btnStopScript.setOnClickListener(v -> sendPost("/api/scripts/stop", "{}", "Stopped script"));

        if (btnAttackTarget != null) {
            btnAttackTarget.setOnClickListener(v -> sendPost("/api/combat/attack", "{}", "Attacking target"));
        }
        if (btnClearTarget != null) {
            btnClearTarget.setOnClickListener(v -> sendPost("/api/combat/cancel-target", "{}", "Target cleared"));
        }

        if (btnUpdateScripts != null) {
            btnUpdateScripts.setOnClickListener(v -> {
                Toast.makeText(this, "Updating scripts from repository...", Toast.LENGTH_SHORT).show();
                sendPost("/api/scripts/update", "{}", "Updating scripts from repository...");
                new Handler(Looper.getMainLooper()).postDelayed(this::fetchScripts, 2000);
            });
        }
        if (btnSortScripts != null) {
            btnSortScripts.setText(scriptsAdapter != null ? scriptsAdapter.getSortLabel() : "SORT: A-Z");
            btnSortScripts.setOnClickListener(this::showScriptsSortMenu);
        }
        editScriptSearch.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int start, int count, int after) { }
            @Override public void onTextChanged(CharSequence s, int start, int before, int count) {
                scriptsAdapter.filter(s != null ? s.toString() : "");
            }
            @Override public void afterTextChanged(Editable s) { }
        });

        btnCatAll.setOnClickListener(v -> selectCategory("All", btnCatAll));
        btnCatArmy.setOnClickListener(v -> selectCategory("Army", btnCatArmy));
        btnCatClasses.setOnClickListener(v -> selectCategory("Classes", btnCatClasses));
        btnCatDailies.setOnClickListener(v -> selectCategory("Dailies", btnCatDailies));
        btnCatEvil.setOnClickListener(v -> selectCategory("Evil", btnCatEvil));
        btnCatFarm.setOnClickListener(v -> selectCategory("Farm", btnCatFarm));
        btnCatGood.setOnClickListener(v -> selectCategory("Good", btnCatGood));
        btnCatLegion.setOnClickListener(v -> selectCategory("Legion", btnCatLegion));
        btnCatLocal.setOnClickListener(v -> selectCategory("Local", btnCatLocal));
        btnCatNation.setOnClickListener(v -> selectCategory("Nation", btnCatNation));
        btnCatOther.setOnClickListener(v -> selectCategory("Other", btnCatOther));
        btnCatRep.setOnClickListener(v -> selectCategory("Rep", btnCatRep));
        btnCatSeasonal.setOnClickListener(v -> selectCategory("Seasonal", btnCatSeasonal));
        btnCatStory.setOnClickListener(v -> selectCategory("Story", btnCatStory));
        btnCatUltras.setOnClickListener(v -> selectCategory("Ultras", btnCatUltras));

        btnSkill1.setOnClickListener(v -> sendPost("/api/combat/skill/1", "{}", "Cast Skill 1"));
        btnSkill2.setOnClickListener(v -> sendPost("/api/combat/skill/2", "{}", "Cast Skill 2"));
        btnSkill3.setOnClickListener(v -> sendPost("/api/combat/skill/3", "{}", "Cast Skill 3"));
        btnSkill4.setOnClickListener(v -> sendPost("/api/combat/skill/4", "{}", "Cast Skill 4"));
        btnSkill5.setOnClickListener(v -> sendPost("/api/combat/skill/5", "{}", "Cast Skill 5"));
        btnRest.setOnClickListener(v -> sendPost("/api/player/rest", "{}", "Resting"));
        btnOpenBank.setOnClickListener(v -> sendPost("/api/bank/open", "{}", "Opened bank"));
        btnRelog.setOnClickListener(v -> sendPost("/api/player/relogin", "{}", "Relogging"));

        btnJump.setOnClickListener(v -> {
            String cell = editCell.getText().toString().trim();
            String pad = editPad.getText().toString().trim();
            if (pad.isEmpty()) pad = "Spawn";
            if (!cell.isEmpty()) {
                try {
                    JSONObject body = new JSONObject();
                    body.put("cell", cell);
                    body.put("pad", pad);
                    sendPost("/api/map/jump", body.toString(), "Jumped to " + cell);
                } catch (Exception ignored) { }
            }
        });

        btnGoto.setOnClickListener(v -> {
            String target = editGoto.getText().toString().trim();
            if (!target.isEmpty()) {
                try {
                    JSONObject body = new JSONObject();
                    body.put("username", target);
                    body.put("target", target);
                    sendPost("/api/map/goto", body.toString(), "Goto " + target);
                } catch (Exception ignored) { }
            }
        });

        btnJoinMap.setOnClickListener(v -> {
            String map = editJoinMap.getText().toString().trim();
            if (!map.isEmpty()) {
                try {
                    JSONObject body = new JSONObject();
                    body.put("map", map);
                    body.put("name", map);
                    sendPost("/api/map/join", body.toString(), "Joining " + map);
                } catch (Exception ignored) { }
            }
        });

        setupChatListeners();

        btnToggleLagKiller.setOnClickListener(v -> toggleOption("lag_killer", btnToggleLagKiller));
        btnToggleAutoRelogin.setOnClickListener(v -> toggleOption("auto_relogin", btnToggleAutoRelogin));
        btnToggleInfiniteRange.setOnClickListener(v -> toggleOption("infinite_range", btnToggleInfiniteRange));
        btnToggleHeadlessMode.setOnClickListener(v -> toggleOption("headless_mode", btnToggleHeadlessMode));
        btnToggleStreamerMode.setOnClickListener(v -> toggleOption("streamer_mode", btnToggleStreamerMode));

        // Vault Unified Hub Subtabs
        subTabBtnInventory.setOnClickListener(v -> switchVaultSubTab(VAULT_SUBTAB_BAG));
        subTabBtnBank.setOnClickListener(v -> switchVaultSubTab(VAULT_SUBTAB_BANK));
        subTabBtnQuests.setOnClickListener(v -> switchVaultSubTab(VAULT_SUBTAB_QUESTS));
        subTabBtnDrops.setOnClickListener(v -> switchVaultSubTab(VAULT_SUBTAB_DROPS));

        // Subtab secondary listeners
        btnReloadQuests.setOnClickListener(v -> pollStatus());
        subTabBtnTempDrops.setOnClickListener(v -> switchDropsSubTab(DROPS_SUBTAB_TEMP));
        btnPickupAll.setOnClickListener(v -> sendPost("/api/drops/pickup-all", "{}", "Picked up all drops"));
        btnReloadDrops.setOnClickListener(v -> pollStatus());

        btnOpenBankInv.setOnClickListener(v -> {
            sendPost("/api/bank/open", "{}", "Opened bank");
            new Handler(Looper.getMainLooper()).postDelayed(this::fetchInventoryAndBank, 1000);
        });
        btnInvSort.setOnClickListener(this::showInventorySortMenu);

        editInventorySearch.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int start, int count, int after) { }
            @Override public void onTextChanged(CharSequence s, int start, int before, int count) {
                if (inventoryAdapter != null) {
                    inventoryAdapter.filter(s != null ? s.toString() : "");
                }
            }
            @Override public void afterTextChanged(Editable s) { }
        });

        btnInvCatAll.setOnClickListener(v -> selectInvCategory("All", btnInvCatAll));
        btnInvCatWeapons.setOnClickListener(v -> selectInvCategory("Weapon", btnInvCatWeapons));
        btnInvCatClasses.setOnClickListener(v -> selectInvCategory("Class", btnInvCatClasses));
        btnInvCatArmors.setOnClickListener(v -> selectInvCategory("Armor", btnInvCatArmors));
        btnInvCatHelms.setOnClickListener(v -> selectInvCategory("Helm", btnInvCatHelms));
        btnInvCatCapes.setOnClickListener(v -> selectInvCategory("Cape", btnInvCatCapes));
        btnInvCatPets.setOnClickListener(v -> selectInvCategory("Pet", btnInvCatPets));
        btnInvCatMisc.setOnClickListener(v -> selectInvCategory("Item", btnInvCatMisc));
        btnInvCatAc.setOnClickListener(v -> selectInvCategory("AC", btnInvCatAc));
        btnInvCatEquipped.setOnClickListener(v -> selectInvCategory("Equipped", btnInvCatEquipped));

        setupEnterKeyTrigger(editChat, btnSendChat);
        setupEnterKeyTrigger(editJoinMap, btnJoinMap);
        setupEnterKeyTrigger(editGoto, btnGoto);
        setupEnterKeyTrigger(editCell, btnJump);
        setupEnterKeyTrigger(editPad, btnJump);
        setupEnterKeyTrigger(editHost, btnConnect);
        setupEnterKeyTrigger(editPort, btnConnect);
    }

    private void setupEnterKeyTrigger(EditText editText, Button buttonToTrigger) {
        if (editText == null || buttonToTrigger == null) return;
        editText.setOnEditorActionListener((v, actionId, event) -> {
            if (actionId == EditorInfo.IME_ACTION_DONE || actionId == EditorInfo.IME_ACTION_GO ||
                    (event != null && event.getKeyCode() == KeyEvent.KEYCODE_ENTER && event.getAction() == KeyEvent.ACTION_DOWN)) {
                buttonToTrigger.performClick();
                InputMethodManager imm = (InputMethodManager) getSystemService(INPUT_METHOD_SERVICE);
                if (imm != null) imm.hideSoftInputFromWindow(v.getWindowToken(), 0);
                return true;
            }
            return false;
        });
    }

    // ==================== IN-GAME CHAT IMPLEMENTATION ====================

    private void setupChatListeners() {
        if (btnSendChat != null) {
            btnSendChat.setOnClickListener(v -> sendChatMessage());
        }

        if (btnChatChanZone != null) {
            btnChatChanZone.setOnClickListener(v -> {
                highlightChatChannelButton(btnChatChanZone);
                setChatPrefix("");
            });
        }

        if (btnChatChanParty != null) {
            btnChatChanParty.setOnClickListener(v -> {
                highlightChatChannelButton(btnChatChanParty);
                setChatPrefix("/p ");
            });
        }

        if (btnChatChanGuild != null) {
            btnChatChanGuild.setOnClickListener(v -> {
                highlightChatChannelButton(btnChatChanGuild);
                setChatPrefix("/g ");
            });
        }

        if (btnChatChanWhisper != null) {
            btnChatChanWhisper.setOnClickListener(v -> {
                highlightChatChannelButton(btnChatChanWhisper);
                setChatPrefix("/w ");
            });
        }
    }

    private void highlightChatChannelButton(Button selected) {
        Button[] buttons = new Button[]{btnChatChanZone, btnChatChanParty, btnChatChanGuild, btnChatChanWhisper};
        for (Button b : buttons) {
            if (b == null) continue;
            if (b == selected) {
                b.setBackgroundResource(R.drawable.bg_account_pill);
                b.setTextColor(ContextCompat.getColor(this, R.color.white));
            } else {
                b.setBackgroundResource(R.drawable.bg_box);
                b.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
            }
        }
    }

    private void setChatPrefix(String prefix) {
        if (editChat == null) return;
        String text = editChat.getText().toString();
        if (text.startsWith("/p ")) text = text.substring(3);
        else if (text.startsWith("/g ")) text = text.substring(3);
        else if (text.startsWith("/w ")) {
            int nextSpace = text.indexOf(' ', 3);
            if (nextSpace > 0) {
                text = text.substring(nextSpace + 1);
            } else {
                text = "";
            }
        }
        String newText = prefix + text.trim();
        if (!prefix.isEmpty() && !newText.endsWith(" ")) {
            newText += " ";
        }
        editChat.setText(newText);
        editChat.setSelection(editChat.getText().length());
        editChat.requestFocus();
    }

    private void sendChatMessage() {
        if (editChat == null) return;
        String raw = editChat.getText().toString().trim();
        if (raw.isEmpty()) return;

        try {
            JSONObject body = new JSONObject();
            body.put("message", raw);
            sendPost("/api/chat/send", body.toString(), null, new ApiClient.Callback<String>() {
                @Override
                public void onSuccess(String result) {
                    editChat.setText("");
                    pollStatus();
                }

                @Override
                public void onError(Exception error) {
                    String msg = error != null ? error.getMessage() : "Failed to send chat";
                    Toast.makeText(MainActivity.this, msg, Toast.LENGTH_LONG).show();
                    AppLogger.w("Chat", "Send chat error: " + msg);
                }
            });
        } catch (Exception e) {
            AppLogger.e("Chat", "Error preparing chat message: " + e.getMessage());
        }
    }

    private void updateChatFeed(List<Models.ChatMessage> messages) {
        if (messages == null || messages.isEmpty()) return;
        boolean hasNew = false;
        for (Models.ChatMessage msg : messages) {
            if (msg == null || msg.message == null || msg.message.trim().isEmpty()) continue;
            String sig = msg.type + "|" + msg.sender + "|" + msg.message + "|" + msg.timestamp;
            if (seenChatMessageSignatures.add(sig)) {
                receivedChatMessages.add(msg);
                hasNew = true;
                if (receivedChatMessages.size() > 150) {
                    receivedChatMessages.remove(0);
                }
            }
        }
        if (hasNew) {
            renderChatHistory();
        }
    }

    private void renderChatHistory() {
        if (layoutChatFeed == null && tvChatFeed == null) return;
        if (receivedChatMessages.isEmpty()) {
            if (layoutChatFeed != null) {
                layoutChatFeed.removeAllViews();
                if (tvChatFeed != null) {
                    tvChatFeed.setText("No messages yet.");
                    layoutChatFeed.addView(tvChatFeed);
                }
            } else if (tvChatFeed != null) {
                tvChatFeed.setText("No messages yet.");
            }
            if (tvChatCount != null) tvChatCount.setText("0 msgs");
            return;
        }

        if (layoutChatFeed != null) {
            layoutChatFeed.removeAllViews();
            float density = getResources().getDisplayMetrics().density;
            for (int i = 0; i < receivedChatMessages.size(); i++) {
                Models.ChatMessage msg = receivedChatMessages.get(i);
                if (msg == null || msg.message == null || msg.message.trim().isEmpty()) continue;

                LinearLayout row = new LinearLayout(this);
                row.setOrientation(LinearLayout.HORIZONTAL);
                row.setPadding(0, (int)(1 * density), 0, (int)(1 * density));

                if (msg.timestamp != null && !msg.timestamp.isEmpty()) {
                    TextView tvTime = new TextView(this);
                    tvTime.setText("[" + msg.timestamp + "] ");
                    tvTime.setTextSize(11);
                    tvTime.setTextColor(Color.parseColor("#64748B"));
                    row.addView(tvTime);
                }

                String type = msg.type != null ? msg.type.toLowerCase() : "zone";
                String sender = msg.sender != null ? msg.sender.trim() : "";

                int colorVal;
                String prefix;
                switch (type) {
                    case "whisper":
                        colorVal = Color.parseColor("#FFE853");
                        prefix = !sender.isEmpty() ? "[" + sender + "]: " : "[Whisper]: ";
                        break;
                    case "party":
                        colorVal = Color.parseColor("#00E5FF");
                        prefix = !sender.isEmpty() ? "[Party] " + sender + ": " : "[Party]: ";
                        break;
                    case "guild":
                        colorVal = Color.parseColor("#00E676");
                        prefix = !sender.isEmpty() ? "[Guild] " + sender + ": " : "[Guild]: ";
                        break;
                    case "server":
                    case "warning":
                        colorVal = Color.parseColor("#FF5252");
                        prefix = "[SERVER]: ";
                        break;
                    case "mod":
                    case "moderator":
                        colorVal = Color.parseColor("#FFB300");
                        prefix = !sender.isEmpty() ? "[MOD] " + sender + ": " : "[MOD]: ";
                        break;
                    case "zone":
                    default:
                        colorVal = Color.parseColor("#FFFFFF");
                        prefix = !sender.isEmpty() ? "[" + sender + "]: " : "";
                        break;
                }

                if (!prefix.isEmpty()) {
                    TextView tvSender = new TextView(this);
                    tvSender.setText(prefix);
                    tvSender.setTextSize(12);
                    tvSender.setTextColor(colorVal);
                    tvSender.setTypeface(null, android.graphics.Typeface.BOLD);
                    if (!type.equals("server") && !type.equals("warning")) {
                        applyStreamerBlur(tvSender, isStreamerMode);
                    }
                    row.addView(tvSender);
                }

                TextView tvMsg = new TextView(this);
                tvMsg.setText(msg.message);
                tvMsg.setTextSize(12);
                tvMsg.setTextColor(colorVal);
                row.addView(tvMsg);

                layoutChatFeed.addView(row);
            }
        }

        if (tvChatCount != null) {
            tvChatCount.setText(receivedChatMessages.size() + " msgs");
        }

        if (scrollChat != null) {
            scrollChat.post(() -> scrollChat.fullScroll(View.FOCUS_DOWN));
        }
    }

    private String escapeHtml(String text) {
        if (text == null) return "";
        return text.replace("&", "&amp;")
                   .replace("<", "&lt;")
                   .replace(">", "&gt;")
                   .replace("\"", "&quot;")
                   .replace("'", "&#39;");
    }

    private void setupAdapters() {
        scriptsAdapter = new ScriptsAdapter(script -> {
            try {
                JSONObject body = new JSONObject();
                body.put("path", script.path != null && !script.path.isEmpty() ? script.path : script.fileName);
                sendPost("/api/scripts/load", body.toString(), "Loaded " + script.name);
            } catch (Exception ignored) { }
        });
        recyclerScripts.setLayoutManager(new LinearLayoutManager(this));
        recyclerScripts.setAdapter(scriptsAdapter);

        dropsAdapter = new DropsAdapter(dropName -> {
            try {
                JSONObject body = new JSONObject();
                body.put("item", dropName);
                sendPost("/api/drops/pickup", body.toString(), "Picked up " + dropName);
            } catch (Exception ignored) { }
        });
        recyclerDrops.setLayoutManager(new LinearLayoutManager(this));
        recyclerDrops.setAdapter(dropsAdapter);

        tempDropsAdapter = new TempItemsAdapter();
        recyclerTempDrops.setLayoutManager(new LinearLayoutManager(this));
        recyclerTempDrops.setAdapter(tempDropsAdapter);

        questsAdapter = new QuestsAdapter(quest -> {
            try {
                JSONObject body = new JSONObject();
                body.put("quest_id", quest.id);
                sendPost("/api/quests/complete", body.toString(), "Completed " + quest.name);
            } catch (Exception ignored) { }
        });
        recyclerQuests.setLayoutManager(new LinearLayoutManager(this));
        recyclerQuests.setAdapter(questsAdapter);

        inventoryAdapter = new InventoryAdapter(new InventoryAdapter.OnItemActionListener() {
            @Override
            public void onAction(Models.InventoryItem item, boolean isBank) {
                try {
                    JSONObject body = new JSONObject();
                    body.put("item_name", item.name);
                    body.put("item_id", item.id);
                    String endpoint = isBank ? "/api/bank/withdraw" : "/api/bank/deposit";
                    String verb = isBank ? "Withdrew " : "Deposited ";
                    sendPost(endpoint, body.toString(), verb + item.name);
                    new Handler(Looper.getMainLooper()).postDelayed(MainActivity.this::fetchInventoryAndBank, 500);
                } catch (Exception ignored) { }
            }

            @Override
            public void onEquip(Models.InventoryItem item) {
                try {
                    JSONObject body = new JSONObject();
                    body.put("item_id", item.id);
                    body.put("char_id", item.charId);
                    sendPost("/api/inventory/equip", body.toString(), "Equipped " + item.name);
                    new Handler(Looper.getMainLooper()).postDelayed(MainActivity.this::fetchInventoryAndBank, 500);
                } catch (Exception ignored) { }
            }
        });
        recyclerInventory.setLayoutManager(new LinearLayoutManager(this));
        recyclerInventory.setAdapter(inventoryAdapter);
    }

    private void selectCategory(String category, Button clickedBtn) {
        for (Button btn : categoryButtons) {
            btn.setBackgroundResource(R.drawable.bg_box);
            btn.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
        }
        clickedBtn.setBackgroundResource(R.drawable.btn_primary);
        clickedBtn.setTextColor(ContextCompat.getColor(this, R.color.white));
        scriptsAdapter.setCategory(category);
    }

    private void showScriptsSortMenu(View anchor) {
        if (scriptsAdapter == null) return;
        PopupMenu popup = new PopupMenu(this, anchor);
        popup.getMenu().add(0, ScriptsAdapter.SORT_NAME_ASC, 0, "Name (A → Z)");
        popup.getMenu().add(0, ScriptsAdapter.SORT_NAME_DESC, 1, "Name (Z → A)");
        popup.getMenu().add(0, ScriptsAdapter.SORT_DATE_DESC, 2, "Date (Newest First)");
        popup.getMenu().add(0, ScriptsAdapter.SORT_DATE_ASC, 3, "Date (Oldest First)");
        popup.getMenu().add(0, ScriptsAdapter.SORT_CATEGORY, 4, "Category");
        popup.getMenu().add(0, ScriptsAdapter.SORT_DOWNLOADED, 5, "Downloaded First");
        popup.setOnMenuItemClickListener(item -> {
            int id = item.getItemId();
            scriptsAdapter.setSortMode(id);
            if (btnSortScripts != null) {
                btnSortScripts.setText(scriptsAdapter.getSortLabel());
            }
            return true;
        });
        popup.show();
    }

    private void showInventorySortMenu(View anchor) {
        if (inventoryAdapter == null) return;
        PopupMenu popup = new PopupMenu(this, anchor);
        popup.getMenu().add(0, InventoryAdapter.SORT_AZ, 0, "Name (A → Z)");
        popup.getMenu().add(0, InventoryAdapter.SORT_ZA, 1, "Name (Z → A)");
        popup.getMenu().add(0, InventoryAdapter.SORT_QTY, 2, "Quantity (High → Low)");
        popup.getMenu().add(0, InventoryAdapter.SORT_EQUIPPED, 3, "Equipped First");
        popup.getMenu().add(0, InventoryAdapter.SORT_AC, 4, "AC Items First");
        popup.setOnMenuItemClickListener(item -> {
            int id = item.getItemId();
            inventoryAdapter.setSortMode(id);
            if (btnInvSort != null) {
                btnInvSort.setText(inventoryAdapter.getSortLabel().toUpperCase());
            }
            return true;
        });
        popup.show();
    }

    private void switchMainPage(int index) {
        if (index < 0) index = 0;
        if (index > 4) index = 4;

        if (tabDashboard != null) tabDashboard.setVisibility(index == 0 ? View.VISIBLE : View.GONE);
        if (tabFleet != null) tabFleet.setVisibility(index == 1 ? View.VISIBLE : View.GONE);
        if (tabScripts != null) tabScripts.setVisibility(index == 2 ? View.VISIBLE : View.GONE);
        if (tabControls != null) tabControls.setVisibility(index == 3 ? View.VISIBLE : View.GONE);
        if (tabInventory != null) tabInventory.setVisibility(index == 4 ? View.VISIBLE : View.GONE);

        updateNavItem(pillNavDashboard, ivNavDashboard, tvNavDashboardText, index == 0);
        updateNavItem(pillNavArmy, ivNavArmy, tvNavArmyText, index == 1);
        updateNavItem(pillNavScripts, ivNavScripts, tvNavScriptsText, index == 2);
        updateNavItem(pillNavControls, ivNavControls, tvNavControlsText, index == 3);
        updateNavItem(pillNavVault, ivNavVault, tvNavVaultText, index == 4);

        if (index == 0) {
            pollStatus();
        } else if (index == 1) {
            fetchTabs();
            fetchSavedAccounts();
            pollArmyQueue();
        } else if (index == 2) {
            fetchScripts();
        } else if (index == 4) {
            if (currentVaultSubTab == VAULT_SUBTAB_BAG || currentVaultSubTab == VAULT_SUBTAB_BANK) {
                fetchInventoryAndBank();
            } else {
                pollStatus();
            }
        }
    }

    private void switchTab(int index) {
        if (index == 5) {
            switchVaultSubTab(VAULT_SUBTAB_BAG);
            switchMainPage(4);
        } else if (index == 4) {
            switchVaultSubTab(VAULT_SUBTAB_DROPS);
            switchMainPage(4);
        } else {
            switchMainPage(index);
        }
    }

    private void updateNavItem(LinearLayout pill, ImageView icon, TextView text, boolean selected) {
        if (pill != null) {
            pill.setBackgroundResource(selected ? R.drawable.bg_nav_active_pill : android.R.color.transparent);
        }
        if (icon != null) {
            icon.setColorFilter(ContextCompat.getColor(this, selected ? R.color.accent : R.color.text_muted), android.graphics.PorterDuff.Mode.SRC_IN);
        }
        if (text != null) {
            text.setTextColor(ContextCompat.getColor(this, selected ? R.color.accent : R.color.text_muted));
            text.setTypeface(null, selected ? android.graphics.Typeface.BOLD : android.graphics.Typeface.NORMAL);
        }
    }

    private void switchVaultSubTab(int subTab) {
        currentVaultSubTab = subTab;

        updateVaultPill(subTabBtnInventory, subTab == VAULT_SUBTAB_BAG);
        updateVaultPill(subTabBtnBank, subTab == VAULT_SUBTAB_BANK);
        updateVaultPill(subTabBtnQuests, subTab == VAULT_SUBTAB_QUESTS);
        updateVaultPill(subTabBtnDrops, subTab == VAULT_SUBTAB_DROPS);

        if (subTab == VAULT_SUBTAB_BAG) {
            if (containerInventoryHub != null) containerInventoryHub.setVisibility(View.VISIBLE);
            if (containerQuests != null) containerQuests.setVisibility(View.GONE);
            if (containerScreenDrops != null) containerScreenDrops.setVisibility(View.GONE);
            if (containerTempDrops != null) containerTempDrops.setVisibility(View.GONE);
            switchInventorySubTab(SUBTAB_INVENTORY);
        } else if (subTab == VAULT_SUBTAB_BANK) {
            if (containerInventoryHub != null) containerInventoryHub.setVisibility(View.VISIBLE);
            if (containerQuests != null) containerQuests.setVisibility(View.GONE);
            if (containerScreenDrops != null) containerScreenDrops.setVisibility(View.GONE);
            if (containerTempDrops != null) containerTempDrops.setVisibility(View.GONE);
            switchInventorySubTab(SUBTAB_BANK);
        } else if (subTab == VAULT_SUBTAB_QUESTS) {
            if (containerInventoryHub != null) containerInventoryHub.setVisibility(View.GONE);
            if (containerQuests != null) containerQuests.setVisibility(View.VISIBLE);
            if (containerScreenDrops != null) containerScreenDrops.setVisibility(View.GONE);
            if (containerTempDrops != null) containerTempDrops.setVisibility(View.GONE);
            pollStatus();
        } else if (subTab == VAULT_SUBTAB_DROPS) {
            if (containerInventoryHub != null) containerInventoryHub.setVisibility(View.GONE);
            if (containerQuests != null) containerQuests.setVisibility(View.GONE);
            if (containerScreenDrops != null) containerScreenDrops.setVisibility(View.VISIBLE);
            if (containerTempDrops != null) containerTempDrops.setVisibility(View.GONE);
            switchDropsSubTab(DROPS_SUBTAB_DROPS);
        }
    }

    private void updateVaultPill(Button btn, boolean selected) {
        if (btn == null) return;
        btn.setBackgroundResource(selected ? R.drawable.btn_primary : R.drawable.bg_box);
        btn.setTextColor(ContextCompat.getColor(this, selected ? R.color.white : R.color.text_muted));
        btn.setTypeface(null, selected ? android.graphics.Typeface.BOLD : android.graphics.Typeface.NORMAL);
    }

    private void switchDropsSubTab(int subTab) {
        currentDropsSubTab = subTab;
        if (subTabBtnQuests != null) {
            subTabBtnQuests.setBackgroundResource(subTab == DROPS_SUBTAB_QUESTS ? R.drawable.btn_primary : R.drawable.bg_box);
            subTabBtnQuests.setTextColor(ContextCompat.getColor(this, subTab == DROPS_SUBTAB_QUESTS ? R.color.white : R.color.text_muted));
        }
        if (subTabBtnTempDrops != null) {
            subTabBtnTempDrops.setBackgroundResource(subTab == DROPS_SUBTAB_TEMP ? R.drawable.btn_primary : R.drawable.bg_box);
            subTabBtnTempDrops.setTextColor(ContextCompat.getColor(this, subTab == DROPS_SUBTAB_TEMP ? R.color.white : R.color.text_muted));
        }
        if (subTabBtnDrops != null) {
            subTabBtnDrops.setBackgroundResource(subTab == DROPS_SUBTAB_DROPS ? R.drawable.btn_primary : R.drawable.bg_box);
            subTabBtnDrops.setTextColor(ContextCompat.getColor(this, subTab == DROPS_SUBTAB_DROPS ? R.color.white : R.color.text_muted));
        }

        if (containerQuests != null) containerQuests.setVisibility(subTab == DROPS_SUBTAB_QUESTS ? View.VISIBLE : View.GONE);
        if (containerTempDrops != null) containerTempDrops.setVisibility(subTab == DROPS_SUBTAB_TEMP ? View.VISIBLE : View.GONE);
        if (containerScreenDrops != null) containerScreenDrops.setVisibility(subTab == DROPS_SUBTAB_DROPS ? View.VISIBLE : View.GONE);

        pollStatus();
    }

    private void switchInventorySubTab(int subTab) {
        currentInventorySubTab = subTab;

        if (subTab == SUBTAB_INVENTORY) {
            btnOpenBankInv.setVisibility(View.GONE);
            inventoryAdapter.setItems(cachedInventory, false);
            updateInventoryCountHeader();
        } else {
            btnOpenBankInv.setVisibility(isBankLoaded ? View.GONE : View.VISIBLE);
            inventoryAdapter.setItems(cachedBank, true);
            updateInventoryCountHeader();
        }
    }

    private void selectInvCategory(String cat, Button clickedBtn) {
        for (Button btn : invCategoryButtons) {
            btn.setBackgroundResource(R.drawable.bg_box);
            btn.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
        }
        clickedBtn.setBackgroundResource(R.drawable.btn_primary);
        clickedBtn.setTextColor(ContextCompat.getColor(this, R.color.white));
        inventoryAdapter.setCategory(cat);
    }

    private void updateInventoryCountHeader() {
        if (currentInventorySubTab == SUBTAB_INVENTORY) {
            tvInventoryCount.setText("Bag (" + invUsedSlots + " / " + invMaxSlots + ")");
        } else {
            String text = "Bank (" + bankUsedSlots + " / " + bankMaxSlots + ")";
            text += isBankLoaded ? " | Loaded" : " | Tap 'Open Bank' to load all";
            tvInventoryCount.setText(text);
        }
    }

    private void fetchInventoryAndBank() {
        ApiClient.getAsync(getBaseUrl() + "/api/inventory", getRequestHeaders(), 15000, new ApiClient.Callback<String>() {
            @Override
            public void onSuccess(String result) {
                try {
                    JSONObject obj = new JSONObject(result);
                    invUsedSlots = obj.has("used_slots") ? obj.optInt("used_slots", 0) : obj.optInt("used", obj.optInt("count", 0));
                    invMaxSlots = obj.has("max_slots") ? obj.optInt("max_slots", 0) : obj.optInt("max", 0);
                    cachedInventory.clear();
                    JSONArray invArr = obj.optJSONArray("items");
                    if (invArr != null) {
                        for (int i = 0; i < invArr.length(); i++) {
                            JSONObject item = invArr.optJSONObject(i);
                            if (item != null) {
                                cachedInventory.add(new Models.InventoryItem(
                                        item.optInt("id", 0),
                                        item.optInt("char_item_id", 0),
                                        item.optString("name", ""),
                                        item.optInt("quantity", 1),
                                        item.optInt("max_quantity", 1),
                                        item.optBoolean("is_ac", false),
                                        item.optBoolean("is_equipped", false),
                                        item.optString("category", "Item"),
                                        item.optBoolean("is_upgrade", false)
                                ));
                            }
                        }
                    }

                    if (currentInventorySubTab == SUBTAB_INVENTORY) {
                        inventoryAdapter.setItems(cachedInventory, false);
                        updateInventoryCountHeader();
                    }
                    updateVaultSubTabCounts();
                } catch (Exception ignored) { }
            }

            @Override
            public void onError(Exception error) {
                consecutivePollFailures++;
                if (consecutivePollFailures >= 2) {
                    onConnectionLost();
                }
            }
        });

        ApiClient.getAsync(getBaseUrl() + "/api/bank", getRequestHeaders(), 15000, new ApiClient.Callback<String>() {
            @Override
            public void onSuccess(String result) {
                try {
                    JSONObject obj = new JSONObject(result);
                    bankUsedSlots = obj.has("used_slots") ? obj.optInt("used_slots", 0) : obj.optInt("used", obj.optInt("count", 0));
                    bankMaxSlots = obj.has("max_slots") ? obj.optInt("max_slots", 0) : obj.optInt("max", 0);
                    isBankLoaded = obj.has("is_loaded") ? obj.optBoolean("is_loaded", false) : obj.optBoolean("loaded", false);
                    cachedBank.clear();
                    JSONArray bankArr = obj.optJSONArray("items");
                    if (bankArr != null) {
                        for (int i = 0; i < bankArr.length(); i++) {
                            JSONObject item = bankArr.optJSONObject(i);
                            if (item != null) {
                                cachedBank.add(new Models.InventoryItem(
                                        item.optInt("id", 0),
                                        item.optInt("char_item_id", 0),
                                        item.optString("name", ""),
                                        item.optInt("quantity", 1),
                                        item.optInt("max_quantity", 1),
                                        item.optBoolean("is_ac", false),
                                        false,
                                        item.optString("category", "Item"),
                                        item.optBoolean("is_upgrade", false)
                                ));
                            }
                        }
                    }

                    if (currentInventorySubTab == SUBTAB_BANK) {
                        btnOpenBankInv.setVisibility(isBankLoaded ? View.GONE : View.VISIBLE);
                        inventoryAdapter.setItems(cachedBank, true);
                        updateInventoryCountHeader();
                    }
                    updateVaultSubTabCounts();
                } catch (Exception ignored) { }
            }

            @Override
            public void onError(Exception error) { }
        });
    }

    private void updateVaultSubTabCounts() {
        if (subTabBtnInventory != null) {
            subTabBtnInventory.setText("Bag (" + invUsedSlots + "/" + invMaxSlots + ")");
        }
        if (subTabBtnBank != null) {
            subTabBtnBank.setText("Bank (" + bankUsedSlots + "/" + bankMaxSlots + ")");
        }
    }

    private String getBaseUrl() {
        return "http://" + currentHost + ":" + currentPort;
    }

    private Map<String, String> getRequestHeaders() {
        Map<String, String> headers = new HashMap<>();
        if (isBroadcastActive) {
            headers.put("X-Broadcast", "true");
        } else if (currentSelectedPid > 0) {
            headers.put("X-Target-PID", String.valueOf(currentSelectedPid));
        }
        return headers;
    }

    private void connectToServer(boolean manualClick) {
        currentHost = editHost.getText().toString().trim();
        try {
            currentPort = Integer.parseInt(editPort.getText().toString().trim());
        } catch (NumberFormatException e) {
            currentPort = 8088;
        }

        prefs.edit().putString(KEY_HOST, currentHost).putInt(KEY_PORT, currentPort).apply();

        tvConnectionStatus.setText("Connecting...");
        tvConnectionStatus.setTextColor(ContextCompat.getColor(this, R.color.orange));

        ApiClient.getAsync(getBaseUrl() + "/api/ping", new ApiClient.Callback<String>() {
            @Override
            public void onSuccess(String result) {
                isConnected = true;
                tvConnectionStatus.setText("Online");
                tvConnectionStatus.setTextColor(ContextCompat.getColor(MainActivity.this, R.color.green));
                panelConnection.setVisibility(View.GONE);

                startPolling();
                fetchTabs();
                fetchScripts();
                fetchSavedAccounts();
                pollArmyQueue();
                updateAccountPill();
                if (manualClick) {
                    Toast.makeText(MainActivity.this, "Connected to VibeSkua!", Toast.LENGTH_SHORT).show();
                }
            }

            @Override
            public void onError(Exception error) {
                isConnected = false;
                tvConnectionStatus.setText("Offline");
                tvConnectionStatus.setTextColor(ContextCompat.getColor(MainActivity.this, R.color.red));
                updateAccountPill();
                if (manualClick) {
                    Toast.makeText(MainActivity.this, "Connection failed: " + error.getMessage(), Toast.LENGTH_SHORT).show();
                }
            }
        });
    }

    private void performLanScan() {
        Toast.makeText(this, "Scanning local network for VibeSkua...", Toast.LENGTH_SHORT).show();
        btnScanLan.setText("Scanning...");
        btnScanLan.setEnabled(false);

        String baseSubnet = ApiClient.getLocalSubnet();

        ApiClient.scanSubnetAsync(baseSubnet, currentPort, new ApiClient.Callback<String>() {
            @Override
            public void onSuccess(String foundHost) {
                btnScanLan.setText("SCAN NETWORK FOR SKUA");
                btnScanLan.setEnabled(true);
                editHost.setText(foundHost);
                Toast.makeText(MainActivity.this, "Found VibeSkua at " + foundHost + "!", Toast.LENGTH_SHORT).show();
                connectToServer(true);
            }

            @Override
            public void onError(Exception error) {
                btnScanLan.setText("SCAN NETWORK FOR SKUA");
                btnScanLan.setEnabled(true);
                Toast.makeText(MainActivity.this, "No VibeSkua instances found on LAN", Toast.LENGTH_SHORT).show();
            }
        });
    }

    private void startPolling() {
        stopPolling();
        pollRunnable = new Runnable() {
            @Override
            public void run() {
                if (isConnected) {
                    pollStatus();
                    fetchTabs();
                }
                pollHandler.postDelayed(this, pollIntervalMs);
            }
        };
        pollHandler.post(pollRunnable);
    }

    private void stopPolling() {
        if (pollRunnable != null) {
            pollHandler.removeCallbacks(pollRunnable);
            pollRunnable = null;
        }
    }

    private void startBackgroundPolling() {
        stopBackgroundPolling();
        backgroundPollRunnable = new Runnable() {
            @Override
            public void run() {
                if (isConnected) {
                    pollStatus();
                    fetchTabs();
                }
                backgroundPollHandler.postDelayed(this, 4000);
            }
        };
        backgroundPollHandler.postDelayed(backgroundPollRunnable, 4000);
    }

    private void stopBackgroundPolling() {
        if (backgroundPollRunnable != null) {
            backgroundPollHandler.removeCallbacks(backgroundPollRunnable);
            backgroundPollRunnable = null;
        }
    }

    private void toggleStreamerMode() {
        isStreamerMode = !isStreamerMode;
        prefs.edit().putBoolean(KEY_STREAMER_MODE, isStreamerMode).apply();
        updateStreamerButtonState();
        if (lastBotStatus != null) {
            updateUi(lastBotStatus);
        }
        updateAccountPill();
        updateAccountChips();
        updateFleetDeck();
        updateSpawnerUi();
        renderChatHistory();
        Toast.makeText(this, isStreamerMode ? "Streamer Mode: ON (Sensitive data blurred)" : "Streamer Mode: OFF", Toast.LENGTH_SHORT).show();
    }

    private void updateStreamerButtonState() {
        if (btnToggleStreamer == null) return;
        btnToggleStreamer.setImageResource(isStreamerMode ? R.drawable.ic_eye_off : R.drawable.ic_eye);
        btnToggleStreamer.setBackgroundResource(isStreamerMode ? R.drawable.bg_account_pill : R.drawable.bg_box);
        btnToggleStreamer.setColorFilter(ContextCompat.getColor(this, isStreamerMode ? R.color.accent : R.color.text_muted), android.graphics.PorterDuff.Mode.SRC_IN);
    }

    private void applyStreamerBlur(View view, boolean blur) {
        if (view == null) return;
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.S) {
            if (blur) {
                view.setRenderEffect(RenderEffect.createBlurEffect(18f, 18f, Shader.TileMode.CLAMP));
            } else {
                view.setRenderEffect(null);
            }
        } else {
            if (view instanceof TextView) {
                TextView tv = (TextView) view;
                if (blur) {
                    tv.setLayerType(View.LAYER_TYPE_SOFTWARE, null);
                    tv.getPaint().setMaskFilter(new BlurMaskFilter(14f, BlurMaskFilter.Blur.NORMAL));
                } else {
                    tv.getPaint().setMaskFilter(null);
                    tv.setLayerType(View.LAYER_TYPE_NONE, null);
                }
                tv.invalidate();
            } else {
                view.setAlpha(blur ? 0.08f : 1.0f);
            }
        }
    }

    private String maskUsername(String name) {
        return name != null ? name : "";
    }

    private String maskMap(String map) {
        return map != null ? map : "";
    }

    private void onConnectionLost() {
        if (!isConnected && runningTabs.isEmpty()) return;
        isConnected = false;
        runningTabs.clear();
        AppLogger.w("Connection", "PC client disconnected or closed.");
        tvConnectionStatus.setText("Offline");
        tvConnectionStatus.setTextColor(ContextCompat.getColor(this, R.color.red));
        updateAccountPill();
        updateAccountChips();
        updateFleetDeck();

        if (tvPlayerName != null) {
            tvPlayerName.setText("Client Offline");
            tvPlayerName.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
        }
        if (tvPlayerClass != null) {
            tvPlayerClass.setText("PC Client Closed / Disconnected");
        }
        if (tvHp != null) tvHp.setText("0 / 0 (0%)");
        if (tvMp != null) tvMp.setText("0 / 0 (0%)");
        if (progressBarHp != null) progressBarHp.setProgress(0);
        if (progressBarMp != null) progressBarMp.setProgress(0);

        if (tvScriptStatus != null) {
            tvScriptStatus.setText("OFFLINE");
            tvScriptStatus.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
        }
        if (tvScriptName != null) tvScriptName.setText("No active connection");
        if (tvScriptPath != null) tvScriptPath.setVisibility(View.GONE);
        if (containerScriptProgress != null) containerScriptProgress.setVisibility(View.GONE);
        if (btnStartScript != null) btnStartScript.setEnabled(false);
        if (btnPauseScript != null) btnPauseScript.setEnabled(false);
        if (btnStopScript != null) btnStopScript.setEnabled(false);

        if (tvTargetName != null) tvTargetName.setText("No Target");
        if (tvTargetHp != null) {
            tvTargetHp.setText("0 / 0 HP");
            tvTargetHp.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
        }

        if (autoReconnect && !currentHost.isEmpty()) {
            new Handler(Looper.getMainLooper()).postDelayed(() -> {
                if (!isConnected) {
                    AppLogger.i("Connection", "Attempting auto-reconnect to " + currentHost + ":" + currentPort + "...");
                    connectToServer(false);
                }
            }, 3000);
        }
    }

    private void pollStatus() {
        ApiClient.getAsync(getBaseUrl() + "/api/status", getRequestHeaders(), 15000, new ApiClient.Callback<String>() {
            @Override
            public void onSuccess(String result) {
                consecutivePollFailures = 0;
                isConnected = true;
                Models.BotStatus status = Models.BotStatus.fromJson(result);
                lastBotStatus = status;
                updateUi(status);
                checkAccountNotifications(status);
                if (status.recentChat != null && !status.recentChat.isEmpty()) {
                    updateChatFeed(status.recentChat);
                }
            }

            @Override
            public void onError(Exception error) {
                consecutivePollFailures++;
                if (consecutivePollFailures >= 2) {
                    onConnectionLost();
                } else {
                    tvConnectionStatus.setText("Reconnecting...");
                    tvConnectionStatus.setTextColor(ContextCompat.getColor(MainActivity.this, R.color.orange));
                }
            }
        });
    }

    private void updateUi(Models.BotStatus s) {
        tvConnectionStatus.setText("Online");
        tvConnectionStatus.setTextColor(ContextCompat.getColor(this, R.color.green));

        tvPlayerName.setText((s.username != null ? s.username : "Player") + " (Lv. " + s.level + ")");
        tvPlayerClass.setText("Class: " + s.currentClass);

        // HP
        int hpPct = s.maxHp > 0 ? (int) Math.min(100, Math.round(((double) s.hp / s.maxHp) * 100)) : 0;
        progressBarHp.setProgress(hpPct);
        tvHp.setText(String.format("%,d / %,d (%d%%)", s.hp, s.maxHp, hpPct));
        progressBarHp.getProgressDrawable().setColorFilter(
                ContextCompat.getColor(this, hpPct < 30 ? R.color.red : hpPct < 60 ? R.color.orange : R.color.green),
                android.graphics.PorterDuff.Mode.SRC_IN
        );

        // MP
        int mpPct = s.maxMp > 0 ? (int) Math.min(100, Math.round(((double) s.mp / s.maxMp) * 100)) : 0;
        progressBarMp.setProgress(mpPct);
        tvMp.setText(String.format("%,d / %,d (%d%%)", s.mp, s.maxMp, mpPct));

        // Script Status
        if (s.scriptRunning) {
            tvScriptStatus.setText("RUNNING");
            tvScriptStatus.setTextColor(ContextCompat.getColor(this, R.color.green));
            btnStartScript.setEnabled(false);
            btnPauseScript.setEnabled(true);
            btnStopScript.setEnabled(true);
        } else {
            tvScriptStatus.setText("STOPPED");
            tvScriptStatus.setTextColor(ContextCompat.getColor(this, R.color.red));
            btnStartScript.setEnabled(true);
            btnPauseScript.setEnabled(false);
            btnStopScript.setEnabled(false);
        }

        // Script Name & Path
        if (s.loadedScript != null && !s.loadedScript.isEmpty()) {
            tvScriptName.setText(s.loadedScript);
            tvScriptPath.setText(s.loadedScriptPath != null ? s.loadedScriptPath : "");
            tvScriptPath.setVisibility(View.VISIBLE);
        } else {
            tvScriptName.setText("No script loaded");
            tvScriptPath.setVisibility(View.GONE);
        }

        // Progress & Current Step
        if (containerScriptProgress != null) {
            if (s.scriptRunning || (s.scriptStatus != null && !s.scriptStatus.isEmpty())) {
                containerScriptProgress.setVisibility(View.VISIBLE);
                if (s.scriptStatus != null && !s.scriptStatus.isEmpty()) {
                    tvScriptCurrentStep.setText(s.scriptStatus);
                } else {
                    tvScriptCurrentStep.setText(s.loadedScript != null && !s.loadedScript.isEmpty() ? "Script loaded, ready" : "Idle");
                }

                boolean hasMap = (s.map != null && !s.map.trim().isEmpty() && !s.map.equalsIgnoreCase("Unknown"));
                boolean hasTarget = (s.targetName != null && !s.targetName.trim().isEmpty());

                if (hasMap || hasTarget) {
                    StringBuilder locSb = new StringBuilder();
                    if (hasMap) {
                        locSb.append(maskMap(s.map.trim()));
                        String cell = (s.cell != null && !s.cell.trim().isEmpty()) ? s.cell.trim() : "";
                        String pad = (s.pad != null && !s.pad.trim().isEmpty()) ? s.pad.trim() : "";
                        if (!cell.isEmpty() && !pad.isEmpty()) {
                            locSb.append(" [").append(cell).append(", ").append(pad).append("]");
                        } else if (!cell.isEmpty()) {
                            locSb.append(" [").append(cell).append("]");
                        }
                    }
                    if (hasTarget) {
                        if (locSb.length() > 0) locSb.append(" | ");
                        locSb.append("TARGET: ").append(s.targetName.trim());
                        if (s.targetMaxHp > 0) {
                            locSb.append(" (").append(s.targetHp).append("/").append(s.targetMaxHp).append(")");
                        }
                    }
                    tvScriptLocation.setText(locSb.toString());
                    tvScriptLocation.setVisibility(View.VISIBLE);
                } else {
                    tvScriptLocation.setText("");
                    tvScriptLocation.setVisibility(View.GONE);
                }

                if (s.activeQuests != null && !s.activeQuests.isEmpty()) {
                    StringBuilder qb = new StringBuilder();
                    for (int i = 0; i < s.activeQuests.size(); i++) {
                        Models.QuestItem q = s.activeQuests.get(i);
                        if (i > 0) qb.append("  |  ");
                        qb.append(q.name);
                        if (q.canComplete) {
                            qb.append(" (Ready)");
                        }
                    }
                    tvScriptQuests.setText(qb.toString());
                    tvScriptQuests.setVisibility(View.VISIBLE);
                } else {
                    tvScriptQuests.setVisibility(View.GONE);
                }
            } else {
                containerScriptProgress.setVisibility(View.GONE);
            }
        }

        // Live Script Logs (Dashboard Card)
        if (containerScriptLogs != null) {
            if (s.scriptLogs != null && !s.scriptLogs.isEmpty()) {
                containerScriptLogs.setVisibility(View.VISIBLE);
                StringBuilder sb = new StringBuilder();
                int maxLines = Math.min(6, s.scriptLogs.size());
                int start = Math.max(0, s.scriptLogs.size() - maxLines);
                for (int i = start; i < s.scriptLogs.size(); i++) {
                    if (sb.length() > 0) sb.append("\n");
                    sb.append("> ").append(s.scriptLogs.get(i));
                }
                tvScriptLogs.setText(sb.toString());
            } else {
                containerScriptLogs.setVisibility(View.GONE);
            }
        }

        // Target Card
        if (s.targetName != null && !s.targetName.isEmpty()) {
            tvTargetName.setText(s.targetName);
            int tPct = s.targetMaxHp > 0 ? (int) Math.min(100, Math.round(((double) s.targetHp / s.targetMaxHp) * 100)) : 0;
            tvTargetHp.setText(String.format("%,d / %,d (%d%%)", s.targetHp, s.targetMaxHp, tPct));
            tvTargetHp.setTextColor(ContextCompat.getColor(this, tPct < 30 ? R.color.red : tPct < 60 ? R.color.orange : R.color.green));
        } else {
            tvTargetName.setText("No Target");
            tvTargetHp.setText("-");
            tvTargetHp.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
        }

        // Stats Card
        tvStatKills.setText(String.format("%,d", s.kills));
        tvStatDrops.setText(String.format("%,d", s.drops));
        tvStatGold.setText(String.format("%,d", s.gold));
        tvStatAcs.setText(String.format("%,d", s.coins));
        tvStatQuests.setText(String.format("%,d", s.questsCompleted));
        tvStatUptime.setText(s.uptime != null && !s.uptime.isEmpty() ? s.uptime : "00:00:00");
        String mapLabel = s.map != null ? s.map : "";
        String cellLabel = (s.cell != null && !s.cell.isEmpty()) ? s.cell : "Enter";
        tvStatMap.setText((!mapLabel.isEmpty() ? mapLabel : "Not in World") + " (" + cellLabel + ")");

        applyStreamerBlur(tvPlayerName, isStreamerMode);
        applyStreamerBlur(tvScriptPath, isStreamerMode);
        applyStreamerBlur(tvStatGold, isStreamerMode);
        applyStreamerBlur(tvStatAcs, isStreamerMode);
        applyStreamerBlur(tvStatMap, isStreamerMode);
        applyStreamerBlur(tvActiveAccountName, isStreamerMode);

        if (s.inventoryMax > 0 && invMaxSlots == 0) {
            invUsedSlots = s.inventoryUsed;
            invMaxSlots = s.inventoryMax;
        }
        if (s.bankMax > 0 && bankMaxSlots == 0) {
            bankUsedSlots = s.bankUsed;
            bankMaxSlots = s.bankMax;
            isBankLoaded = s.bankLoaded;
        }
        updateVaultSubTabCounts();
        updateInventoryCountHeader();

        // Subtabs count badges in Vault
        if (subTabBtnQuests != null && s.activeQuests != null) {
            subTabBtnQuests.setText("Quests (" + s.activeQuests.size() + ")");
        }
        if (tvQuestsHeader != null && s.activeQuests != null) {
            tvQuestsHeader.setText("ACTIVE QUESTS (" + s.activeQuests.size() + ")");
        }
        if (subTabBtnDrops != null && s.currentDrops != null) {
            subTabBtnDrops.setText("Drops (" + s.currentDrops.size() + ")");
        }
        if (subTabBtnTempDrops != null && s.tempItems != null) {
            subTabBtnTempDrops.setText("Temp (" + s.tempItems.size() + ")");
        }

        // Update adapters
        if (dropsAdapter != null && s.currentDrops != null) {
            dropsAdapter.setDrops(s.currentDrops);
        }
        if (tempDropsAdapter != null && s.tempItems != null) {
            tempDropsAdapter.setItems(s.tempItems);
        }
        if (questsAdapter != null && s.activeQuests != null) {
            questsAdapter.setQuests(s.activeQuests);
        }

        // Options toggles
        updateToggleState(btnToggleLagKiller, s.lagKiller);
        updateToggleState(btnToggleAutoRelogin, s.autoRelogin);
        updateToggleState(btnToggleInfiniteRange, s.infiniteRange);
        updateToggleState(btnToggleHeadlessMode, s.headlessMode);
        updateToggleState(btnToggleStreamerMode, s.streamerMode);
    }

    private void updateToggleState(Button btn, boolean enabled) {
        if (btn == null) return;
        btn.setBackgroundResource(enabled ? R.drawable.btn_primary : R.drawable.bg_box);
        btn.setTextColor(ContextCompat.getColor(this, enabled ? R.color.white : R.color.text_muted));
    }

    private void toggleOption(String optKey, Button btn) {
        try {
            JSONObject body = new JSONObject();
            body.put("option", optKey);
            sendPost("/api/options/toggle", body.toString(), "Toggled " + optKey);
        } catch (Exception ignored) { }
    }

    private void sendPost(String endpoint, String jsonBody, String successMsg) {
        sendPost(endpoint, jsonBody, successMsg, null);
    }

    private void sendPost(String endpoint, String jsonBody, String successMsg, ApiClient.Callback<String> callback) {
        AppLogger.i("Action", "Request [" + endpoint + "] " + (jsonBody != null && !jsonBody.equals("{}") ? jsonBody : ""));
        ApiClient.postAsync(getBaseUrl() + endpoint, jsonBody, getRequestHeaders(), 15000, new ApiClient.Callback<String>() {
            @Override
            public void onSuccess(String result) {
                AppLogger.i("Action", "Success [" + endpoint + "]");
                if (successMsg != null && !successMsg.isEmpty()) {
                    Toast.makeText(MainActivity.this, successMsg, Toast.LENGTH_SHORT).show();
                }
                pollStatus();
                if (callback != null) callback.onSuccess(result);
            }

            @Override
            public void onError(Exception error) {
                AppLogger.e("Action", "Error [" + endpoint + "]: " + error.getMessage());
                Toast.makeText(MainActivity.this, "Error: " + error.getMessage(), Toast.LENGTH_SHORT).show();
                if (callback != null) callback.onError(error);
            }
        });
    }

    // MULTI-ACCOUNT & ARMY COMMAND DECK
    private void toggleBroadcast() {
        isBroadcastActive = !isBroadcastActive;
        if (isBroadcastActive) {
            btnBroadcastToggle.setBackgroundResource(R.drawable.btn_red);
            btnBroadcastToggle.setTextColor(ContextCompat.getColor(this, R.color.white));
            btnBroadcastToggle.setText("BROADCAST ON");
            if (tvFleetBroadcastIndicator != null) {
                tvFleetBroadcastIndicator.setVisibility(View.VISIBLE);
                tvFleetBroadcastIndicator.setText("MIRROR ACTIVE • Commands broadcast to all accounts");
            }
            Toast.makeText(this, "Broadcast Mode ACTIVE: Commands mirror to all accounts!", Toast.LENGTH_SHORT).show();
        } else {
            btnBroadcastToggle.setBackgroundResource(R.drawable.bg_box);
            btnBroadcastToggle.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
            btnBroadcastToggle.setText("Broadcast Off");
            if (tvFleetBroadcastIndicator != null) {
                tvFleetBroadcastIndicator.setVisibility(View.GONE);
            }
            Toast.makeText(this, "Broadcast Mode Disabled", Toast.LENGTH_SHORT).show();
        }
        updateAccountPill();
        updateAccountChips();
    }

    private void updateAccountPill() {
        if (tvActiveAccountName == null) return;
        if (isBroadcastActive) {
            tvActiveAccountName.setText("ALL ARMY TABS");
            if (ivTopLeaderCrown != null) ivTopLeaderCrown.setVisibility(View.GONE);
            if (viewTopStatusDot != null) viewTopStatusDot.setBackgroundResource(R.drawable.shape_status_dead);
            return;
        }
        if (!isConnected || runningTabs.isEmpty()) {
            tvActiveAccountName.setText(isConnected ? "MANAGER ONLINE" : "OFFLINE");
            if (ivTopLeaderCrown != null) ivTopLeaderCrown.setVisibility(View.GONE);
            if (viewTopStatusDot != null) viewTopStatusDot.setBackgroundResource(isConnected ? R.drawable.shape_status_online : R.drawable.shape_status_offline);
            return;
        }
        Models.TabAccount currentTab = null;
        for (Models.TabAccount t : runningTabs) {
            if (t.pid == currentSelectedPid) {
                currentTab = t;
                break;
            }
        }
        if (currentTab == null && !runningTabs.isEmpty()) {
            currentTab = runningTabs.get(0);
            currentSelectedPid = currentTab.pid;
        }
        if (currentTab != null) {
            String name = (currentTab.username != null && !currentTab.username.isEmpty()) ? currentTab.username : "Skua 1";
            tvActiveAccountName.setText(name.toUpperCase());
            applyStreamerBlur(tvActiveAccountName, isStreamerMode);
            if (ivTopLeaderCrown != null) ivTopLeaderCrown.setVisibility(View.VISIBLE);
            if (viewTopStatusDot != null) {
                if (currentTab.hp <= 0 && currentTab.maxHp > 0) {
                    viewTopStatusDot.setBackgroundResource(R.drawable.shape_status_dead);
                } else if (currentTab.scriptRunning) {
                    viewTopStatusDot.setBackgroundResource(R.drawable.shape_status_running);
                } else {
                    viewTopStatusDot.setBackgroundResource(R.drawable.shape_status_online);
                }
            }
        }
    }

    private void showAccountSwitcherDialog() {
        AlertDialog.Builder builder = new AlertDialog.Builder(this);
        View view = getLayoutInflater().inflate(R.layout.dialog_account_switcher, null);
        builder.setView(view);
        AlertDialog dialog = builder.create();
        if (dialog.getWindow() != null) {
            dialog.getWindow().setBackgroundDrawableResource(android.R.color.transparent);
        }

        Button btnAccountDialogClose = view.findViewById(R.id.btnAccountDialogClose);
        Button btnBroadcastDialog = view.findViewById(R.id.btnBroadcastDialog);
        LinearLayout layoutAccountDialogList = view.findViewById(R.id.layoutAccountDialogList);
        TextView tvNoAccountsWarning = view.findViewById(R.id.tvNoAccountsWarning);
        Button btnLaunchNewAccount = view.findViewById(R.id.btnLaunchNewAccount);

        if (isBroadcastActive) {
            btnBroadcastDialog.setText("BROADCAST ON");
            btnBroadcastDialog.setBackgroundResource(R.drawable.btn_red);
            btnBroadcastDialog.setTextColor(ContextCompat.getColor(this, R.color.white));
        } else {
            btnBroadcastDialog.setText("Broadcast Off");
            btnBroadcastDialog.setBackgroundResource(R.drawable.bg_box);
            btnBroadcastDialog.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
        }

        btnBroadcastDialog.setOnClickListener(v -> {
            toggleBroadcast();
            dialog.dismiss();
        });

        btnLaunchNewAccount.setOnClickListener(v -> {
            dialog.dismiss();
            showQuickLaunchDialog();
        });

        if (btnAccountDialogClose != null) {
            btnAccountDialogClose.setOnClickListener(v -> dialog.dismiss());
        }

        if (runningTabs.isEmpty()) {
            tvNoAccountsWarning.setVisibility(View.VISIBLE);
        } else {
            tvNoAccountsWarning.setVisibility(View.GONE);
            for (Models.TabAccount tab : runningTabs) {
                View itemView = getLayoutInflater().inflate(R.layout.item_account_switch, layoutAccountDialogList, false);
                ImageView ivAccountLeaderCrown = itemView.findViewById(R.id.ivAccountLeaderCrown);
                View viewAccountStatusDot = itemView.findViewById(R.id.viewAccountStatusDot);
                TextView tvAccountUsername = itemView.findViewById(R.id.tvAccountUsername);
                TextView tvAccountPid = itemView.findViewById(R.id.tvAccountPid);
                TextView tvAccountDetails = itemView.findViewById(R.id.tvAccountDetails);
                TextView tvAccountScriptStatus = itemView.findViewById(R.id.tvAccountScriptStatus);
                Button btnItemFocusPc = itemView.findViewById(R.id.btnItemFocusPc);

                tvAccountUsername.setText(tab.username);
                tvAccountPid.setText("PID: " + tab.pid);
                String details = (tab.server != null && !tab.server.isEmpty() ? tab.server : "Game") +
                        (!tab.map.isEmpty() ? " | " + tab.map : "") +
                        (tab.maxHp > 0 ? " | HP: " + tab.hp + "/" + tab.maxHp : "");
                tvAccountDetails.setText(details);
                applyStreamerBlur(tvAccountUsername, isStreamerMode);
                applyStreamerBlur(tvAccountDetails, isStreamerMode);

                if (tab.hp <= 0 && tab.maxHp > 0) {
                    tvAccountScriptStatus.setText("Player Dead");
                    tvAccountScriptStatus.setTextColor(ContextCompat.getColor(this, R.color.red));
                } else if (tab.scriptRunning) {
                    String sName = (tab.scriptName != null && !tab.scriptName.isEmpty()) ? tab.scriptName : "Active";
                    tvAccountScriptStatus.setText("Running: " + sName);
                    tvAccountScriptStatus.setTextColor(ContextCompat.getColor(this, R.color.green));
                } else {
                    tvAccountScriptStatus.setText("Script Idle");
                    tvAccountScriptStatus.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
                }

                boolean isSelected = (tab.pid == currentSelectedPid);
                if (ivAccountLeaderCrown != null) {
                    ivAccountLeaderCrown.setVisibility(isSelected ? View.VISIBLE : View.GONE);
                }
                if (viewAccountStatusDot != null) {
                    if (tab.hp <= 0 && tab.maxHp > 0) {
                        viewAccountStatusDot.setBackgroundResource(R.drawable.shape_status_dead);
                    } else if (tab.scriptRunning) {
                        viewAccountStatusDot.setBackgroundResource(R.drawable.shape_status_running);
                    } else {
                        viewAccountStatusDot.setBackgroundResource(R.drawable.shape_status_online);
                    }
                }
                itemView.setBackgroundResource(isSelected ? R.drawable.bg_account_pill : R.drawable.bg_box);

                itemView.setOnClickListener(v -> {
                    currentSelectedPid = tab.pid;
                    updateAccountPill();
                    updateAccountChips();
                    pollStatus();
                    dialog.dismiss();
                    Toast.makeText(MainActivity.this, "Switched to " + tab.username, Toast.LENGTH_SHORT).show();
                });

                if (btnItemFocusPc != null) {
                    btnItemFocusPc.setOnClickListener(v -> {
                        try {
                            JSONObject body = new JSONObject();
                            body.put("pid", tab.pid);
                            sendPost("/api/tabs/focus-pc", body.toString(), "Focused " + tab.username + " on PC");
                        } catch (Exception ignored) { }
                    });
                }

                layoutAccountDialogList.addView(itemView);
            }
        }

        dialog.show();
    }

    private void showConnectionSettingsDialog() {
        AlertDialog.Builder builder = new AlertDialog.Builder(this);
        View view = getLayoutInflater().inflate(R.layout.dialog_connection_settings, null);
        builder.setView(view);
        AlertDialog dialog = builder.create();
        if (dialog.getWindow() != null) {
            dialog.getWindow().setBackgroundDrawableResource(android.R.color.transparent);
        }

        TextView tvSettingsStatus = view.findViewById(R.id.tvSettingsStatus);
        EditText dlgEditHost = view.findViewById(R.id.editHost);
        EditText dlgEditPort = view.findViewById(R.id.editPort);
        Button dlgBtnConnect = view.findViewById(R.id.btnConnect);
        Button dlgBtnScanLan = view.findViewById(R.id.btnScanLan);
        Button btnSettingsClose = view.findViewById(R.id.btnSettingsClose);

        Button btnPollFast = view.findViewById(R.id.btnPollFast);
        Button btnPollNormal = view.findViewById(R.id.btnPollNormal);
        Button btnPollSaver = view.findViewById(R.id.btnPollSaver);
        CheckBox chkAutoReconnect = view.findViewById(R.id.chkAutoReconnect);

        // Notification Controls moved into Settings
        spinnerNotifyAccounts = view.findViewById(R.id.spinnerNotifyAccounts);
        chkNotifyMuteAll = view.findViewById(R.id.chkNotifyMuteAll);
        chkNotifyScriptStopped = view.findViewById(R.id.chkNotifyScriptStopped);
        chkNotifyPlayerDeath = view.findViewById(R.id.chkNotifyPlayerDeath);
        chkNotifyDisconnect = view.findViewById(R.id.chkNotifyDisconnect);
        chkNotifyItemDrop = view.findViewById(R.id.chkNotifyItemDrop);
        btnTestNotification = view.findViewById(R.id.btnTestNotification);

        Button btnViewAppLogs = view.findViewById(R.id.btnViewAppLogs);

        if (isConnected) {
            tvSettingsStatus.setText("Connected: " + (currentHost.isEmpty() ? "127.0.0.1" : currentHost) + ":" + currentPort);
            tvSettingsStatus.setTextColor(ContextCompat.getColor(this, R.color.primary));
        } else {
            tvSettingsStatus.setText("Disconnected from PC");
            tvSettingsStatus.setTextColor(ContextCompat.getColor(this, R.color.red));
        }

        dlgEditHost.setText(currentHost != null ? currentHost : "");
        dlgEditPort.setText(String.valueOf(currentPort));

        // Setup Polling Rate Buttons
        Runnable updatePollButtons = () -> {
            if (btnPollFast != null) {
                btnPollFast.setBackgroundResource(pollIntervalMs <= 1000 ? R.drawable.bg_account_pill : R.drawable.bg_box);
                btnPollFast.setTextColor(ContextCompat.getColor(this, pollIntervalMs <= 1000 ? R.color.white : R.color.text_muted));
            }
            if (btnPollNormal != null) {
                btnPollNormal.setBackgroundResource(pollIntervalMs == 2000 ? R.drawable.bg_account_pill : R.drawable.bg_box);
                btnPollNormal.setTextColor(ContextCompat.getColor(this, pollIntervalMs == 2000 ? R.color.white : R.color.text_muted));
            }
            if (btnPollSaver != null) {
                btnPollSaver.setBackgroundResource(pollIntervalMs >= 5000 ? R.drawable.bg_account_pill : R.drawable.bg_box);
                btnPollSaver.setTextColor(ContextCompat.getColor(this, pollIntervalMs >= 5000 ? R.color.white : R.color.text_muted));
            }
        };
        updatePollButtons.run();

        if (btnPollFast != null) {
            btnPollFast.setOnClickListener(v -> {
                pollIntervalMs = 1000;
                updatePollButtons.run();
                startPolling();
                AppLogger.i("Settings", "Poll interval set to 1s (Fast)");
            });
        }
        if (btnPollNormal != null) {
            btnPollNormal.setOnClickListener(v -> {
                pollIntervalMs = 2000;
                updatePollButtons.run();
                startPolling();
                AppLogger.i("Settings", "Poll interval set to 2s (Normal)");
            });
        }
        if (btnPollSaver != null) {
            btnPollSaver.setOnClickListener(v -> {
                pollIntervalMs = 5000;
                updatePollButtons.run();
                startPolling();
                AppLogger.i("Settings", "Poll interval set to 5s (Saver)");
            });
        }

        if (chkAutoReconnect != null) {
            chkAutoReconnect.setChecked(autoReconnect);
            chkAutoReconnect.setOnCheckedChangeListener((btn, isChecked) -> {
                autoReconnect = isChecked;
                AppLogger.i("Settings", "Auto-reconnect " + (isChecked ? "enabled" : "disabled"));
            });
        }

        // Initialize Notification Dropdown & Checkboxes inside dialog
        updateNotifyAccountSpinner();
        setupNotificationListeners();
        if (btnTestNotification != null) {
            btnTestNotification.setOnClickListener(v -> triggerTestNotification());
        }

        if (btnViewAppLogs != null) {
            btnViewAppLogs.setOnClickListener(v -> showAppLogsDialog());
        }

        if (isStreamerMode) {
            applyStreamerBlur(tvSettingsStatus, true);
            applyStreamerBlur(dlgEditHost, true);
            applyStreamerBlur(spinnerNotifyAccounts, true);
        }

        setupEnterKeyTrigger(dlgEditHost, dlgBtnConnect);
        setupEnterKeyTrigger(dlgEditPort, dlgBtnConnect);

        dlgBtnConnect.setOnClickListener(v -> {
            String newHost = dlgEditHost.getText().toString().trim();
            String newPortStr = dlgEditPort.getText().toString().trim();
            editHost.setText(newHost);
            editPort.setText(newPortStr);
            currentHost = newHost;
            try { currentPort = Integer.parseInt(newPortStr); } catch (Exception ignored) { }
            prefs.edit().putString(KEY_HOST, currentHost).putInt(KEY_PORT, currentPort).apply();
            AppLogger.i("Settings", "Saved host settings: " + currentHost + ":" + currentPort);
            connectToServer(true);
            dialog.dismiss();
        });

        dlgBtnScanLan.setOnClickListener(v -> {
            dialog.dismiss();
            performLanScan();
        });

        if (btnSettingsClose != null) {
            btnSettingsClose.setOnClickListener(v -> dialog.dismiss());
        }

        dialog.show();
    }

    private void showAppLogsDialog() {
        AlertDialog.Builder builder = new AlertDialog.Builder(this);
        View view = getLayoutInflater().inflate(R.layout.dialog_app_logs, null);
        builder.setView(view);
        AlertDialog dialog = builder.create();
        if (dialog.getWindow() != null) {
            dialog.getWindow().setBackgroundDrawableResource(android.R.color.transparent);
        }

        TextView tvLogsContent = view.findViewById(R.id.tvLogsContent);
        ScrollView scrollLogs = view.findViewById(R.id.scrollLogs);
        Button btnLogsClose = view.findViewById(R.id.btnLogsClose);
        Button btnFilterAllLogs = view.findViewById(R.id.btnFilterAllLogs);
        Button btnFilterErrorLogs = view.findViewById(R.id.btnFilterErrorLogs);
        Button btnCopyLogs = view.findViewById(R.id.btnCopyLogs);
        Button btnClearLogs = view.findViewById(R.id.btnClearLogs);

        final boolean[] errorsOnly = {false};

        Runnable updateLogDisplay = () -> {
            StringBuilder html = new StringBuilder();
            List<AppLogger.LogEntry> list = AppLogger.getEntries();
            int count = 0;
            for (AppLogger.LogEntry e : list) {
                if (errorsOnly[0] && !e.isError() && !e.isWarn()) continue;
                count++;
                String levelColor = e.isError() ? "#EF4444" : (e.isWarn() ? "#F59E0B" : "#38BDF8");
                String msgColor = e.isError() ? "#FCA5A5" : (e.isWarn() ? "#FDE68A" : "#E2E8F0");
                html.append("<font color='#64748B'>[").append(e.getFormattedTime()).append("]</font> ")
                    .append("<b><font color='").append(levelColor).append("'>[").append(e.level).append("]</font></b> ")
                    .append("<font color='#818CF8'>[").append(e.tag).append("]</font> ")
                    .append("<font color='").append(msgColor).append("'>")
                    .append(escapeHtml(e.message))
                    .append("</font><br/>");
            }
            if (count == 0) {
                tvLogsContent.setText(errorsOnly[0] ? "No errors recorded." : "No logs recorded yet.");
                tvLogsContent.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
            } else {
                if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.N) {
                    tvLogsContent.setText(android.text.Html.fromHtml(html.toString(), android.text.Html.FROM_HTML_MODE_LEGACY));
                } else {
                    tvLogsContent.setText(android.text.Html.fromHtml(html.toString()));
                }
            }
            scrollLogs.post(() -> scrollLogs.fullScroll(View.FOCUS_DOWN));
        };

        updateLogDisplay.run();

        if (btnFilterAllLogs != null) {
            btnFilterAllLogs.setOnClickListener(v -> {
                errorsOnly[0] = false;
                btnFilterAllLogs.setBackgroundResource(R.drawable.bg_account_pill);
                btnFilterAllLogs.setTextColor(ContextCompat.getColor(this, R.color.white));
                if (btnFilterErrorLogs != null) {
                    btnFilterErrorLogs.setBackgroundResource(R.drawable.bg_box);
                    btnFilterErrorLogs.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
                }
                updateLogDisplay.run();
            });
        }

        if (btnFilterErrorLogs != null) {
            btnFilterErrorLogs.setOnClickListener(v -> {
                errorsOnly[0] = true;
                btnFilterErrorLogs.setBackgroundResource(R.drawable.bg_account_pill);
                btnFilterErrorLogs.setTextColor(ContextCompat.getColor(this, R.color.white));
                if (btnFilterAllLogs != null) {
                    btnFilterAllLogs.setBackgroundResource(R.drawable.bg_box);
                    btnFilterAllLogs.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
                }
                updateLogDisplay.run();
            });
        }

        if (btnCopyLogs != null) {
            btnCopyLogs.setOnClickListener(v -> {
                ClipboardManager clipboard = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
                ClipData clip = ClipData.newPlainText("VibeSkua Logs", tvLogsContent.getText().toString());
                clipboard.setPrimaryClip(clip);
                Toast.makeText(this, "Logs copied to clipboard", Toast.LENGTH_SHORT).show();
            });
        }

        if (btnClearLogs != null) {
            btnClearLogs.setOnClickListener(v -> {
                AppLogger.clear();
                updateLogDisplay.run();
            });
        }

        if (btnLogsClose != null) {
            btnLogsClose.setOnClickListener(v -> dialog.dismiss());
        }

        dialog.show();
    }

    private void focusSelectedTabPc() {
        if (currentSelectedPid <= 0) return;
        try {
            JSONObject body = new JSONObject();
            body.put("pid", currentSelectedPid);
            sendPost("/api/tabs/focus-pc", body.toString(), "Focusing window on PC");
        } catch (Exception ignored) { }
    }

    private void fetchTabs() {
        ApiClient.getAsync(getBaseUrl() + "/api/tabs", new ApiClient.Callback<String>() {
            @Override
            public void onSuccess(String result) {
                try {
                    JSONObject obj = new JSONObject(result);
                    JSONArray arr = obj.optJSONArray("tabs");
                    runningTabs.clear();
                    if (arr != null) {
                        for (int i = 0; i < arr.length(); i++) {
                            JSONObject t = arr.optJSONObject(i);
                            if (t != null) {
                                Models.TabAccount tabAcc = Models.TabAccount.fromJson(t, i + 1);
                                runningTabs.add(tabAcc);
                                checkTabNotifications(tabAcc);
                            }
                        }
                    }

                    boolean currentPidStillValid = false;
                    for (Models.TabAccount tab : runningTabs) {
                        if (tab.pid == currentSelectedPid) {
                            currentPidStillValid = true;
                            break;
                        }
                    }
                    if (!currentPidStillValid) {
                        currentSelectedPid = runningTabs.isEmpty() ? 0 : runningTabs.get(0).pid;
                    }

                    updateAccountPill();
                    updateAccountChips();
                    updateFleetDeck();
                    setupNotifyAccountSpinner();
                } catch (Exception ignored) { }
            }

            @Override
            public void onError(Exception error) { }
        });
    }

    private void updateAccountChips() {
        if (layoutAccountChips == null) return;
        layoutAccountChips.removeAllViews();

        if (runningTabs.isEmpty()) {
            if (tvNoTabsWarning != null) tvNoTabsWarning.setVisibility(View.VISIBLE);
            if (btnFocusPc != null) btnFocusPc.setVisibility(View.GONE);
            return;
        }

        if (tvNoTabsWarning != null) tvNoTabsWarning.setVisibility(View.GONE);
        if (btnFocusPc != null) btnFocusPc.setVisibility(View.VISIBLE);

        float density = getResources().getDisplayMetrics().density;
        for (Models.TabAccount tab : runningTabs) {
            Button chip = new Button(this);
            chip.setAllCaps(false);
            chip.setTextSize(12);
            int padH = (int) (12 * density);
            int padV = (int) (4 * density);
            chip.setPadding(padH, padV, padH, padV);

            boolean isCurrent = (tab.pid == currentSelectedPid);
            chip.setBackgroundResource(isCurrent ? R.drawable.bg_account_pill : R.drawable.bg_box);
            chip.setTextColor(ContextCompat.getColor(this, isCurrent ? R.color.white : R.color.text_muted));

            String statusDot = tab.scriptRunning ? "● " : "○ ";
            chip.setText(statusDot + tab.username);
            applyStreamerBlur(chip, isStreamerMode);

            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.WRAP_CONTENT, (int) (32 * density));
            lp.setMargins(0, 0, (int) (6 * density), 0);
            chip.setLayoutParams(lp);

            chip.setOnClickListener(v -> {
                currentSelectedPid = tab.pid;
                updateAccountPill();
                updateAccountChips();
                updateFleetDeck();
                pollStatus();
                Toast.makeText(this, "Controlling " + tab.username, Toast.LENGTH_SHORT).show();
            });

            chip.setOnLongClickListener(v -> {
                try {
                    JSONObject body = new JSONObject();
                    body.put("pid", tab.pid);
                    sendPost("/api/tabs/focus-pc", body.toString(), "Focused " + tab.username + " on PC");
                } catch (Exception ignored) { }
                return true;
            });

            layoutAccountChips.addView(chip);
        }
    }

    private void fetchSavedAccounts() {
        ApiClient.getAsync(getBaseUrl() + "/api/accounts", new ApiClient.Callback<String>() {
            @Override
            public void onSuccess(String result) {
                parseSavedAccounts(result);
            }

            @Override
            public void onError(Exception error) {
                ApiClient.getAsync(getBaseUrl() + "/api/accounts/saved", new ApiClient.Callback<String>() {
                    @Override
                    public void onSuccess(String result) {
                        parseSavedAccounts(result);
                    }

                    @Override
                    public void onError(Exception e) { }
                });
            }
        });
    }

    private void parseSavedAccounts(String result) {
        try {
            JSONObject obj = new JSONObject(result);
            savedAccounts.clear();
            JSONArray accArr = obj.optJSONArray("accounts");
            if (accArr != null) {
                for (int i = 0; i < accArr.length(); i++) {
                    JSONObject a = accArr.optJSONObject(i);
                    if (a != null) {
                        savedAccounts.add(Models.ManagedAccount.fromJson(a));
                    }
                }
            }

            savedGroups.clear();
            JSONArray grpArr = obj.optJSONArray("groups");
            if (grpArr != null) {
                for (int i = 0; i < grpArr.length(); i++) {
                    JSONObject g = grpArr.optJSONObject(i);
                    if (g != null) {
                        savedGroups.add(Models.ManagedGroup.fromJson(g));
                    }
                }
            }

            updateSpawnerUi();
            setupNotifyAccountSpinner();
        } catch (Exception ignored) { }
    }

    private void showQuickLaunchDialog() {
        List<String> displayItems = new ArrayList<>();
        List<Runnable> actions = new ArrayList<>();

        for (Models.ManagedGroup g : savedGroups) {
            displayItems.add("Group: " + g.name + " (" + g.accounts.size() + " accounts)");
            actions.add(() -> {
                try {
                    JSONObject body = new JSONObject();
                    body.put("group", g.name);
                    sendPost("/api/accounts/launch", body.toString(), "Launching group " + g.name);
                    new Handler(Looper.getMainLooper()).postDelayed(MainActivity.this::fetchTabs, 2000);
                } catch (Exception ignored) { }
            });
        }

        for (Models.ManagedAccount a : savedAccounts) {
            displayItems.add("Account: " + a.displayName);
            actions.add(() -> {
                try {
                    JSONObject body = new JSONObject();
                    body.put("username", a.username);
                    body.put("account", a.username);
                    sendPost("/api/accounts/launch", body.toString(), "Launching account " + a.displayName);
                    new Handler(Looper.getMainLooper()).postDelayed(MainActivity.this::fetchTabs, 2000);
                } catch (Exception ignored) { }
            });
        }

        if (displayItems.isEmpty()) {
            Toast.makeText(this, "No saved accounts or groups found in Skua.Manager", Toast.LENGTH_SHORT).show();
            return;
        }

        new AlertDialog.Builder(this)
                .setTitle("Launch Account or Group")
                .setItems(displayItems.toArray(new String[0]), (dialog, which) -> {
                    if (which >= 0 && which < actions.size()) {
                        actions.get(which).run();
                    }
                })
                .setNegativeButton("Cancel", null)
                .show();
    }

    private void updateSpawnerUi() {
        float density = getResources().getDisplayMetrics().density;

        if (layoutSavedGroups != null) {
            layoutSavedGroups.removeAllViews();
            if (savedGroups.isEmpty()) {
                if (tvNoSavedGroups != null) tvNoSavedGroups.setVisibility(View.VISIBLE);
            } else {
                if (tvNoSavedGroups != null) tvNoSavedGroups.setVisibility(View.GONE);
                for (Models.ManagedGroup g : savedGroups) {
                    LinearLayout card = new LinearLayout(this);
                    card.setOrientation(LinearLayout.VERTICAL);
                    card.setBackgroundResource(R.drawable.bg_card);
                    int pad = (int) (12 * density);
                    card.setPadding(pad, pad, pad, pad);
                    LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                            LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
                    lp.setMargins(0, 0, 0, (int) (8 * density));
                    card.setLayoutParams(lp);

                    LinearLayout topRow = new LinearLayout(this);
                    topRow.setOrientation(LinearLayout.HORIZONTAL);

                    TextView tvTitle = new TextView(this);
                    tvTitle.setText(g.name);
                    tvTitle.setTextSize(14);
                    tvTitle.setTextColor(ContextCompat.getColor(this, R.color.text_main));
                    tvTitle.setTypeface(null, android.graphics.Typeface.BOLD);
                    topRow.addView(tvTitle, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1.0f));

                    Button btnLaunch = new Button(this);
                    btnLaunch.setText("LAUNCH (" + g.accounts.size() + ")");
                    btnLaunch.setTextSize(11);
                    btnLaunch.setBackgroundResource(R.drawable.btn_primary);
                    btnLaunch.setTextColor(ContextCompat.getColor(this, R.color.white));
                    btnLaunch.setPadding((int)(10 * density), (int)(4 * density), (int)(10 * density), (int)(4 * density));
                    btnLaunch.setLayoutParams(new LinearLayout.LayoutParams(
                            LinearLayout.LayoutParams.WRAP_CONTENT, (int)(34 * density)));
                    btnLaunch.setOnClickListener(v -> {
                        try {
                            JSONObject body = new JSONObject();
                            body.put("group", g.name);
                            sendPost("/api/accounts/launch", body.toString(), "Launching group " + g.name);
                            new Handler(Looper.getMainLooper()).postDelayed(MainActivity.this::fetchTabs, 2000);
                        } catch (Exception ignored) { }
                    });
                    topRow.addView(btnLaunch);
                    card.addView(topRow);

                    TextView tvMembers = new TextView(this);
                    StringBuilder mb = new StringBuilder("Members: ");
                    for (int i = 0; i < g.accounts.size(); i++) {
                        if (i > 0) mb.append(", ");
                        mb.append(g.accounts.get(i));
                    }
                    tvMembers.setText(mb.toString());
                    tvMembers.setTextSize(11);
                    tvMembers.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
                    applyStreamerBlur(tvMembers, isStreamerMode);
                    card.addView(tvMembers);

                    layoutSavedGroups.addView(card);
                }
            }
        }

        if (layoutSavedAccounts != null) {
            layoutSavedAccounts.removeAllViews();
            if (savedAccounts.isEmpty()) {
                if (tvNoSavedAccounts != null) tvNoSavedAccounts.setVisibility(View.VISIBLE);
            } else {
                if (tvNoSavedAccounts != null) tvNoSavedAccounts.setVisibility(View.GONE);
                for (Models.ManagedAccount a : savedAccounts) {
                    LinearLayout row = new LinearLayout(this);
                    row.setOrientation(LinearLayout.HORIZONTAL);
                    row.setBackgroundResource(R.drawable.bg_box);
                    row.setGravity(android.view.Gravity.CENTER_VERTICAL);
                    int pad = (int) (12 * density);
                    row.setPadding(pad, pad, pad, pad);
                    LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                            LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
                    lp.setMargins(0, 0, 0, (int) (8 * density));
                    row.setLayoutParams(lp);

                    LinearLayout leftCol = new LinearLayout(this);
                    leftCol.setOrientation(LinearLayout.VERTICAL);
                    leftCol.setLayoutParams(new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1.0f));

                    TextView tvAcc = new TextView(this);
                    tvAcc.setText(a.displayName);
                    tvAcc.setTextSize(13);
                    tvAcc.setTextColor(ContextCompat.getColor(this, R.color.text_main));
                    tvAcc.setTypeface(null, android.graphics.Typeface.BOLD);
                    applyStreamerBlur(tvAcc, isStreamerMode);
                    leftCol.addView(tvAcc);

                    if (a.tags != null && !a.tags.isEmpty()) {
                        LinearLayout tagsLayout = new LinearLayout(this);
                        tagsLayout.setOrientation(LinearLayout.HORIZONTAL);
                        tagsLayout.setPadding(0, (int) (4 * density), 0, 0);

                        for (String tag : a.tags) {
                            if (tag == null || tag.trim().isEmpty()) continue;
                            TextView tvTag = new TextView(this);
                            tvTag.setText(tag.trim().toUpperCase());
                            tvTag.setTextSize(9);
                            tvTag.setTextColor(Color.parseColor("#38BDF8"));
                            tvTag.setBackgroundResource(R.drawable.bg_tag);
                            int tagPadH = (int) (6 * density);
                            int tagPadV = (int) (2 * density);
                            tvTag.setPadding(tagPadH, tagPadV, tagPadH, tagPadV);
                            LinearLayout.LayoutParams tagLp = new LinearLayout.LayoutParams(
                                    LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT);
                            tagLp.setMargins(0, 0, (int) (4 * density), 0);
                            tvTag.setLayoutParams(tagLp);
                            tagsLayout.addView(tvTag);
                        }
                        leftCol.addView(tagsLayout);
                    }
                    row.addView(leftCol);

                    Button btnLaunch = new Button(this);
                    btnLaunch.setText("LAUNCH");
                    btnLaunch.setTextSize(11);
                    btnLaunch.setBackgroundResource(R.drawable.btn_primary);
                    btnLaunch.setTextColor(ContextCompat.getColor(this, R.color.white));
                    btnLaunch.setPadding((int)(12 * density), (int)(4 * density), (int)(12 * density), (int)(4 * density));
                    btnLaunch.setLayoutParams(new LinearLayout.LayoutParams(
                            LinearLayout.LayoutParams.WRAP_CONTENT, (int)(32 * density)));
                    btnLaunch.setOnClickListener(v -> {
                        try {
                            JSONObject body = new JSONObject();
                            body.put("username", a.username);
                            body.put("account", a.username);
                            sendPost("/api/accounts/launch", body.toString(), "Launching " + a.displayName);
                            new Handler(Looper.getMainLooper()).postDelayed(MainActivity.this::fetchTabs, 2000);
                        } catch (Exception ignored) { }
                    });
                    row.addView(btnLaunch);

                    layoutSavedAccounts.addView(row);
                }
            }
        }
    }

    private void updateFleetDeck() {
        if (tvFleetSummaryCount != null) {
            int runningScripts = 0;
            for (Models.TabAccount tab : runningTabs) {
                if (tab.scriptRunning) runningScripts++;
            }
            tvFleetSummaryCount.setText(runningTabs.size() + " Running Accounts | " + runningScripts + " Active Scripts");
        }
        if (tvRosterCountBadge != null) {
            tvRosterCountBadge.setText(runningTabs.size() + " ONLINE");
        }

        if (layoutFleetRoster == null) return;
        layoutFleetRoster.removeAllViews();

        if (runningTabs.isEmpty()) {
            if (tvEmptyFleet != null) tvEmptyFleet.setVisibility(View.VISIBLE);
            return;
        }

        if (tvEmptyFleet != null) tvEmptyFleet.setVisibility(View.GONE);
        float density = getResources().getDisplayMetrics().density;

        for (Models.TabAccount tab : runningTabs) {
            LinearLayout card = new LinearLayout(this);
            card.setOrientation(LinearLayout.VERTICAL);
            card.setBackgroundResource(tab.pid == currentSelectedPid ? R.drawable.bg_card : R.drawable.bg_box);
            int pad = (int) (12 * density);
            card.setPadding(pad, pad, pad, pad);
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
            lp.setMargins(0, 0, 0, (int) (8 * density));
            card.setLayoutParams(lp);

            // Header Row: Username + Status pill
            LinearLayout header = new LinearLayout(this);
            header.setOrientation(LinearLayout.HORIZONTAL);

            TextView tvUser = new TextView(this);
            tvUser.setText(tab.username + (tab.pid == currentSelectedPid ? " (ACTIVE)" : ""));
            tvUser.setTextSize(14);
            tvUser.setTextColor(ContextCompat.getColor(this, R.color.text_main));
            tvUser.setTypeface(null, android.graphics.Typeface.BOLD);
            applyStreamerBlur(tvUser, isStreamerMode);
            header.addView(tvUser, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1.0f));

            TextView tvStatus = new TextView(this);
            tvStatus.setText(tab.scriptRunning ? "RUNNING" : "IDLE");
            tvStatus.setTextSize(11);
            tvStatus.setTextColor(ContextCompat.getColor(this, tab.scriptRunning ? R.color.green : R.color.orange));
            tvStatus.setTypeface(null, android.graphics.Typeface.BOLD);
            header.addView(tvStatus);
            card.addView(header);

            // Details: Map / Server / Script
            TextView tvDetails = new TextView(this);
            String mapStr = !tab.map.isEmpty() ? (tab.map + " (" + tab.cell + ")") : "In Lobby / World";
            String scriptStr = tab.scriptRunning ? tab.scriptName : "None";
            tvDetails.setText("MAP: " + mapStr + " | SCRIPT: " + scriptStr);
            tvDetails.setTextSize(11);
            tvDetails.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
            tvDetails.setPadding(0, (int)(3 * density), 0, (int)(3 * density));
            applyStreamerBlur(tvDetails, isStreamerMode);
            card.addView(tvDetails);

            // HP / MP summary
            if (tab.maxHp > 0) {
                TextView tvVitals = new TextView(this);
                tvVitals.setText("HP: " + tab.hp + " / " + tab.maxHp + "   MP: " + tab.mp + " / " + tab.maxMp);
                tvVitals.setTextSize(11);
                tvVitals.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
                card.addView(tvVitals);
            }

            // Actions row: [Control] [Focus PC] [Start/Stop Script] [Close]
            LinearLayout actions = new LinearLayout(this);
            actions.setOrientation(LinearLayout.HORIZONTAL);
            actions.setPadding(0, (int)(6 * density), 0, 0);

            // Control
            Button btnSwitch = new Button(this);
            btnSwitch.setText("CONTROL");
            btnSwitch.setTextSize(11);
            btnSwitch.setBackgroundResource(R.drawable.btn_primary);
            btnSwitch.setTextColor(ContextCompat.getColor(this, R.color.white));
            btnSwitch.setPadding((int)(8 * density), 0, (int)(8 * density), 0);
            LinearLayout.LayoutParams btnLp = new LinearLayout.LayoutParams(0, (int)(32 * density), 1.0f);
            btnLp.setMargins(0, 0, (int)(4 * density), 0);
            btnSwitch.setLayoutParams(btnLp);
            btnSwitch.setOnClickListener(v -> {
                currentSelectedPid = tab.pid;
                updateAccountChips();
                switchTab(0);
                Toast.makeText(this, "Controlling " + tab.username, Toast.LENGTH_SHORT).show();
            });
            actions.addView(btnSwitch);

            // Focus PC
            Button btnFoc = new Button(this);
            btnFoc.setText("FOCUS PC");
            btnFoc.setTextSize(11);
            btnFoc.setBackgroundResource(R.drawable.bg_box);
            btnFoc.setTextColor(ContextCompat.getColor(this, R.color.text_main));
            btnFoc.setPadding((int)(8 * density), 0, (int)(8 * density), 0);
            LinearLayout.LayoutParams focLp = new LinearLayout.LayoutParams(0, (int)(32 * density), 1.0f);
            focLp.setMargins(0, 0, (int)(4 * density), 0);
            btnFoc.setLayoutParams(focLp);
            btnFoc.setOnClickListener(v -> {
                try {
                    JSONObject body = new JSONObject();
                    body.put("pid", tab.pid);
                    sendPost("/api/tabs/focus-pc", body.toString(), "Focused " + tab.username + " on PC");
                } catch (Exception ignored) { }
            });
            actions.addView(btnFoc);

            // Start/Stop Script
            Button btnToggle = new Button(this);
            btnToggle.setText(tab.scriptRunning ? "STOP" : "START");
            btnToggle.setTextSize(11);
            btnToggle.setBackgroundResource(tab.scriptRunning ? R.drawable.btn_red : R.drawable.bg_box);
            btnToggle.setTextColor(ContextCompat.getColor(this, tab.scriptRunning ? R.color.white : R.color.text_main));
            btnToggle.setPadding((int)(8 * density), 0, (int)(8 * density), 0);
            LinearLayout.LayoutParams togLp = new LinearLayout.LayoutParams(0, (int)(32 * density), 1.0f);
            togLp.setMargins(0, 0, (int)(4 * density), 0);
            btnToggle.setLayoutParams(togLp);
            btnToggle.setOnClickListener(v -> {
                Map<String, String> targetHeader = new HashMap<>();
                targetHeader.put("X-Target-PID", String.valueOf(tab.pid));
                String ep = tab.scriptRunning ? "/api/scripts/stop" : "/api/scripts/start";
                ApiClient.postAsync(getBaseUrl() + ep, "{}", targetHeader, 15000, new ApiClient.Callback<String>() {
                    @Override
                    public void onSuccess(String result) {
                        Toast.makeText(MainActivity.this, (tab.scriptRunning ? "Stopped" : "Started") + " script for " + tab.username, Toast.LENGTH_SHORT).show();
                        fetchTabs();
                    }

                    @Override
                    public void onError(Exception error) {
                        Toast.makeText(MainActivity.this, "Action failed: " + error.getMessage(), Toast.LENGTH_SHORT).show();
                    }
                });
            });
            actions.addView(btnToggle);

            // Close Tab
            Button btnClose = new Button(this);
            btnClose.setText("CLOSE");
            btnClose.setTextSize(11);
            btnClose.setBackgroundResource(R.drawable.bg_box);
            btnClose.setTextColor(ContextCompat.getColor(this, R.color.red));
            btnClose.setPadding((int)(8 * density), 0, (int)(8 * density), 0);
            LinearLayout.LayoutParams closeLp = new LinearLayout.LayoutParams(0, (int)(32 * density), 1.0f);
            btnClose.setLayoutParams(closeLp);
            btnClose.setOnClickListener(v -> {
                new AlertDialog.Builder(this)
                        .setTitle("Close Tab")
                        .setMessage("Are you sure you want to close " + tab.username + " (PID " + tab.pid + ")?")
                        .setPositiveButton("Close", (dialog, which) -> {
                            try {
                                JSONObject body = new JSONObject();
                                body.put("pid", tab.pid);
                                sendPost("/api/tabs/close", body.toString(), "Closing tab " + tab.username);
                                new Handler(Looper.getMainLooper()).postDelayed(MainActivity.this::fetchTabs, 1500);
                            } catch (Exception ignored) { }
                        })
                        .setNegativeButton("Cancel", null)
                        .show();
            });
            actions.addView(btnClose);

            card.addView(actions);
            layoutFleetRoster.addView(card);
        }
    }

    private void toggleFleetHeadless() {
        isFleetHeadlessEnabled = !isFleetHeadlessEnabled;
        try {
            JSONObject body = new JSONObject();
            body.put("value", isFleetHeadlessEnabled);
            body.put("enabled", isFleetHeadlessEnabled);
            sendPost("/api/fleet/headless", body.toString(), "Headless (All) " + (isFleetHeadlessEnabled ? "enabled" : "disabled"));
            btnFleetHeadlessAll.setText(isFleetHeadlessEnabled ? "HEADLESS (ON)" : "HEADLESS (ALL)");
        } catch (Exception ignored) { }
    }

    // ARMY SCRIPT SCHEDULER
    private void startArmyScheduler() {
        if (schedulerQueue.isEmpty()) {
            Toast.makeText(this, "Queue is empty. Tap '+ QUEUE SCRIPT' first.", Toast.LENGTH_SHORT).show();
            return;
        }

        JSONArray arr = new JSONArray();
        for (Models.ScriptItem s : schedulerQueue) {
            arr.put(s.path != null && !s.path.isEmpty() ? s.path : s.fileName);
        }

        isArmySchedulerRunning = true;
        sendPost("/api/fleet/scheduler/start", arr.toString(), "Started Army Script Scheduler");
        updateSchedulerQueueUi();
    }

    private void stopArmyScheduler() {
        isArmySchedulerRunning = false;
        sendPost("/api/fleet/scheduler/stop", "{}", "Stopped Army Script Scheduler");
        updateSchedulerQueueUi();
    }

    private void clearArmySchedulerQueue() {
        schedulerQueue.clear();
        updateSchedulerQueueUi();
        Toast.makeText(this, "Scheduler queue cleared", Toast.LENGTH_SHORT).show();
    }

    private void updateSchedulerQueueUi() {
        if (tvSchedulerStatus != null) {
            String status = isArmySchedulerRunning ? "RUNNING" : "IDLE";
            tvSchedulerStatus.setText(status + " (" + schedulerQueue.size() + " in queue)");
            tvSchedulerStatus.setTextColor(ContextCompat.getColor(this, isArmySchedulerRunning ? R.color.green : R.color.text_muted));
        }

        if (layoutSchedulerQueue == null) return;
        layoutSchedulerQueue.removeAllViews();

        if (schedulerQueue.isEmpty()) {
            if (tvEmptySchedulerQueue != null) tvEmptySchedulerQueue.setVisibility(View.VISIBLE);
            return;
        }

        if (tvEmptySchedulerQueue != null) tvEmptySchedulerQueue.setVisibility(View.GONE);
        float density = getResources().getDisplayMetrics().density;

        for (int i = 0; i < schedulerQueue.size(); i++) {
            final int index = i;
            Models.ScriptItem s = schedulerQueue.get(i);

            LinearLayout row = new LinearLayout(this);
            row.setOrientation(LinearLayout.HORIZONTAL);
            row.setBackgroundResource(R.drawable.bg_box);
            row.setGravity(android.view.Gravity.CENTER_VERTICAL);
            int pad = (int) (8 * density);
            row.setPadding(pad, pad, pad, pad);
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
            lp.setMargins(0, 0, 0, (int) (4 * density));
            row.setLayoutParams(lp);

            LinearLayout textCol = new LinearLayout(this);
            textCol.setOrientation(LinearLayout.VERTICAL);

            TextView tvTitle = new TextView(this);
            tvTitle.setText((index + 1) + ". " + (s.name != null && !s.name.isEmpty() ? s.name : s.fileName));
            tvTitle.setTextSize(12);
            tvTitle.setTextColor(ContextCompat.getColor(this, R.color.text_main));
            tvTitle.setTypeface(null, android.graphics.Typeface.BOLD);
            textCol.addView(tvTitle);

            TextView tvSub = new TextView(this);
            tvSub.setText((s.category != null ? s.category : "Script") + " | " + (s.path != null ? s.path : ""));
            tvSub.setTextSize(10);
            tvSub.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
            textCol.addView(tvSub);

            row.addView(textCol, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1.0f));

            Button btnRemove = new Button(this);
            btnRemove.setText("X");
            btnRemove.setTextSize(11);
            btnRemove.setBackgroundResource(R.drawable.bg_card);
            btnRemove.setTextColor(ContextCompat.getColor(this, R.color.red));
            LinearLayout.LayoutParams remLp = new LinearLayout.LayoutParams((int)(32 * density), (int)(32 * density));
            btnRemove.setLayoutParams(remLp);
            btnRemove.setOnClickListener(v -> {
                if (index < schedulerQueue.size()) {
                    schedulerQueue.remove(index);
                    updateSchedulerQueueUi();
                }
            });
            row.addView(btnRemove);

            layoutSchedulerQueue.addView(row);
        }
    }

    // ARMY QUEUE (MANAGER SQUADS)
    private void pollArmyQueue() {
        ApiClient.getAsync(getBaseUrl() + "/api/army/queue", new ApiClient.Callback<String>() {
            @Override
            public void onSuccess(String result) {
                try {
                    JSONObject obj = new JSONObject(result);
                    isArmyQueueRunning = obj.optBoolean("is_running", false);
                    isArmyQueuePaused = obj.optBoolean("is_paused", false);
                    String statusText = obj.optString("status", "Idle");
                    JSONArray stepsArr = obj.optJSONArray("steps");

                    if (tvArmyQueueBadge != null) {
                        if (isArmyQueueRunning) {
                            tvArmyQueueBadge.setText(isArmyQueuePaused ? "PAUSED" : "RUNNING");
                            tvArmyQueueBadge.setTextColor(ContextCompat.getColor(MainActivity.this, isArmyQueuePaused ? R.color.orange : R.color.green));
                        } else {
                            tvArmyQueueBadge.setText("IDLE");
                            tvArmyQueueBadge.setTextColor(ContextCompat.getColor(MainActivity.this, R.color.text_muted));
                        }
                    }

                    if (tvArmyQueueStatus != null) {
                        tvArmyQueueStatus.setText(statusText);
                    }

                    if (btnArmyQueuePause != null) {
                        btnArmyQueuePause.setText(isArmyQueuePaused ? "RESUME" : "PAUSE");
                    }

                    renderArmyQueueSteps(stepsArr);
                } catch (Exception ignored) { }
            }

            @Override
            public void onError(Exception error) { }
        });
    }

    private void renderArmyQueueSteps(JSONArray stepsArr) {
        if (layoutArmyQueueSquads == null) return;
        layoutArmyQueueSquads.removeAllViews();

        if (stepsArr == null || stepsArr.length() == 0) {
            if (tvEmptyArmyQueue != null) tvEmptyArmyQueue.setVisibility(View.VISIBLE);
            return;
        }

        if (tvEmptyArmyQueue != null) tvEmptyArmyQueue.setVisibility(View.GONE);
        float density = getResources().getDisplayMetrics().density;

        for (int i = 0; i < stepsArr.length(); i++) {
            final int stepIndex = i;
            JSONObject sObj = stepsArr.optJSONObject(i);
            if (sObj == null) continue;

            String sName = sObj.optString("name", "Step " + (stepIndex + 1));
            String sScript = sObj.optString("script", "");
            String sStatus = sObj.optString("status", "Ready");
            JSONArray membersArr = sObj.optJSONArray("members");

            LinearLayout card = new LinearLayout(this);
            card.setOrientation(LinearLayout.VERTICAL);
            card.setBackgroundResource(R.drawable.bg_box);
            int pad = (int) (10 * density);
            card.setPadding(pad, pad, pad, pad);
            LinearLayout.LayoutParams cardLp = new LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
            cardLp.setMargins(0, 0, 0, (int) (6 * density));
            card.setLayoutParams(cardLp);

            // Step Header
            LinearLayout header = new LinearLayout(this);
            header.setOrientation(LinearLayout.HORIZONTAL);

            TextView tvHeader = new TextView(this);
            tvHeader.setText("STEP " + (stepIndex + 1) + ": " + sName);
            tvHeader.setTextSize(13);
            tvHeader.setTextColor(ContextCompat.getColor(this, R.color.text_main));
            tvHeader.setTypeface(null, android.graphics.Typeface.BOLD);
            header.addView(tvHeader, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1.0f));

            TextView tvStepStatus = new TextView(this);
            tvStepStatus.setText("[" + sStatus.toUpperCase() + "]");
            tvStepStatus.setTextSize(11);
            tvStepStatus.setTextColor(ContextCompat.getColor(this, R.color.accent));
            tvStepStatus.setTypeface(null, android.graphics.Typeface.BOLD);
            header.addView(tvStepStatus);
            card.addView(header);

            // Script Assigned Row
            LinearLayout scriptRow = new LinearLayout(this);
            scriptRow.setOrientation(LinearLayout.HORIZONTAL);
            scriptRow.setGravity(android.view.Gravity.CENTER_VERTICAL);
            scriptRow.setPadding(0, (int)(4 * density), 0, (int)(4 * density));

            TextView tvAssigned = new TextView(this);
            tvAssigned.setText("Script: " + (!sScript.isEmpty() ? sScript : "No script assigned"));
            tvAssigned.setTextSize(11);
            tvAssigned.setTextColor(ContextCompat.getColor(this, !sScript.isEmpty() ? R.color.white : R.color.text_muted));
            scriptRow.addView(tvAssigned, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1.0f));

            Button btnAssign = new Button(this);
            btnAssign.setText("ASSIGN SCRIPT");
            btnAssign.setTextSize(10);
            btnAssign.setBackgroundResource(R.drawable.bg_account_pill);
            btnAssign.setTextColor(ContextCompat.getColor(this, R.color.white));
            btnAssign.setPadding((int)(8 * density), 0, (int)(8 * density), 0);
            LinearLayout.LayoutParams assignLp = new LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.WRAP_CONTENT, (int)(28 * density));
            btnAssign.setLayoutParams(assignLp);
            btnAssign.setOnClickListener(v -> showScriptPickerDialog(SCRIPT_PICKER_MODE_ARMY_QUEUE_STEP, stepIndex));
            scriptRow.addView(btnAssign);

            card.addView(scriptRow);

            // Squad Members
            if (membersArr != null && membersArr.length() > 0) {
                StringBuilder mb = new StringBuilder("Squad Members (").append(membersArr.length()).append("): ");
                for (int m = 0; m < membersArr.length(); m++) {
                    JSONObject mem = membersArr.optJSONObject(m);
                    if (mem != null) {
                        if (m > 0) mb.append(", ");
                        String uName = mem.optString("display_name", mem.optString("username", "Member"));
                        String mStat = mem.optString("status", "");
                        mb.append(uName);
                        if (!mStat.isEmpty()) mb.append(" [").append(mStat).append("]");
                    }
                }
                TextView tvMems = new TextView(this);
                tvMems.setText(mb.toString());
                tvMems.setTextSize(10);
                tvMems.setTextColor(ContextCompat.getColor(this, R.color.text_muted));
                applyStreamerBlur(tvMems, isStreamerMode);
                card.addView(tvMems);
            }

            layoutArmyQueueSquads.addView(card);
        }
    }

    private void startArmyQueue() {
        sendPost("/api/army/queue/start", "{}", "Started Army Squad Queue");
        new Handler(Looper.getMainLooper()).postDelayed(this::pollArmyQueue, 800);
    }

    private void pauseResumeArmyQueue() {
        sendPost("/api/army/queue/pause", "{}", "Toggled Squad Queue Pause");
        new Handler(Looper.getMainLooper()).postDelayed(this::pollArmyQueue, 800);
    }

    private void stopArmyQueue() {
        sendPost("/api/army/queue/stop", "{}", "Stopped Army Squad Queue");
        new Handler(Looper.getMainLooper()).postDelayed(this::pollArmyQueue, 800);
    }

    // SCRIPT PICKER DIALOG (ALL SCRIPT SEARCH WITH LOAD / QUEUE BUTTON)
    private void showScriptPickerDialog(int mode, int stepIndex) {
        AlertDialog.Builder builder = new AlertDialog.Builder(this);
        View dialogView = getLayoutInflater().inflate(R.layout.dialog_script_picker, null);
        builder.setView(dialogView);
        AlertDialog dialog = builder.create();
        if (dialog.getWindow() != null) {
            dialog.getWindow().setBackgroundDrawableResource(android.R.color.transparent);
        }

        TextView tvTitle = dialogView.findViewById(R.id.tvScriptPickerTitle);
        Button btnClose = dialogView.findViewById(R.id.btnScriptPickerClose);
        EditText editSearch = dialogView.findViewById(R.id.editScriptPickerSearch);
        TextView tvEmpty = dialogView.findViewById(R.id.tvEmptyScriptPicker);
        RecyclerView recycler = dialogView.findViewById(R.id.recyclerScriptPicker);

        Button btnChipAll = dialogView.findViewById(R.id.btnChipAll);
        Button btnChipArmy = dialogView.findViewById(R.id.btnChipArmy);
        Button btnChipBots = dialogView.findViewById(R.id.btnChipBots);
        Button btnChipStory = dialogView.findViewById(R.id.btnChipStory);
        Button btnChipFarm = dialogView.findViewById(R.id.btnChipFarm);
        Button btnChipRep = dialogView.findViewById(R.id.btnChipRep);
        final Button[] chips = new Button[]{btnChipAll, btnChipArmy, btnChipBots, btnChipStory, btnChipFarm, btnChipRep};

        String actionLabel = "QUEUE";
        if (mode == SCRIPT_PICKER_MODE_ARMY_LOAD) {
            tvTitle.setText("LOAD SCRIPT (ALL ARMY TABS)");
            actionLabel = "LOAD";
        } else if (mode == SCRIPT_PICKER_MODE_SCHEDULER_QUEUE) {
            tvTitle.setText("QUEUE SCRIPT TO SCHEDULER");
            actionLabel = "QUEUE";
        } else if (mode == SCRIPT_PICKER_MODE_ARMY_QUEUE_STEP) {
            tvTitle.setText("ASSIGN SCRIPT (STEP #" + (stepIndex + 1) + ")");
            actionLabel = "ASSIGN";
        }

        ScriptPickerAdapter pickerAdapter = new ScriptPickerAdapter(script -> {
            dialog.dismiss();
            handleScriptPicked(script, mode, stepIndex);
        });
        pickerAdapter.setActionLabel(actionLabel);
        pickerAdapter.setScripts(allCachedScripts);

        recycler.setLayoutManager(new LinearLayoutManager(this));
        recycler.setAdapter(pickerAdapter);

        if (allCachedScripts.isEmpty()) {
            tvEmpty.setVisibility(View.VISIBLE);
            tvEmpty.setText("No scripts available. Fetching...");
            fetchScripts();
        } else {
            tvEmpty.setVisibility(View.GONE);
        }

        btnClose.setOnClickListener(v -> dialog.dismiss());

        editSearch.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int start, int count, int after) { }
            @Override public void onTextChanged(CharSequence s, int start, int before, int count) {
                pickerAdapter.filter(s != null ? s.toString() : "");
                tvEmpty.setVisibility(pickerAdapter.getFilteredCount() == 0 ? View.VISIBLE : View.GONE);
            }
            @Override public void afterTextChanged(Editable s) { }
        });

        View.OnClickListener chipListener = v -> {
            for (Button c : chips) {
                c.setBackgroundResource(R.drawable.bg_box);
                c.setTextColor(ContextCompat.getColor(MainActivity.this, R.color.text_muted));
            }
            v.setBackgroundResource(R.drawable.bg_account_pill);
            ((Button) v).setTextColor(ContextCompat.getColor(MainActivity.this, R.color.white));
            String cat = ((Button) v).getText().toString();
            pickerAdapter.setCategory(cat);
            tvEmpty.setVisibility(pickerAdapter.getFilteredCount() == 0 ? View.VISIBLE : View.GONE);
        };

        for (Button c : chips) {
            c.setOnClickListener(chipListener);
        }

        dialog.show();
    }

    private void handleScriptPicked(Models.ScriptItem script, int mode, int stepIndex) {
        if (script == null) return;
        String sPath = script.path != null && !script.path.isEmpty() ? script.path : script.fileName;

        if (mode == SCRIPT_PICKER_MODE_ARMY_LOAD) {
            try {
                JSONObject body = new JSONObject();
                body.put("script", sPath);
                body.put("path", sPath);
                sendPost("/api/fleet/load-script", body.toString(), "Loaded " + script.name + " to all army tabs");
            } catch (Exception ignored) { }
        } else if (mode == SCRIPT_PICKER_MODE_SCHEDULER_QUEUE) {
            schedulerQueue.add(script);
            updateSchedulerQueueUi();
            Toast.makeText(this, "Queued " + script.name + " in scheduler", Toast.LENGTH_SHORT).show();
        } else if (mode == SCRIPT_PICKER_MODE_ARMY_QUEUE_STEP) {
            try {
                JSONObject body = new JSONObject();
                body.put("step_index", stepIndex);
                body.put("script_path", sPath);
                sendPost("/api/army/queue/step/script", body.toString(), "Assigned " + script.name + " to step #" + (stepIndex + 1));
                new Handler(Looper.getMainLooper()).postDelayed(this::pollArmyQueue, 800);
            } catch (Exception ignored) { }
        }
    }

    private void fetchScripts() {
        ApiClient.getAsync(getBaseUrl() + "/api/scripts", new ApiClient.Callback<String>() {
            @Override
            public void onSuccess(String result) {
                try {
                    JSONObject obj = new JSONObject(result);
                    JSONArray arr = obj.optJSONArray("scripts");
                    List<Models.ScriptItem> list = new ArrayList<>();
                    if (arr != null) {
                        for (int i = 0; i < arr.length(); i++) {
                            JSONObject item = arr.getJSONObject(i);
                            Object tagsObj = item.opt("tags");
                            StringBuilder sbTags = new StringBuilder();
                            if (tagsObj instanceof JSONArray) {
                                JSONArray tagsArr = (JSONArray) tagsObj;
                                for (int t = 0; t < tagsArr.length(); t++) {
                                    String tag = tagsArr.optString(t);
                                    if (tag != null && !tag.isEmpty()) {
                                        if (sbTags.length() > 0) sbTags.append("  ");
                                        sbTags.append(tag.startsWith("#") ? tag : "#" + tag);
                                    }
                                }
                            } else if (tagsObj instanceof String) {
                                String tagStr = (String) tagsObj;
                                if (!tagStr.isEmpty()) {
                                    sbTags.append(tagStr.startsWith("#") ? tagStr : "#" + tagStr);
                                }
                            }
                            long lastMod = item.optLong("last_modified", item.optLong("lastModified", 0L));
                            if (lastMod == 0) {
                                String dateStr = item.optString("date", "");
                                if (!dateStr.isEmpty()) {
                                    try {
                                        java.text.SimpleDateFormat sdf = new java.text.SimpleDateFormat("yyyy-MM-dd", java.util.Locale.US);
                                        java.util.Date d = sdf.parse(dateStr);
                                        if (d != null) lastMod = d.getTime();
                                    } catch (Exception ignored) { }
                                }
                            }
                            list.add(new Models.ScriptItem(
                                    item.optString("name"),
                                    item.optString("fileName"),
                                    item.optString("path"),
                                    item.optString("category"),
                                    item.optString("description"),
                                    sbTags.toString(),
                                    item.optBoolean("downloaded", false),
                                    item.optLong("size", 0),
                                    lastMod
                            ));
                        }
                    }
                    allCachedScripts.clear();
                    allCachedScripts.addAll(list);
                    if (scriptsAdapter != null) {
                        scriptsAdapter.setScripts(list);
                    }
                    if (editScriptSearch != null) {
                        editScriptSearch.setHint("Search " + list.size() + " scripts...");
                    }
                    AppLogger.i("Scripts", "Loaded " + list.size() + " scripts from PC.");
                } catch (Exception e) {
                    AppLogger.e("Scripts", "Failed to parse scripts JSON: " + e.getMessage());
                }
            }

            @Override
            public void onError(Exception error) {
                AppLogger.e("Scripts", "Failed to fetch scripts: " + error.getMessage());
            }
        });
    }

    // HIGH IMPORTANCE NOTIFICATIONS & BACKGROUND MONITORING
    private static final String NOTIF_CHANNEL_ID = "vibeskua_alerts";
    private static final String NOTIF_CHANNEL_NAME = "VibeSkua Bot Alerts";

    private void createNotificationChannel() {
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
            NotificationChannel channel = new NotificationChannel(
                    NOTIF_CHANNEL_ID,
                    NOTIF_CHANNEL_NAME,
                    NotificationManager.IMPORTANCE_HIGH
            );
            channel.setDescription("Alerts for player death, script completion, disconnection, and rare drops");
            channel.enableVibration(true);
            channel.setShowBadge(true);
            NotificationManager manager = getSystemService(NotificationManager.class);
            if (manager != null) {
                manager.createNotificationChannel(channel);
            }
        }
    }

    private void showNotification(String title, String message, int notifId) {
        try {
            if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.TIRAMISU) {
                if (ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
                    return;
                }
            }
            NotificationCompat.Builder builder = new NotificationCompat.Builder(this, NOTIF_CHANNEL_ID)
                    .setSmallIcon(R.mipmap.ic_launcher)
                    .setContentTitle(title)
                    .setContentText(message)
                    .setStyle(new NotificationCompat.BigTextStyle().bigText(message))
                    .setPriority(NotificationCompat.PRIORITY_HIGH)
                    .setDefaults(NotificationCompat.DEFAULT_ALL)
                    .setAutoCancel(true);

            NotificationManagerCompat manager = NotificationManagerCompat.from(this);
            manager.notify(notifId, builder.build());
        } catch (Exception ignored) { }
    }

    private void triggerTestNotification() {
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.TIRAMISU) {
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
                ActivityCompat.requestPermissions(this, new String[]{Manifest.permission.POST_NOTIFICATIONS}, 101);
                return;
            }
        }
        showNotification("VibeSkua Alert - Test Notification", "Push notifications are active and functioning properly!", (int) System.currentTimeMillis());
        Toast.makeText(this, "Test notification dispatched", Toast.LENGTH_SHORT).show();
    }

    private void checkAccountNotifications(Models.BotStatus s) {
        if (s == null) return;
        int pid = currentSelectedPid;
        String user = (s.username != null && !s.username.isEmpty() && !s.username.equals("Unknown")) ? s.username : ("PID_" + pid);
        String normUser = user.toLowerCase();

        boolean muted = prefs.getBoolean("notify_mute_" + normUser, false) || prefs.getBoolean("notify_mute_" + user, false);
        if (muted) return;

        // 1. Script stopped
        Boolean prevScript = lastScriptRunning.get(pid);
        if (prevScript != null && prevScript && !s.scriptRunning && s.loggedIn) {
            boolean shouldNotify = prefs.getBoolean("notify_script_stopped_" + normUser, true) && prefs.getBoolean("notify_script_stopped_" + user, true);
            if (shouldNotify) {
                String sName = (s.loadedScript != null && !s.loadedScript.isEmpty()) ? s.loadedScript : "Active Script";
                showNotification("Script Stopped: " + user, sName + " finished or stopped", pid * 10 + 1);
            }
        }
        lastScriptRunning.put(pid, s.scriptRunning);

        // 2. Player death
        Integer prevHp = lastHp.get(pid);
        if (prevHp != null && prevHp > 0 && (s.hp <= 0 || !s.alive) && s.loggedIn) {
            boolean shouldNotify = prefs.getBoolean("notify_death_" + normUser, true) && prefs.getBoolean("notify_death_" + user, true);
            if (shouldNotify) {
                showNotification("Player Defeated: " + user, user + " died in " + s.map + " (" + s.cell + ")", pid * 10 + 2);
            }
        }
        lastHp.put(pid, s.hp);

        // 3. Disconnected
        Boolean prevLoggedIn = lastLoggedIn.get(pid);
        if (prevLoggedIn != null && prevLoggedIn && !s.loggedIn) {
            boolean shouldNotify = prefs.getBoolean("notify_disconnect_" + normUser, true) && prefs.getBoolean("notify_disconnect_" + user, true);
            if (shouldNotify) {
                showNotification("Disconnected: " + user, user + " disconnected from AQW server!", pid * 10 + 3);
            }
        }
        lastLoggedIn.put(pid, s.loggedIn);

        // 4. Item drops
        if (s.currentDrops != null && !s.currentDrops.isEmpty()) {
            Integer prevDrops = lastDropsCount.get(pid);
            if (prevDrops != null && s.currentDrops.size() > prevDrops) {
                boolean shouldNotify = prefs.getBoolean("notify_drops_" + normUser, true) && prefs.getBoolean("notify_drops_" + user, true);
                if (shouldNotify) {
                    String lastDrop = s.currentDrops.get(s.currentDrops.size() - 1);
                    showNotification("Item Dropped: " + user, "Drop detected: " + lastDrop, pid * 10 + 4);
                }
            }
            lastDropsCount.put(pid, s.currentDrops.size());
        }
    }

    private void checkTabNotifications(Models.TabAccount tab) {
        if (tab == null || tab.pid <= 0) return;
        if (tab.pid == currentSelectedPid) return;

        String user = (tab.username != null && !tab.username.isEmpty() && !tab.username.startsWith("Tab "))
                ? tab.username : ("PID_" + tab.pid);
        String normUser = user.toLowerCase();

        boolean muted = prefs.getBoolean("notify_mute_" + normUser, false) || prefs.getBoolean("notify_mute_" + user, false);
        if (muted) return;

        // 1. Script stopped alert for background tab
        Boolean prevScript = lastScriptRunning.get(tab.pid);
        if (prevScript != null && prevScript && !tab.scriptRunning) {
            boolean shouldNotify = prefs.getBoolean("notify_script_stopped_" + normUser, true) && prefs.getBoolean("notify_script_stopped_" + user, true);
            if (shouldNotify) {
                String sName = (tab.scriptName != null && !tab.scriptName.isEmpty()) ? tab.scriptName : "Active Script";
                showNotification("Script Stopped: " + user, sName + " finished or stopped", tab.pid * 10 + 1);
            }
        }
        lastScriptRunning.put(tab.pid, tab.scriptRunning);

        // 2. Player death alert for background tab
        Integer prevHp = lastHp.get(tab.pid);
        if (prevHp != null && prevHp > 0 && tab.maxHp > 0 && tab.hp <= 0) {
            boolean shouldNotify = prefs.getBoolean("notify_death_" + normUser, true) && prefs.getBoolean("notify_death_" + user, true);
            if (shouldNotify) {
                String loc = (tab.map != null && !tab.map.isEmpty()) ? (tab.map + " (" + tab.cell + ")") : "World";
                showNotification("Player Defeated: " + user, user + " died in " + loc, tab.pid * 10 + 2);
            }
        }
        lastHp.put(tab.pid, tab.hp);
    }

    private boolean isUpdatingNotifyUi = false;
    private final Map<String, String> notifySpinnerToUsername = new HashMap<>();

    private void setupNotifyAccountSpinner() {
        if (spinnerNotifyAccounts == null) return;
        List<String> displayNames = new ArrayList<>();
        notifySpinnerToUsername.clear();

        for (Models.TabAccount t : runningTabs) {
            if (t.username != null && !t.username.isEmpty()) {
                String display = t.username;
                if (!displayNames.contains(display)) {
                    displayNames.add(display);
                    notifySpinnerToUsername.put(display, t.username);
                }
            }
        }
        for (Models.ManagedAccount a : savedAccounts) {
            String display = (a.displayName != null && !a.displayName.isEmpty()) ? a.displayName : a.username;
            if (display != null && !display.isEmpty() && !displayNames.contains(display)) {
                displayNames.add(display);
                notifySpinnerToUsername.put(display, (a.username != null && !a.username.isEmpty()) ? a.username : display);
            }
        }
        if (displayNames.isEmpty()) {
            displayNames.add("Default Account");
            notifySpinnerToUsername.put("Default Account", "default");
        }

        String currentSelected = (String) spinnerNotifyAccounts.getSelectedItem();
        ArrayAdapter<String> adapter = new ArrayAdapter<>(this, android.R.layout.simple_spinner_item, displayNames);
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        spinnerNotifyAccounts.setAdapter(adapter);

        if (currentSelected != null && displayNames.contains(currentSelected)) {
            spinnerNotifyAccounts.setSelection(displayNames.indexOf(currentSelected));
        }

        spinnerNotifyAccounts.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override
            public void onItemSelected(AdapterView<?> parent, View view, int position, long id) {
                loadNotificationPrefsForAccount(displayNames.get(position));
            }

            @Override
            public void onNothingSelected(AdapterView<?> parent) { }
        });

        loadNotificationPrefsForAccount((String) spinnerNotifyAccounts.getSelectedItem());

        // Setup CheckBox change listeners
        if (chkNotifyMuteAll != null) {
            chkNotifyMuteAll.setOnCheckedChangeListener((btn, isChecked) -> {
                if (isUpdatingNotifyUi) return;
                String userKey = getNotifyUserKey();
                prefs.edit().putBoolean("notify_mute_" + userKey, isChecked).apply();
            });
        }
        if (chkNotifyScriptStopped != null) {
            chkNotifyScriptStopped.setOnCheckedChangeListener((btn, isChecked) -> {
                if (isUpdatingNotifyUi) return;
                String userKey = getNotifyUserKey();
                prefs.edit().putBoolean("notify_script_stopped_" + userKey, isChecked).apply();
            });
        }
        if (chkNotifyPlayerDeath != null) {
            chkNotifyPlayerDeath.setOnCheckedChangeListener((btn, isChecked) -> {
                if (isUpdatingNotifyUi) return;
                String userKey = getNotifyUserKey();
                prefs.edit().putBoolean("notify_death_" + userKey, isChecked).apply();
            });
        }
        if (chkNotifyDisconnect != null) {
            chkNotifyDisconnect.setOnCheckedChangeListener((btn, isChecked) -> {
                if (isUpdatingNotifyUi) return;
                String userKey = getNotifyUserKey();
                prefs.edit().putBoolean("notify_disconnect_" + userKey, isChecked).apply();
            });
        }
        if (chkNotifyItemDrop != null) {
            chkNotifyItemDrop.setOnCheckedChangeListener((btn, isChecked) -> {
                if (isUpdatingNotifyUi) return;
                String userKey = getNotifyUserKey();
                prefs.edit().putBoolean("notify_drops_" + userKey, isChecked).apply();
            });
        }
    }

    private void updateNotifyAccountSpinner() {
        setupNotifyAccountSpinner();
    }

    private void setupNotificationListeners() {
        // Checkboxes are wired in setupNotifyAccountSpinner()
    }

    private String getNotifyUserKey() {
        if (spinnerNotifyAccounts == null) return "default";
        String sel = (String) spinnerNotifyAccounts.getSelectedItem();
        if (sel == null) return "default";
        String un = notifySpinnerToUsername.get(sel);
        return (un != null ? un : sel).toLowerCase();
    }

    private void loadNotificationPrefsForAccount(String account) {
        if (account == null) return;
        isUpdatingNotifyUi = true;
        try {
            String un = notifySpinnerToUsername.get(account);
            String norm = (un != null ? un : account).toLowerCase();
            if (chkNotifyMuteAll != null) {
                chkNotifyMuteAll.setChecked(prefs.getBoolean("notify_mute_" + norm, prefs.getBoolean("notify_mute_" + account, false)));
            }
            if (chkNotifyScriptStopped != null) {
                chkNotifyScriptStopped.setChecked(prefs.getBoolean("notify_script_stopped_" + norm, prefs.getBoolean("notify_script_stopped_" + account, true)));
            }
            if (chkNotifyPlayerDeath != null) {
                chkNotifyPlayerDeath.setChecked(prefs.getBoolean("notify_death_" + norm, prefs.getBoolean("notify_death_" + account, true)));
            }
            if (chkNotifyDisconnect != null) {
                chkNotifyDisconnect.setChecked(prefs.getBoolean("notify_disconnect_" + norm, prefs.getBoolean("notify_disconnect_" + account, true)));
            }
            if (chkNotifyItemDrop != null) {
                chkNotifyItemDrop.setChecked(prefs.getBoolean("notify_drops_" + norm, prefs.getBoolean("notify_drops_" + account, true)));
            }
        } finally {
            isUpdatingNotifyUi = false;
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        stopBackgroundPolling();
        if (isConnected) {
            startPolling();
        }
    }

    @Override
    protected void onPause() {
        super.onPause();
        stopPolling();
        startBackgroundPolling();
    }
}
