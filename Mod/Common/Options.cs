using System;
using System.Collections.Generic;

using XRL;
using XRL.World;
using XRL.World.Parts;
using XRL.World.Parts.Mutation;
using XRL.World.WorldBuilders;

namespace UD_Relic_Revealer
{
    [HasModSensitiveStaticCache]
    [HasOptionFlagUpdate(Prefix = "Option_UD_Relic_Revealer_")]
    public static class Options
    {
        [OptionFlag] public static bool EnableShowOnWorldGen;
        [OptionFlag] public static bool EnableReshowOnGameLoad;
        
        public static bool _EnableRelicRevealerActivatedAbility;
        [OptionFlag] public static bool EnableRelicRevealerActivatedAbility
        {
            get => _EnableRelicRevealerActivatedAbility;
            set
            {
                _EnableRelicRevealerActivatedAbility = value;
                if (The.Player is GameObject player
                    && player.RequirePart<UD_Player_RelicRevealer>() is UD_Player_RelicRevealer playerRelicRevealer)
                {
                    if (value)
                        playerRelicRevealer.AddAbilities(player);
                    else
                        playerRelicRevealer.RemoveAbilities(player);
                }
            }
        }
    }
}
