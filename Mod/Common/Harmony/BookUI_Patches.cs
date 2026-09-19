using HarmonyLib;

using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;

using XRL.World;
using XRL.World.Parts;
using XRL.UI;
using XRL;

namespace UD_Relic_Revealer.Mod.Harmony
{
    [HarmonyPatch(typeof(BookUI))]
    public static class BookUI_Patches
    {
        public static bool Success { get; private set; }

        private static bool DoVomit => false;

        [HarmonyPatch(
            declaringType: typeof(BookUI),
            methodName: nameof(BookUI.RenderDynamicBook))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> RenderDynamicBook_AddTitle_Transpiler(
            IEnumerable<CodeInstruction> Instructions,
            ILGenerator Generator,
            MethodBase OriginalMethod
            )
        {
            string patchMethodName = $"{nameof(BookUI_Patches)}.{nameof(BookUI.RenderDynamicBook)}";

            CodeMatcher codeMatcher = new(Instructions, Generator);

            int metricsCheckSteps = 0;

            if (DoVomit)
                Utils.Info($"{patchMethodName}, untouched");
            codeMatcher.Vomit(Generator, DoVomit);

            // 	BookInfo bookInfo = new BookInfo
            // 	{
            // 		Dynamic = true
            // 	};
            //
            //      IL_00cf: newobj instance void XRL.UI.BookInfo::.ctor()
            //      IL_00d4: dup
            //      IL_00d5: ldc.i4.1
            //      IL_00d6: stfld bool XRL.UI.BookInfo::Dynamic
            //      IL_00db: stloc.3

            var match_new_BookInfo_Dynamic_True = new CodeMatch[5]
            {
                new(OpCodes.Newobj, AccessTools.Constructor(typeof(BookInfo), new Type[0])),
                new(OpCodes.Dup),
                new(OpCodes.Ldc_I4_1),
                new(OpCodes.Stfld, AccessTools.Field(typeof(BookInfo), nameof(BookInfo.Dynamic))),
                new(OpCodes.Stloc_3),
            };

            if (codeMatcher.Start().MatchEndForward(match_new_BookInfo_Dynamic_True).IsInvalid)
            {
                Success = false;
                Utils.LogTranspilationError(
                    PatchMethodName: patchMethodName,
                    Pos: codeMatcher.Pos,
                    MetricsCheckSteps: metricsCheckSteps,
                    CodeMatchMethod: nameof(CodeMatcher.MatchStartForward),
                    CodeMatchesName: nameof(match_new_BookInfo_Dynamic_True),
                    CodeMatches: match_new_BookInfo_Dynamic_True,
                    Indent: 1);
                codeMatcher.Vomit(Generator, DoVomit);
                return Instructions;
            }
            metricsCheckSteps++;

            var inst_ConcatString_Loc0 = new CodeInstruction[]
            {
                new(OpCodes.Dup),
                new(OpCodes.Ldstr, "XRL.World.Parts."),
                new(OpCodes.Ldloc_0),
                new(OpCodes.Call, AccessTools.Method(typeof(string), nameof(string.Concat), new Type[] { typeof(string), typeof(string) })),
            };

            var inst_ResolveType_String = new CodeInstruction[]
            {
                new(OpCodes.Ldc_I4_0),
                new(OpCodes.Ldc_I4_0),
                new(OpCodes.Ldc_I4_1),
                new(OpCodes.Call, AccessTools.Method(typeof(ModManager), nameof(ModManager.ResolveType), new Type[] { typeof(string), typeof(bool), typeof(bool), typeof(bool) })),
            };

            var inst_GetMethod_GetTitle = new CodeInstruction[]
            {
                new(OpCodes.Ldstr, "GetTitle"),
                new(OpCodes.Callvirt, AccessTools.Method(typeof(Type), nameof(Type.GetMethod), new Type[] { typeof(string) })),
            };

            var inst_Invoke_GetTitle_obj = new CodeInstruction[]
            {
                new(OpCodes.Ldloc_1),
                new(OpCodes.Ldc_I4_0),
                new(OpCodes.Newarr, typeof(object)),
                new(OpCodes.Callvirt, AccessTools.Method(typeof(MethodBase), nameof(MethodBase.Invoke), new Type[] { typeof(object), typeof(object[]) })),
            };

            var inst_CastString_AssignTitle = new CodeInstruction[]
            {
                new(OpCodes.Castclass, typeof(string)),
                new(OpCodes.Stfld, AccessTools.Field(typeof(BookInfo), nameof(BookInfo.Title))),
            };

            var inst_BookInfo_Title_type_GetTitle_obj = new List<CodeInstruction>();

            inst_BookInfo_Title_type_GetTitle_obj.AddRange(inst_ConcatString_Loc0);
            inst_BookInfo_Title_type_GetTitle_obj.AddRange(inst_ResolveType_String);
            inst_BookInfo_Title_type_GetTitle_obj.AddRange(inst_GetMethod_GetTitle);
            inst_BookInfo_Title_type_GetTitle_obj.AddRange(inst_Invoke_GetTitle_obj);
            inst_BookInfo_Title_type_GetTitle_obj.AddRange(inst_CastString_AssignTitle);

            codeMatcher/*.Advance(1)*/.InsertAndAdvance(inst_BookInfo_Title_type_GetTitle_obj);

            if (DoVomit)
                Utils.Info($"{patchMethodName}, inserted {nameof(inst_BookInfo_Title_type_GetTitle_obj)}");

            codeMatcher.Vomit(DoVomit, 12);

            // success??

            return codeMatcher.Vomit(Generator, DoVomit).InstructionEnumeration();
        }

        [HarmonyCleanup]
        public static Exception RenderDynamicBook_AddTitle_Cleanup(MethodBase OriginalMethod, Exception Exception)
        {
            if (OriginalMethod == null)
                return Exception;

            string patchMethodName = $"{nameof(BookUI_Patches)}.{nameof(BookUI.RenderDynamicBook)}";

            if (Exception != null)
            {
                Success = false;
                Utils.Warn($"Failed to transpile {patchMethodName}", DoVomit ? Exception : null);
                return null;
            }

            Success = true;
            Utils.Info($"Successfully transpiled {patchMethodName}");
            return null;
        }
    }
}
