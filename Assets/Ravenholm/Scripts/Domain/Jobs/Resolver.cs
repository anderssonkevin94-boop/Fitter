using System.Collections.Generic;
using Ravenholm.Domain.Rng;

namespace Ravenholm.Domain.Jobs
{
    public enum Outcome
    {
        Death = 0,
        Failure = 1,
        PartialInjury = 2,
        Success = 3,
        GreatSuccess = 4,
    }

    /// <summary>A potion carried on an expedition, as frozen at dispatch.</summary>
    public sealed class CarriedPotion
    {
        public string InstanceId = "";
        public string DefinitionId = "";
        public int SurvivalBonus;
        public string CounterTag = "";
        /// <summary>Food: always eaten on the trip.</summary>
        public bool Food;
    }

    public sealed class Resolution
    {
        public Outcome Outcome;
        public bool Forced;
        /// <summary>A shield turned a death into a wounded retreat.</summary>
        public bool Blocked;
        public double J, K, A, C, E, V, SurvivalContext;
        public readonly List<string> ConsumedPotions = new List<string>();
        public readonly List<string> EffectsFired = new List<string>();
        public readonly List<ThreatAssessment> Threats = new List<ThreatAssessment>();
        /// <summary>Two or three plain-language reasons, largest first (GDD 13).</summary>
        public readonly List<string> Contributors = new List<string>();
    }

    /// <summary>
    /// GDD 12 resolver, version 1. Pure function of the dispatch snapshot and the expedition seed:
    /// the same inputs always produce the same outcome. Hidden threats count here even though the
    /// player's forecast could not include them.
    /// </summary>
    public static class Resolver
    {
        public const int Version = 1;

        /// <summary>Triangular on [−half, +half] with mode 0 (sum of two uniforms).</summary>
        public static double Triangular(IRandomStream rng, double half) => half * (rng.NextDouble() + rng.NextDouble() - 1.0);

        public static Resolution Resolve(ResolverCoefficients k, LoadoutStats snapshot, int difficulty, IReadOnlyList<ThreatSpec> threats,
            IReadOnlyList<CarriedPotion> potions, ulong seed, Outcome? forced = null, int routeBonus = 0)
        {
            var r = new Resolution();
            var f = ForecastCalculator.Evaluate(k, snapshot, difficulty, threats, includeUnknown: true, routeBonus: routeBonus);
            r.A = f.Readiness;
            r.C = f.Context;
            r.SurvivalContext = f.SurvivalContext;
            r.Threats.AddRange(f.Threats);

            var rng = new Pcg32(seed, 99);
            r.J = Triangular(rng, 10);
            r.K = Triangular(rng, 6);

            // Triggers are decided before the margins that use them (GDD 12).
            var present = new HashSet<string>();
            foreach (var t in threats) if (!string.IsNullOrEmpty(t.CounterTag)) present.Add(t.CounterTag);
            foreach (var p in potions)
                if (!string.IsNullOrEmpty(p.CounterTag) && present.Contains(p.CounterTag))
                {
                    r.ConsumedPotions.Add(p.InstanceId);
                    r.EffectsFired.Add($"{p.DefinitionId} countered {p.CounterTag.ToLowerInvariant()}");
                }

            r.E = r.A - difficulty + r.J;
            var survivalNoPotion = snapshot.Survival + k.SurvivalProtection * snapshot.ArmorProtection + snapshot.FoodSurvival + r.SurvivalContext
                                   - k.SurvivalDifficulty * difficulty + r.K;
            foreach (var p in potions)
                if (p.Food && !r.ConsumedPotions.Contains(p.InstanceId))
                {
                    r.ConsumedPotions.Add(p.InstanceId);   // eaten on the way, whatever happens
                    r.EffectsFired.Add($"{p.DefinitionId} eaten on the road");
                }
            r.V = survivalNoPotion + snapshot.SurvivalPotionBonus;

            if (forced.HasValue)
            {
                r.Outcome = forced.Value;
                r.Forced = true;
            }
            else if (r.E >= 10) r.Outcome = Outcome.GreatSuccess;
            else if (r.E >= 0) r.Outcome = Outcome.Success;
            else if (r.E >= -6) r.Outcome = Outcome.PartialInjury;
            else r.Outcome = r.V >= 0 ? Outcome.Failure : Outcome.Death;
            // A shield may block the killing blow: a wounded retreat instead (drawn only when a shield is carried, so
            // everything else rolls exactly as before).
            if (!r.Forced && r.Outcome == Outcome.Death && snapshot.BlockChance > 0 && rng.NextDouble() * 100 < snapshot.BlockChance)
            {
                r.Outcome = Outcome.Failure;
                r.Blocked = true;
                r.EffectsFired.Add("the shield blocked a killing blow");
            }

            // A survival tonic is spent only when the retreat check happens (E below −6).
            if (r.E < -6 || r.Outcome == Outcome.Failure || r.Outcome == Outcome.Death)
                foreach (var p in potions)
                    if (p.SurvivalBonus > 0 && !r.ConsumedPotions.Contains(p.InstanceId))
                    {
                        r.ConsumedPotions.Add(p.InstanceId);
                        r.EffectsFired.Add($"{p.DefinitionId} used during the retreat");
                        break;   // duplicate effects do not stack
                    }

            Explain(r, k, snapshot, difficulty, survivalNoPotion);
            return r;
        }

