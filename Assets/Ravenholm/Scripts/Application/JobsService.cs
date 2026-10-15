using System;
using System.Collections.Generic;
using System.Globalization;
using Ravenholm.Content;
using Ravenholm.Domain.Jobs;
using Ravenholm.Infrastructure.Save;

namespace Ravenholm.Services
{
    /// <summary>Exposes authored content to the job domain as plain data.</summary>
    public sealed class CatalogueJobContent : IJobContent
    {
        private readonly ContentCatalogue _c;
        private readonly Dictionary<string, ClassInfo> _classes = new Dictionary<string, ClassInfo>();
        private readonly Dictionary<string, TraitInfo> _traits = new Dictionary<string, TraitInfo>();
        private readonly Dictionary<string, QuestInfo> _quests = new Dictionary<string, QuestInfo>();
        private readonly Dictionary<string, ItemInfo> _items = new Dictionary<string, ItemInfo>();
        private readonly List<string> _genClasses = new List<string>();
        private readonly List<string> _genTraits = new List<string>();
        private readonly List<string> _given = new List<string>();
        private readonly List<string> _family = new List<string>();
        private readonly List<string> _questIds = new List<string>();

        private static List<KeyValuePair<string, int>> Pairs(IReadOnlyList<ResourceCost> list)
        {
            var r = new List<KeyValuePair<string, int>>();
            foreach (var c in list) if (c.resource != null) r.Add(new KeyValuePair<string, int>(c.resource.Id, c.quantity));
            return r;
        }

