using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.Services;

public sealed class ArtixCalendarService
{
    private const string CalendarUrl = "https://www.artix.com/calendar/";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly Regex EventsBlock = new(@"events\s*:\s*\[(?<events>.*?)\]\s*[,}]?", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex EventObject = new(@"\{[^{}]*\}", RegexOptions.Compiled);
    private static readonly Regex Field = new(@"(?<key>title|url|start|end)\s*:\s*(?<value>'(?:[^'\\]|\\.)*'(?:\s*\+\s*'(?:[^'\\]|\\.)*')*)", RegexOptions.Compiled);
    private static readonly Regex Literal = new(@"'((?:[^'\\]|\\.)*)'", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private IReadOnlyList<CalendarEvent> _officialEvents = [];
    private DateTimeOffset _lastFetch;
    private readonly string _customEventsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Skua", "calendar-custom-events.json");

    public DateTimeOffset? LastSuccessfulSync => _lastFetch == default ? null : _lastFetch;

    public async Task<IReadOnlyList<CalendarEvent>> GetOfficialEventsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _officialEvents.Count > 0 && DateTimeOffset.UtcNow - _lastFetch < CacheDuration)
            return _officialEvents;

        try
        {
            using HttpResponseMessage response = await HttpClient.GetAsync(CalendarUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            List<CalendarEvent> parsed = Parse(html);

            // Never replace a valid cache with an empty result after a markup change or blocked response.
            if (parsed.Count == 0)
                throw new InvalidDataException("The Artix calendar page did not contain any readable AQWorlds events.");

            _officialEvents = parsed;
            _lastFetch = DateTimeOffset.UtcNow;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Could not refresh the Artix AQWorlds calendar. Keeping the last successful result: {exception}");
            if (_officialEvents.Count == 0)
                throw;
        }

        return _officialEvents;
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetCustomEventsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_customEventsPath))
            return [];

        try
        {
            await using FileStream stream = File.OpenRead(_customEventsPath);
            return await JsonSerializer.DeserializeAsync<List<CalendarEvent>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            Console.Error.WriteLine($"Could not read custom AQWorlds calendar events: {exception}");
            return [];
        }
    }

    public async Task SaveCustomEventsAsync(IReadOnlyCollection<CalendarEvent> events, CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(_customEventsPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("The calendar data directory could not be resolved.");

        Directory.CreateDirectory(directory);
        string temporaryPath = _customEventsPath + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, events, JsonOptions, cancellationToken).ConfigureAwait(false);

        File.Move(temporaryPath, _customEventsPath, true);
    }

    private static List<CalendarEvent> Parse(string html)
    {
        Match block = EventsBlock.Match(html);
        if (!block.Success)
            return [];

        List<CalendarEvent> events = [];
        foreach (Match eventMatch in EventObject.Matches(block.Groups["events"].Value))
        {
            Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);
            foreach (Match fieldMatch in Field.Matches(eventMatch.Value))
            {
                string value = string.Concat(Literal.Matches(fieldMatch.Groups["value"].Value)
                    .Select(match => UnescapeJavaScriptString(match.Groups[1].Value)));
                fields[fieldMatch.Groups["key"].Value] = value;
            }

            if (!fields.TryGetValue("title", out string? title) ||
                string.IsNullOrWhiteSpace(title) ||
                !fields.TryGetValue("start", out string? startText) ||
                !DateTime.TryParse(startText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime start))
                continue;

            DateTime? end = fields.TryGetValue("end", out string? endText) &&
                DateTime.TryParse(endText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsedEnd)
                    ? parsedEnd
                    : null;

            string? url = fields.GetValueOrDefault("url");
            if (!string.IsNullOrWhiteSpace(url))
            {
                url = url.Split('?', '#')[0].Trim();
                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    url = "https://" + url[7..];
                else if (url.StartsWith("/", StringComparison.Ordinal))
                    url = "https://www.artix.com" + url;
                else if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                    url = null;
            }

            events.Add(new CalendarEvent(Guid.NewGuid().ToString("N"), title.Trim(), start, end, url, false));
        }

        return events.OrderBy(calendarEvent => calendarEvent.Start).ToList();
    }

    private static string UnescapeJavaScriptString(string value) =>
        value.Replace(@"\'", "'").Replace(@"\\", @"\").Replace(@"\n", " ").Replace(@"\r", " ");
}

public sealed record CalendarEvent(string Id, string Title, DateTime Start, DateTime? End, string? Url, bool IsCustom)
{
    public string Source => IsCustom ? "CUSTOM" : "AQW";
    public string DateDisplay => Start.ToString("ddd, MMM d, yyyy h:mm tt");
}