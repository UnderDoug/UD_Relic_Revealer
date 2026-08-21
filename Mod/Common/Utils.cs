using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

using ConsoleLib.Console;

using Qud.UI;

using XRL;
using XRL.Collections;
using XRL.UI;
using XRL.Wish;
using XRL.World;
using XRL.World.Capabilities;
using XRL.World.Parts;

namespace UD_Relic_Revealer.Mod
{
    [HasGameBasedStaticCache]
    public static class Utils
    {
        public const string MOD_ID = "UD_Relic_Revealer";

        public static ModInfo ThisMod => ModManager.GetMod(MOD_ID);

        public static XRL.Version ModVersion => ThisMod.Manifest.Version;
        public static string ModTitle => ThisMod.Manifest.Title;
        public static string Author => ThisMod.Manifest.Author;
        public static string AuthorOnPlatforms => $"{Author} on GitHub (UnderDoug), on Discord (.underdoug), or on the Steam Workshop (UnderDoug)";

        public const string TICK = "\u221A";  // √
        public const string CROSS = "\u0058"; // X

        public const string BULLET = "\u0007"; // •
        public const string NBSP = "\xFF"; // " "
        public const string DF = "\u000f"; // ☼

        #region Pseudo-Debug

        public static HashSet<string> SingleTimeLogMessages = new();
        public static Dictionary<ModInfo, HashSet<string>> SingleTimeErrorMessages = new();
        public static Dictionary<ModInfo, HashSet<string>> SingleTimeWarnMessages = new();

        public static void Error(ModInfo ModInfo, object Message)
            => (ModInfo ?? ThisMod).Error(Message)
            ;

        public static void Error(object Message)
            => Error(ModInfo: null, Message)
            ;

        public static void Error(ModInfo ModInfo, object Context, Exception X)
            => Error(ModInfo, $"{Context}: {X}")
            ;

        public static void Error(object Context, Exception X)
            => Error(ModInfo: null, Context, X)
            ;

        public static void ErrorOnce(ModInfo ModInfo, object Message)
        {
            ModInfo ??= ThisMod;
            SingleTimeErrorMessages ??= new();
            if (!SingleTimeErrorMessages.ContainsKey(ModInfo))
                SingleTimeErrorMessages[ModInfo] = new();

            string message = Message.ToString();
            if (SingleTimeErrorMessages[ModInfo].Add(message))
                ModInfo.Error(message);
        }

        public static void ErrorOnce(object Message)
            => ErrorOnce(ModInfo: null, Message)
            ;

        public static void ErrorOnce(ModInfo ModInfo, object Context, Exception X)
        {
            ModInfo ??= ThisMod;
            SingleTimeErrorMessages ??= new();
            if (!SingleTimeErrorMessages.ContainsKey(ModInfo))
                SingleTimeErrorMessages[ModInfo] = new();

            string message = Context.ToString();
            if (SingleTimeErrorMessages[ModInfo].Add(message))
                ModInfo.Error($"{message}: {X}");
        }

        public static void ErrorOnce(object Context, Exception X)
            => ErrorOnce(ModInfo: null, Context, X)
            ;

        public static void Warn(ModInfo ModInfo, object Message)
            => (ModInfo ?? ThisMod).Warn(Message)
            ;

        public static void Warn(object Message)
            => Warn(ModInfo: null, Message)
            ;

        public static void Warn(ModInfo ModInfo, object Context, Exception X)
            => Warn(ModInfo, $"{Context}: {X}")
            ;

        public static void Warn(object Context, Exception X)
            => Warn(ModInfo: null, Context, X)
            ;

        public static void Warn(ModInfo ModInfo, object Message, StackTrace WithTrace)
            => Warn(ModInfo: ModInfo, Message: (WithTrace ?? new StackTrace(1)).FramesToString(Count: 5, SkipLines: 0, TextLineBefore: $"{Message}:"))
            ;

        public static void Warn(object Message, StackTrace WithTrace)
            => Warn(ModInfo: null, Message: Message, WithTrace: WithTrace ?? new StackTrace(1))
            ;

