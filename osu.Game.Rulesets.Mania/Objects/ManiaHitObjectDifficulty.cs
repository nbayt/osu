// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using osu.Game.Rulesets.Objects.Types;
using System;
using System.Collections.Generic;

namespace osu.Game.Rulesets.Mania.Objects
{
    internal class ManiaHitObjectDifficulty
    {
        /// <summary>
        /// Factor by how much individual / overall strain decays per second.
        /// </summary>
        /// <remarks>
        /// These values are results of tweaking a lot and taking into account general feedback.
        /// </remarks>
        internal const double INDIVIDUAL_DECAY_BASE = 0.125;
        internal const double INDIVIDUAL_DECAY_HOLD_BASE = 0.15;
        internal const double OVERALL_DECAY_BASE = 0.30;

        internal ManiaHitObject BaseHitObject;

        private readonly int beatmapColumnCount;

        internal readonly double endTime;
        private readonly double[] heldUntil;

        /// <summary>
        /// Stores the last seen note in each column.
        /// </summary>
        private ManiaHitObjectDifficulty[] prior_notes;

        /// <summary>
        /// Stores all last seen notes that have not finished being held yet.
        /// </summary>
        private List<ManiaHitObjectDifficulty> prior_hold_notes = new List<ManiaHitObjectDifficulty>();

        /// <summary>
        ///  Measures jacks or more generally: repeated presses of the same button
        /// </summary>
        private readonly double[] individualStrains;

        internal double IndividualStrain
        {
            get
            {
                return individualStrains[BaseHitObject.Column];
            }

            set
            {
                individualStrains[BaseHitObject.Column] = value;
            }
        }

        /// <summary>
        /// Measures note density in a way
        /// </summary>
        internal double OverallStrain = 1;

        /// <summary>
        /// Used to compute shared max strain for all notes of StartTime.
        /// </summary>
        internal double sharedMaxOverallStrain = 0;

        public ManiaHitObjectDifficulty(ManiaHitObject baseHitObject, int columnCount)
        {
            BaseHitObject = baseHitObject;

            endTime = (baseHitObject as IHasEndTime)?.EndTime ?? baseHitObject.StartTime;

            beatmapColumnCount = columnCount;
            heldUntil = new double[beatmapColumnCount];
            individualStrains = new double[beatmapColumnCount];
            prior_notes = new ManiaHitObjectDifficulty[beatmapColumnCount];

            for (int i = 0; i < beatmapColumnCount; ++i)
            {
                individualStrains[i] = 0;
                heldUntil[i] = 0;
                prior_notes[i] = null;
            }

            // This is done to make sure the first note seen gets 2 individual strain.
            IndividualStrain = 2;
            sharedMaxOverallStrain = OverallStrain;

            // These will get overridden if not the first note.
            prior_notes[BaseHitObject.Column] = this;
            heldUntil[BaseHitObject.Column] = endTime;

            if (endTime > BaseHitObject.StartTime)
            {
                //prior_hold_notes.Add(this);
                //prior_hold_notes.Sort((x, y) => x.endTime.CompareTo(y.endTime));
            }
        }