        /// <summary>Picks the largest factors and states them plainly. No invented blow-by-blow narrative.</summary>
        private static void Explain(Resolution r, ResolverCoefficients k, LoadoutStats s, int d, double survivalNoPotion)
        {
            var reasons = new List<(double weight, string text)>();
            foreach (var t in r.Threats)
            {
                var name = t.Threat.Id;
                if (t.Status == ThreatStatus.Countered) reasons.Add((System.Math.Abs(t.Supplied) + 2, $"The {name} threat was countered."));
                else reasons.Add((4, t.Threat.Known ? $"The {name} threat was not countered." : $"An unidentified threat ({name}) was waiting."));
            }
            if (r.J <= -5) reasons.Add((-r.J, "The road went badly."));
            if (r.J >= 5) reasons.Add((r.J, "Things went their way."));
            var gear = k.WeaponPower * s.WeaponPower + k.ArmorProtection * s.ArmorProtection;
            if (r.Outcome >= Outcome.Success && gear >= d * 0.5) reasons.Add((gear / 2, "Good gear carried the job."));
            if (r.Outcome <= Outcome.PartialInjury && s.WeaponPower == 0) reasons.Add((6, "They went without a weapon."));
            if (r.Outcome == Outcome.Failure)
            {
                if (survivalNoPotion < 0 && s.SurvivalPotionBonus > 0) reasons.Add((9, "The Recovery Tonic got them home."));
                else if (s.ArmorProtection > 0) reasons.Add((k.SurvivalProtection * s.ArmorProtection, "Armor protected them on the retreat."));
            }
            if (r.Outcome == Outcome.Death && s.ArmorProtection == 0) reasons.Add((7, "They had no armor for the retreat."));
            if (r.Blocked) reasons.Add((12, "Their shield blocked what would have been a killing blow."));
            reasons.Sort((a, b) => b.weight.CompareTo(a.weight));
            for (var i = 0; i < reasons.Count && r.Contributors.Count < 3; i++)
                if (!r.Contributors.Contains(reasons[i].text)) r.Contributors.Add(reasons[i].text);
            if (r.Forced) r.Contributors.Insert(0, "DEBUG: this outcome was forced.");
        }

        public static double GoldMultiplier(Outcome o) =>
            o == Outcome.GreatSuccess ? 1.25 : o == Outcome.Success ? 1.0 : o == Outcome.PartialInjury ? 0.4 : 0.0;

        /// <summary>Reputation base change in tenths (GDD 13: +4, +3, 0, −2, −7).</summary>
        public static int ReputationBaseTenths(Outcome o) =>
            o == Outcome.GreatSuccess ? 40 : o == Outcome.Success ? 30 : o == Outcome.PartialInjury ? 0 : o == Outcome.Failure ? -20 : -70;

        /// <summary>GDD 14: positive gains shrink above the job's prestige target; losses are never reduced.</summary>
        public static int ApplyReputation(int currentTenths, int baseTenths, int prestigeTarget)
        {
            var delta = baseTenths;
            if (baseTenths > 0)
            {
                var m = (prestigeTarget + 20 - currentTenths / 10.0) / 20.0;
                m = m < 0.15 ? 0.15 : m > 1 ? 1 : m;
                delta = (int)System.Math.Round(baseTenths * m, System.MidpointRounding.AwayFromZero);
                if (delta < 1) delta = 1;
            }
            var next = currentTenths + delta;
            return next < 0 ? 0 : next > 1000 ? 1000 : next;
        }
    }
}
