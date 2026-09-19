using HarmonyLib;

using System;

using HistoryKit;
using XRL.World;
using System.Linq;
using XRL.World.Parts;

namespace UD_Relic_Revealer.Mod.Harmony
{
    [HarmonyPatch(typeof(RelicGenerator))]
    public static class RelicGenerator_Patches
    {
        /// <summary>
        /// Prefix patche for <see cref="RelicGenerator.GenerateRelic(HistoricEntitySnapshot, string, bool)"/> to get the tier of the relic to be generated, 
        /// pass it and the original arguments to <see cref="RelicGenerator.GenerateRelic(HistoricEntitySnapshot, int, string, bool)"/>, 
        /// and assign the generated relic to <paramref name="__result"/>.<br/>
        /// If the result is <see langword="null"/>, returns <see langword="true"/> to fall back to the original method; otherwise, returns <see langword="false"/> to skip the original method.
        /// </summary>
        /// <param name="__result">Original return value</param>
        /// <param name="Snapshot">Original argument</param>
        /// <param name="Type">Original argument</param>
        /// <param name="RandomName">Original argument</param>
        /// <returns>
        /// <see langword="true"/> to fall back to the original method ff the result is <see langword="null"/>; otherwise, <br/>
        /// <see langword="false"/> to skip the original method
        /// </returns>
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

        /// <summary>
        /// Postfix patch of <see cref="RelicGenerator.GenerateRelic(HistoricEntitySnapshot, int, string, bool)"/> to add a string property to the resultant relic object containing the relic's "period".
        /// </summary>
        /// <param name="__result">Original return value</param>
        /// <param name="Snapshot">Original argument</param>
        [HarmonyPatch(
            declaringType: typeof(RelicGenerator),
            methodName: nameof(RelicGenerator.GenerateRelic),
            argumentTypes: new Type[]
            {
                typeof(HistoricEntitySnapshot),
                typeof(int),
                typeof(string),
                typeof(bool),
            },
            argumentVariations: new ArgumentType[]
            {
                ArgumentType.Normal,
                ArgumentType.Normal,
                ArgumentType.Normal,
                ArgumentType.Normal,
            })]
        [HarmonyPostfix]
        public static void GenerateRelic_AddPops_Postfix(
            ref GameObject __result,
            HistoricEntitySnapshot Snapshot
            )
        {
            string itemType = Snapshot.GetProperty("itemType");
            if (itemType == "unknown")
            {
                if (__result?.GetStringProperty(RelicRecord.RelicTypeProp) is string type)
                    itemType = RelicGenerator.TypeMap.Where(e => e.Value == type).Select(e => e.Key).GetRandomElementCosmetic();

                if (itemType == "hammers")
                    itemType = "hammer";

                if (itemType == "boot"
                    || itemType == "glove")
                    itemType += "s";
            }

            string periodString = Snapshot.GetProperty("period");

            if (__result.TryGetPart(out UD_RelicData relicData))
            {
                if (int.TryParse(periodString, out int period))
                    relicData.Period = period;

                relicData.ItemType = itemType;
            }

            __result?.SetStringProperty(RelicRecord.RelicEraProp, periodString);
            __result?.SetStringProperty(RelicRecord.RelicItemTypeProp, itemType);
        }

        /// <summary>
        /// Postfix patch of <see cref="RelicGenerator.GenerateBaseRelic(string, int, bool)"/> to adjust the "Level" <see cref="Statistic"/> of the resultant relic, provided its <paramref name="Type"/> is "Book", to be <paramref name="Tier"/> * 5, with the goal being to make the book relic's tier match the intended one.<br/>
        /// <br/>
        /// Also used to add the final string Type as a string property of the relic.
        /// </summary>
        /// <param name="__result">Original return value</param>
        /// <param name="Type">Original argument</param>
        /// <param name="Tier">Original argument</param>
        [HarmonyPatch(
            declaringType: typeof(RelicGenerator),
            methodName: "GenerateBaseRelic",
            argumentTypes: new Type[]
            {
                typeof(string),
                typeof(int),
                typeof(bool),
            },
            argumentVariations: new ArgumentType[]
            {
                ArgumentType.Ref,
                ArgumentType.Normal,
                ArgumentType.Normal,
            })]
        [HarmonyPostfix]
        public static void GenerateBaseRelic_UpdateBookTier_Postfix(
            ref GameObject __result,
            ref string Type,
            int Tier
            )
        {
            var relicData = new UD_RelicData
            {
                Type = Type,
                Tier = Tier,
            };

            __result.RemovePart<UD_RelicData>();
            __result.AddPart(relicData);

            __result?.SetStringProperty(RelicRecord.RelicTypeProp, Type, RemoveIfNull: true);
        }
    }
}
