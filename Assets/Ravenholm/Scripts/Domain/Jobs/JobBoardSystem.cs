using System;
using System.Collections.Generic;
using Ravenholm.Domain.Rng;
using Ravenholm.Domain.Sites;

namespace Ravenholm.Domain.Jobs
{
    public enum ContractorStatus
    {
        Available,
        OnExpedition,
        Injured,
        Dead,
    }

    public enum Willingness
    {
        Neutral,
        Cautious,
        Bold,
    }

    /// <summary>A persistent named independent contractor (GDD 10). Not an employee.</summary>
    public sealed class ContractorRecord
    {
        public string Id = "";
        public string Name = "";
        public string ClassId = "";
        public int Level = 1;
        public int Xp;
        public readonly List<string> Traits = new List<string>();
        public double PersonalityPrice = 1.0;
        public ContractorStatus Status;
        public long InjuredUntilMs;
        public bool Sponsored;
        public int Skin;
        public int Jobs;
        public int Successes;
        public string LastNote = "";
    }

    public sealed class ApplicantRecord
    {
        public string ContractorId = "";
        public int Fee;
        public long ArriveMs;
    }

    public sealed class PostingRecord
    {
        public string QuestId = "";
        public long CreatedMs;
        public ulong WindowSeed;
        public readonly List<ApplicantRecord> Applicants = new List<ApplicantRecord>();
        /// <summary>Why nobody applied (empty when someone did).</summary>
        public string NoApplicantReason = "";
    }

    /// <summary>
    /// A dispatched expedition. Everything the resolver needs is frozen here at dispatch (GDD 11/12):
    /// later tool upgrades or reputation changes never alter an expedition underway.
    /// </summary>
    public sealed class ExpeditionRecord
    {
        public string Id = "";
        public string QuestId = "";
        public string ContractorId = "";
        public int FeeQuoted;
        public int FeePaid;
        public bool Sponsored;
        public readonly List<string> Items = new List<string>();
        public long StartMs;
        public long EndMs;
        public ulong Seed;
        public LoadoutStats Snapshot = new LoadoutStats();
        public readonly List<string> ThreatIds = new List<string>();
        public string ForecastBand = "";
        /// <summary>Phase 4 fixture: a departure that never resolves (debug-resettable).</summary>
        public bool TestDeparture;
        // Frozen resolution inputs (GDD 23: in-flight quests use their dispatch snapshot and resolver version).
        public int Difficulty;
        public readonly List<ThreatSpec> Threats = new List<ThreatSpec>();
        public readonly List<CarriedPotion> Potions = new List<CarriedPotion>();
        public int ResolverVersion = Resolver.Version;
    }

    public sealed class QuestProgress
    {
        public int Attempts;
        public int Successes;
        public bool Unlocked;
        public readonly SortedSet<string> RevealedThreats = new SortedSet<string>(StringComparer.Ordinal);
    }

    /// <summary>Applied, immutable result (GDD 13). Acknowledging it is presentation only.</summary>
    public sealed class ResultRecord
    {
        public string ExpeditionId = "";
        public string QuestId = "";
        public string ContractorId = "";
        public string ContractorName = "";
        public Outcome Outcome;
        public bool Forced;
        public bool Sponsored;
        public long CompletedMs;
        public int GoldEarned;
        public int FeePaid;
        public readonly List<string> Materials = new List<string>();      // "resource.x:2"
        public readonly List<string> ReturnedItems = new List<string>();  // item definition IDs
        public readonly List<string> ConsumedItems = new List<string>();
        public readonly List<string> LostItems = new List<string>();
        public string BlueprintGranted;
        public bool SubstituteGranted;
        public int ReputationDeltaTenths;
        public bool LeveledUp;
        public int NewLevel;
        public bool Injured;
        public bool Died;
        public readonly List<string> RevealedThreats = new List<string>();
        public readonly List<string> QuestsUnlocked = new List<string>();
        public readonly List<string> Contributors = new List<string>();
        public double J, K, A, E, V;
        public bool Acknowledged;
    }

    public sealed class JobsState
    {
        public long SimMs;
        public int Gold;
        /// <summary>Reputation in tenths, 0..1000 (GDD 14: fractional recovery, integer display).</summary>
        public int ReputationTenths;
        public readonly SortedDictionary<string, ContractorRecord> Contractors = new SortedDictionary<string, ContractorRecord>(StringComparer.Ordinal);
        public PostingRecord Posting;
        public readonly List<ExpeditionRecord> Expeditions = new List<ExpeditionRecord>();
        public readonly SortedDictionary<string, int> WindowIndex = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public long NextExpeditionNumber = 1;
        public int GeneratedContractors;
        public bool FirstDispatchDone;
        public readonly SortedDictionary<string, QuestProgress> Progress = new SortedDictionary<string, QuestProgress>(StringComparer.Ordinal);
        /// <summary>Most recent last; bounded.</summary>
        public readonly List<ResultRecord> Results = new List<ResultRecord>();
        /// <summary>DEBUG: forces the next resolved expedition's outcome (clearly marked on the result).</summary>
        public Outcome? ForcedNext;

        public int Reputation => (int)Math.Round(ReputationTenths / 10.0, MidpointRounding.AwayFromZero);
    }

    // ---------------------------------------------------------------------- content and ledger seams

    public sealed class ClassInfo
    {
        public string Id = "";
        public string Name = "";
        public int Might, Survival, Insight, GainPerLevel = 1, BaseFee = 8;
        public readonly List<string> WeaponTags = new List<string>();
        public readonly List<KeyValuePair<string, int>> Counters = new List<KeyValuePair<string, int>>();
        public bool BonusRareRoll;
    }

    public sealed class TraitInfo
    {
        public string Id = "";
        public string Name = "";
        public Willingness Willingness;
        public readonly List<KeyValuePair<string, int>> Counters = new List<KeyValuePair<string, int>>();
    }

