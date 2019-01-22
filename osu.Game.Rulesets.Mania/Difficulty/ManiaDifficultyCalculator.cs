// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Mania.Difficulty
{
    internal class ManiaDifficultyCalculator : DifficultyCalculator
    {
        private const double star_scaling_factor = 0.018;

        /// <summary>
        /// In milliseconds. For difficulty calculation we will only look at the highest strain value in each time interval of size strain_step.
        /// This is to eliminate higher influence of stream over aim by simply having more HitObjects with high strain.
        /// The higher this value, the less strains there will be, indirectly giving long beatmaps an advantage.
        /// </summary>
        private const double strain_step = 400;

        /// <summary>
        /// The weighting of each strain value decays to this number * it's previous value
        /// </summary>
        private const double decay_weight = 0.92;

        private readonly bool isForCurrentRuleset;

        public ManiaDifficultyCalculator(Ruleset ruleset, WorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
            isForCurrentRuleset = beatmap.BeatmapInfo.Ruleset.Equals(ruleset.RulesetInfo);
        }

        protected override DifficultyAttributes Calculate(IBeatmap beatmap, Mod[] mods, double timeRate)
        {
            if (!beatmap.HitObjects.Any())
                return new ManiaDifficultyAttributes(mods, 0);

            var difficultyHitObjects = new List<ManiaHitObjectDifficulty>();

            int columnCount = ((ManiaBeatmap)beatmap).TotalColumns;

            // Sort DifficultyHitObjects by StartTime of the HitObjects - just to make sure.
            // Note: Stable sort is done so that the ordering of hitobjects with equal start times doesn't change
            // Ordering is now ordered by start time, then endtime, then by column from left to right.
            difficultyHitObjects.AddRange(beatmap.HitObjects.Select(h => new ManiaHitObjectDifficulty((ManiaHitObject)h, columnCount)).OrderBy(h => h.BaseHitObject.StartTime).ThenBy(h => -h.endTime).ThenBy(h => h.BaseHitObject.Column));

            if (!calculateStrainValues(difficultyHitObjects, timeRate))
                return new DifficultyAttributes(mods, 0);
            double starRating = calculateDifficulty(difficultyHitObjects, timeRate); // Star scaling factor applied in calculateDifficulty().

            return new ManiaDifficultyAttributes(mods, starRating)
            {
                // Todo: This int cast is temporary to achieve 1:1 results with osu!stable, and should be removed in the future
                GreatHitWindow = (int)(beatmap.HitObjects.First().HitWindows.Great / 2) / timeRate
            };
        }

        private bool calculateStrainValues(List<ManiaHitObjectDifficulty> objects, double timeRate)
        {
            // Traverse hitObjects in pairs to calculate the strain value of NextHitObject from the strain value of CurrentHitObject and environment.
            using (var hitObjectsEnumerator = objects.GetEnumerator())
            {
                if (!hitObjectsEnumerator.MoveNext())
                    return false;

                ManiaHitObjectDifficulty current = hitObjectsEnumerator.Current;

                // First hitObject starts at strain 1. 1 is the default for strain values, so we don't need to set it here. See DifficultyHitObject.
                while (hitObjectsEnumerator.MoveNext())
                {
                    var next = hitObjectsEnumerator.Current;
                    next?.CalculateStrains(current, timeRate);
                    current = next;
                }

                return true;
            }
        }

        private double calculateDifficulty(List<ManiaHitObjectDifficulty> objects, double timeRate)
        {
            double actualStrainStep = strain_step * timeRate;

            // Find the highest strain value within each strain step
            List<double> highestStrains = new List<double>();
            double intervalEndTime = actualStrainStep;
            double maximumStrain = 0; // We need to keep track of the maximum strain in the current interval

            // Strain summation variables
            List<double> summedStrains = new List<double>();
            summedStrains.Add(0);
            double summedStrainsAvg = 0, summedStrainsMax = 0;

            ManiaHitObjectDifficulty previousHitObject = null;
            foreach (var hitObject in objects)
            {
                // While we are beyond the current interval push the currently available maximum to our strain list
                while (hitObject.BaseHitObject.StartTime > intervalEndTime)
                {
                    highestStrains.Add(maximumStrain);
                    summedStrains.Add(0);

                    // The maximum strain of the next interval is not zero by default! We need to take the last hitObject we encountered, take its strain and apply the decay
                    // until the beginning of the next interval.
                    if (previousHitObject == null)
                    {
                        maximumStrain = 0;
                    }
                    else
                    {
                        double individualDecay = Math.Pow(ManiaHitObjectDifficulty.INDIVIDUAL_DECAY_BASE, (intervalEndTime - previousHitObject.BaseHitObject.StartTime) / 1000);
                        double overallDecay = Math.Pow(ManiaHitObjectDifficulty.OVERALL_DECAY_BASE, (intervalEndTime - previousHitObject.BaseHitObject.StartTime) / 1000);
                        maximumStrain = previousHitObject.IndividualStrain * individualDecay + previousHitObject.OverallStrain * overallDecay;
                    }

                    // Go to the next time interval
                    intervalEndTime += actualStrainStep;
                }

                // Obtain maximum strain
                double strain = hitObject.IndividualStrain + hitObject.OverallStrain;
                summedStrains[summedStrains.Count - 1] += strain;
                maximumStrain = Math.Max(strain, maximumStrain);

                previousHitObject = hitObject;
            }

            // Build the weighted sum over the highest strains for each interval
            double difficulty = 0;
            double weight = 1;
            highestStrains.Sort((a, b) => b.CompareTo(a)); // Sort from highest to lowest strain.

            foreach (double strain in highestStrains)
            {
                difficulty += weight * strain;
                weight *= decay_weight;
            }

            difficulty *= 0.8;
            difficulty *= star_scaling_factor;

            // Compute summed strains average and max
            foreach (double strain in summedStrains)
            {
                summedStrainsAvg += strain;
                if (strain > summedStrainsMax)
                    summedStrainsMax = strain;
            }
            summedStrainsAvg /= summedStrains.Count;

            // compute bonus or penalty
            double error = 0.0;
            foreach (double strain in summedStrains)
            {
                error += Math.Pow((summedStrainsMax - strain) / summedStrainsAvg, 2.25);
            }
            error /= summedStrains.Count;
            error /= 100.0;

            double rating_weight = Math.Min(difficulty / 2.5, 1.0);
            double star_rating = (1 - (.025 * rating_weight)) * difficulty;

            if(error <= 0.13)
            {
                double bonus = 1 + ((Math.Pow((0.13 - error) / 0.13, 1.80) * 0.065) * rating_weight);
                star_rating *= bonus;
            }
            else
            {
                double penalty = 1 - ((0.11 - Math.Pow(Math.Max(0.15 - (error - 0.13), 0.0) / 0.15, 1.45) * 0.11) * rating_weight);
                star_rating *= penalty;
            }
            return star_rating;
        }

        protected override Mod[] DifficultyAdjustmentMods
        {
            get
            {
                var mods = new Mod[]
                {
                    new ManiaModDoubleTime(),
                    new ManiaModHalfTime(),
                    new ManiaModEasy(),
                    new ManiaModHardRock(),
                };

                if (isForCurrentRuleset)
                    return mods;

                // if we are a convert, we can be played in any key mod.
                return mods.Concat(new Mod[]
                {
                    new ManiaModKey1(),
                    new ManiaModKey2(),
                    new ManiaModKey3(),
                    new ManiaModKey4(),
                    new ManiaModKey5(),
                    new ManiaModKey6(),
                    new ManiaModKey7(),
                    new ManiaModKey8(),
                    new ManiaModKey9(),
                }).ToArray();
            }
        }
    }
}