        public CatalogueJobContent(ContentCatalogue c)
        {
            _c = c;
            if (c == null) return;
            foreach (var d in c.Classes)
            {
                if (d == null) continue;
                var ci = new ClassInfo { Id = d.Id, Name = d.DisplayName, Might = d.Might, Survival = d.Survival, Insight = d.Insight, GainPerLevel = d.GainPerLevel, BaseFee = d.BaseFee, BonusRareRoll = d.BonusRareRoll };
                foreach (var t in d.WeaponTags) ci.WeaponTags.Add(t.ToString());
                foreach (var cv in d.Counters) ci.Counters.Add(new KeyValuePair<string, int>(cv.tag.ToString(), cv.strength));
                _classes[d.Id] = ci;
                _genClasses.Add(d.Id);
            }
            foreach (var d in c.Traits)
            {
                if (d == null) continue;
                var ti = new TraitInfo { Id = d.Id, Name = d.DisplayName, Willingness = (Willingness)(int)d.Willingness };
                foreach (var cv in d.Counters) ti.Counters.Add(new KeyValuePair<string, int>(cv.tag.ToString(), cv.strength));
                _traits[d.Id] = ti;
                _genTraits.Add(d.Id);
            }
            foreach (var d in c.Quests)
            {
                if (d == null) continue;
                var qi = new QuestInfo
                {
                    Id = d.Id, Name = d.DisplayName, Difficulty = d.Difficulty, DurationMs = (int)Math.Round(d.DurationSeconds * 1000), Repeatable = d.Repeatable,
                    Recovery = d.RecoveryContract, BaseGold = d.BaseGold, PrestigeTarget = d.PrestigeTarget, ReputationGate = d.ReputationGate, MapTier = d.MapTier,
                    FirstSuccessBlueprint = d.FirstSuccessBlueprint != null ? d.FirstSuccessBlueprint.Id : null,
                    RareResource = d.RareResource != null ? d.RareResource.Id : null, RareChance = d.RareChance,
                };
                foreach (var pre in d.Prerequisites) if (pre != null) qi.Prerequisites.Add(pre.Id);
                qi.Region = d.Region != null ? d.Region.Id : "";
                qi.CampaignStep = d.CampaignStep;
                qi.Finale = d.CampaignFinale;
                qi.MinLevel = d.MinContractorLevel;
                qi.DispatchReputation = d.DispatchReputation;
                qi.MinWeaponPower = d.MinWeaponPower;
                qi.MinArmorProtection = d.MinArmorProtection;
                qi.RequireCounterPotion = d.RequireCounterPotion;
                qi.FailureObservation = string.IsNullOrWhiteSpace(d.FailureObservation) ? null : d.FailureObservation;
                foreach (var k in d.Knowledge) qi.Knowledge.Add(new KnowledgeStepInfo { Successes = k.successes, Fact = k.fact, ReadinessBonus = k.readinessBonus });
                foreach (var prep in d.Preparations)
                    if (prep.quest != null && prep.threat != null) qi.Prepares.Add(new KeyValuePair<string, string>(prep.quest.Id, prep.threat.Id));
                qi.SuccessMaterials.AddRange(Pairs(d.SuccessMaterials));
                qi.GreatBonusMaterials.AddRange(Pairs(d.GreatBonusMaterials));
                qi.PartialMaterials.AddRange(Pairs(d.PartialMaterials));
                qi.SubstituteMaterials.AddRange(Pairs(d.SubstituteMaterials));
                _questIds.Add(d.Id);
                foreach (var t in d.Threats)
                {
                    if (t.threat == null) continue;
                    qi.Threats.Add(new ThreatSpec
                    {
                        Id = t.threat.Id, CounterTag = t.threat.CounterTag.ToString(), Required = t.threat.RequiredCounterStrength,
                        UnmetReadiness = t.threat.UnmetReadinessPenalty, UnmetSurvival = t.threat.UnmetSurvivalPenalty,
                        ArmorThreshold = t.threat.ArmorThreshold, ArmorReadiness = t.threat.ArmorReadinessBonus, ArmorSurvival = t.threat.ArmorSurvivalBonus,
                        Known = t.knownAtStart,
                    });
                }
                _quests[d.Id] = qi;
            }
            foreach (var d in c.Items)
            {
                if (d == null) continue;
                var ii = new ItemInfo
                {
                    Id = d.Id, Kind = (ItemKind)(int)d.Category, Power = d.Power, Protection = d.Protection, WeaponTag = d.WeaponTag.ToString(),
                    CounterTag = d.CounterTag == CounterTag.None ? "" : d.CounterTag.ToString(), CounterStrength = d.CounterStrength,
                };
                switch (d.PotionEffect)
                {
                    case PotionEffect.SurvivalMargin: ii.SurvivalBonus = d.PotionStrength; break;
                    default:
                        if (d.PotionCounterTag != CounterTag.None) { ii.CounterTag = d.PotionCounterTag.ToString(); ii.CounterStrength = d.PotionStrength; }
                        break;
                }
                _items[d.Id] = ii;
            }
            if (c.NamePool != null)
            {
                _given.AddRange(c.NamePool.GivenNames);
                _family.AddRange(c.NamePool.FamilyNames);
            }
        }

        public IReadOnlyList<string> QuestIds => _questIds;
        public ClassInfo Class(string id) => id != null && _classes.TryGetValue(id, out var v) ? v : null;
        public TraitInfo Trait(string id) => id != null && _traits.TryGetValue(id, out var v) ? v : null;
        public QuestInfo Quest(string id) => id != null && _quests.TryGetValue(id, out var v) ? v : null;
        public ItemInfo Item(string id) => id != null && _items.TryGetValue(id, out var v) ? v : null;
        public IReadOnlyList<string> GeneratableClasses => _genClasses;
        public IReadOnlyList<string> GeneratableTraits => _genTraits;
        public IReadOnlyList<string> GivenNames => _given;
        public IReadOnlyList<string> FamilyNames => _family;
    }

    /// <summary>Quest rewards: blueprints through production, materials through the shared ledger.</summary>
    public sealed class RewardLedger : IRewardLedger
    {
        private readonly ProductionService _production;
        private readonly LogisticsService _logistics;

        public RewardLedger(ProductionService production, LogisticsService logistics)
        {
            _production = production;
            _logistics = logistics;
        }

