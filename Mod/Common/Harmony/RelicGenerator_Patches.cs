using HarmonyLib;

using System;

using HistoryKit;
using XRL.World;

namespace UD_Relic_Revealer.Mod.Harmony
{
    [HarmonyPatch(typeof(RelicGenerator))]
    public static class RelicGenerator_Patches
    {
        [HarmonyPatch(
            declaringType: typeof(RelicGenerator),
            methodName: nameof(RelicGenerator.GenerateRelic),
            argumentTypes: new Type[]
            {
                typeof(HistoricEntitySnapshot),
                typeof(string),
                typeof(bool),
            },
            argumentVariations: new ArgumentType[]
            {
                ArgumentType.Normal,
                ArgumentType.Normal,
                ArgumentType.Normal,
            })]
        [HarmonyPrefix]
        public static bool GenerateRelic_PassName_Prefix(
            ref GameObject __result,
            HistoricEntitySnapshot Snapshot,
            string Type = null,
            bool RandomName = false
            )
        {
            int tier = RelicGenerator.GetRelicTierFromPeriod(int.Parse(Snapshot.GetProperty("period")));
            __result = RelicGenerator.GenerateRelic(Snapshot, tier, Type, RandomName);
            return __result == null; // fall back to the un-patched method if the relic fails to generate; otherwise, skip it
        }
    }
}
