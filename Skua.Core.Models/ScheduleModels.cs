using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Skua.Core.Models;

public enum ScheduleTriggerType
{
    Once = 0,
    Daily = 1,
    Weekly = 2,
    Interval = 3,
    DailyResetSync = 4,
    ServerResetSync = 4,
    WeeklyResetSync = 5
}

public enum ScheduleTargetType
{
    SingleAccount = 0,
    AccountGroup = 1,
    ArmyQueuePreset = 2,
    MultipleAccounts = 3
}

public enum SchedulePipelineMode
{
    PlaylistFile = 0,
    CustomSteps = 1
}

public enum ScheduleConflictPolicy
{
    WaitUntilFree = 0,
    SkipTrigger = 1,
    ForceTerminateAndStart = 2
}

public enum SystemPowerAction
{
    None = 0,
    SleepPC = 1,
    ShutdownPC = 2
}

public class ScheduleScriptStep
{
    [JsonPropertyName("Id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("ScriptPath")]
    public string ScriptPath { get; set; } = string.Empty;

    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("Storage")]
    public string? Storage { get; set; }
}

public class ScheduleTriggerConfig
{
    [JsonPropertyName("TriggerType")]
    public ScheduleTriggerType TriggerType { get; set; } = ScheduleTriggerType.Daily;

    [JsonPropertyName("TimeOfDay")]
    public TimeSpan TimeOfDay { get; set; } = TimeSpan.FromHours(0); // 00:00:00

    [JsonPropertyName("DaysOfWeek")]
    public List<DayOfWeek> DaysOfWeek { get; set; } = new()
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };

    [JsonPropertyName("IntervalMinutes")]
    public int IntervalMinutes { get; set; } = 60;

    [JsonPropertyName("ServerResetOffsetMinutes")]
    public int ServerResetOffsetMinutes { get; set; } = 5; // e.g. +5 min after midnight EST

    [JsonPropertyName("SpecificDateTime")]
    public DateTimeOffset? SpecificDateTime { get; set; }
}

public class ScheduleTargetConfig
{
    [JsonPropertyName("TargetType")]
    public ScheduleTargetType TargetType { get; set; } = ScheduleTargetType.SingleAccount;

    [JsonPropertyName("TargetName")]
    public string? TargetName { get; set; }

    [JsonPropertyName("TargetAccounts")]
    public List<string> TargetAccounts { get; set; } = new();

    [JsonPropertyName("Server")]
    public string? Server { get; set; }
}

public class SchedulePipelineConfig
{
    [JsonPropertyName("PipelineMode")]
    public SchedulePipelineMode PipelineMode { get; set; } = SchedulePipelineMode.CustomSteps;

    [JsonPropertyName("PlaylistPath")]
    public string? PlaylistPath { get; set; }

    [JsonPropertyName("Steps")]
    public List<ScheduleScriptStep> Steps { get; set; } = new();

    [JsonPropertyName("RestartClientBetweenScripts")]
    public bool RestartClientBetweenScripts { get; set; } = false;
}

public class SchedulePostRunConfig
{
    [JsonPropertyName("ExitClientOnFinish")]
    public bool ExitClientOnFinish { get; set; } = true;

    [JsonPropertyName("DiscordWebhookUrl")]
    public string? DiscordWebhookUrl { get; set; }

    [JsonPropertyName("NotifyOnStart")]
    public bool NotifyOnStart { get; set; } = true;

    [JsonPropertyName("NotifyOnFinish")]
    public bool NotifyOnFinish { get; set; } = true;

    [JsonPropertyName("NotifyOnError")]
    public bool NotifyOnError { get; set; } = true;

    [JsonPropertyName("PowerAction")]
    public SystemPowerAction PowerAction { get; set; } = SystemPowerAction.None;
}

public class ScheduleExecutionRecord
{
    [JsonPropertyName("Id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("Timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.Now;

    [JsonPropertyName("Duration")]
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;

    [JsonPropertyName("Status")]
    public string Status { get; set; } = "Completed";

    [JsonPropertyName("Message")]
    public string? Message { get; set; }

    [JsonPropertyName("Logs")]
    public List<string> Logs { get; set; } = new();
}

public class ScheduledJob
{
    [JsonPropertyName("Id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("Name")]
    public string Name { get; set; } = "New Scheduled Task";

    [JsonPropertyName("Description")]
    public string? Description { get; set; }

    [JsonPropertyName("IsEnabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("SortOrder")]
    public int SortOrder { get; set; } = 0;

    [JsonPropertyName("Trigger")]
    public ScheduleTriggerConfig Trigger { get; set; } = new();

    [JsonPropertyName("Target")]
    public ScheduleTargetConfig Target { get; set; } = new();

    [JsonPropertyName("Pipeline")]
    public SchedulePipelineConfig Pipeline { get; set; } = new();

    [JsonPropertyName("ConflictPolicy")]
    public ScheduleConflictPolicy ConflictPolicy { get; set; } = ScheduleConflictPolicy.WaitUntilFree;

    [JsonPropertyName("PostRun")]
    public SchedulePostRunConfig PostRun { get; set; } = new();

    [JsonPropertyName("LastRunTime")]
    public DateTime? LastRunTime { get; set; }

    [JsonPropertyName("LastDuration")]
    public TimeSpan? LastDuration { get; set; }

    [JsonPropertyName("LastResult")]
    public string? LastResult { get; set; }

    [JsonPropertyName("NextRunTime")]
    public DateTime? NextRunTime { get; set; }

    [JsonPropertyName("History")]
    public List<ScheduleExecutionRecord> History { get; set; } = new();
}