    public sealed class QuestInfo
    {
        public string Id = "";
        public string Name = "";
        public int Difficulty;
        public int DurationMs;
        public bool Repeatable = true;
        public bool Recovery;
        public List<ThreatSpec> Threats = new List<ThreatSpec>();
        public int BaseGold;
        public int PrestigeTarget = 20;
        public int ReputationGate;
        public int MapTier;
        public readonly List<string> Prerequisites = new List<string>();
        public readonly List<KeyValuePair<string, int>> SuccessMaterials = new List<KeyValuePair<string, int>>();
        public readonly List<KeyValuePair<string, int>> GreatBonusMaterials = new List<KeyValuePair<string, int>>();
        public readonly List<KeyValuePair<string, int>> PartialMaterials = new List<KeyValuePair<string, int>>();
        public readonly List<KeyValuePair<string, int>> SubstituteMaterials = new List<KeyValuePair<string, int>>();
        public string FirstSuccessBlueprint;
        public string RareResource;
        public double RareChance;

        // Phase 7: map region, campaign chain and dispatch requirements (GDD 14).
        public string Region = "";
        public int CampaignStep;
        public bool Finale;
        /// <summary>(quest, threat): on first success this quest removes that threat from that later quest.</summary>
        public readonly List<KeyValuePair<string, string>> Prepares = new List<KeyValuePair<string, string>>();
        public int MinLevel;
        public int DispatchReputation;
        public int MinWeaponPower;
        public int MinArmorProtection;
        public bool RequireCounterPotion;
        /// <summary>View only: threats this company's earlier legs have already dealt with.</summary>
        public List<string> PreparedThreats = new List<string>();

        public QuestInfo CloneShallow()
        {
            var q = (QuestInfo)MemberwiseClone();
            return q;
        }
    }

    public enum ItemKind { Weapon, Armor, Potion }

    public sealed class ItemInfo
    {
        public string Id = "";
        public ItemKind Kind;
        public int Power, Protection;
        public string WeaponTag = "";
        public string CounterTag = "";
        public int CounterStrength;
        /// <summary>Potions: survival margin (Recovery Tonic) or counter (Antidote, Warding Draught).</summary>
        public int SurvivalBonus;
    }

    /// <summary>Blueprints and shared materials that quest rewards grant.</summary>
    public interface IRewardLedger
    {
        bool OwnsBlueprint(string blueprintId);
        void GrantBlueprint(string blueprintId);
        void ProduceShared(string resourceId, int quantity, string reason);
    }

    public interface IJobContent
    {
        IReadOnlyList<string> QuestIds { get; }
        ClassInfo Class(string id);
        TraitInfo Trait(string id);
        QuestInfo Quest(string id);
        ItemInfo Item(string definitionId);
        IReadOnlyList<string> GeneratableClasses { get; }
        IReadOnlyList<string> GeneratableTraits { get; }
        IReadOnlyList<string> GivenNames { get; }
        IReadOnlyList<string> FamilyNames { get; }
    }

    /// <summary>Store item custody for loadouts and expeditions. Implemented by the logistics system.</summary>
    public interface IGearLedger
    {
        IReadOnlyList<string> StoreItems { get; }
        string DefinitionOfItem(string instanceId);
        bool IsReservedByOther(string instanceId, string owner);
        /// <summary>Atomically replaces <paramref name="owner"/>'s reservations, or changes nothing.</summary>
        bool Reserve(string owner, IReadOnlyList<string> instanceIds);
        void ReleaseReservations(string owner);
        /// <summary>
        /// Moves items out of the Store (all or nothing). Items reserved by anyone other than
        /// <paramref name="owner"/> cannot be taken.
        /// </summary>
        bool TakeFromStore(IReadOnlyList<string> instanceIds, string owner, string reason);
        void ReturnToStore(IReadOnlyList<string> instanceIds, string reason);
        /// <summary>Permanently destroys items held outside the Store (expedition loss, consumed potions).</summary>
        void DestroyItems(IReadOnlyList<string> instanceIds, string reason);
    }

    public sealed class Loadout
    {
        public string Weapon;
        public string Armor;
        public string Potion1;
        public string Potion2;

        public IReadOnlyList<string> All()
        {
            var l = new List<string>();
            foreach (var x in new[] { Weapon, Armor, Potion1, Potion2 }) if (!string.IsNullOrEmpty(x)) l.Add(x);
            return l;
        }
    }

    public sealed class JobBoardConfig
    {
        public ResolverCoefficients Resolver = new ResolverCoefficients();
        public FeeCoefficients Fees = new FeeCoefficients();
        public int ActiveSlots = 2;
        public int ApplicantsBase = 2, ThirdAt = 30, FourthAt = 60;
        public int ArrivalMinMs = 10000, ArrivalMaxMs = 30000;
        public int PoolTarget = 12;
        public int LevelCap = 10;
        public int XpPerLevel = 3;
        public long InjuryMs = 10 * 60 * 1000;
        public int ResultHistory = 30;
    }

    /// <summary>
    /// Job Board and hiring (GDD 10/11). The player posts a quest and never sets a reward; independent
    /// contractors decide whether to apply and quote their own fee. Dispatch is one atomic commit.
    /// </summary>
    public sealed class JobBoardSystem
    {
        public const string LoadoutOwner = "loadout";

        private readonly JobsState _s;
        private readonly IJobContent _content;
        private readonly IGearLedger _gear;
        private readonly JobBoardConfig _cfg;
        private readonly ulong _worldSeed;
        private readonly IRewardLedger _rewards;

        public JobBoardSystem(JobsState state, IJobContent content, IGearLedger gear, JobBoardConfig config, ulong worldSeed, IRewardLedger rewards = null)
        {
            _s = state;
            _content = content;
            _gear = gear;
            _cfg = config;
            _worldSeed = worldSeed;
            _rewards = rewards;
            RefreshUnlocks();
        }

        /// <summary>(result) after an expedition's consequences were applied, exactly once.</summary>
        public event Action<ResultRecord> Completed;

