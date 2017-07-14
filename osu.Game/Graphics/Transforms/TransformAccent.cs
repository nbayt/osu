﻿// Copyright (c) 2007-2017 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using OpenTK.Graphics;
using osu.Framework.Graphics.Transforms;
using osu.Framework.MathUtils;

namespace osu.Game.Graphics.Transforms
{
    public class TransformAccent : Transform<Color4, IHasAccentColour>
    {
        public TransformAccent(IHasAccentColour target) : base(target)
        {
        }

        /// <summary>
        /// Current value of the transformed colour in linear colour space.
        /// </summary>
        public virtual Color4 CurrentValue
        {
            get
            {
                double time = Time?.Current ?? 0;
                if (time < StartTime) return StartValue;
                if (time >= EndTime) return EndValue;

                return Interpolation.ValueAt(time, StartValue, EndValue, StartTime, EndTime, Easing);
            }
        }

        public override void Apply(IHasAccentColour d) => d.AccentColour = CurrentValue;
        public override void ReadIntoStartValue(IHasAccentColour d) => StartValue = d.AccentColour;
    }
}
