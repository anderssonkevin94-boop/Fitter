using System;
using System.Collections.Generic;

namespace Ravenholm.Domain.Jobs
{
    public enum RiskBand
    {
        Suicidal = 0,
        Dangerous = 1,
        Risky = 2,
        Good = 3,
        Excellent = 4,
    }

    public enum ThreatStatus
    {
        /// <summary>Required counter strength reached: unmet penalties removed.</summary>
        Countered,
        /// <summary>Some counter strength, below the requirement: bonus applies, penalty stays.</summary>
        Helps,
        Unmatched,
    }

    /// <summary>Resolver coefficients (BalanceProfile). Defaults are the GDD 12 starting points.</summary>
    public sealed class ResolverCoefficients
    {
        public double Might = 0.45, Survival = 0.30, Insight = 0.25, WeaponPower = 0.80, ArmorProtection = 0.65;
        public double ContextCap = 8;
        public double SurvivalProtection = 0.80, SurvivalDifficulty = 0.55, SurvivalContextCap = 4;
        public double ExcellentAt = 10, GoodAt = 3, RiskyAt = -3, DangerousAt = -9;
    }

    public sealed class ThreatSpec
    {
        public string Id = "";
        public string CounterTag = "";
        public int Required = 2;
        public int UnmetReadiness = -4;
        public int UnmetSurvival;
        public int ArmorThreshold;
        public int ArmorReadiness;
        public int ArmorSurvival;
        public bool Known = true;
    }

    /// <summary>Everything about a contractor plus loadout that the resolver reads. Also the dispatch snapshot.</summary>
    public sealed class LoadoutStats
    {
        public int Might, Survival, Insight;
        public int WeaponPower;
        public int ArmorProtection;
        /// <summary>Counter tag → summed strength (class, traits, weapon, counter potions).</summary>
        public readonly SortedDictionary<string, int> Counters = new SortedDictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Recovery Tonic survival margin, counted in forecasts as if its failure trigger fires.</summary>
        public int SurvivalPotionBonus;
        /// <summary>A shield's chance (percent) of blocking a killing blow (the crafting expansion, 2026-10-16).</summary>
        public int BlockChance;
        /// <summary>Food eaten on the trip: added to survival and readiness (the crafting expansion, 2026-10-16).</summary>
        public int FoodSurvival, FoodReadiness;

        public void AddCounter(string tag, int strength)
        {
            if (string.IsNullOrEmpty(tag) || strength == 0) return;
            Counters.TryGetValue(tag, out var v);
            Counters[tag] = v + strength;
        }
    }

    public sealed class ThreatAssessment
    {
        public ThreatSpec Threat;
        public ThreatStatus Status;
        public int Supplied;
    }

    public sealed class ForecastResult
    {
        public double Readiness;          // A
        public double Context;            // C after clamp
        public double MeanMargin;         // A − D (J has mean 0)
        public double SurvivalWithoutRoll; // V excluding K
        public double SurvivalContext;
        public RiskBand Band;
        public bool LimitedKnowledge;
        public readonly List<ThreatAssessment> Threats = new List<ThreatAssessment>();
    }

    /// <summary>
    /// The single forecast calculator (GDD 12), shared by applicant screening, the loadout review and
    /// (Phase 5) the resolver, so the UI never has its own formula.
    /// </summary>
    public static class ForecastCalculator
    {
        /// <param name="threats">Present threats. Only <see cref="ThreatSpec.Known"/> ones count unless <paramref name="includeUnknown"/>.</param>
        public static ForecastResult Evaluate(ResolverCoefficients k, LoadoutStats s, int difficulty, IReadOnlyList<ThreatSpec> threats, bool includeUnknown = false, int routeBonus = 0)
        {
            var r = new ForecastResult();
            double c = 0, survCtx = 0;
            foreach (var t in threats)
            {
                if (!t.Known && !includeUnknown)
                {
                    r.LimitedKnowledge = true;
                    continue;
                }
                var a = new ThreatAssessment { Threat = t };
                if (t.ArmorThreshold > 0)
                {
                    if (s.ArmorProtection >= t.ArmorThreshold)
                    {
                        a.Status = ThreatStatus.Countered;
                        a.Supplied = s.ArmorProtection;
                        c += t.ArmorReadiness;
                        survCtx += t.ArmorSurvival;
                    }
                    else
                    {
                        a.Status = ThreatStatus.Unmatched;
                        c += t.UnmetReadiness;
                        survCtx += t.UnmetSurvival;
                    }
                }
                else
                {
                    s.Counters.TryGetValue(t.CounterTag, out var supplied);
                    a.Supplied = supplied;
                    c += supplied;   // counters contribute through C, never as free stat additions (GDD 10)
                    if (supplied >= t.Required && t.Required > 0)
                        a.Status = ThreatStatus.Countered;
                    else
                    {
                        a.Status = supplied > 0 ? ThreatStatus.Helps : ThreatStatus.Unmatched;
                        c += t.UnmetReadiness;
                        survCtx += t.UnmetSurvival;
                    }
                }
                r.Threats.Add(a);
            }

            c += routeBonus;   // a discovered route or shortcut for this location (GDD 13: small, quest-specific, never global)
            r.Context = Clamp(c, -k.ContextCap, k.ContextCap);
            r.SurvivalContext = Clamp(survCtx, -k.SurvivalContextCap, k.SurvivalContextCap);
            r.Readiness = k.Might * s.Might + k.Survival * s.Survival + k.Insight * s.Insight
                          + k.WeaponPower * s.WeaponPower + k.ArmorProtection * s.ArmorProtection + r.Context + s.FoodReadiness;
            r.MeanMargin = r.Readiness - difficulty;
            r.SurvivalWithoutRoll = s.Survival + k.SurvivalProtection * s.ArmorProtection + s.SurvivalPotionBonus + s.FoodSurvival + r.SurvivalContext
                                    - k.SurvivalDifficulty * difficulty;
            r.Band = Band(k, r.MeanMargin);
            // A failure the contractor could not survive worsens the word by one step (GDD 12).
            if (r.SurvivalWithoutRoll < 0 && r.Band > RiskBand.Suicidal) r.Band--;
            return r;
        }

        public static RiskBand Band(ResolverCoefficients k, double margin)
        {
            if (margin >= k.ExcellentAt) return RiskBand.Excellent;
            if (margin >= k.GoodAt) return RiskBand.Good;
            if (margin >= k.RiskyAt) return RiskBand.Risky;
            if (margin >= k.DangerousAt) return RiskBand.Dangerous;
            return RiskBand.Suicidal;
        }

        private static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;
    }

    /// <summary>GDD 10 fee formula. Quotes are computed once per applicant per posting and saved.</summary>
    public sealed class FeeCoefficients
    {
        public double PerLevel = 0.12, DifficultyDivisor = 40, RepBase = 1.15, RepDivisor = 250, RepMin = 0.80, RepMax = 1.15;

        public double ReputationPrice(double reputation)
        {
            var p = RepBase - reputation / RepDivisor;
            return p < RepMin ? RepMin : p > RepMax ? RepMax : p;
        }

        public int Quote(int baseFee, int level, int difficulty, double personalityPrice, double reputation)
        {
            var raw = baseFee * (1 + PerLevel * (level - 1)) * (1 + difficulty / DifficultyDivisor) * personalityPrice * ReputationPrice(reputation);
            return Math.Max(1, (int)Math.Ceiling(raw - 1e-9));
        }
    }
}