        // ------------------------------------------------------------------ knowledge and the map (GDD 13/14)

        public QuestProgress ProgressOf(string questId)
        {
            if (!_s.Progress.TryGetValue(questId, out var p)) _s.Progress[questId] = p = new QuestProgress();
            return p;
        }

        /// <summary>The quest as the company currently knows it: revealed threats become known.</summary>
        public QuestInfo Quest(string id)
        {
            var q = _content.Quest(id);
            if (q == null) return null;
            var view = q.CloneShallow();
            var threats = new List<ThreatSpec>();
            _s.Progress.TryGetValue(id, out var prog);
            var prepared = PreparedThreatsFor(id);
            view.PreparedThreats = prepared;
            foreach (var t in q.Threats)
            {
                if (prepared.Contains(t.Id)) continue;   // a completed preparation leg removed it for good
                var known = t.Known || (prog != null && prog.RevealedThreats.Contains(t.Id));
                threats.Add(new ThreatSpec
                {
                    Id = t.Id, CounterTag = t.CounterTag, Required = t.Required, UnmetReadiness = t.UnmetReadiness, UnmetSurvival = t.UnmetSurvival,
                    ArmorThreshold = t.ArmorThreshold, ArmorReadiness = t.ArmorReadiness, ArmorSurvival = t.ArmorSurvival, Known = known,
                });
            }
            view.Threats = threats;
            return view;
        }

        /// <summary>Threats removed from <paramref name="questId"/> by preparation legs that have succeeded.</summary>
        public List<string> PreparedThreatsFor(string questId)
        {
            var r = new List<string>();
            foreach (var other in _content.QuestIds)
            {
                if (!_s.Progress.TryGetValue(other, out var p) || p.Successes == 0) continue;
                foreach (var kv in _content.Quest(other).Prepares)
                    if (kv.Key == questId && !r.Contains(kv.Value)) r.Add(kv.Value);
            }
            return r;
        }

        /// <summary>The campaign breadcrumb: the earliest main-chain quest not yet succeeded (null when complete or none).</summary>
        public string CampaignObjective()
        {
            string best = null;
            var bestStep = int.MaxValue;
            foreach (var id in _content.QuestIds)
            {
                var q = _content.Quest(id);
                if (q.CampaignStep <= 0 || q.CampaignStep >= bestStep) continue;
                if (_s.Progress.TryGetValue(id, out var p) && p.Successes > 0) continue;
                best = id;
                bestStep = q.CampaignStep;
            }
            return best;
        }

        public bool CampaignComplete
        {
            get
            {
                foreach (var id in _content.QuestIds)
                    if (_content.Quest(id).Finale && _s.Progress.TryGetValue(id, out var p) && p.Successes > 0) return true;
                return false;
            }
        }

        /// <summary>Quests unlock once their prerequisites have succeeded and the reputation gate was first reached; they stay unlocked.</summary>
        public void RefreshUnlocks()
        {
            foreach (var id in _content.QuestIds)
            {
                var q = _content.Quest(id);
                var p = ProgressOf(id);
                if (p.Unlocked) continue;
                var ok = _s.Reputation >= q.ReputationGate;
                foreach (var pre in q.Prerequisites)
                    if (!_s.Progress.TryGetValue(pre, out var pp) || pp.Successes == 0) ok = false;
                if (ok) p.Unlocked = true;
            }
        }

        public bool IsUnlocked(string questId) => _s.Progress.TryGetValue(questId, out var p) && p.Unlocked;

        public string LockReason(string questId)
        {
            var q = _content.Quest(questId);
            if (q == null || IsUnlocked(questId)) return null;
            var needs = new List<string>();
            foreach (var pre in q.Prerequisites)
                if (!_s.Progress.TryGetValue(pre, out var pp) || pp.Successes == 0) needs.Add(_content.Quest(pre)?.Name ?? pre);
            var text = needs.Count > 0 ? "Succeed at " + string.Join(" and ", needs) : "";
            if (_s.Reputation < q.ReputationGate) text += (text.Length > 0 ? ", and reach " : "Reach ") + $"reputation {q.ReputationGate}";
            return "Locked: " + text;
        }

        public JobsState State => _s;
        public JobBoardConfig Config => _cfg;

        public event Action Changed;
        public event Action<ExpeditionRecord> Dispatched;

        // ------------------------------------------------------------------ contractors

        public ContractorRecord Contractor(string id) => id != null && _s.Contractors.TryGetValue(id, out var c) ? c : null;

        public void AddContractor(ContractorRecord c) => _s.Contractors[c.Id] = c;

        /// <summary>Contractor stats at their level (level 1 base + gain per level).</summary>
        public LoadoutStats BaseStats(ContractorRecord c)
        {
            var cls = _content.Class(c.ClassId);
            var s = new LoadoutStats();
            if (cls == null) return s;
            var g = cls.GainPerLevel * (c.Level - 1);
            s.Might = cls.Might + g;
            s.Survival = cls.Survival + g;
            s.Insight = cls.Insight + g;
            foreach (var kv in cls.Counters) s.AddCounter(kv.Key, kv.Value);
            foreach (var t in c.Traits)
            {
                var ti = _content.Trait(t);
                if (ti != null) foreach (var kv in ti.Counters) s.AddCounter(kv.Key, kv.Value);
            }
            return s;
        }

        public Willingness WillingnessOf(ContractorRecord c)
        {
            foreach (var t in c.Traits)
            {
                var ti = _content.Trait(t);
                if (ti != null && ti.Willingness != Willingness.Neutral) return ti.Willingness;
            }
            return Willingness.Neutral;
        }

        /// <summary>The lowest risk word a contractor accepts (GDD 10: Bold Dangerous, Cautious Good, others Risky).</summary>
        public static RiskBand MinimumBand(Willingness w) =>
            w == Willingness.Bold ? RiskBand.Dangerous : w == Willingness.Cautious ? RiskBand.Good : RiskBand.Risky;