        public bool OwnsBlueprint(string id) => _production.System.State.Blueprints.Contains(id);
        public void GrantBlueprint(string id) => _production.System.GrantBlueprint(id);
        public void ProduceShared(string id, int quantity, string reason) => _logistics.System.ProduceShared(id, quantity, reason);
    }

    /// <summary>
    /// Application wrapper for gold, reputation, contractors and the Job Board. Persists the "jobs"
    /// section. Phase 4 dispatches reach a test departure; Phase 5 resolves them.
    /// </summary>
    public sealed class JobsService : ISaveSection
    {
        private readonly SaveScheduler _scheduler;
        private readonly LogisticsService _logistics;
        private readonly SitesService _sites;
        private readonly ContentCatalogue _catalogue;
        private readonly BalanceProfile _balance;
        private readonly JobsState _state = new JobsState();
        private readonly CatalogueJobContent _content;
        private readonly JobBoardConfig _config;
        private readonly RewardLedger _rewards;
        private double _msRemainder;
        private bool _completedThisTick;

        public JobsService(SaveScheduler scheduler, LogisticsService logistics, SitesService sites, ProductionService production, ContentCatalogue catalogue, BalanceProfile balance)
        {
            _rewards = new RewardLedger(production, logistics);
            _scheduler = scheduler;
            _logistics = logistics;
            _sites = sites;
            _catalogue = catalogue;
            _balance = balance;
            _content = new CatalogueJobContent(catalogue);
            _config = ConfigFrom(balance);
            logistics.System.RegisterExternalItemHolder(() => Board != null ? Board.ExpeditionItems() : Array.Empty<string>());
            scheduler.Register(this);
        }

        /// <summary>Rebuilt on load because the world seed is only known then.</summary>
        public JobBoardSystem Board { get; private set; }
        public IJobContent Content => _content;
        public JobsState State => _state;

        public event Action Reloaded;
        /// <summary>(result) after an expedition resolved and was saved.</summary>
        public event Action<ResultRecord> Completed;

        public static JobBoardConfig ConfigFrom(BalanceProfile b)
        {
            var c = new JobBoardConfig();
            if (b == null) return c;
            c.Resolver = new ResolverCoefficients
            {
                Might = b.mightWeight, Survival = b.survivalWeight, Insight = b.insightWeight, WeaponPower = b.weaponPowerWeight,
                ArmorProtection = b.armorProtectionWeight, ContextCap = b.contextCap, SurvivalProtection = b.survivalProtectionWeight,
                SurvivalDifficulty = b.survivalDifficultyWeight, SurvivalContextCap = b.survivalContextCap,
                ExcellentAt = b.excellentAt, GoodAt = b.goodAt, RiskyAt = b.riskyAt, DangerousAt = b.dangerousAt,
            };
            c.Fees = new FeeCoefficients
            {
                PerLevel = b.feePerLevel, DifficultyDivisor = b.feeDifficultyDivisor, RepBase = b.repPriceBase, RepDivisor = b.repPriceDivisor,
                RepMin = b.repPriceMin, RepMax = b.repPriceMax,
            };
            c.ActiveSlots = b.activeExpeditionSlots;
            c.ApplicantsBase = b.applicantsBase;
            c.ThirdAt = b.thirdApplicantAt;
            c.FourthAt = b.fourthApplicantAt;
            c.ArrivalMinMs = (int)Math.Round(b.applicationMinSeconds * 1000);
            c.ArrivalMaxMs = (int)Math.Round(b.applicationMaxSeconds * 1000);
            c.PoolTarget = b.contractorPoolTarget;
            c.CastlePosts = b.castlePostsJobs;
            c.CastleFirstMs = (long)Math.Round(b.castleFirstOfferSeconds * 1000);
            c.CastleIntervalMs = (long)Math.Round(b.castleOfferIntervalSeconds * 1000);
            c.ShopSharePercent = b.shopSharePercent;
            return c;
        }

