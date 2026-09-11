using HarmonyLib;

using XRL.World;

using UD_Relic_Revealer.Mod.Events;

namespace UD_Relic_Revealer.Mod.Harmony
{
    [HarmonyPatch(typeof(Zone))]
    public static class Zone_Patches
    {
        /// <summary>
        /// Postfix patch of <see cref="Zone.Activated"/> to send custom <see cref="MinEvent"/>, <see cref="AfterZoneActivatedEvent"/>.
        /// </summary>
        /// <param name="__instance">The <see cref="Zone"/> calling the patched method</param>
        [HarmonyPatch(
            declaringType: typeof(Zone),
            methodName: nameof(Zone.Activated))]
        [HarmonyPostfix]
        public static void ZoneSend_IncludeAfter_Postfix(ref Zone __instance)
        {
            AfterZoneActivatedEvent.Send(__instance);
        }

        /// <summary>
        /// Postfix patch of <see cref="InteriorZone.Activated"/> to send custom <see cref="MinEvent"/>, <see cref="AfterZoneActivatedEvent"/>.
        /// </summary>
        /// <param name="__instance">The <see cref="Zone"/> calling the patched method</param>
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