        public bool CanWield(ContractorRecord c, ItemInfo weapon)
        {
            var cls = _content.Class(c.ClassId);
            return cls != null && weapon != null && weapon.Kind == ItemKind.Weapon && cls.WeaponTags.Contains(weapon.WeaponTag);
        }

        /// <summary>Stats with a specific loadout applied (unknown or empty slots ignored).</summary>
        public LoadoutStats StatsWith(ContractorRecord c, Loadout l)
        {
            var s = BaseStats(c);
            var w = Info(l.Weapon);
            if (w != null && w.Kind == ItemKind.Weapon)
            {
                s.WeaponPower = w.Power;
                s.AddCounter(w.CounterTag, w.CounterStrength);
            }
            var a = Info(l.Armor);
            if (a != null && a.Kind == ItemKind.Armor)
            {
                s.ArmorProtection = a.Protection;
                s.AddCounter(a.CounterTag, a.CounterStrength);
            }
            // Duplicate potion effects do not stack (GDD 9).
            var seen = new HashSet<string>();
            foreach (var p in new[] { l.Potion1, l.Potion2 })
            {
                var pi = Info(p);
                if (pi == null || pi.Kind != ItemKind.Potion || !seen.Add(pi.Id)) continue;
                s.SurvivalPotionBonus += pi.SurvivalBonus;
                s.AddCounter(pi.CounterTag, pi.CounterStrength);
            }
            return s;
        }

        private ItemInfo Info(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;
            var def = _gear.DefinitionOfItem(instanceId);
            return def != null ? _content.Item(def) : null;
        }

        public ForecastResult Forecast(ContractorRecord c, Loadout l, QuestInfo q) =>
            ForecastCalculator.Evaluate(_cfg.Resolver, StatsWith(c, l), q.Difficulty, q.Threats);

        /// <summary>
        /// Screening estimate (GDD 10): innate capability plus the company's best compatible gear that is
        /// currently available. Not a guarantee that gear will still be there at dispatch.
        /// </summary>
        public ForecastResult ScreeningForecast(ContractorRecord c, QuestInfo q)
        {
            var l = BestAvailableLoadout(c, null);
            AddBestCounterPotions(l, q, null);   // potions are company gear too
            return Forecast(c, l, q);
        }

        /// <summary>Fills empty potion slots with available potions that counter this job's known threats (one per effect).</summary>
        public void AddBestCounterPotions(Loadout l, QuestInfo q, string owner)
        {
            var used = new HashSet<string>();
            foreach (var t in q.Threats)
            {
                if (!t.Known || string.IsNullOrEmpty(t.CounterTag) || (l.Potion1 != null && l.Potion2 != null)) continue;
                foreach (var inst in _gear.StoreItems)
                {
                    if (_gear.IsReservedByOther(inst, owner ?? "") || inst == l.Potion1 || inst == l.Potion2) continue;
                    var i = Info(inst);
                    if (i == null || i.Kind != ItemKind.Potion || i.CounterTag != t.CounterTag || !used.Add(i.Id)) continue;
                    if (l.Potion1 == null) l.Potion1 = inst; else l.Potion2 = inst;
                    break;
                }
            }
        }

        /// <summary>Strongest compatible, unreserved weapon and armor in the Store (potions left to the player).</summary>
        public Loadout BestAvailableLoadout(ContractorRecord c, string owner)
        {
            var l = new Loadout();
            int bestW = -1, bestA = -1;
            foreach (var inst in _gear.StoreItems)
            {
                if (_gear.IsReservedByOther(inst, owner ?? "")) continue;
                var i = Info(inst);
                if (i == null) continue;
                if (i.Kind == ItemKind.Weapon && CanWield(c, i) && i.Power > bestW) { bestW = i.Power; l.Weapon = inst; }
                if (i.Kind == ItemKind.Armor && i.Protection > bestA) { bestA = i.Protection; l.Armor = inst; }
            }
            return l;
        }

        // ------------------------------------------------------------------ posting

        public int ApplicantTarget =>
            _s.Reputation >= _cfg.FourthAt ? _cfg.ApplicantsBase + 2 : _s.Reputation >= _cfg.ThirdAt ? _cfg.ApplicantsBase + 1 : _cfg.ApplicantsBase;

        public int ActiveExpeditions => _s.Expeditions.Count;

        /// <summary>Why a quest cannot be posted now (null = it can).</summary>
        public string PostBlockedReason(string questId)
        {
            var q = Quest(questId);
            if (q == null) return "Unknown job";
            if (!IsUnlocked(questId)) return LockReason(questId);
            if (!q.Repeatable && _s.Progress.TryGetValue(questId, out var done) && done.Successes > 0) return "Completed: this expedition is done for good";
            if (_s.Posting != null) return "One open posting at a time: cancel or fill the current one";
            foreach (var e in _s.Expeditions)
                if (e.QuestId == questId) return q.Repeatable ? "Already running (one at a time for now)" : "Already underway";
            return null;
        }

        /// <summary>Posts a job. Posting is free; contractors decide whether to apply (GDD 11).</summary>
        public bool Post(string questId, out string failure)
        {
            failure = PostBlockedReason(questId);
            if (failure != null) return false;
            var q = Quest(questId);
            _s.WindowIndex.TryGetValue(questId, out var window);
            // The candidate window seed only advances when a posting is filled, so cancel/repost cannot reroll.
            var seed = RandomStreams.SplitMix64(_worldSeed ^ SiteMath.StableHash(questId) ^ ((ulong)(window + 1) * 0x9E3779B97F4A7C15UL));
            var p = new PostingRecord { QuestId = questId, CreatedMs = _s.SimMs, WindowSeed = seed };
            _s.Posting = p;
            SelectApplicants(p, q);
            Changed?.Invoke();
            return true;
        }

        public void CancelPosting()
        {
            if (_s.Posting == null) return;
            _s.Posting = null;
            _gear.ReleaseReservations(LoadoutOwner);
            Changed?.Invoke();
        }