        public void Tick(float deltaSeconds)
        {
            if (Board == null || deltaSeconds <= 0) return;
            var exact = deltaSeconds * 1000.0 * _sites.SpeedMultiplier + _msRemainder;
            var whole = (long)Math.Floor(exact);
            _msRemainder = exact - whole;
            if (whole <= 0) return;
            _completedThisTick = false;
            Board.Advance(whole);
            // A completion is a critical transaction: save at once so it can never be applied twice.
            if (_completedThisTick) _scheduler.SaveNow("expedition completed");
            else _scheduler.MarkDirty();
        }

        // ------------------------------------------------------------------ commands (critical: save at once)

        /// <summary>Advances the board by simulated milliseconds, saving at once if anything completed (tests, debug).</summary>
        public void AdvanceMs(long ms)
        {
            _completedThisTick = false;
            Board.Advance(ms);
            if (_completedThisTick && !DeferCompletionSaves) _scheduler.SaveNow("expedition completed");
            else _scheduler.MarkDirty();
        }

        /// <summary>
        /// Offline catch-up commits once at the end: until then nothing is written, so an interrupted
        /// catch-up simply re-runs from the previous save and no completion is ever applied twice.
        /// </summary>
        public bool DeferCompletionSaves { get; set; }

        public bool Post(string questId, out string failure)
        {
            var ok = Board.Post(questId, out failure);
            if (ok) _scheduler.SaveNow("job posted");
            return ok;
        }

        /// <summary>Sends the adventurer waiting at the shop away; the castle offers another job later.</summary>
        public void TurnAway()
        {
            Board.TurnAway();
            _scheduler.SaveNow("jobs: adventurer turned away");
        }

        public void CancelPosting()
        {
            Board.CancelPosting();
            _scheduler.SaveNow("posting cancelled");
        }

        public bool Dispatch(string contractorId, Loadout loadout, out string failure, out ExpeditionRecord expedition)
        {
            var ok = Board.Dispatch(contractorId, loadout, testDeparture: false, out failure, out expedition);
            if (ok) _scheduler.SaveNow("dispatched");
            return ok;
        }

        public int DebugReturnTestDepartures()
        {
            _scheduler.NoteAssisted("return test departures");
            var n = Board.DebugReturnTestDepartures();
            _scheduler.SaveNow("debug: return test departures");
            return n;
        }

        public void Acknowledge(string expeditionId)
        {
            Board.Acknowledge(expeditionId);
            _scheduler.MarkDirty();
        }

        /// <summary>DEBUG: the next resolved expedition gets this outcome (marked as forced on the result).</summary>
        /// <summary>DEBUG (GDD 24 "reveal knowledge"): every threat of every job becomes known.</summary>
        public int DebugRevealKnowledge()
        {
            _scheduler.NoteAssisted("reveal knowledge");
            var n = 0;
            foreach (var id in _content.QuestIds)
            {
                var p = Board.ProgressOf(id);
                foreach (var t in _content.Quest(id).Threats) if (!t.Known && p.RevealedThreats.Add(t.Id)) n++;
            }
            _scheduler.SaveNow("debug: reveal knowledge");
            return n;
        }

        public void DebugForceNext(Outcome? outcome)
        {
            if (outcome != null) _scheduler.NoteAssisted("forced outcome");
            _state.ForcedNext = outcome;
        }

        public void DebugFinishExpeditionsNow()
        {
            _scheduler.NoteAssisted("finish expeditions now");
            Board.DebugFinishExpeditionsNow();
        }

        /// <summary>Company gold change (purchases, sales). Never goes below zero: callers check first (no debt, GDD 15).</summary>
        public void AddGold(int delta)
        {
            _state.Gold = Math.Max(0, _state.Gold + delta);
            _scheduler.MarkDirty();
        }

        /// <summary>Ordinary (non-recovery) quest successes so far, for upgrade prerequisites.</summary>
        public int OrdinarySuccesses()
        {
            var n = 0;
            foreach (var kv in _state.Progress)
            {
                var q = _content.Quest(kv.Key);
                if (q != null && !q.Recovery) n += kv.Value.Successes;
            }
            return n;
        }

