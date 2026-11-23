using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace League_Data_Recorder;

#region Alias catalog

/// <summary>One editable source for transcription, post-processing and role evidence.</summary>
internal sealed class LeagueAliasCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
    private static readonly string[] UnsafeRoleTerms =
    [
        "q",
        "w",
        "e",
        "r",
        "ult",
        "ulti",
        "ultimate",
        "passive",
        "ability",
        "spell",
        "stun",
        "root",
        "slow",
        "dash",
        "shield",
        "heal",
        "knockup",
        "knock up",
    ];
    private readonly LeagueAliasFile file;
    internal string Fingerprint { get; }
    internal string Filename { get; }
    internal IReadOnlyList<string> CommonTerms => file.CommonTerms;
    internal IReadOnlyList<ChampionAlias> Champions => file.Champions;

    private LeagueAliasCatalog(LeagueAliasFile file, string fingerprint, string filename)
    {
        this.file = file;
        Fingerprint = fingerprint;
        Filename = filename;
    }

    internal static async Task<LeagueAliasCatalog> LoadAsync(
        string? path = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default
    )
    {
        string fullPath = Path.GetFullPath(path ?? "league_aliases.json", AppContext.BaseDirectory);
        if (!File.Exists(fullPath))
        {
            if (path is not null)
                throw new FileNotFoundException(
                    "The configured League alias catalog was not found.",
                    fullPath
                );
            log?.Invoke(
                "league_aliases.json is missing; using canonical names and basic League terms."
            );
            return new LeagueAliasCatalog(
                new LeagueAliasFile(),
                "missing",
                Path.GetFileName(fullPath)
            );
        }
        string json = await File.ReadAllTextAsync(fullPath, cancellationToken)
            .ConfigureAwait(false);
        LeagueAliasFile data;
        try
        {
            data =
                JsonSerializer.Deserialize<LeagueAliasFile>(json, JsonOptions)
                ?? throw new JsonException("Empty catalog.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Invalid League alias catalog {fullPath}: {ex.Message}",
                ex
            );
        }
        Validate(data);
        return new LeagueAliasCatalog(
            data,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))),
            Path.GetFileName(fullPath)
        );
    }

    private static void Validate(LeagueAliasFile data)
    {
        if (
            data.Version != 1
            || data.Champions is null
            || data.Abilities is null
            || data.Items is null
            || data.SummonerSpells is null
            || data.CommonTerms is null
            || data.IgnoreForRoleAssignment is null
        )
            throw new InvalidDataException(
                "league_aliases.json needs version 1 and non-null champions, abilities, items, summonerSpells, commonTerms and ignoreForRoleAssignment arrays."
            );
        ValidateEntries(data.Champions, "champions", e => e.Id.ToString());
        ValidateEntries(data.Items, "items", e => e.Id.ToString());
        ValidateEntries(data.SummonerSpells, "summonerSpells", e => e.Id.ToString());
        ValidateEntries(data.Abilities, "abilities", e => $"{e.ChampionId}:{e.Id}");
        if (
            data.Abilities.Any(e =>
                e.ChampionId <= 0
                || string.IsNullOrWhiteSpace(e.Id)
                || e.Slot is not ("Q" or "W" or "E" or "R" or "P")
            )
        )
            throw new InvalidDataException(
                "Every ability needs a championId, its Data Dragon id (or <ChampionKey>:passive), and a P/Q/W/E/R slot."
            );
        if (
            data.Champions.Any(e => e.Id <= 0)
            || data.Items.Any(e => e.Id <= 0)
            || data.SummonerSpells.Any(e => e.Id <= 0)
            || data.CommonTerms.Concat(data.IgnoreForRoleAssignment).Any(string.IsNullOrWhiteSpace)
        )
            throw new InvalidDataException(
                "Entity IDs must be positive and vocabulary terms must be nonempty strings."
            );

        static void ValidateEntries<T>(T[] entries, string label, Func<T, string> key)
            where T : EntityAlias
        {
            if (
                entries.Any(e =>
                    e is null
                    || string.IsNullOrWhiteSpace(e.Name)
                    || e.Aliases is null
                    || e.Aliases.Any(string.IsNullOrWhiteSpace)
                    || e.RoleAliases?.Any(string.IsNullOrWhiteSpace) == true
                    || e.PostProcessingAliases?.Any(string.IsNullOrWhiteSpace) == true
                )
            )
                throw new InvalidDataException($"Invalid name or aliases in {label}.");
            if (entries.GroupBy(key, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                throw new InvalidDataException($"Duplicate IDs in {label}.");
        }
    }

    internal static string Key(string? text) =>
        string.Concat((text ?? "").Where(char.IsLetterOrDigit)).ToLowerInvariant();

    internal static string[] Merge(params IEnumerable<string>?[] sets) =>
        sets.Where(s => s is not null)
            .SelectMany(s => s!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal ChampionAlias? Champion(int? id, string? name = null) =>
        (id is > 0 ? file.Champions.FirstOrDefault(e => e.Id == id) : null)
        ?? UniqueName(file.Champions, name, e => e.Key);

    internal ItemAlias? Item(int? id, string? name = null) =>
        (id is > 0 ? file.Items.FirstOrDefault(e => e.Id == id) : null)
        ?? UniqueName(file.Items, name);

    internal SummonerAlias? Summoner(int? id, string? name = null) =>
        (id is > 0 ? file.SummonerSpells.FirstOrDefault(e => e.Id == id) : null)
        ?? UniqueName(file.SummonerSpells, name);

    internal AbilityAlias? Ability(int? championId, string? id, string? name, string? slot = null)
    {
        var entries = file
            .Abilities.Where(e => championId is not > 0 || e.ChampionId == championId)
            .ToArray();
        return (
                !string.IsNullOrWhiteSpace(id)
                    ? entries.FirstOrDefault(e =>
                        e.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
                    )
                    : null
            )
            ?? UniqueName(entries, name)
            ?? (
                !string.IsNullOrWhiteSpace(slot) && championId is > 0
                    ? entries.FirstOrDefault(e => e.Slot == slot)
                    : null
            );
    }

    internal IEnumerable<AbilityAlias> Abilities(int? championId) =>
        file.Abilities.Where(e => e.ChampionId == championId);

    private static T? UniqueName<T>(
        IEnumerable<T> entries,
        string? name,
        Func<T, string?>? extra = null
    )
        where T : EntityAlias
    {
        string key = Key(name);
        if (key.Length == 0)
            return null;
        T[] matches = entries
            .Where(e => Key(e.Name) == key || extra is not null && Key(extra(e)) == key)
            .Take(2)
            .ToArray();
        // Items may have mode-specific IDs with the same canonical name. Their spoken names are interchangeable.
        if (matches.Length > 0)
            return matches[0];
        matches = entries.Where(e => e.Aliases.Any(a => Key(a) == key)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    internal string[] Names(EntityAlias? entry, string? canonical) =>
        Merge(
            string.IsNullOrWhiteSpace(canonical) ? [] : [canonical],
            entry is null ? [] : [entry.Name],
            entry?.Aliases
        );

    internal string[] RoleNames(
        EntityAlias? entry,
        string? canonical,
        IEnumerable<string>? saved = null
    ) =>
        Merge(
                string.IsNullOrWhiteSpace(canonical) ? [] : [canonical],
                entry is null ? [] : [entry.Name],
                entry?.RoleAliases ?? entry?.Aliases,
                saved
            )
            .Where(IsRoleTerm)
            .ToArray();

    internal bool IsRoleTerm(string term) =>
        !UnsafeRoleTerms.Concat(file.IgnoreForRoleAssignment).Any(t => Key(t) == Key(term));

    internal string[] AbilityNames(
        int? championId,
        string? champion,
        string? id,
        string name,
        string? slot,
        bool roleEvidence = false,
        IEnumerable<string>? saved = null
    )
    {
        var entry = Ability(championId, id, name, slot);
        slot ??= entry?.Slot;
        string[] championNames = Names(Champion(championId, champion), champion);
        string[] spokenSlots = slot switch
        {
            "R" => ["ult", "ultimate", "ulti", "R"],
            "P" => ["passive"],
            null => [],
            _ => [slot],
        };
        var qualified = championNames.SelectMany(n => spokenSlots.SelectMany(s =>
            new[] { $"{n} {s}", $"{n}'s {s}" }));
        return Merge(qualified, roleEvidence ? RoleNames(entry, name, saved) : Merge(Names(entry, name), saved));
    }

    /// <summary>Refreshes catalog-derived aliases in memory. Legacy/manual context aliases are preserved.</summary>
    internal RoleGameContext Enrich(RoleGameContext source)
    {
        bool keepSaved = source.PreserveAliases || source.AliasCatalogFingerprint is null;
        return source with
        {
            AliasCatalogFilename = Filename,
            AliasCatalogFingerprint = Fingerprint,
            Roles = source.Roles.ToDictionary(
                p => p.Key,
                p => EnrichRoster(p.Value),
                StringComparer.Ordinal
            ),
        };

        RoleRoster EnrichRoster(RoleRoster roster)
        {
            var ally = Champion(roster.AlliedChampionId, roster.AlliedChampion);
            var enemy = Champion(roster.OpposingChampionId, roster.OpposingChampion);
            int? allyId = roster.AlliedChampionId is > 0 ? roster.AlliedChampionId : ally?.Id;
            string? allyName = roster.AlliedChampion ?? ally?.Name;
            var abilities = roster.AlliedAbilityCues.ToList();
            foreach (var entry in Abilities(allyId))
                if (
                    !abilities.Any(a =>
                        a.Id == entry.Id
                        || Key(a.Ability) == Key(entry.Name)
                        || a.Slot == entry.Slot
                    )
                )
                    abilities.Add(
                        new AbilityCue(entry.Name, []) { Id = entry.Id, Slot = entry.Slot }
                    );
            return roster with
            {
                AlliedChampionId = allyId,
                OpposingChampionId = roster.OpposingChampionId is > 0
                    ? roster.OpposingChampionId
                    : enemy?.Id,
                AlliedChampion = allyName,
                OpposingChampion = roster.OpposingChampion ?? enemy?.Name,
                AlliedChampionAliases = RoleNames(
                    ally,
                    allyName,
                    keepSaved ? roster.AlliedChampionAliases : null
                ),
                OpposingChampionAliases = RoleNames(
                    enemy,
                    roster.OpposingChampion ?? enemy?.Name,
                    keepSaved ? roster.OpposingChampionAliases : null
                ),
                AlliedAbilityCues = abilities
                    .Select(a =>
                    {
                        var entry = Ability(allyId, a.Id, a.Ability, a.Slot);
                        return a with
                        {
                            Ability = entry?.Name ?? a.Ability,
                            Id = a.Id ?? entry?.Id,
                            Slot = a.Slot ?? entry?.Slot,
                            Aliases = AbilityNames(
                                allyId,
                                allyName,
                                a.Id,
                                a.Ability,
                                a.Slot,
                                true,
                                keepSaved ? a.Aliases : null
                            ),
                        };
                    })
                    .ToArray(),
                SummonerSpells = roster
                    .SummonerSpells.Select(s => EnrichLoadout(s, true))
                    .ToArray(),
                EndgameItems = roster.EndgameItems.Select(s => EnrichLoadout(s, false)).ToArray(),
            };
        }
        LoadoutCue EnrichLoadout(LoadoutCue cue, bool spell)
        {
            EntityAlias? entry = spell ? Summoner(cue.Id, cue.Name) : Item(cue.Id, cue.Name);
            int? id = cue.Id ?? (entry as SummonerAlias)?.Id ?? (entry as ItemAlias)?.Id;
            return cue with
            {
                Name = entry?.Name ?? cue.Name,
                Id = id,
                Aliases = RoleNames(entry, cue.Name, keepSaved ? cue.Aliases : null),
            };
        }
    }
}

internal sealed class LeagueAliasFile
{
    public int Version { get; init; } = 1;
    public string[] CommonTerms { get; init; } =
    ["prio", "cs", "gank", "reset", "peel", "ult", "sums", "tp", "baron", "drake"];
    public string[] IgnoreForRoleAssignment { get; init; } = [];
    public ChampionAlias[] Champions { get; init; } = [];
    public AbilityAlias[] Abilities { get; init; } = [];
    public ItemAlias[] Items { get; init; } = [];
    public SummonerAlias[] SummonerSpells { get; init; } = [];
}

internal abstract class EntityAlias
{
    public string Name { get; init; } = "";
    public string[] Aliases { get; init; } = [];

    // Optional: override which aliases can contribute role evidence. Empty means canonical name only.
    public string[]? RoleAliases { get; init; }

    // Null derives cleanup aliases from Aliases. Empty preserves all shorthand for this entity.
    // These need not be injected into the ASR prompt (e.g. known transcription mishearings).
    public string[]? PostProcessingAliases { get; init; }
}

internal sealed class ChampionAlias : EntityAlias
{
    public int Id { get; init; }
    public string? Key { get; init; }
}

internal sealed class AbilityAlias : EntityAlias
{
    public string Id { get; init; } = "";
    public int ChampionId { get; init; }
    public string Slot { get; init; } = "";
}

internal sealed class ItemAlias : EntityAlias
{
    public int Id { get; init; }
}

internal sealed class SummonerAlias : EntityAlias
{
    public int Id { get; init; }
}

#endregion

#region Match vocabulary and prompts

/// <summary>Only entities actually present in this recording, plus the common vocabulary.</summary>
internal sealed class LeagueMatchVocabulary
{
    public string AliasCatalogFilename { get; init; } = "";
    public string AliasCatalogFingerprint { get; init; } = "";
    public string? DataDragonVersion { get; init; }
    public List<LeagueVocabularyEntity> Champions { get; } = [];
    public List<LeagueVocabularyEntity> Abilities { get; } = [];
    public List<LeagueVocabularyEntity> Items { get; } = [];
    public List<LeagueVocabularyEntity> SummonerSpells { get; } = [];
    public string[] CommonTerms { get; init; } = [];
    public string[] RoleContext { get; init; } = [];
    internal IEnumerable<string> Terms =>
        Champions
            .Concat(Abilities)
            .Concat(Items)
            .Concat(SummonerSpells)
            .SelectMany(e => e.Terms)
            .Concat(CommonTerms);

    internal static string? InferGamePath(string audioPath, string? configured) =>
        configured is not null ? Path.GetFullPath(configured)
        : !string.IsNullOrWhiteSpace(audioPath)
        && Path.GetFileNameWithoutExtension(audioPath)
            .EndsWith("_Audio", StringComparison.OrdinalIgnoreCase)
            ? Path.ChangeExtension(Path.GetFullPath(audioPath), null)[..^6]
        : null;

    internal static async Task<LeagueMatchVocabulary> LoadAsync(
        string audioPath,
        TranscriptPostProcessingOptions options,
        Action<string>? log,
        CancellationToken ct
    )
    {
        var aliases = await LeagueAliasCatalog
            .LoadAsync(options.AliasesPath, log, ct)
            .ConfigureAwait(false);
        string? gamePath = InferGamePath(audioPath, options.GamePath);
        var championIds = new HashSet<int>();
        var championNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var itemIds = new HashSet<int>();
        var spellIds = new HashSet<int>();
        var spellNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (gamePath is not null)
            foreach (
                string suffix in new[]
                {
                    "_DraftData.json",
                    "_ParsedGameData.json",
                    "_ParsedPostGameData.json",
                }
            )
                if (await ReadOptional(gamePath + suffix).ConfigureAwait(false) is { } root)
                    Collect(root);
        string? contextPath = options.GameContextPath;
        if (contextPath is null && gamePath is not null)
            contextPath = new[]
            {
                gamePath + "_game_context.json",
                gamePath + "_Audio_game_context.json",
            }.FirstOrDefault(File.Exists);
        RoleGameContext? context = null;
        if (
            contextPath is not null
            && await ReadOptional(Path.GetFullPath(contextPath)).ConfigureAwait(false) is { } saved
        )
        {
            context = System.Text.Json.JsonSerializer.Deserialize<RoleGameContext>(
                saved.ToString(),
                new System.Text.Json.JsonSerializerOptions(
                    System.Text.Json.JsonSerializerDefaults.Web
                )
            );
            if (context is not null)
            {
                context = aliases.Enrich(context);
                foreach (var roster in context.Roles.Values)
                {
                    if (roster.AlliedChampionId is > 0)
                        championIds.Add(roster.AlliedChampionId.Value);
                    if (roster.OpposingChampionId is > 0)
                        championIds.Add(roster.OpposingChampionId.Value);
                    if (roster.AlliedChampion is not null)
                        championNames.Add(roster.AlliedChampion);
                    if (roster.OpposingChampion is not null)
                        championNames.Add(roster.OpposingChampion);
                    foreach (var cue in roster.EndgameItems)
                        if (cue.Id is > 0)
                            itemIds.Add(cue.Id.Value);
                    foreach (var cue in roster.SummonerSpells)
                        spellNames.Add(cue.Name);
                }
            }
        }
        bool remote = options.DataDragonDirectory is null;
        string? catalog = options.DataDragonDirectory ?? RecordedRoleContext.FindCachedCatalog();
        // A missing/ambiguous role roster must not prevent vocabulary creation from raw match files.
        if (
            catalog is null
            && remote
            && (championIds.Count > 0 || championNames.Count > 0 || itemIds.Count > 0)
        )
            catalog = await RecordedRoleContext
                .FetchCatalogDirectoryAsync(ct)
                .ConfigureAwait(false);
        var championsJson = await RecordedRoleContext
            .ReadCatalogAsync(catalog, "champion.json", remote, ct)
            .ConfigureAwait(false);
        JObject[] championData =
            (championsJson?["data"] as JObject)
                ?.Properties()
                .Select(p => p.Value)
                .OfType<JObject>()
                .ToArray()
            ?? [];
        var selected = new Dictionary<int, (string Name, string? Key)>();
        foreach (int id in championIds.Order())
        {
            var entry = championData.FirstOrDefault(c => Int(c["key"]) == id);
            var fallback = aliases.Champion(id);
            string? name = Str(entry?["name"]) ?? fallback?.Name;
            if (name is not null)
                selected[id] = (name, Str(entry?["id"]) ?? fallback?.Key);
        }
        var unresolved = new List<string>();
        foreach (string name in championNames.Order(StringComparer.OrdinalIgnoreCase))
        {
            var entry = championData.FirstOrDefault(c =>
                LeagueAliasCatalog.Key(Str(c["name"])) == LeagueAliasCatalog.Key(name)
                || LeagueAliasCatalog.Key(Str(c["id"])) == LeagueAliasCatalog.Key(name)
            );
            var fallback = aliases.Champion(null, name);
            int id = Int(entry?["key"]);
            if (id == 0)
                id = fallback?.Id ?? 0;
            if (id > 0)
                selected.TryAdd(
                    id,
                    (
                        Str(entry?["name"]) ?? fallback?.Name ?? name,
                        Str(entry?["id"]) ?? fallback?.Key
                    )
                );
            else
                unresolved.Add(name);
        }
        var result = new LeagueMatchVocabulary
        {
            AliasCatalogFilename = aliases.Filename,
            AliasCatalogFingerprint = aliases.Fingerprint,
            DataDragonVersion = Str(championsJson?["version"]),
            CommonTerms = aliases.CommonTerms.ToArray(),
            RoleContext =
                context
                    ?.Roles.Select(p =>
                        $"{p.Key}: {p.Value.AlliedChampion ?? "unknown"} vs {p.Value.OpposingChampion ?? "unknown"}"
                    )
                    .ToArray()
                ?? [],
        };
        var detailTasks = selected.ToDictionary(
            p => p.Key,
            p =>
                string.IsNullOrWhiteSpace(p.Value.Key)
                || !p.Value.Key.All(char.IsAsciiLetterOrDigit)
                    ? Task.FromResult<JObject?>(null)
                    : RecordedRoleContext.ReadCatalogAsync(
                        catalog,
                        $"champion/{p.Value.Key}.json",
                        remote,
                        ct
                    )
        );
        var itemTask =
            itemIds.Count == 0
                ? Task.FromResult<JObject?>(null)
                : RecordedRoleContext.ReadCatalogAsync(catalog, "item.json", remote, ct);
        await Task.WhenAll(detailTasks.Values.Append(itemTask)).ConfigureAwait(false);
        foreach (var (id, champion) in selected)
        {
            var entry = aliases.Champion(id, champion.Name);
            var alliedRole = context?.Roles.FirstOrDefault(p => p.Value.AlliedChampionId == id).Key;
            var enemyRole = context
                ?.Roles.FirstOrDefault(p => p.Value.OpposingChampionId == id)
                .Key;
            result.Champions.Add(
                Entity(id.ToString(), champion.Name, entry) with
                {
                    Team =
                        alliedRole is not null ? "ally"
                        : enemyRole is not null ? "enemy"
                        : null,
                    Role = alliedRole ?? enemyRole,
                }
            );
            JObject? details = (await detailTasks[id].ConfigureAwait(false))?["data"] as JObject;
            JObject? detail = details
                ?.Properties()
                .Select(p => p.Value)
                .OfType<JObject>()
                .FirstOrDefault();
            var abilities = (detail?["spells"] as JArray)?.OfType<JObject>().ToArray() ?? [];
            for (int i = 0; i < abilities.Length; i++)
            {
                string? name = Str(abilities[i]["name"]);
                if (name is null)
                    continue;
                string? abilityId = Str(abilities[i]["id"]);
                string? slot = i < 4 ? "QWER"[i].ToString() : null;
                result.Abilities.Add(
                    Entity(abilityId, name, aliases.Ability(id, abilityId, name, slot), id, slot)
                );
            }
            if (detail?["passive"] is JObject passive && Str(passive["name"]) is { } passiveName)
            {
                string passiveId = champion.Key + ":passive";
                result.Abilities.Add(
                    Entity(
                        passiveId,
                        passiveName,
                        aliases.Ability(id, passiveId, passiveName, "P"),
                        id,
                        "P"
                    )
                );
            }
            // The bundled catalog also makes this usable offline, without per-champion cache files.
            if (detail is null)
                result.Abilities.AddRange(
                    aliases.Abilities(id).Select(a => Entity(a.Id, a.Name, a, id, a.Slot))
                );
        }
        result.Champions.AddRange(unresolved.Select(name => Entity(null, name, null)));
        JObject? items = (await itemTask.ConfigureAwait(false))?["data"] as JObject;
        foreach (int id in itemIds.Order())
        {
            var entry = aliases.Item(id);
            string? name = Str(items?[id.ToString()]?["name"]) ?? entry?.Name;
            if (name is not null)
                result.Items.Add(Entity(id.ToString(), name, entry));
        }
        foreach (int id in spellIds.Order())
            if (aliases.Summoner(id) is { } spell)
                result.SummonerSpells.Add(Entity(id.ToString(), spell.Name, spell));
        foreach (string name in spellNames.Order(StringComparer.OrdinalIgnoreCase))
        {
            var spell = aliases.Summoner(null, name);
            if (
                !result.SummonerSpells.Any(s =>
                    LeagueAliasCatalog.Key(s.Name) == LeagueAliasCatalog.Key(spell?.Name ?? name)
                )
            )
                result.SummonerSpells.Add(Entity(spell?.Id.ToString(), spell?.Name ?? name, spell));
        }
        // Manually supplied role contexts may contain custom names absent from Data Dragon.
        if (context is not null)
            foreach (var roster in context.Roles.Values)
            {
                foreach (var cue in roster.AlliedAbilityCues)
                    if (
                        !result.Abilities.Any(a =>
                            a.ChampionId == roster.AlliedChampionId
                            && LeagueAliasCatalog.Key(a.Name) == LeagueAliasCatalog.Key(cue.Ability)
                        )
                    )
                        result.Abilities.Add(
                            new LeagueVocabularyEntity(cue.Id, cue.Ability, cue.Aliases)
                            {
                                ChampionId = roster.AlliedChampionId,
                                Slot = cue.Slot,
                            }
                        );
                foreach (var cue in roster.EndgameItems)
                    if (
                        !result.Items.Any(i =>
                            LeagueAliasCatalog.Key(i.Name) == LeagueAliasCatalog.Key(cue.Name)
                        )
                    )
                        result.Items.Add(
                            Entity(cue.Id?.ToString(), cue.Name, aliases.Item(cue.Id, cue.Name))
                        );
            }
        log?.Invoke(
            $"Match vocabulary: {result.Champions.Count} champions, {result.Abilities.Count} abilities, {result.Items.Count} observed items, {result.CommonTerms.Length} common terms."
        );
        return result;

        LeagueVocabularyEntity Entity(
            string? id,
            string name,
            EntityAlias? entry,
            int? championId = null,
            string? slot = null
        ) =>
            new(
                id,
                name,
                (championId is > 0
                    ? aliases.AbilityNames(championId, aliases.Champion(championId)?.Name, id, name, slot)
                    : aliases.Names(entry, name))
                    .Where(n => !n.Equals(name, StringComparison.OrdinalIgnoreCase))
                    .ToArray()
            )
            {
                ChampionId = championId,
                Slot = slot,
                // Spell shorthand must not expand into a formal ability name.
                PostProcessingAliases = entry?.PostProcessingAliases ?? (championId is > 0 ? [] : null),
            };

        async Task<JObject?> ReadOptional(string path)
        {
            try
            {
                return JObject.Parse(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }
            catch (Exception ex)
                when (ex
                        is IOException
                            or UnauthorizedAccessException
                            or Newtonsoft.Json.JsonException
                )
            {
                log?.Invoke(
                    $"Could not read match vocabulary context {Path.GetFileName(path)}: {ex.Message}"
                );
                return null;
            }
        }
        void Collect(
            JToken token,
            string? property = null,
            bool inItems = false,
            bool inSpells = false
        )
        {
            if (token is JObject obj)
            {
                foreach (var p in obj.Properties())
                    Collect(
                        p.Value,
                        p.Name,
                        inItems || property == "items",
                        inSpells || property == "summonerSpells"
                    );
            }
            else if (token is JArray array)
            {
                foreach (var child in array)
                    Collect(child, property, inItems || property == "items", inSpells);
            }
            else if (property is "championId" or "allyPicks" or "enemyPicks")
                AddId(championIds, token);
            else if (property is "itemID" or "itemId" || inItems && property is ("id" or "items"))
                AddId(itemIds, token);
            else if (
                property is { Length: 5 }
                && property.StartsWith("ITEM", StringComparison.OrdinalIgnoreCase)
                && property[4] is >= '0' and <= '6'
            )
                AddId(itemIds, token);
            else if (property is "spell1Id" or "spell2Id")
                AddId(spellIds, token);
            else if (inSpells && property is "displayName" && Str(token) is { } spell)
                spellNames.Add(spell);
            else if (property is "championName" && Str(token) is { } champion)
                championNames.Add(champion);
        }
        static void AddId(HashSet<int> ids, JToken token)
        {
            int id = Int(token);
            if (id > 0)
                ids.Add(id);
        }
    }

    /// <summary>A small prioritized prompt. The character cap is a heuristic; Whisper applies its native token limit.</summary>
    internal string BuildPrompt(string intro, IEnumerable<string> extraTerms, int maxCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, 256);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxCharacters, 2000);
        var prompt = new StringBuilder(intro);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sections = new List<string>();
        int remaining = maxCharacters - prompt.Length;
        // Reserve room for basic slang even when champion names are unusually long.
        var orderedChampions = Champions
            .OrderBy(c =>
                c.Team == "ally" ? 0
                : c.Team == "enemy" ? 1
                : 2
            )
            .ThenBy(c => Array.IndexOf(SpeakerRoleScorer.Roles, c.Role));
        AddSection(
            " Champions: ",
            orderedChampions.Select(c =>
                Format(c, 1) + (c.Role is null ? "" : $" {c.Team} {c.Role}")
            ),
            Math.Max(0, remaining - 140)
        );
        AddSection(" Common: ", CommonTerms, Math.Min(115, maxCharacters - prompt.Length));
        AddSection(
            " Spells: ",
            SummonerSpells.Select(s => s.Name == "Teleport" ? Format(s, 1) : s.Name),
            Math.Min(85, maxCharacters - prompt.Length)
        );
        AddSection(" Extra: ", extraTerms, Math.Min(80, maxCharacters - prompt.Length));
        // Fairly interleave items and ability nicknames. A long item list cannot starve abilities.
        // One ability alias per champion per round, so champions later in the catalog also get space.
        var byChampion = orderedChampions
            .Where(c => int.TryParse(c.Id, out _))
            .Select(c =>
                Abilities
                    .Where(a => a.ChampionId?.ToString() == c.Id)
                    .OrderBy(a => a.Slot == "R" ? 0 : a.Slot == "P" ? 2 : 1)
                    .SelectMany(a => a.Slot == "P" ? Array.Empty<string>() : a.Aliases.Take(1))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            )
            .ToArray();
        int rounds = byChampion.Select(g => g.Length).DefaultIfEmpty().Max();
        var abilityTerms = Enumerable
            .Range(0, rounds)
            .SelectMany(i => byChampion.Where(g => i < g.Length).Select(g => g[i]))
            .ToArray();
        var itemTerms = Items.Select(i => i.Aliases.FirstOrDefault() ?? i.Name).ToArray();
        var relevant = Enumerable
            .Range(0, Math.Max(abilityTerms.Length, itemTerms.Length))
            .SelectMany(i =>
                new[]
                {
                    i < abilityTerms.Length ? abilityTerms[i] : null,
                    i < itemTerms.Length ? itemTerms[i] : null,
                }
            )
            .OfType<string>()
            .Concat(Items.Select(i => i.Name));
        AddSection(" Match: ", relevant, maxCharacters - prompt.Length);
        // whisper.cpp retains the tail of an oversized carried prompt. Keep the most important
        // champion/role context and basic slang at that end, while selecting them first above.
        return intro + string.Concat(sections.AsEnumerable().Reverse());

        static string Format(LeagueVocabularyEntity entity, int count) =>
            entity.Aliases.Length == 0
                ? entity.Name
                : entity.Name + " (" + string.Join(", ", entity.Aliases.Take(count)) + ")";
        void AddSection(string label, IEnumerable<string> terms, int budget)
        {
            int start = prompt.Length;
            bool added = false;
            foreach (string term in terms)
            {
                if (string.IsNullOrWhiteSpace(term) || used.Contains(term))
                    continue;
                string prefix = added ? ", " : label;
                if (
                    prompt.Length + prefix.Length + term.Length + 1 > maxCharacters
                    || prompt.Length - start + prefix.Length + term.Length + 1 > budget
                )
                    continue;
                prompt.Append(prefix).Append(term);
                used.Add(term);
                added = true;
            }
            if (added)
            {
                prompt.Append('.');
                sections.Add(prompt.ToString(start, prompt.Length - start));
            }
        }
    }

    private static int Int(JToken? token) =>
        token is JValue value && int.TryParse(value.ToString(), out int id) ? id : 0;

    private static string? Str(JToken? token) =>
        token?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)token)
            ? ((string)token!).Trim()
            : null;
}

internal sealed record LeagueVocabularyEntity(string? Id, string Name, string[] Aliases)
{
    public int? ChampionId { get; init; }
    public string? Slot { get; init; }
    public string[]? PostProcessingAliases { get; init; }
    public string? Team { get; init; }
    public string? Role { get; init; }
    internal IEnumerable<string> Terms => new[] { Name }.Concat(Aliases);
}

#endregion

#region Guarded alias corrections

/// <summary>Turns only match-relevant aliases into guarded cleanup rules.</summary>
internal static class LeagueAliasPostProcessing
{
    private static readonly HashSet<string> Generic = new(StringComparer.Ordinal)
    {
        "q",
        "w",
        "e",
        "r",
        "p",
        "ult",
        "ulti",
        "ultimate",
        "passive",
        "ability",
        "spell",
        "stun",
        "root",
        "slow",
        "dash",
        "shield",
        "heal",
        "knockup",
        "knock up",
        "ball",
        "balls",
        "chain",
        "chains",
        "wave",
        "wall",
        "box",
        "boxes",
        "mist",
        "eat",
        "pool",
        "orb",
        "gate",
        "staff",
        "control",
        "pot",
        "tear",
        "edge",
        "void",
        "infinity",
    };
    private static readonly HashSet<string> NounPrefixes = new(StringComparer.Ordinal)
    {
        "my",
        "your",
        "his",
        "her",
        "our",
        "their",
        "its",
        "a",
        "an",
        "the",
        "this",
        "that",
        "have",
        "has",
        "had",
        "got",
        "get",
        "bought",
        "buy",
        "built",
        "build",
        "with",
        "without",
        "used",
        "use",
        "using",
        "cast",
        "casts",
        "casting",
        "land",
        "landed",
        "hit",
        "missed",
        "no",
    };
    private static readonly HashSet<string> Availability = new(StringComparer.Ordinal)
    {
        "is",
        "isn't",
        "was",
        "wasn't",
        "ready",
        "up",
        "down",
        "cooldown",
        "cooldowns",
        "cd",
    };

    internal static IEnumerable<TranscriptCorrectionRule> BuildRules(LeagueMatchVocabulary match)
    {
        var entities = match
            .Champions.Select(e => (Entity: e, Kind: "champion"))
            .Concat(match.Abilities.Select(e => (Entity: e, Kind: "ability")))
            .Concat(match.Items.Select(e => (Entity: e, Kind: "item")))
            .Concat(match.SummonerSpells.Select(e => (Entity: e, Kind: "spell")))
            .ToArray();
        var common = match.CommonTerms.Select(PhraseKey).ToHashSet(StringComparer.Ordinal);
        // Protect canonical names of other match entities, even if they have no correction aliases.
        var canonicalOwners = entities
            .GroupBy(e => PhraseKey(e.Entity.Name), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g =>
                    g.Select(e => e.Entity.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
            );
        foreach (var (entity, kind) in entities)
        {
            string canonical = PhraseKey(entity.Name);
            string[] eligible = (entity.PostProcessingAliases ?? entity.Aliases)
                .Where(a =>
                    PhraseKey(a) is { Length: > 0 } key
                    && key != canonical
                    && !Generic.Contains(key)
                    && !common.Contains(key)
                    && (
                        !canonicalOwners.TryGetValue(key, out var owners)
                        || owners.All(n => PhraseKey(n) == canonical)
                    )
                    && !IsInflectedVerb(a, kind)
                )
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (eligible.Length == 0)
                continue;
            yield return new TranscriptCorrectionRule
            {
                Canonical = entity.Name,
                Aliases = eligible,
                SpokenForms = entity.Aliases,
                RequireEvidence = true,
                FromAliasCatalog = true,
                CatalogKind = kind,
            };
        }
    }

    internal static Dictionary<string, string[]> AliasOwners(LeagueMatchVocabulary match) =>
        match
            .Champions.Concat(match.Abilities)
            .Concat(match.Items)
            .Concat(match.SummonerSpells)
            .SelectMany(e =>
                e.Terms.Concat(e.PostProcessingAliases ?? [])
                    .Select(a => (Alias: PhraseKey(a), e.Name))
            )
            .Where(p => p.Alias.Length > 0)
            .GroupBy(p => p.Alias, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.Ordinal
            );

    internal static bool HasNounContext(List<WordResult> words, int start, int count)
    {
        int last = start + count - 1;
        if (
            start > 0
            && words[start - 1].Speaker == words[start].Speaker
            && words[start].Start - words[start - 1].End <= 0.65
            && !TranscriptPostProcessor.EndsSentence(words[start - 1])
            && NounPrefixes.Contains(TranscriptPostProcessor.Key(words[start - 1].Word))
        )
            return true;
        return last + 1 < words.Count
            && words[last + 1].Speaker == words[last].Speaker
            && words[last + 1].Start - words[last].End <= 0.65
            && !TranscriptPostProcessor.EndsSentence(words[last])
            && Availability.Contains(TranscriptPostProcessor.Key(words[last + 1].Word));
    }

    private static bool IsInflectedVerb(string alias, string kind)
    {
        if (kind is not ("ability" or "spell") || alias.Any(char.IsWhiteSpace))
            return false;
        string key = TranscriptPostProcessor.Key(alias);
        return key.EndsWith("ed", StringComparison.Ordinal)
            || key.EndsWith("ing", StringComparison.Ordinal)
            || kind == "spell" && key.EndsWith('s');
    }

    private static string PhraseKey(string value) =>
        string.Join(
            ' ',
            Regex
                .Split(value.Trim(), @"\s+")
                .Select(TranscriptPostProcessor.Key)
                .Where(k => k.Length > 0)
        );
}

#endregion

#region Speech misspellings

internal static class LeagueSpeechVocabulary
{
    // Misspellings are corrected only when this champion is supported by the recording.
    internal static readonly Dictionary<string, string[]> Misspellings = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["Corki"] = ["corky"],
        ["Wukong"] = ["wokong", "wu kong"],
        ["K'Sante"] = ["casante", "cassante", "kassante", "kasante", "ksante"],
        ["Ornn"] = ["orn", "orna"],
        ["Naafiri"] = ["nefiri", "nefiry", "nafiri", "naffiri"],
        ["Malphite"] = ["malfight", "malphight"],
        ["Ezreal"] = ["ezrael"],
        ["Milio"] = ["millio", "melio"],
        ["Taliyah"] = ["taliah", "taliya"],
    };

    // Spoken aliases live in league_aliases.json. These conservative spelling corrections remain separate.
    internal static IEnumerable<string> SpokenForms(string name) => new[] { name };
}

#endregion