        private void SelectApplicants(PostingRecord p, QuestInfo q)
        {
            var rng = new Pcg32(p.WindowSeed, 54);
            var target = ApplicantTarget;

            if (q.Recovery)
            {
                // The protected recovery contract always has a willing sponsored applicant (GDD 15).
                var sponsor = FindOrCreateSponsor();
                AddApplicant(p, q, sponsor, rng);
                return;
            }

            var interested = new List<ContractorRecord>();
            var anyAvailable = false;
            foreach (var c in _s.Contractors.Values)
            {
                if (c.Sponsored || c.Status != ContractorStatus.Available) continue;
                anyAvailable = true;
                if (IsInterested(c, q)) interested.Add(c);
            }

            // Grow the pool only when eligible availability requires it (GDD 11). An elite posting nobody alive
            // qualifies for may draw up to two seasoned newcomers, as seasoned as current reputation allows.
            if (q.MinLevel > 1 && !HasLevel(q.MinLevel) && SeasonedLevel() >= q.MinLevel)
                for (var n = 0; n < 2 && _s.Contractors.Count < _cfg.PoolTarget + 4; n++)
                {
                    var c = Generate();
                    c.Level = Math.Max(c.Level, q.MinLevel);
                    anyAvailable = true;
                    if (IsInterested(c, q)) interested.Add(c);
                }
            var guard = 0;
            while (interested.Count < target && _s.Contractors.Count < _cfg.PoolTarget && guard++ < 20)
            {
                var c = Generate();
                anyAvailable = true;
                if (IsInterested(c, q)) interested.Add(c);
            }

            // Deterministic pick from the window seed.
            for (var i = interested.Count - 1; i > 0; i--)
            {
                var j = rng.NextInt(0, i + 1);
                (interested[i], interested[j]) = (interested[j], interested[i]);
            }
            for (var i = 0; i < interested.Count && i < target; i++) AddApplicant(p, q, interested[i], rng);

            if (p.Applicants.Count == 0)
                p.NoApplicantReason = !anyAvailable
                    ? "Every contractor is away or recovering. Try again later, or post the Local Supply Survey."
                    : q.MinLevel > 1 && !HasLevel(q.MinLevel)
                        ? $"This expedition needs a contractor of level {q.MinLevel} or higher. Contractors level up through successful jobs, and a higher reputation attracts seasoned newcomers."
                    : _s.Reputation < 10
                        ? "Your reputation is too low for anyone to risk this. Easier jobs and the Local Supply Survey rebuild it."
                        : "Nobody will take this on: even with your best gear it looks too dangerous. Try an easier job or improve your stock.";
        }

        private bool IsInterested(ContractorRecord c, QuestInfo q)
        {
            if (c.Level < q.MinLevel) return false;   // only seasoned contractors answer an elite posting
            var band = ScreeningForecast(c, q).Band;
            return band != RiskBand.Suicidal && band >= MinimumBand(WillingnessOf(c));   // Suicidal candidates never apply
        }

        /// <summary>The most seasoned newcomer current reputation attracts: level 1 at 20, 6 at 70.</summary>
        public int SeasonedLevel() => Math.Min(_cfg.LevelCap, 1 + Math.Max(0, _s.Reputation - 20) / 10);

        private bool HasLevel(int level)
        {
            foreach (var c in _s.Contractors.Values) if (!c.Sponsored && c.Status != ContractorStatus.Dead && c.Level >= level) return true;
            return false;
        }

        private void AddApplicant(PostingRecord p, QuestInfo q, ContractorRecord c, IRandomStream rng)
        {
            var cls = _content.Class(c.ClassId);
            var fee = _cfg.Fees.Quote(cls?.BaseFee ?? 8, c.Level, q.Difficulty, c.PersonalityPrice, _s.ReputationTenths / 10.0);
            var arrive = p.CreatedMs + rng.NextInt(_cfg.ArrivalMinMs, _cfg.ArrivalMaxMs + 1);
            p.Applicants.Add(new ApplicantRecord { ContractorId = c.Id, Fee = fee, ArriveMs = arrive });
        }

        private ContractorRecord FindOrCreateSponsor()
        {
            foreach (var c in _s.Contractors.Values)
                if (c.Sponsored && c.Status == ContractorStatus.Available) return c;
            // A replacement sponsored applicant exists if the former one died or is away (GDD 15).
            var r = Generate();
            r.Sponsored = true;
            r.Name += " (sponsored)";
            return r;
        }

        /// <summary>Creates a new named contractor from the Applications stream (deterministic per world).</summary>
        public ContractorRecord Generate()
        {
            var rng = RandomStreams.Create(_worldSeed, RandomStreamId.Applications, (ulong)(_s.GeneratedContractors + 1) * 7919UL);
            _s.GeneratedContractors++;
            var given = _content.GivenNames;
            var family = _content.FamilyNames;
            var classes = _content.GeneratableClasses;
            var traits = _content.GeneratableTraits;
            var c = new ContractorRecord
            {
                Id = $"contractor.gen_{_s.GeneratedContractors:D3}",
                Name = given.Count > 0 && family.Count > 0 ? $"{given[rng.NextInt(0, given.Count)]} {family[rng.NextInt(0, family.Count)]}" : $"Contractor {_s.GeneratedContractors}",
                ClassId = classes.Count > 0 ? classes[rng.NextInt(0, classes.Count)] : "",
                // Reputation draws more seasoned newcomers (GDD 14): level 1 at 20, up to 6 at 70.
                Level = 1 + rng.NextInt(0, SeasonedLevel()),
                PersonalityPrice = Math.Round(0.9 + rng.NextDouble() * 0.25, 2),
                Skin = rng.NextInt(0, 3),
            };
            if (traits.Count > 0 && rng.NextDouble() < 0.6) c.Traits.Add(traits[rng.NextInt(0, traits.Count)]);
            _s.Contractors[c.Id] = c;
            return c;
        }

        // ------------------------------------------------------------------ time