        public void DebugSetGold(int gold)
        {
            _scheduler.NoteAssisted("set gold");
            _state.Gold = Math.Max(0, gold);
            _scheduler.SaveNow("debug: gold");
        }

        public void DebugSetReputation(int reputation)
        {
            _scheduler.NoteAssisted("set reputation");
            _state.ReputationTenths = Math.Max(0, Math.Min(100, reputation)) * 10;
            _scheduler.SaveNow("debug: reputation");
        }

        // ------------------------------------------------------------------ persistence

        void ISaveSection.Read(SaveEnvelope env)
        {
            _state.Contractors.Clear();
            _state.Expeditions.Clear();
            _state.WindowIndex.Clear();
            _state.Posting = null;
            _msRemainder = 0;
            _state.Progress.Clear();
            _state.Results.Clear();
            _state.ForcedNext = null;
            _state.Gold = 0;
            _state.ReputationTenths = 0;   // never evaluate unlocks against a previous profile's reputation
            var d = env?.Jobs;
            if (d != null) LoadProgress(d);   // before the board refreshes unlocks
            Board = new JobBoardSystem(_state, _content, _logistics.System, _config, _scheduler.Coordinator.WorldSeed, _rewards);
            Board.Completed += r => { _completedThisTick = true; Completed?.Invoke(r); };
            if (d == null) StartFresh();
            else Load(d);
            Board.RefreshUnlocks();
            Reloaded?.Invoke();
        }

        private void StartFresh()
        {
            _state.SimMs = 0;
            _state.Gold = _balance != null ? _balance.startingGold : 60;
            _state.ReputationTenths = (_balance != null ? _balance.startingReputation : 20) * 10;
            _state.NextExpeditionNumber = 1;
            _state.GeneratedContractors = 0;
            _state.FirstDispatchDone = false;
            if (_catalogue == null) return;
            foreach (var f in _catalogue.Contractors)
            {
                if (f == null || f.Class == null) continue;
                var c = new ContractorRecord
                {
                    Id = f.Id, Name = f.DisplayName, ClassId = f.Class.Id, Level = f.Level, PersonalityPrice = f.PersonalityPrice,
                    Sponsored = f.Sponsored, Skin = f.Skin,
                };
                foreach (var t in f.Traits) if (t != null) c.Traits.Add(t.Id);
                Board.AddContractor(c);
            }
            // Fill the persistent pool (GDD 11: about 12) with generated named candidates.
            var nonSponsored = 0;
            foreach (var c in _state.Contractors.Values) if (!c.Sponsored) nonSponsored++;
            for (var i = nonSponsored; i < _config.PoolTarget - 1; i++) Board.Generate();
        }

        private void LoadProgress(JobsSaveData d)
        {
            if (d.Progress != null)
                foreach (var kv in d.Progress)
                {
                    var p = new QuestProgress { Attempts = kv.Value.Attempts, Successes = kv.Value.Successes, Unlocked = kv.Value.Unlocked, Observed = kv.Value.Observed };
                    foreach (var t in kv.Value.RevealedThreats) p.RevealedThreats.Add(t);
                    _state.Progress[kv.Key] = p;
                }
            if (d.Results != null)
                foreach (var rd in d.Results)
                {
                    var r = new ResultRecord
                    {
                        ExpeditionId = rd.ExpeditionId, QuestId = rd.QuestId, ContractorId = rd.ContractorId, ContractorName = rd.ContractorName,
                        Outcome = Enum.TryParse(rd.Outcome, out Outcome o) ? o : Outcome.Failure, Forced = rd.Forced, Sponsored = rd.Sponsored,
                        CompletedMs = rd.CompletedMs, GoldEarned = rd.GoldEarned, FeePaid = rd.FeePaid, BlueprintGranted = rd.BlueprintGranted,
                        SubstituteGranted = rd.SubstituteGranted, ReputationDeltaTenths = rd.ReputationDeltaTenths, LeveledUp = rd.LeveledUp,
                        NewLevel = rd.NewLevel, Injured = rd.Injured, Died = rd.Died, J = rd.J, K = rd.K, A = rd.A, E = rd.E, V = rd.V,
                        Acknowledged = rd.Acknowledged,
                    };
                    r.Materials.AddRange(rd.Materials);
                    r.ReturnedItems.AddRange(rd.ReturnedItems);
                    r.ConsumedItems.AddRange(rd.ConsumedItems);
                    r.LostItems.AddRange(rd.LostItems);
                    r.RevealedThreats.AddRange(rd.RevealedThreats);
                    if (rd.Facts != null) r.Facts.AddRange(rd.Facts);
                    r.QuestsUnlocked.AddRange(rd.QuestsUnlocked);
                    r.Contributors.AddRange(rd.Contributors);
                    _state.Results.Add(r);
                }
        }

