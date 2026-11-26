using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Skua.Core.Services.AgentApi;
using Skua.Core.Tests.Fakes;
using Xunit;

namespace Skua.Core.Tests;

/// <summary>
/// The map reader: what the decompiled map says, and what happens around that
/// read (cache, refresh, missing Java, a map that is not loaded).
///
/// The fixture is FFDec-shaped text, not invented syntax: placement functions
/// in MainTimeline.as, and one <c>*_APOP.as</c> class per NPC. That is what the
/// real decompiler produces for an AQW map.
/// </summary>
public sealed class MapInfoServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _scripts;
    private readonly FakeWorld _world = new();
    private readonly CountingDecompiler _decompiler;
    private readonly MapInfoOptions _options;

    public MapInfoServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "vibeskoa-mapinfo-" + Guid.NewGuid().ToString("N"));
        _scripts = Path.Combine(_root, "fixture", "scripts", "battleon_fla");
        Directory.CreateDirectory(_scripts);
        WriteMainTimeline();
        WriteApop();
        _decompiler = new CountingDecompiler(_scripts);
        _options = new MapInfoOptions { CacheRoot = Path.Combine(_root, "cache") };
        _world.Set("Map.Name", "battleon");
        _world.Set("Map.FilePath", "maps/Battleon.mw");
        _world.Set("Map.RoomID", 5011);
        _world.Set("Map.Cells", new List<string> { "Enter", "Grounds" });
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup is best effort.
        }
    }

    /// <summary>A real map SWF arrives as bytes; a text stand-in would lose the tags.</summary>
    private MapInfoService Service(byte[]? swf = null) =>
        new(_world.Bot, new StubDownloader(Path.Combine(_root, "map.swf"), swf ?? System.Array.Empty<byte>()), _decompiler, _options);

    private void WriteMainTimeline(string? text = null) =>
        File.WriteAllText(Path.Combine(_scripts, "MainTimeline.as"), text ?? """
            class battleon_fla.MainTimeline extends MovieClip {
                internal function place1()
                {
                    if (param1 >= 1 && param1 <= 30)
                    {
                        this.npcLinkage = "Wulthys_NPC";
                        this.apopLinkage = "Wulthys_APOP";
                    }
                }
                internal function place2()
                {
                    if (param1 >= 31 && param1 <= 60)
                    {
                        this.npcLinkage = "Nullshaper_NPC";
                    }
                }
            }
            """);

    private void WriteApop() =>
        File.WriteAllText(Path.Combine(_scripts, "Wulthys_APOP.as"), """
            public class Wulthys_APOP extends APOPClass {
                internal function init()
                {
                    this.strQuests = "101,102";
                    this.strTurnIns = "102";
                    this.strAction = "Show Quests";
                    this.strString = "103";
                }
                internal function openShop()
                {
                    this.strAction = "Item Shop";
                    this.intID = 2283;
                }
                internal function leave()
                {
                    this.strAction = "/Join";
                    this.strString = "nullshapers_keep";
                    this.strFrame = "Enter";
                }
                internal function talk()
                {
                    this.strAction = "GotoAndPlay";
                }
            }
            """);

    /// <summary>A minimal SWF body: labels on frames 1 and 31.</summary>
    private static byte[] LabelsBody()
    {
        List<byte> body = new() { (5 << 3) | 4 };              // 5-bit rectangles, no more fields
        body.AddRange(new byte[] { 0, 0, 0 });                 // four 5-bit rect fields
        body.AddRange(new byte[] { 24, 0 });                   // frame rate 24
        body.AddRange(new byte[] { 2, 0 });                    // two frames

        void Label(string name)
        {
            int length = name.Length + 1;
            int header = (43 << 6) | length;
            body.Add((byte)(header & 0xFF));
            body.Add((byte)(header >> 8));
            body.AddRange(Encoding.UTF8.GetBytes(name));
            body.Add(0);
        }

        Label("Enter");
        body.Add(1 << 6); body.Add(0);                          // ShowFrame
        Label("Grounds");
        body.Add(0); body.Add(0);                               // End
        return body.ToArray();
    }

    private static byte[] UncompressedSwf(byte[] body)
    {
        List<byte> file = new() { (byte)'F', (byte)'W', (byte)'S', 8 };
        int length = file.Count + body.Length;
        file.Add((byte)(length & 0xFF));
        file.Add((byte)((length >> 8) & 0xFF));
        file.Add((byte)((length >> 16) & 0xFF));
        file.Add((byte)((length >> 24) & 0xFF));
        file.AddRange(body);
        return file.ToArray();
    }

    // ------------------------------------------------------------- the parser

    [Fact]
    public void Parse_GivesEachPlacedNpcTheCellOfItsFrameRange()
    {
        List<(int Frame, string Name)> labels = new() { (1, "Enter"), (31, "Grounds") };

        MapInfo info = MapInfoParser.Parse("battleon", "maps/Battleon.mw", 5011, new List<string> { "Enter", "Grounds" }, _scripts, labels);

        Assert.Equal("battleon", info.Map);
        Assert.Equal(5011, info.RoomId);
        Assert.Equal(2, info.Npcs.Count);
        Assert.Equal("Wulthys", info.Npcs[0].Name);
        Assert.Equal(new[] { "Enter" }, info.Npcs[0].Cells);
        Assert.Equal("Nullshaper", info.Npcs[1].Name);
        Assert.Equal(new[] { "Grounds" }, info.Npcs[1].Cells);
    }

    [Fact]
    public void Parse_OrdersNpcsByTheGamesOwnCellOrder()
    {
        List<(int Frame, string Name)> labels = new() { (1, "Enter"), (31, "Grounds") };

        MapInfo info = MapInfoParser.Parse("battleon", "maps/Battleon.mw", 1, new List<string> { "Grounds", "Enter" }, _scripts, labels);

        Assert.Equal("Nullshaper", info.Npcs[0].Name);
        Assert.Equal("Wulthys", info.Npcs[1].Name);
    }

    [Fact]
    public void Parse_ReadsQuestsShopAndExitFromTheNpcClass()
    {
        List<(int Frame, string Name)> labels = new() { (1, "Enter") };

        MapInfo info = MapInfoParser.Parse("battleon", "maps/Battleon.mw", 1, new List<string> { "Enter" }, _scripts, labels);
        MapNpc wulthys = info.Npcs.Single(n => n.Name == "Wulthys");

        Assert.Equal(new[] { 101, 102, 103 }, wulthys.Quests.Select(q => q.Id).ToArray());
        Assert.True(wulthys.Quests.Single(q => q.Id == 102).TurnIn);
        Assert.False(wulthys.Quests.Single(q => q.Id == 101).TurnIn);
        Assert.Equal(new[] { 2283 }, wulthys.Shops.Select(s => s.Id).ToArray());
        Assert.Equal("nullshapers_keep", wulthys.Joins.Single().Map);
        Assert.Equal("Enter", wulthys.Joins.Single().Cell);
        Assert.True(wulthys.HasDialogue);
    }

    [Fact]
    public void Parse_DropsNpcClassesTheTimelineDoesNotPlace()
    {
        // Battleon ships APOP classes for other rooms; showing them would send
        // the agent to an NPC that is not standing there.
        File.WriteAllText(Path.Combine(_scripts, "UnusedEvent_APOP.as"), "public class UnusedEvent_APOP { }");
        List<(int Frame, string Name)> labels = new() { (1, "Enter") };

        MapInfo info = MapInfoParser.Parse("battleon", "maps/Battleon.mw", 1, new List<string> { "Enter" }, _scripts, labels);

        Assert.DoesNotContain(info.Npcs, n => n.Name == "UnusedEvent");
    }

    [Fact]
    public void Parse_KeepsEveryClassWhenNothingIsPlaced()
    {
        // A timeline this parser cannot read must not produce an empty answer.
        WriteMainTimeline("class MainTimeline extends MovieClip { }");
        List<(int Frame, string Name)> labels = new() { (1, "Enter") };

        MapInfo info = MapInfoParser.Parse("battleon", "maps/Battleon.mw", 1, new List<string> { "Enter" }, _scripts, labels);

        Assert.Contains(info.Npcs, n => n.Name == "Wulthys");
    }

    [Fact]
    public void Parse_WorksWithoutFrameLabels()
    {
        WriteMainTimeline();

        MapInfo info = MapInfoParser.Parse("battleon", "maps/Battleon.mw", 1, new List<string>(), _scripts, new List<(int, string)>());

        Assert.Equal(2, info.Npcs.Count);
        Assert.All(info.Npcs, npc => Assert.Empty(npc.Cells));
    }

    // -------------------------------------------------- the SWF label reader

    [Fact]
    public void FrameLabels_ReadFromAnUncompressedSwf()
    {
        List<(int Frame, string Name)> labels = SwfFrameLabels.Parse(UncompressedSwf(LabelsBody()));

        // The frame number counts ShowFrame tags, so the second label sits on
        // frame 2 in this body: one label, one ShowFrame, one label.
        Assert.Equal(new[] { "Enter", "Grounds" }, labels.Select(l => l.Name).ToArray());
        Assert.Equal(new[] { 1, 2 }, labels.Select(l => l.Frame).ToArray());
    }

    [Fact]
    public void FrameLabels_ReadFromACompressedSwf()
    {
        byte[] body = LabelsBody();
        byte[] compressed;
        using (MemoryStream raw = new())
        {
            using (System.IO.Compression.ZLibStream zlib = new(raw, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(body);
            }
            compressed = raw.ToArray();
        }
        List<byte> file = new() { (byte)'C', (byte)'W', (byte)'S', 8 };
        int length = body.Length + 8;
        file.Add((byte)(length & 0xFF));
        file.Add((byte)((length >> 8) & 0xFF));
        file.Add((byte)((length >> 16) & 0xFF));
        file.Add((byte)((length >> 24) & 0xFF));
        file.AddRange(compressed);

        List<(int Frame, string Name)> labels = SwfFrameLabels.Parse(file.ToArray());

        Assert.Equal(new[] { "Enter", "Grounds" }, labels.Select(l => l.Name).ToArray());
    }

    [Fact]
    public void FrameLabels_AreEmptyForSomethingThatIsNotAnSwf()
    {
        Assert.Empty(SwfFrameLabels.Parse(Encoding.UTF8.GetBytes("<html>404 not found</html>")));
        Assert.Empty(SwfFrameLabels.Parse(Array.Empty<byte>()));
    }

    // ------------------------------------------------- read, cache, refresh

    [Fact]
    public async Task GetCurrentMap_ParsesTheLoadedMap()
    {
        MapInfo info = await Service(UncompressedSwf(LabelsBody())).GetCurrentMapAsync(false, default);

        Assert.Equal("battleon", info.Map);
        Assert.Equal(5011, info.RoomId);
        Assert.False(info.FromCache);
        Assert.Equal("Wulthys", info.Npcs[0].Name);
        Assert.Equal(new[] { "Enter" }, info.Npcs[0].Cells);
        Assert.Equal(1, _decompiler.Calls);
    }

    [Fact]
    public async Task GetCurrentMap_CachesPerMapFileAndReusesIt()
    {
        MapInfoService service = Service(UncompressedSwf(LabelsBody()));

        MapInfo first = await service.GetCurrentMapAsync(false, default);
        MapInfo second = await service.GetCurrentMapAsync(false, default);

        Assert.Equal(1, _decompiler.Calls);
        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(first.Npcs[0].Name, second.Npcs[0].Name);
        Assert.Equal(5011, second.RoomId);
    }

    [Fact]
    public async Task GetCurrentMap_RefreshDecompilesAgain()
    {
        MapInfoService service = Service(UncompressedSwf(LabelsBody()));
        await service.GetCurrentMapAsync(false, default);

        MapInfo refreshed = await service.GetCurrentMapAsync(true, default);

        Assert.Equal(2, _decompiler.Calls);
        Assert.False(refreshed.FromCache);
    }

    [Fact]
    public async Task GetCurrentMap_TakesCellsFromTheRunningGameNotTheCache()
    {
        MapInfoService service = Service(UncompressedSwf(LabelsBody()));
        await service.GetCurrentMapAsync(false, default);
        _world.Set("Map.Cells", new List<string> { "Enter", "Grounds", "Cavern" });

        MapInfo cached = await service.GetCurrentMapAsync(false, default);

        Assert.True(cached.FromCache);
        Assert.Equal(new[] { "Enter", "Grounds", "Cavern" }, cached.Cells);
    }

    [Fact]
    public async Task GetCurrentMap_IgnoresACacheWrittenByAnOlderParser()
    {
        MapInfoService service = Service(UncompressedSwf(LabelsBody()));
        await service.GetCurrentMapAsync(false, default);

        // Simulate an upgrade: the parser can now find more than this cache holds.
        string cacheFile = Directory.GetFiles(Path.Combine(_options.CacheRoot, "mapinfo"), "*.json").Single();
        JsonNode? node = JsonNode.Parse(File.ReadAllText(cacheFile));
        node!["version"] = MapInfoOptions.CurrentCacheVersion - 1;
        File.WriteAllText(cacheFile, node.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        MapInfo info = await service.GetCurrentMapAsync(false, default);

        Assert.Equal(2, _decompiler.Calls);
        Assert.False(info.FromCache);
    }

    [Fact]
    public async Task GetCurrentMap_WithoutALoadedMap_IsItsOwnError()
    {
        _world.Set("Map.FilePath", string.Empty);

        MapInfoException error = await Assert.ThrowsAsync<MapInfoException>(
            () => Service().GetCurrentMapAsync(false, default));

        Assert.Equal("map_not_loaded", error.Code);
        Assert.Equal(0, _decompiler.Calls);
    }

    [Fact]
    public async Task GetCurrentMap_PassesOnADecompileFailure()
    {
        _decompiler.Failure = new MapInfoException("java_missing", "no java");

        MapInfoException error = await Assert.ThrowsAsync<MapInfoException>(
            () => Service().GetCurrentMapAsync(false, default));

        Assert.Equal("java_missing", error.Code);
    }

    [Fact]
    public async Task GetCurrentMap_ReportsAFailedParseAsItsOwnError()
    {
        WriteMainTimeline("this is not actionscript at all");
        File.Delete(Path.Combine(_scripts, "Wulthys_APOP.as"));

        // No placements and no classes is a valid but empty read, so force a
        // parse crash by removing the folder under the parser's feet.
        _decompiler.DeleteAfterHandingOver = true;

        MapInfoException error = await Assert.ThrowsAsync<MapInfoException>(
            () => Service(UncompressedSwf(LabelsBody())).GetCurrentMapAsync(true, default));

        Assert.Equal("map_parse_failed", error.Code);
    }

    // ---------------------------------------------------- download plumbing

    [Fact]
    public void SafeRelativePath_RejectsAnEmptyPath()
    {
        Assert.Equal(Path.Combine("maps", "x.swf"), AqwMapSwfDownloader.SafeRelativePath("maps/x"));
        Assert.Equal(Path.Combine("maps", "x.swf"), AqwMapSwfDownloader.SafeRelativePath("/maps/./x.swf"));
        Assert.Equal("x.swf", AqwMapSwfDownloader.SafeRelativePath("../../x"));

        MapInfoException error = Assert.Throws<MapInfoException>(() => AqwMapSwfDownloader.SafeRelativePath("../.."));
        Assert.Equal("map_path_invalid", error.Code);
    }

    [Fact]
    public async Task Downloader_FailsWithACodeWhenTheCdnAnswersWithAnErrorPage()
    {
        string cacheRoot = Path.Combine(_root, "dl-cache");
        MapInfoOptions options = new() { CacheRoot = cacheRoot };
        AqwMapSwfDownloader downloader = new(options, _world.Logs, (url, ct) => Task.FromResult(new byte[10]));

        MapInfoException error = await Assert.ThrowsAsync<MapInfoException>(
            () => downloader.EnsureLocalAsync("maps/Battleon.mw", default));

        Assert.Equal("map_download_failed", error.Code);
    }

    [Fact]
    public async Task Downloader_ReusesAMapTheClientAlreadyDownloaded()
    {
        string cacheRoot = Path.Combine(_root, "shared-cache");
        Directory.CreateDirectory(cacheRoot);
        File.WriteAllBytes(Path.Combine(cacheRoot, "Battleon.swf"), new byte[4096]);
        AqwMapSwfDownloader downloader = new(new MapInfoOptions { CacheRoot = cacheRoot }, _world.Logs,
            (url, ct) => throw new InvalidOperationException("must not download"));

        string path = await downloader.EnsureLocalAsync("maps/Battleon.swf", default);

        Assert.Equal(Path.Combine(cacheRoot, "Battleon.swf"), path);
    }

    // ------------------------------------------------------------ the routes

    [Fact]
    public async Task MapNpcs_ReportsWhatTheReaderKnows()
    {
        FakeWorld world = new();
        AgentApiOptions options = new() { ScriptsRoot = Path.GetTempPath() };
        AgentApiRouter router = new(options, world.Bot, world.Manager, world.Logs, new StubReader());

        AgentApiResponse response = await router.HandleAsync(new AgentApiRequest { Method = "GET", Path = "/map/npcs" });

        Assert.Equal(200, response.Status);
        JsonElement data = TestJson(root: response.Data!);
        Assert.Equal("battleon", data.GetProperty("map").GetString());
        Assert.True(data.GetProperty("fromCache").GetBoolean());
        Assert.Equal("Wulthys", data.GetProperty("npcs")[0].GetProperty("name").GetString());
        Assert.Equal(101, data.GetProperty("npcs")[0].GetProperty("quests")[0].GetProperty("id").GetInt32());
        Assert.Equal(2283, data.GetProperty("npcs")[0].GetProperty("shops")[0].GetInt32());
        Assert.Equal("nullshapers_keep", data.GetProperty("npcs")[0].GetProperty("joins")[0].GetProperty("map").GetString());
    }

    [Fact]
    public async Task MapNpcs_WithoutAReaderSaysSo()
    {
        FakeWorld world = new();
        AgentApiOptions options = new() { ScriptsRoot = Path.GetTempPath() };
        AgentApiRouter router = new(options, world.Bot, world.Manager, world.Logs);

        AgentApiResponse response = await router.HandleAsync(new AgentApiRequest { Method = "GET", Path = "/map/npcs" });

        Assert.Equal(501, response.Status);
        Assert.Equal("map_info_unavailable", response.Error);
    }

    [Fact]
    public async Task MapNpcs_SeparatesMissingJavaFromAFailedRead()
    {
        FakeWorld world = new();
        AgentApiOptions options = new() { ScriptsRoot = Path.GetTempPath() };

        AgentApiResponse noJava = await new AgentApiRouter(options, world.Bot, world.Manager, world.Logs,
            new ThrowingReader("decompiler_unavailable")).HandleAsync(new AgentApiRequest { Method = "GET", Path = "/map/npcs" });
        AgentApiResponse badMap = await new AgentApiRouter(options, world.Bot, world.Manager, world.Logs,
            new ThrowingReader("map_download_failed")).HandleAsync(new AgentApiRequest { Method = "GET", Path = "/map/npcs" });

        Assert.Equal(503, noJava.Status);
        Assert.Equal(502, badMap.Status);
    }

    // ------------------------------------------------------------- test doubles

    private static JsonElement TestJson(object root) =>
        JsonSerializer.SerializeToElement(root, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private sealed class StubReader : IMapInfoReader
    {
        public Task<MapInfo> GetCurrentMapAsync(bool refresh, CancellationToken ct) => Task.FromResult(new MapInfo
        {
            Map = "battleon",
            RoomId = 1,
            FilePath = "maps/Battleon.mw",
            Cells = new List<string> { "Enter" },
            FromCache = true,
            Npcs = new List<MapNpc>
            {
                new()
                {
                    Name = "Wulthys",
                    Cells = new List<string> { "Enter" },
                    Quests = new List<NpcQuest> { new() { Id = 101 } },
                    Shops = new List<NpcShop> { new() { Id = 2283 } },
                    Joins = new List<NpcJoin> { new() { Map = "nullshapers_keep", Cell = "Enter" } },
                },
            },
        });
    }

    private sealed class ThrowingReader(string code) : IMapInfoReader
    {
        public Task<MapInfo> GetCurrentMapAsync(bool refresh, CancellationToken ct) =>
            throw new MapInfoException(code, "stub");
    }

    private sealed class StubDownloader(string path, byte[] contents) : IMapSwfDownloader
    {
        public Task<string> EnsureLocalAsync(string filePath, CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, contents);
            return Task.FromResult(path);
        }
    }

    /// <summary>Hands out a fresh copy of the fixture scripts, like a real decompile would.</summary>
    private sealed class CountingDecompiler : IMapDecompiler
    {
        private readonly string _source;

        public CountingDecompiler(string source) => _source = source;

        public int Calls { get; private set; }
        public MapInfoException? Failure { get; set; }
        public bool DeleteAfterHandingOver { get; set; }

        public Task<string> DecompileAsync(string swfPath, CancellationToken ct)
        {
            Calls++;
            if (Failure is not null)
                throw Failure;

            string output = Path.Combine(Path.GetTempPath(), "vibeskoa-map-test-" + Guid.NewGuid().ToString("N"), "scripts", "room_fla");
            Directory.CreateDirectory(output);
            foreach (string file in Directory.GetFiles(_source, "*.as"))
                File.Copy(file, Path.Combine(output, Path.GetFileName(file)));

            if (DeleteAfterHandingOver)
                Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(output)!), true);

            return Task.FromResult(output);
        }
    }
}