        public void Advance(long ms)
        {
            if (ms <= 0) return;
            _s.SimMs += ms;
            // Completion-time order, then stable expedition ID (GDD 13).
            var due = _s.Expeditions.FindAll(e => !e.TestDeparture && e.EndMs <= _s.SimMs);
            due.Sort((a, b) => a.EndMs != b.EndMs ? a.EndMs.CompareTo(b.EndMs) : string.CompareOrdinal(a.Id, b.Id));
            foreach (var e in due) Complete(e);
            foreach (var c in _s.Contractors.Values)
                if (c.Status == ContractorStatus.Injured && _s.SimMs >= c.InjuredUntilMs) c.Status = ContractorStatus.Available;
            if (due.Count > 0) Changed?.Invoke();
        }

        /// <summary>Milliseconds until the next expedition completes (long.MaxValue if none). Used to order events.</summary>
        public long NextCompletionDelay()
        {
            var best = long.MaxValue;
            foreach (var e in _s.Expeditions)
                if (!e.TestDeparture) best = Math.Min(best, Math.Max(0, e.EndMs - _s.SimMs));
            return best;
        }

        /// <summary>DEBUG: brings every running expedition's end time to now (they resolve on the next tick).</summary>
        public void DebugFinishExpeditionsNow()
        {
            foreach (var e in _s.Expeditions) if (!e.TestDeparture && e.EndMs > _s.SimMs) e.EndMs = _s.SimMs;
        }

        /// <summary>
        /// Applies one expedition's consequences in a single unit of work (GDD 13/23): gold, gear return
        /// or loss, consumed potions, materials, blueprint (duplicate-safe), XP, injury or death, reputation,
        /// knowledge and unlocks, then the result record. The expedition ID is the idempotency key.
        /// </summary>
        public ResultRecord Complete(ExpeditionRecord e)
        {
            if (_s.Results.Exists(r => r.ExpeditionId == e.Id) || !_s.Expeditions.Contains(e)) return null;
            var q = Quest(e.QuestId);
            var c = Contractor(e.ContractorId);
            var rng = new Pcg32(e.Seed, 7);   // reward rolls: separate from the resolver's own draws

            var res = new ResultRecord
            {
                ExpeditionId = e.Id, QuestId = e.QuestId, ContractorId = e.ContractorId, ContractorName = c?.Name ?? e.ContractorId,
                CompletedMs = e.EndMs, FeePaid = e.FeePaid, Sponsored = e.Sponsored,
            };

            Outcome outcome;
            Resolution resolution = null;
            if (q != null && q.Recovery)
            {
                outcome = Outcome.Success;   // authored safe result, never the random formula (GDD 15)
                res.Contributors.Add("A safe survey: nothing on the road could go badly.");
            }
            else
            {
                var forced = _s.ForcedNext;
                _s.ForcedNext = null;
                resolution = Resolver.Resolve(_cfg.Resolver, e.Snapshot, e.Difficulty, e.Threats, e.Potions, e.Seed, forced);
                outcome = resolution.Outcome;
                res.Forced = resolution.Forced;
                res.J = resolution.J; res.K = resolution.K; res.A = resolution.A; res.E = resolution.E; res.V = resolution.V;
                res.Contributors.AddRange(resolution.Contributors);
            }
            res.Outcome = outcome;

            // Gold: floor(base × multiplier).
            res.GoldEarned = q == null ? 0 : (int)Math.Floor(q.BaseGold * Resolver.GoldMultiplier(outcome));
            _s.Gold += res.GoldEarned;

            // Gear.
            if (outcome == Outcome.Death)
            {
                foreach (var i in e.Items) res.LostItems.Add(_gear.DefinitionOfItem(i));
                _gear.DestroyItems(e.Items, $"lost with {res.ContractorName}");
            }
            else
            {
                var consumed = resolution != null ? resolution.ConsumedPotions : new List<string>();
                var back = new List<string>();
                foreach (var i in e.Items)
                {
                    if (consumed.Contains(i)) res.ConsumedItems.Add(_gear.DefinitionOfItem(i));
                    else { back.Add(i); res.ReturnedItems.Add(_gear.DefinitionOfItem(i)); }
                }
                var used = new List<string>(consumed);
                used.RemoveAll(x => !e.Items.Contains(x));
                _gear.DestroyItems(used, "potion consumed");
                _gear.ReturnToStore(back, "expedition return");
            }

            // Materials, blueprint and rare rolls.
            if (q != null && !q.Recovery && _rewards != null)
            {
                var success = outcome == Outcome.Success || outcome == Outcome.GreatSuccess;
                if (success) Grant(res, q.SuccessMaterials);
                if (outcome == Outcome.GreatSuccess) Grant(res, q.GreatBonusMaterials);
                if (outcome == Outcome.PartialInjury) Grant(res, q.PartialMaterials);
                var prog = ProgressOf(q.Id);
                if (success && prog.Successes == 0 && !string.IsNullOrEmpty(q.FirstSuccessBlueprint))
                {
                    // Resolved against current ownership at grant time; duplicates become the substitute (GDD 9).
                    if (_rewards.OwnsBlueprint(q.FirstSuccessBlueprint))
                    {
                        Grant(res, q.SubstituteMaterials);
                        res.SubstituteGranted = true;
                    }
                    else
                    {
                        _rewards.GrantBlueprint(q.FirstSuccessBlueprint);
                        res.BlueprintGranted = q.FirstSuccessBlueprint;
                    }
                }
                var rolls = (outcome == Outcome.GreatSuccess ? 1 : 0) + (success && HasBonusRareRoll(c) ? 1 : 0);
                for (var i = 0; i < rolls; i++)
                    if (!string.IsNullOrEmpty(q.RareResource) && rng.NextDouble() < q.RareChance)
                        Grant(res, new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>(q.RareResource, 1) });
            }