        private void Load(JobsSaveData d)
        {
            _state.SimMs = d.SimMs;
            _state.Gold = d.Gold;
            _state.ReputationTenths = d.ReputationTenths;
            _state.NextExpeditionNumber = d.NextExpeditionNumber;
            _state.GeneratedContractors = d.GeneratedContractors;
            _state.FirstDispatchDone = d.FirstDispatchDone;
            _state.NextCastleOfferMs = d.NextCastleOfferMs;
            _state.CastleOffers = d.CastleOffers;
            _state.CastleSkipOnce = d.CastleSkipOnce ?? "";
            foreach (var kv in d.WindowIndex) _state.WindowIndex[kv.Key] = kv.Value;
            foreach (var cd in d.Contractors)
            {
                var c = new ContractorRecord
                {
                    Id = cd.Id, Name = cd.Name, ClassId = cd.ClassId, Level = cd.Level, Xp = cd.Xp, PersonalityPrice = cd.PersonalityPrice,
                    Status = Enum.TryParse(cd.Status, out ContractorStatus st) ? st : ContractorStatus.Available,
                    InjuredUntilMs = cd.InjuredUntilMs, Sponsored = cd.Sponsored, Skin = cd.Skin, Jobs = cd.Jobs, Successes = cd.Successes,
                    LastNote = cd.LastNote ?? "", Injuries = cd.Injuries, HighestDifficulty = cd.HighestDifficulty, DeathNote = cd.DeathNote ?? "",
                };
                c.Traits.AddRange(cd.Traits);
                if (cd.Discoveries != null) c.Discoveries.AddRange(cd.Discoveries);
                if (cd.Recent != null) c.Recent.AddRange(cd.Recent);
                _state.Contractors[c.Id] = c;
            }
            if (d.Posting != null)
            {
                var p = new PostingRecord
                {
                    QuestId = d.Posting.QuestId, CreatedMs = d.Posting.CreatedMs,
                    WindowSeed = ulong.Parse(d.Posting.WindowSeed ?? "0", CultureInfo.InvariantCulture),
                    NoApplicantReason = d.Posting.NoApplicantReason ?? "", ByCastle = d.Posting.ByCastle,
                };
                foreach (var a in d.Posting.Applicants) p.Applicants.Add(new ApplicantRecord { ContractorId = a.ContractorId, Fee = a.Fee, ArriveMs = a.ArriveMs });
                _state.Posting = p;
            }
            foreach (var ed in d.Expeditions)
            {
                var e = new ExpeditionRecord
                {
                    Id = ed.Id, QuestId = ed.QuestId, ContractorId = ed.ContractorId, FeeQuoted = ed.FeeQuoted, FeePaid = ed.FeePaid, Sponsored = ed.Sponsored,
                    StartMs = ed.StartMs, EndMs = ed.EndMs, Seed = ulong.Parse(ed.Seed ?? "0", CultureInfo.InvariantCulture),
                    ForecastBand = ed.ForecastBand ?? "", TestDeparture = ed.TestDeparture, ShopSharePercent = ed.ShopSharePercent,
                };
                e.Items.AddRange(ed.Items);
                e.ThreatIds.AddRange(ed.ThreatIds);
                e.Snapshot.Might = ed.Might;
                e.Snapshot.Survival = ed.Survival;
                e.Snapshot.Insight = ed.Insight;
                e.Snapshot.WeaponPower = ed.WeaponPower;
                e.Snapshot.ArmorProtection = ed.ArmorProtection;
                e.Snapshot.SurvivalPotionBonus = ed.SurvivalPotionBonus;
                foreach (var kv in ed.Counters) e.Snapshot.Counters[kv.Key] = kv.Value;
                e.Difficulty = ed.Difficulty;
                e.ResolverVersion = ed.ResolverVersion;
                foreach (var t in ed.ThreatSpecs)
                    e.Threats.Add(new ThreatSpec
                    {
                        Id = t.Id, CounterTag = t.CounterTag, Required = t.Required, UnmetReadiness = t.UnmetReadiness, UnmetSurvival = t.UnmetSurvival,
                        ArmorThreshold = t.ArmorThreshold, ArmorReadiness = t.ArmorReadiness, ArmorSurvival = t.ArmorSurvival, Known = t.Known,
                    });
                e.RouteBonus = ed.RouteBonus;
                foreach (var p in ed.Potions)
                    e.Potions.Add(new CarriedPotion { InstanceId = p.InstanceId, DefinitionId = p.DefinitionId, SurvivalBonus = p.SurvivalBonus, CounterTag = p.CounterTag });
                // Pre-Phase 5 expeditions saved no frozen inputs: rebuild them from current content once.
                if (e.ResolverVersion == 0 && !e.TestDeparture)
                {
                    var q = _content.Quest(e.QuestId);
                    if (q != null) { e.Difficulty = q.Difficulty; e.Threats.AddRange(q.Threats); }
                    e.ResolverVersion = Resolver.Version;
                }
                _state.Expeditions.Add(e);
            }
        }