        internal void CalculateStrains(ManiaHitObjectDifficulty previousHitObject, double timeRate)
        {
            // TODO: Factor in holds
            double timeElapsed = (BaseHitObject.StartTime - previousHitObject.BaseHitObject.StartTime) / timeRate;
            double individualDecay = Math.Pow(INDIVIDUAL_DECAY_BASE, timeElapsed / 1000);
            double overallDecay = Math.Pow(OVERALL_DECAY_BASE, timeElapsed / 1000);

            double holdFactor = 1.0; // Factor to all additional strains in case something else is held
            double holdAddition = 0; // Addition to the current note in case it's a hold and has to be released awkwardly

            prior_hold_notes = previousHitObject.prior_hold_notes;

            // Fill up the heldUntil array
            for (int i = 0; i < beatmapColumnCount; ++i)
            {
                prior_notes[i] = previousHitObject.prior_notes[i];
                heldUntil[i] = previousHitObject.heldUntil[i];

                // If there is at least one other overlapping end or note, then we get an addition, buuuuuut...
                // With the new sorting logic, this rule only applies to previous hold notes NOT on the same beat.
                if (BaseHitObject.StartTime < heldUntil[i] && endTime > heldUntil[i])
                {
                    holdAddition = 0.55;
                }

                // ... this addition only is valid if there is _no_ other note with the same ending. Releasing multiple notes at the same time is just as easy as releasing 1
                // if no prior beat note meets this criteria, then only the first hold that passes above check will get the bonus. (Left -> Right preference)
                if (endTime == heldUntil[i])
                {
                    holdAddition = 0;
                }

                // We give a slight bonus to everything if something is held meanwhile
                if (heldUntil[i] > endTime)
                {
                    if (holdFactor == 1.0)
                    {
                        holdFactor = 1.25;
                    }
                    else
                    {
                        holdFactor += (1.415 - holdFactor) * 0.45;
                    }
                    
                }

                // Decay individual strains
                individualStrains[i] = previousHitObject.individualStrains[i] * individualDecay;
            }

            // Accounts for tail shield taps and individual column decay is slower while a note is held.
            if (prior_notes[BaseHitObject.Column] != null)
            {
                ManiaHitObjectDifficulty prior_note = prior_notes[BaseHitObject.Column];
                if (prior_note.endTime > prior_note.BaseHitObject.StartTime)
                {
                    double prior_col_strain = prior_note.IndividualStrain;

                    double hold_time = (prior_note.endTime - prior_note.BaseHitObject.StartTime) / timeRate;
                    prior_col_strain *= Math.Pow(INDIVIDUAL_DECAY_HOLD_BASE, hold_time / 1000);
                    double tailHoldFactor = 1.0;
                    double duration_factor = 1.0;
                    double key_factor = 1.0;
                    if (hold_time > 40)
                    {
                        duration_factor = Math.Min(1.0, (hold_time - 40) / 30);
                        if(beatmapColumnCount > 9)
                        {
                            key_factor = 0.85;
                        }
                        else if (beatmapColumnCount > 4)
                        {
                            key_factor = 1 - (0.15 * ((beatmapColumnCount - 4) / 5));
                        }
                        for (int i = 0; i < beatmapColumnCount; i++)
                        {
                            if (i != BaseHitObject.Column && heldUntil[i] > prior_note.endTime)
                            {
                                tailHoldFactor += (1.25 - tailHoldFactor) * 0.4;
                            }
                        }
                        prior_col_strain += (0.7 * tailHoldFactor * key_factor * duration_factor);
                    }
                    double empty_time = (BaseHitObject.StartTime - prior_note.endTime) / timeRate;
                    prior_col_strain *= Math.Pow(INDIVIDUAL_DECAY_BASE, empty_time / 1000);

                    IndividualStrain = prior_col_strain;
                }
            }

            // Increase individual strain in own column
            IndividualStrain += 2.0 * holdFactor;
            heldUntil[BaseHitObject.Column] = endTime;

            // Tail notes contribute to overall difficulty slightly.
            if(prior_hold_notes.Count>0)
            {
                double old_overall_strain = previousHitObject.OverallStrain;
                double previous_time = previousHitObject.BaseHitObject.StartTime;

                while(prior_hold_notes.Count > 0 && prior_hold_notes[0].endTime <= BaseHitObject.StartTime)
                {
                    ManiaHitObjectDifficulty prior_hold_note = prior_hold_notes[0];
                    double elapsed_time = (prior_hold_note.endTime - previous_time) / timeRate;
                    double hold_time = (prior_hold_note.endTime - prior_hold_note.BaseHitObject.StartTime) / timeRate;

                    old_overall_strain *= Math.Pow(OVERALL_DECAY_BASE, elapsed_time / 1000);

                    double key_factor = 1.0;
                    double duration_factor = 1.0;
                    if(hold_time > 40)
                    {
                        duration_factor = Math.Min(1.0, ((hold_time - 40) / 40));
                        if (beatmapColumnCount > 9)
                        {
                            key_factor = 0.76;
                        }
                        else if (beatmapColumnCount > 4)
                        {
                            key_factor = 1 - (0.24 * (((double) (beatmapColumnCount) - 4.0) / 5.0));
                        }
                        old_overall_strain += (0.35 * key_factor * duration_factor);
                    }
                    previous_time = prior_hold_note.endTime;
                    prior_hold_notes.RemoveAt(0);
                }

                double elapsed_time_ = (BaseHitObject.StartTime - previous_time) / timeRate;
                old_overall_strain *= Math.Pow(OVERALL_DECAY_BASE, elapsed_time_ / 1000);

                OverallStrain = old_overall_strain + (1.0 + holdAddition) * holdFactor;
            }
            else
            {
                OverallStrain = previousHitObject.OverallStrain * overallDecay + (1.0 + holdAddition) * holdFactor;
            }

            // Computes shared overall strain for notes of same StartTime.
            sharedMaxOverallStrain = OverallStrain;
            if(previousHitObject.BaseHitObject.StartTime == BaseHitObject.StartTime)
            {
                sharedMaxOverallStrain = Math.Max(sharedMaxOverallStrain, previousHitObject.sharedMaxOverallStrain);
                for(int i = 0; i < beatmapColumnCount; i++)
                {
                    if(prior_notes[i] != null && prior_notes[i].BaseHitObject.StartTime == BaseHitObject.StartTime)
                    {
                        prior_notes[i].OverallStrain = sharedMaxOverallStrain;
                        prior_notes[i].sharedMaxOverallStrain = sharedMaxOverallStrain;
                    }
                }
            }
            prior_notes[BaseHitObject.Column] = this;
            if(endTime>BaseHitObject.StartTime)
            {
                prior_hold_notes.Add(this);
                prior_hold_notes.Sort((x,y) => x.endTime.CompareTo(y.endTime));
            }
        }
    }
}