        public static void WarnOnce(ModInfo ModInfo, object Message)
        {
            ModInfo ??= ThisMod;
            SingleTimeWarnMessages ??= new();
            if (!SingleTimeWarnMessages.ContainsKey(ModInfo))
                SingleTimeWarnMessages[ModInfo] = new();

            string message = Message.ToString();
            if (SingleTimeWarnMessages[ModInfo].Add(message))
                ModInfo.Warn(message);
        }

        public static void WarnOnce(object Message)
            => WarnOnce(ModInfo: null, Message)
            ;

        public static void WarnOnce(ModInfo ModInfo, object Context, Exception X)
        {
            ModInfo ??= ThisMod;
            SingleTimeWarnMessages ??= new();
            if (!SingleTimeWarnMessages.ContainsKey(ModInfo))
                SingleTimeWarnMessages[ModInfo] = new();

            string message = Context.ToString();
            if (SingleTimeWarnMessages[ModInfo].Add(message))
                ModInfo.Warn($"{message}: {X}");
        }

        public static void WarnOnce(object Context, Exception X)
            => WarnOnce(ModInfo: null, Context, X)
            ;

        public static void WarnOnce(ModInfo ModInfo, object Message, StackTrace WithTrace)
            => WarnOnce(ModInfo: ModInfo, Message: (WithTrace ?? new StackTrace(1)).FramesToString(Count: 5, SkipLines: 0, TextLineBefore: $"{Message}:"))
            ;

        public static void WarnOnce(object Message, StackTrace WithTrace)
            => WarnOnce(ModInfo: null, Message: Message, WithTrace: WithTrace ?? new StackTrace(1))
            ;

        public static void Info(object Message)
            => MetricsManager.LogModInfo(ThisMod, Message)
            ;

        public static void Log(object Message)
            => UnityEngine.Debug.Log(Message)
            ;

        public static void LogOnce(object Message)
        {
            SingleTimeLogMessages ??= new();
            string message = Message.ToString();
            if (SingleTimeLogMessages.Add(message))
                Log(message);
        }

        public static T LogReturn<T>(object Message, T Return)
        {
            Log(Message);
            return Return;
        }

        private static string SafeInvoke<T>(this Func<string, string> PostProc, Func<T, string> Proc, T Element, string NoArg)
        {
            string proc = Proc?.Invoke(Element) ?? Element?.ToString() ?? NoArg;
            if (PostProc != null)
                proc = PostProc(proc);
            return proc;
        }

        public static IEnumerable<T> Log<T>(IEnumerable<T> Source, object Message)
        {
            Log(Message);
            return Source;
        }

        public static IEnumerable<T> Loggregate<T>(
            IEnumerable<T> Source,
            Func<T, string> Proc = null,
            string Empty = null,
            Func<string, string> PostProc = null
            )
            => Source.IsNullOrEmpty()
            ? Log(Source, PostProc?.Invoke(Empty) ?? Empty)
            : Source.Aggregate(
                seed: Source,
                func: (a, n) => Log(a, PostProc.SafeInvoke(Proc, n, "NO_ELEMENT")))
            ;

        #endregion
        #region Aggregator Functions

        public static string DelimitedAggregator<T>(string Accumulator, T Next, string Delimiter)
           => $"{Accumulator}{(!Accumulator.IsNullOrEmpty() ? Delimiter : null)}{Next}"
           ;

        public static string CommaDelimitedAggregator<T>(string Accumulator, T Next)
            => DelimitedAggregator(Accumulator, Next, ",")
            ;

        public static string CommaSpaceDelimitedAggregator<T>(string Accumulator, T Next)
            => DelimitedAggregator(Accumulator, Next, ", ")
            ;

        public static string NewLineDelimitedAggregator<T>(string Accumulator, T Next)
            => DelimitedAggregator(Accumulator, Next, "\n")
            ;

        public static string PeriodDelimitedAggregator<T>(string Accumulator, T Next)
            => DelimitedAggregator(Accumulator, Next, ".")
            ;

