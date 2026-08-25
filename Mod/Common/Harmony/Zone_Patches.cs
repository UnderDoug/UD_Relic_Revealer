using HarmonyLib;

using System;

using XRL.World;

using UD_Relic_Revealer.Mod.Events;

namespace UD_Relic_Revealer.Mod.Harmony
{
    [HarmonyPatch(typeof(Zone))]
    public static class Zone_Patches
    {
        [HarmonyPatch(
            declaringType: typeof(Zone),
            methodName: nameof(Zone.Activated))]
        [HarmonyPostfix]
        public static void ZoneSend_IncludeAfter_Postfix(ref Zone __instance)
        {
            AfterZoneActivatedEvent.Send(__instance);
        }

        [HarmonyPatch(
            declaringType: typeof(InteriorZone),
            methodName: nameof(InteriorZone.Activated))]
        [HarmonyPostfix]
        public static void InteriorZoneSend_IncludeAfter_Postfix(ref Zone __instance)
        {
            AfterZoneActivatedEvent.Send(__instance);
        }
    }
}