        void ISaveSection.Write(SaveEnvelope env)
        {
            var d = new JobsSaveData
            {
                SimMs = _state.SimMs, Gold = _state.Gold, ReputationTenths = _state.ReputationTenths, NextExpeditionNumber = _state.NextExpeditionNumber,
                GeneratedContractors = _state.GeneratedContractors, FirstDispatchDone = _state.FirstDispatchDone,
                NextCastleOfferMs = _state.NextCastleOfferMs, CastleOffers = _state.CastleOffers, CastleSkipOnce = _state.CastleSkipOnce,
            };
            foreach (var kv in _state.WindowIndex) d.WindowIndex[kv.Key] = kv.Value;
            foreach (var c in _state.Contractors.Values)
            {
                var cd = new ContractorSaveData
                {
                    Id = c.Id, Name = c.Name, ClassId = c.ClassId, Level = c.Level, Xp = c.Xp, PersonalityPrice = c.PersonalityPrice,
                    Status = c.Status.ToString(), InjuredUntilMs = c.InjuredUntilMs, Sponsored = c.Sponsored, Skin = c.Skin, Jobs = c.Jobs,
                    Successes = c.Successes, LastNote = c.LastNote, Injuries = c.Injuries, HighestDifficulty = c.HighestDifficulty, DeathNote = c.DeathNote,
                };
                cd.Traits.AddRange(c.Traits);
                cd.Discoveries.AddRange(c.Discoveries);
                cd.Recent.AddRange(c.Recent);
                d.Contractors.Add(cd);
            }
            if (_state.Posting != null)
            {
                var p = _state.Posting;
                var pd = new PostingSaveData
                {
                    QuestId = p.QuestId, CreatedMs = p.CreatedMs, WindowSeed = p.WindowSeed.ToString(CultureInfo.InvariantCulture),
                    NoApplicantReason = p.NoApplicantReason, ByCastle = p.ByCastle,
                };
                foreach (var a in p.Applicants) pd.Applicants.Add(new ApplicantSaveData { ContractorId = a.ContractorId, Fee = a.Fee, ArriveMs = a.ArriveMs });
                d.Posting = pd;
            }
            foreach (var e in _state.Expeditions)
            {
                var ed = new ExpeditionSaveData
                {
                    Id = e.Id, QuestId = e.QuestId, ContractorId = e.ContractorId, FeeQuoted = e.FeeQuoted, FeePaid = e.FeePaid, Sponsored = e.Sponsored,
                    StartMs = e.StartMs, EndMs = e.EndMs, Seed = e.Seed.ToString(CultureInfo.InvariantCulture),
                    Might = e.Snapshot.Might, Survival = e.Snapshot.Survival, Insight = e.Snapshot.Insight, WeaponPower = e.Snapshot.WeaponPower,
                    ArmorProtection = e.Snapshot.ArmorProtection, SurvivalPotionBonus = e.Snapshot.SurvivalPotionBonus,
                    ForecastBand = e.ForecastBand, TestDeparture = e.TestDeparture, ShopSharePercent = e.ShopSharePercent,
                };
                ed.Items.AddRange(e.Items);
                ed.ThreatIds.AddRange(e.ThreatIds);
                foreach (var kv in e.Snapshot.Counters) ed.Counters[kv.Key] = kv.Value;
                ed.Difficulty = e.Difficulty;
                ed.ResolverVersion = e.ResolverVersion;
                foreach (var t in e.Threats)
                    ed.ThreatSpecs.Add(new ThreatSpecData
                    {
                        Id = t.Id, CounterTag = t.CounterTag, Required = t.Required, UnmetReadiness = t.UnmetReadiness, UnmetSurvival = t.UnmetSurvival,
                        ArmorThreshold = t.ArmorThreshold, ArmorReadiness = t.ArmorReadiness, ArmorSurvival = t.ArmorSurvival, Known = t.Known,
                    });
                ed.RouteBonus = e.RouteBonus;
                foreach (var p in e.Potions)
                    ed.Potions.Add(new CarriedPotionData { InstanceId = p.InstanceId, DefinitionId = p.DefinitionId, SurvivalBonus = p.SurvivalBonus, CounterTag = p.CounterTag });
                d.Expeditions.Add(ed);
            }
            foreach (var kv in _state.Progress)
            {
                var pd = new QuestProgressData { Attempts = kv.Value.Attempts, Successes = kv.Value.Successes, Unlocked = kv.Value.Unlocked, Observed = kv.Value.Observed };
                pd.RevealedThreats.AddRange(kv.Value.RevealedThreats);
                d.Progress[kv.Key] = pd;
            }
            foreach (var r in _state.Results)
            {
                var rd = new ResultSaveData
                {
                    ExpeditionId = r.ExpeditionId, QuestId = r.QuestId, ContractorId = r.ContractorId, ContractorName = r.ContractorName,
                    Outcome = r.Outcome.ToString(), Forced = r.Forced, Sponsored = r.Sponsored, CompletedMs = r.CompletedMs, GoldEarned = r.GoldEarned,
                    FeePaid = r.FeePaid, BlueprintGranted = r.BlueprintGranted, SubstituteGranted = r.SubstituteGranted,
                    ReputationDeltaTenths = r.ReputationDeltaTenths, LeveledUp = r.LeveledUp, NewLevel = r.NewLevel, Injured = r.Injured, Died = r.Died,
                    J = r.J, K = r.K, A = r.A, E = r.E, V = r.V, Acknowledged = r.Acknowledged,
                };
                rd.Materials.AddRange(r.Materials);
                rd.ReturnedItems.AddRange(r.ReturnedItems);
                rd.ConsumedItems.AddRange(r.ConsumedItems);
                rd.LostItems.AddRange(r.LostItems);
                rd.RevealedThreats.AddRange(r.RevealedThreats);
                rd.Facts.AddRange(r.Facts);
                rd.QuestsUnlocked.AddRange(r.QuestsUnlocked);
                rd.Contributors.AddRange(r.Contributors);
                d.Results.Add(rd);
            }
            env.Jobs = d;
        }
    }
}