        public static string PeriodSpaceDelimitedAggregator<T>(string Accumulator, T Next)
            => DelimitedAggregator(Accumulator, Next, ". ")
            ;

        public static string PipeDelimitedAggregator<T>(string Accumulator, T Next, Func<string, T, string> Proc)
            => DelimitedAggregator(Accumulator, Proc?.Invoke(Accumulator, Next) ?? Next?.ToString(), "|")
            ;

        public static string PipeDelimitedAggregator<T>(string Accumulator, T Next)
            => PipeDelimitedAggregator(Accumulator, Next, null)
            ;

        public static string CallChain(params string[] Strings)
            => Strings?.Aggregate("", PeriodDelimitedAggregator)
            ;

        #endregion

        public static string GetProcessedItem(List<string> item, bool IsFirstSentence, List<List<string>> items, GameObject Object)
        {
            if (item.IsNullOrEmpty()
                || item.Count < 2)
                return null;

            string verb = item[0];
            string effect = item[1];
            var firstElement = items[0];
            bool isFirstInList = item == firstElement;
            string does = Object.GetVerb(verb, PrependSpace: false);
            string @is = Object.Are();
            switch (verb)
            {
                // "It effect" || "effect"
                case "":
                    if (!IsFirstSentence
                        && isFirstInList)
                        verb = $"{Object.It} ";
                    break;

                // "It is effect" || "is effect"
                case null:
                    if (!IsFirstSentence
                        && isFirstInList)
                        verb = $"{Object.Itis} ";
                    else
                    if (!isFirstInList
                        || items.All(e => e == null || e.Count < 1 || e[0] == null))
                        verb = $"{@is} ";
                    break;

                // "It verbs" || "verbs"
                default:
                    if (!IsFirstSentence
                        && isFirstInList)
                        verb = $"{Object.It} {does} ";
                    else
                        verb = $"{does} ";
                    break;
            }
            return GameText.VariableReplace($"{verb}{effect}", Object);
        }

        public class StringPair
        {
            public KeyValuePair<string, string> Pair;

            public string Key => Pair.Key;
            public string Value => Pair.Value;

            public StringPair()
            { }

            public StringPair(string Key, string Value)
                : this()
            {
                Pair = new(Key, Value);
            }

            public void Deconstruct(out KeyValuePair<string, string> Pair)
            {
                Pair = this.Pair;
            }

            public void Deconstruct(out string Key, out string Value)
            {
                Key = Pair.Key;
                Value = Pair.Value;
            }
        }

        public static string GetProcessedItem(
            StringPair Item,
            bool IsFirstSentence,
            IList<StringPair> Items,
            GameObject Object,
            string It = null,
            string Is = null,
            string ItIs = null
            )
        {
            if (Item.Key.IsNullOrEmpty()
                && Item.Value.IsNullOrEmpty())
                return null;

            string verb = Item.Key;
            string effect = Item.Value;
            var firstElement = Items[0];
            bool isFirstInList = Item == firstElement;
            string does = Object?.GetVerb(verb, PrependSpace: false) ?? verb;
            string @is = Object?.Are() ?? Is ?? "is";
            switch (verb)
            {
                // "It effect" || "effect"
                case "":
                    if (!IsFirstSentence
                        && isFirstInList)
                        verb = $"{Object?.It ?? It ?? "It"} ";
                    break;

                // "It is effect" || "is effect"
                case null:
                    if (!IsFirstSentence
                        && isFirstInList)
                        verb = $"{Object?.Itis ?? ItIs ?? "It is"} ";
                    else
                    if (!isFirstInList
                        || Items.All(e => e.Key == null))
                        verb = $"{@is} ";
                    break;

                // "It verbs" || "verbs"
                default:
                    if (!IsFirstSentence
                        && isFirstInList)
                        verb = $"{Object?.It ?? It ?? "It"} {does} ";
                    else
                        verb = $"{does} ";
                    break;
            }
            string output = $"{verb}{effect}";
            if (Object != null)
                output = GameText.VariableReplace(output, Object);
            return output;
        }
    }
}