            // Contractor.
            if (c != null)
            {
                c.Jobs++;
                switch (outcome)
                {
                    case Outcome.GreatSuccess:
                    case Outcome.Success:
                        c.Successes++;
                        if (q == null || !q.Recovery)
                        {
                            c.Xp++;
                            if (c.Xp >= _cfg.XpPerLevel && c.Level < _cfg.LevelCap)
                            {
                                c.Xp -= _cfg.XpPerLevel;
                                c.Level++;
                                res.LeveledUp = true;
                            }
                        }
                        c.Status = ContractorStatus.Available;
                        c.LastNote = $"{outcome} at {q?.Name}";
                        break;
                    case Outcome.PartialInjury:
                        c.Status = ContractorStatus.Injured;
                        c.InjuredUntilMs = _s.SimMs + _cfg.InjuryMs;
                        res.Injured = true;
                        c.LastNote = $"injured at {q?.Name}";
                        break;
                    case Outcome.Failure:
                        c.Status = ContractorStatus.Available;
                        c.LastNote = $"turned back at {q?.Name}";
                        break;
                    default:
                        c.Status = ContractorStatus.Dead;
                        res.Died = true;
                        c.LastNote = $"died at {q?.Name}";
                        break;
                }
                res.NewLevel = c.Level;
            }

            // Reputation (tenths, clamped after each result, in completion order).
            var before = _s.ReputationTenths;
            var baseTenths = q != null && q.Recovery ? 10 : Resolver.ReputationBaseTenths(outcome);
            _s.ReputationTenths = Resolver.ApplyReputation(before, baseTenths, q?.PrestigeTarget ?? 20);
            res.ReputationDeltaTenths = _s.ReputationTenths - before;

            // Knowledge and the map.
            if (q != null)
            {
                var prog = ProgressOf(q.Id);
                if (outcome == Outcome.Success || outcome == Outcome.GreatSuccess)
                {
                    foreach (var t in q.Threats) if (!t.Known && prog.RevealedThreats.Add(t.Id)) res.RevealedThreats.Add(t.Id);
                    prog.Successes++;
                }
                else if (outcome == Outcome.PartialInjury)
                {
                    foreach (var t in q.Threats)
                        if (!t.Known && prog.RevealedThreats.Add(t.Id)) { res.RevealedThreats.Add(t.Id); break; }   // one threat fact
                }
                var lockedBefore = new HashSet<string>();
                foreach (var id in _content.QuestIds) if (!IsUnlocked(id)) lockedBefore.Add(id);
                RefreshUnlocks();
                foreach (var id in lockedBefore) if (IsUnlocked(id)) res.QuestsUnlocked.Add(id);
            }

            _s.Expeditions.Remove(e);
            _s.Results.Add(res);
            while (_s.Results.Count > _cfg.ResultHistory)
            {
                var oldest = _s.Results.FindIndex(r => r.Acknowledged);
                _s.Results.RemoveAt(oldest >= 0 ? oldest : 0);
            }
            Completed?.Invoke(res);
            return res;
        }

        private bool HasBonusRareRoll(ContractorRecord c) => c != null && _content.Class(c.ClassId) is ClassInfo ci && ci.BonusRareRoll;

        private void Grant(ResultRecord res, IReadOnlyList<KeyValuePair<string, int>> materials)
        {
            foreach (var m in materials)
            {
                if (m.Value <= 0) continue;
                _rewards.ProduceShared(m.Key, m.Value, "quest reward");
                res.Materials.Add($"{m.Key}:{m.Value}");
            }
        }

        public void Acknowledge(string expeditionId)
        {
            var r = _s.Results.Find(x => x.ExpeditionId == expeditionId);
            if (r != null) r.Acknowledged = true;
        }

        public int UnacknowledgedResults => _s.Results.FindAll(r => !r.Acknowledged).Count;

        public bool HasArrived(ApplicantRecord a) => _s.SimMs >= a.ArriveMs;

        // ------------------------------------------------------------------ reservations and dispatch

        /// <summary>Replaces the loadout editor's reservations atomically. Returns false and changes nothing on conflict.</summary>
        public bool ReserveLoadout(Loadout l) => _gear.Reserve(LoadoutOwner, l.All());

        public void ReleaseLoadout() => _gear.ReleaseReservations(LoadoutOwner);

        /// <summary>Why this applicant cannot be sent with this loadout (null = can). Checked again at commit.</summary>
        public string DispatchBlockedReason(string contractorId, Loadout l)
        {
            var p = _s.Posting;
            if (p == null) return "No open posting";
            var a = p.Applicants.Find(x => x.ContractorId == contractorId);
            if (a == null) return "Not an applicant for this job";
            if (!HasArrived(a)) return "Still on the way to the Store";
            var c = Contractor(contractorId);
            if (c == null || c.Status != ContractorStatus.Available) return $"{c?.Name ?? "Contractor"} is not available";
            if (_s.Expeditions.Count >= _cfg.ActiveSlots) return $"Both expedition slots are in use ({_cfg.ActiveSlots})";
            var q = Quest(p.QuestId);

            var ids = l.All();
            if (new HashSet<string>(ids).Count != ids.Count) return "The same item is in two slots";
            foreach (var id in ids)
            {
                if (!Contains(_gear.StoreItems, id)) return "An item in this loadout is no longer in the Store";
                if (_gear.IsReservedByOther(id, LoadoutOwner)) return "An item is reserved elsewhere";
            }
            var w = Info(l.Weapon);
            if (l.Weapon != null && (w == null || w.Kind != ItemKind.Weapon)) return "Weapon slot holds a non-weapon";
            if (w != null && !CanWield(c, w)) return $"{c.Name} cannot use that weapon";
            var ar = Info(l.Armor);
            if (l.Armor != null && (ar == null || ar.Kind != ItemKind.Armor)) return "Armor slot holds non-armor";
            foreach (var pid in new[] { l.Potion1, l.Potion2 })
            {
                var pi = Info(pid);
                if (pid != null && (pi == null || pi.Kind != ItemKind.Potion)) return "Potion slot holds a non-potion";
            }
            var req = RequirementProblem(c, l, q);
            if (req != null) return req;
            if (!q.Recovery && Forecast(c, l, q).Band == RiskBand.Suicidal)
                return $"{c.Name} refuses: with this gear the job looks suicidal";
            if (!q.Recovery && _s.Gold < a.Fee) return $"Not enough gold: the fee is {a.Fee}, you have {_s.Gold}";
            return null;
        }

