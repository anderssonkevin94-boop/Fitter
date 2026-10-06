using System;
using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Logistics;
using Ravenholm.Domain.Sites;
using Ravenholm.Infrastructure.Save;

namespace Ravenholm.Services
{
    /// <summary>
    /// First-hour prompts (GDD 19): contextual and dismissible. Progress is saved ("onboarding"), so a
    /// suspended or restarted session resumes where it was. Finishing a later step finishes every earlier
    /// one. Prompts only read state and events; they never change odds, rewards or timing.
    /// </summary>
    public sealed class TutorialService : ISaveSection
    {
        private readonly SaveScheduler _scheduler;
        private readonly ContentCatalogue _catalogue;
        private readonly LogisticsService _logistics;
        private readonly SitesService _sites;
        private readonly ProductionService _production;
        private readonly JobsService _jobs;
        private readonly EconomyService _economy;
        private readonly List<TutorialStepDefinition> _steps = new List<TutorialStepDefinition>();
        private readonly HashSet<string> _done = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<TutorialCondition> _events = new HashSet<TutorialCondition>();
        private int _baselineUpgrades = -1;

        public TutorialService(SaveScheduler scheduler, ContentCatalogue catalogue, LogisticsService logistics, SitesService sites,
            ProductionService production, JobsService jobs, EconomyService economy)
        {
            _scheduler = scheduler;
            _catalogue = catalogue;
            _logistics = logistics;
            _sites = sites;
            _production = production;
            _jobs = jobs;
            _economy = economy;
            if (catalogue != null) foreach (var s in catalogue.Tutorial) if (s != null) _steps.Add(s);
            _steps.Sort((a, b) => a.Order.CompareTo(b.Order));
            logistics.System.Delivered += (receiver, unit) => { if (unit.Kind == UnitKind.Material) _events.Add(TutorialCondition.DeliveredRawMaterial); };
            production.System.ItemCrafted += (r, id) => _events.Add(TutorialCondition.CraftedItem);
            scheduler.Register(this);
        }

        public bool Hidden { get; private set; }
        public IReadOnlyList<TutorialStepDefinition> Steps => _steps;
        public bool IsDone(string stepId) => _done.Contains(stepId);

        /// <summary>DEBUG/tests: notes that the Trader was used (the economy service calls this).</summary>
        public void NoteTraderUsed() => _events.Add(TutorialCondition.TraderUsed);

        /// <summary>The prompt to show now, or null (all done, hidden, or none authored).</summary>
        public TutorialStepDefinition Current
        {
            get
            {
                if (Hidden) return null;
                foreach (var s in _steps) if (!_done.Contains(s.Id)) return s;
                return null;
            }
        }

        /// <summary>Checks conditions; completing a step also completes all earlier ones. Returns true if anything changed.</summary>
        public bool Refresh()
        {
            var latest = -1;
            for (var i = 0; i < _steps.Count; i++)
                if (!_done.Contains(_steps[i].Id) && _steps[i].Condition != TutorialCondition.Acknowledge && Met(_steps[i].Condition)) latest = i;
            if (latest < 0) return false;
            for (var i = 0; i <= latest; i++) _done.Add(_steps[i].Id);
            _scheduler.MarkDirty();
            return true;
        }

        public void Acknowledge(string stepId)
        {
            var i = _steps.FindIndex(s => s.Id == stepId);
            if (i < 0) return;
            for (var k = 0; k <= i; k++) _done.Add(_steps[k].Id);
            _scheduler.MarkDirty();
        }

        public void SetHidden(bool hidden)
        {
            Hidden = hidden;
            _scheduler.MarkDirty();
        }

        /// <summary>DEBUG: start the prompts again from the beginning.</summary>
        public void Restart()
        {
            _done.Clear();
            _events.Clear();
            Hidden = false;
            _baselineUpgrades = _economy.Purchased.Count;
            _scheduler.SaveNow("tips restarted");
        }

        private bool Met(TutorialCondition c)
        {
            if (_events.Contains(c)) return true;
            var jobs = _jobs.State;
            switch (c)
            {
                case TutorialCondition.CarryingSomething: return _logistics.System.CarryCount > 0;
                case TutorialCondition.SiteDecisionMade:
                    foreach (var s in _sites.Sites)
                        if (s.Record.SectionsCompleted > 0 && s.Record.Status == SiteStatus.Working) return true;
                    return false;
                case TutorialCondition.GearInStore: return _logistics.System.StoreItems.Count > 0;
                case TutorialCondition.JobPosted: return jobs.Posting != null || jobs.FirstDispatchDone;
                case TutorialCondition.ContractorSent: return jobs.FirstDispatchDone;
                case TutorialCondition.ResultRead: return jobs.Results.Exists(r => r.Acknowledged);
                case TutorialCondition.NewBlueprintOwned:
                    foreach (var b in _production.System.State.Blueprints)
                        if (_catalogue.TryGet<BlueprintDefinition>(b, out var d) && !d.OwnedAtStart) return true;
                    return false;
                case TutorialCondition.UpgradeBought: return _economy.Purchased.Count > Math.Max(0, _baselineUpgrades);
                default: return false;
            }
        }

        void ISaveSection.Read(SaveEnvelope env)
        {
            _done.Clear();
            _events.Clear();
            Hidden = false;
            _baselineUpgrades = 0;
            var d = env?.Onboarding;
            if (d == null) return;
            foreach (var id in d.Done) _done.Add(id);
            Hidden = d.Hidden;
        }

        void ISaveSection.Write(SaveEnvelope env)
        {
            var d = new OnboardingSaveData { Hidden = Hidden };
            foreach (var s in _steps) if (_done.Contains(s.Id)) d.Done.Add(s.Id);
            env.Onboarding = d;
        }
    }
}