        /// <summary>The quest's authored dispatch requirements (level, current reputation, gear, counter potion), or null.</summary>
        public string RequirementProblem(ContractorRecord c, Loadout l, QuestInfo q)
        {
            if (q.MinLevel > 0 && c.Level < q.MinLevel) return $"Needs a level {q.MinLevel} contractor ({c.Name} is level {c.Level})";
            if (q.DispatchReputation > 0 && _s.Reputation < q.DispatchReputation)
                return $"Needs reputation {q.DispatchReputation} when you send it (you have {_s.Reputation})";
            var w = Info(l.Weapon);
            if (q.MinWeaponPower > 0 && (w == null || w.Power < q.MinWeaponPower)) return $"Needs a weapon with Power {q.MinWeaponPower} or more";
            var a = Info(l.Armor);
            if (q.MinArmorProtection > 0 && (a == null || a.Protection < q.MinArmorProtection)) return $"Needs armor with Protection {q.MinArmorProtection} or more";
            if (q.RequireCounterPotion)
            {
                var ok = false;
                foreach (var pid in new[] { l.Potion1, l.Potion2 })
                {
                    var pi = Info(pid);
                    if (pi == null || pi.Kind != ItemKind.Potion || string.IsNullOrEmpty(pi.CounterTag)) continue;
                    foreach (var t in q.Threats) if (t.CounterTag == pi.CounterTag) ok = true;
                }
                if (!ok) return "Needs a potion that counters one of the known threats";
            }
            return null;
        }

        /// <summary>
        /// Atomic dispatch (GDD 11): fee debit, contractor availability, item custody, timing, threat set,
        /// seed and frozen inputs commit together, or nothing changes.
        /// </summary>
        public bool Dispatch(string contractorId, Loadout l, bool testDeparture, out string failure, out ExpeditionRecord expedition)
        {
            expedition = null;
            failure = DispatchBlockedReason(contractorId, l);
            if (failure != null) return false;

            var p = _s.Posting;
            var q = Quest(p.QuestId);
            var a = p.Applicants.Find(x => x.ContractorId == contractorId);
            var c = Contractor(contractorId);
            var snapshot = StatsWith(c, l);
            var forecast = Forecast(c, l, q);
            var id = $"expedition.{_s.NextExpeditionNumber:D5}";

            if (!_gear.TakeFromStore(l.All(), LoadoutOwner, $"dispatched with {id}"))
            {
                failure = "Gear could not be taken from the Store";
                return false;
            }

            var e = new ExpeditionRecord
            {
                Id = id,
                QuestId = q.Id,
                ContractorId = c.Id,
                FeeQuoted = a.Fee,
                FeePaid = q.Recovery ? 0 : a.Fee,
                Sponsored = q.Recovery,
                StartMs = _s.SimMs,
                EndMs = _s.SimMs + q.DurationMs,
                Seed = RandomStreams.SplitMix64(p.WindowSeed ^ (ulong)_s.NextExpeditionNumber),
                Snapshot = snapshot,
                ForecastBand = forecast.Band.ToString(),
                TestDeparture = testDeparture,
            };
            e.Items.AddRange(l.All());
            foreach (var t in q.Threats) e.ThreatIds.Add(t.Id);
            e.Difficulty = q.Difficulty;
            e.Threats.AddRange(q.Threats);
            var seenEffects = new HashSet<string>();
            foreach (var pid in new[] { l.Potion1, l.Potion2 })
            {
                var pi = Info(pid);
                if (pi == null || pi.Kind != ItemKind.Potion || !seenEffects.Add(pi.Id)) continue;
                e.Potions.Add(new CarriedPotion { InstanceId = pid, DefinitionId = pi.Id, SurvivalBonus = pi.SurvivalBonus, CounterTag = pi.CounterTag });
            }

            _s.NextExpeditionNumber++;
            ProgressOf(q.Id).Attempts++;
            _s.Gold -= e.FeePaid;
            c.Status = ContractorStatus.OnExpedition;
            _s.Expeditions.Add(e);
            _s.WindowIndex.TryGetValue(q.Id, out var w);
            _s.WindowIndex[q.Id] = w + 1;
            _s.Posting = null;            // unselected applicants return to the pool
            _s.FirstDispatchDone = true;
            _gear.ReleaseReservations(LoadoutOwner);
            expedition = e;
            Dispatched?.Invoke(e);
            Changed?.Invoke();
            return true;
        }

        /// <summary>DEBUG (Phase 4 only): undoes test departures: gear back to the Store, contractor available, fee refunded.</summary>
        public int DebugReturnTestDepartures()
        {
            var n = 0;
            for (var i = _s.Expeditions.Count - 1; i >= 0; i--)
            {
                var e = _s.Expeditions[i];
                if (!e.TestDeparture) continue;
                _gear.ReturnToStore(e.Items, "test departure reset");
                var c = Contractor(e.ContractorId);
                if (c != null) c.Status = ContractorStatus.Available;
                _s.Gold += e.FeePaid;
                _s.Expeditions.RemoveAt(i);
                n++;
            }
            if (n > 0) Changed?.Invoke();
            return n;
        }

        /// <summary>Item instances held by expeditions (for the ownership audit).</summary>
        public IEnumerable<string> ExpeditionItems()
        {
            foreach (var e in _s.Expeditions) foreach (var i in e.Items) yield return i;
        }

        private static bool Contains(IReadOnlyList<string> list, string id)
        {
            for (var i = 0; i < list.Count; i++) if (list[i] == id) return true;
            return false;
        }
    }
}
