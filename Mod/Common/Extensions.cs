using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using XRL;
using XRL.Collections;
using XRL.Language;
using XRL.World;
using XRL.World.Capabilities;
using XRL.World.Parts;
using XRL.World.Text;
using XRL.World.Text.Attributes;
using ReplacerContext = XRL.World.Text.Delegates.DelegateContext;

using UD_Relic_Revealer.Mod.UI;

namespace UD_Relic_Revealer.Mod
{
    public static class Extensions
    {
        public static IEnumerable<T> IteratorSafe<T>(this IEnumerable<T> Source)
            => Source ?? Enumerable.Empty<T>()
            ;

        public static char GetNextHotKey(
            this IEnumerable<char> Source,
            IEnumerable<char> Excluding = null,
            char StartAt = 'a',
            char FinishAt = 'z'
            )
        {
            char lastHotkey = Source.LastOrDefault(c => Excluding?.Contains(c) is not true);

            if (lastHotkey == default)
                return StartAt;

            if (lastHotkey != ' ')
            {
                if (lastHotkey == '\0'
                    || lastHotkey == default)
                    lastHotkey = StartAt;

                while (Source.Contains(lastHotkey)
                    && lastHotkey <= FinishAt)
                    lastHotkey++;

                if (lastHotkey > FinishAt)
                    lastHotkey = ' ';
            }
            return lastHotkey;
        }

        public static IEnumerable<string> FramesToStrings(this StackTrace StackTrace, int? Count = null, int SkipLines = 0)
        {
            StackTrace ??= new(SkipLines + 1);
            var frames = StackTrace.GetFrames();
            int count = frames?.Length ?? 0;
            count = Math.Min(Count ?? count, count);
            for (int i = 0; i < count; i++)
                if (frames[i] is StackFrame frame)
                    yield return frame.ToString();
        }

        public static string FramesToString(this StackTrace StackTrace, int? Count = null, int SkipLines = 0, string TextLineBefore = null)
            => StackTrace.FramesToStrings(Count, SkipLines + 1)
                .Aggregate(
                    seed: TextLineBefore,
                    func: Utils.NewLineDelimitedAggregator)
            ;

        public static IEnumerable<T> Loggregate<T>(
            this IEnumerable<T> Source,
            Func<T, string> Proc = null,
            string Empty = null,
            Func<string, string> PostProc = null
            )
            => Utils.Loggregate(
                Source: Source,
                Proc: Proc,
                Empty: Empty,
                PostProc: PostProc)
            ;

        public static bool IsPooled(this GameObject Object)
            => Object != null
            && (Object.Flags & GameObject.FLAG_POOLED) != 0
            ;

        public static bool None<TSource>(this IEnumerable<TSource> Source, Func<TSource, bool> predicate)
            => !Source.Any(predicate)
            ;

        public static void PerformActionRecursively(this GameObject Object, Action<GameObject, int> Action, int Depth = 0)
        {
            Action.Invoke(Object, Depth);

            foreach (var inventoryObject in Object.GetInventoryAndEquipmentAndDefaultEquipment().IteratorSafe())
                inventoryObject.PerformActionRecursively(Action, Depth + 1);

            foreach (var installedCybernetic in Object.GetInstalledCybernetics().IteratorSafe())
                installedCybernetic.PerformActionRecursively(Action, Depth + 1);

            foreach (var contentsObject in Object.GetContents().IteratorSafe())
                contentsObject.PerformActionRecursively(Action, Depth + 1);
        }

        public static IEnumerable<GameObject> GetObjectsRecursively(
            this GameObject Object,
            Predicate<GameObject> Where = null,
            int Depth = 0,
            int? MaxDepth = null
            )
        {
            if (MaxDepth.HasValue
                && MaxDepth.GetValueOrDefault() > Depth)
                yield break;

            if (Where?.Invoke(Object) is not false)
                yield return Object;

            foreach (var inventoryObject in Object.GetInventoryAndEquipmentAndDefaultEquipment().IteratorSafe())
                foreach (var recursiveObject in inventoryObject.GetObjectsRecursively(Where, Depth + 1, MaxDepth))
                    if (Where?.Invoke(recursiveObject) is not false)
                        yield return recursiveObject;

            foreach (var installedCybernetic in Object.GetInstalledCybernetics().IteratorSafe())
                foreach (var recursiveObject in installedCybernetic.GetObjectsRecursively(Where, Depth + 1, MaxDepth))
                    if (Where?.Invoke(recursiveObject) is not false)
                        yield return recursiveObject;

            foreach (var contentsObject in Object.GetContents().IteratorSafe())
                foreach (var recursiveObject in contentsObject.GetObjectsRecursively(Where, Depth + 1, MaxDepth))
                    if (Where?.Invoke(recursiveObject) is not false)
                        yield return recursiveObject;
        }

        public static void PerformActionRecursivelyInRandomOrder(
            this GameObject Object,
            Action<GameObject> Action,
            Predicate<GameObject> Where = null,
            int Depth = 0,
            Random Rnd = null
            )
        {
            using var objectsList = ScopeDisposedList<GameObject>.GetFromPoolFilledWith(Object.GetObjectsRecursively(Where, Depth));
            objectsList.ShuffleInPlace(Rnd);
            foreach (var randomObject in objectsList)
                Action.Invoke(randomObject);
        }

        public static void PerformActionRecursively(this GameObject Object, Action<GameObject> Action, int Depth = 0)
            => Object.PerformActionRecursively(
                Action: delegate (GameObject go, int depth)
                {
                    Action.Invoke(go);
                },
                Depth: Depth)
            ;

        public static IEnumerable<T> PerformFunctionRecursively<T>(
            this GameObject Object,
            Func<GameObject, int, T> Func,
            int Depth = 0
            )
        {
            using var result = ScopeDisposedList<T>.GetFromPool();
            result.Add(Func.Invoke(Object, Depth));

            int newDepth = Depth + 1;
            var inventoryObjects = Object.GetInventoryAndEquipmentAndDefaultEquipment().IteratorSafe();
            foreach (var inventoryObject in inventoryObjects)
                foreach (var output in inventoryObject.PerformFunctionRecursively(Func, newDepth).IteratorSafe())
                    result.Add(output);

            var installedCybernetics = Object.GetInstalledCybernetics().IteratorSafe();
            foreach (var installedCybernetic in installedCybernetics)
                foreach (var output in installedCybernetic.PerformFunctionRecursively(Func, newDepth).IteratorSafe())
                    result.Add(output);

            var contentsObjects = Object.GetContents().IteratorSafe();
            foreach (var contentsObject in contentsObjects)
                foreach (var output in contentsObject.PerformFunctionRecursively(Func, newDepth).IteratorSafe())
                    result.Add(output);

            while (!result.IsNullOrEmpty()
                && result.TakeAt(0) is T output)
                yield return output;
        }

        public static IEnumerable<T> PerformFunctionRecursively<T>(this GameObject Object, Func<GameObject, T> Func, int Depth = 0)
            => Object.PerformFunctionRecursively(
                Func: delegate (GameObject go, int depth)
                {
                    return Func.Invoke(go);
                },
                Depth: Depth)
            ;

        public static StringBuilder AppendPair<TKey, TValue>(this StringBuilder SB, TKey Key, TValue Value)
            => SB.Append(Key).Append(": ").Append(Value)
            ;

        public static StringBuilder AppendPair<TKey, TValue>(this StringBuilder SB, KeyValuePair<TKey, TValue> KVP)
            => SB.AppendPair(KVP.Key, KVP.Value)
            ;

        public static string Colored(this string Text, string Color)
            => Color != null
            ? Text?.WithColor(Color)
            : Text
            ;

        public static string Are(this GameObject Object)
            => Object.IsPlural
            ? "are"
            : "is"
            ;

        public static void SuspendExaminerDuringAction(this GameObject Relic, Action Action)
        {
            int epistemicStatus = -1;
            var examiner = Relic?.GetPart<Examiner>();
            if (examiner != null)
            {
                epistemicStatus = examiner.EpistemicStatus;
                examiner.EpistemicStatus = Examiner.EPISTEMIC_STATUS_KNOWN;
            }
            try
            {
                Action?.Invoke();
            }
            finally
            {
                if (examiner != null)
                    examiner.EpistemicStatus = epistemicStatus;
            }
        }

        public static void RemoveAll<T>(this ScopeDisposedList<T> Source, Predicate<T> Where)
        {
            if (Source.IsNullOrEmpty())
                return;

            for (int i = Source.Count - 1; i >= 0; i--)
                if (Where?.Invoke(Source[i]) is true)
                    Source.RemoveAt(i);
        }

        public static void RemoveLast<T>(this ScopeDisposedList<T> Source)
        {
            if (Source.IsNullOrEmpty())
                return;

            Source.RemoveAt(Source.Count - 1);
        }

        public static bool IsEmptyOrDefault(this Guid Guid)
            => Guid == default
            || Guid == Guid.Empty
            ;

        public static string OrdinalSuffix(this int Number)
            => $"{Number}{Grammar.Ordinal(Number)[^2..]}"
            ;

        public static IEnumerable<GameObjectBlueprint> SafelyGetBlueprintsInheritingFrom(
            this GameObjectFactory Factory,
            string Name,
            bool ExcludeBase = true,
            bool IncludeSelf = false
            )
        {
            foreach (GameObjectBlueprint blueprint in Factory.BlueprintList.IteratorSafe())
                if (blueprint.InheritsFromSafe(Name, IncludeSelf)
                    && (!ExcludeBase
                        || !blueprint.IsBaseBlueprint()))
                    yield return blueprint;
        }

        public static List<string> InheritanceRoots => new()
        {
            nameof(Object),
            "SultanMuralController",
        };

        public static bool InheritsFromSafe(
            this GameObjectBlueprint GameObjectBlueprint,
            string What,
            bool IncludeSelf = true
            )
        {
            if (IncludeSelf
                && GameObjectBlueprint?.Name == What)
                return true;

            string parentBlueprint = GameObjectBlueprint.Inherits;
            while (!parentBlueprint.IsNullOrEmpty())
            {
                if (parentBlueprint == What)
                    return true;

                string inherits = parentBlueprint;
                parentBlueprint = GameObjectFactory.Factory?.GetBlueprintIfExists(parentBlueprint)?.Inherits;
                if (parentBlueprint.IsNullOrEmpty()
                    && !InheritanceRoots.Contains(inherits))
                {
                    Utils.WarnOnce($"{nameof(Extensions)}.{nameof(InheritsFromSafe)}(\"{What}\"):" +
                        $" bluprint ancestor \"{inherits}\" does not exist in blueprint list." +
                        $" The first mention of this blueprint in this log should reveal the mod with this inheritance issue.");
                }
            }
            return false;
        }

        public static T WaitResult<T>(this Task<T> Task)
        {
            if (Task == null)
                return default;

            Task.Wait();

            return Task.Result;
        }

        public static async Task<TResult> AwaitResultIfNotIsCompletedSuccessfully<TResult>(this Task<TResult> ResultTask)
            => (ResultTask?.IsCompletedSuccessfully) is true
            ? ResultTask.Result
            : await ResultTask
            ;

        public static bool IsTwixt(this int Value, int LowerInclusive, int UpperExclusive)
            => Value >= LowerInclusive
            && Value < UpperExclusive
            ;

        public static ulong ToUInt64<T>(this T Value)
            where T : Enum
            => Convert.GetTypeCode(Value) switch
            {
                TypeCode.SByte or
                TypeCode.Int16 or
                TypeCode.Int32 or
                TypeCode.Int64 => (ulong)Convert.ToInt64(Value, CultureInfo.InvariantCulture),

                TypeCode.Boolean or
                TypeCode.Char or
                TypeCode.Byte or
                TypeCode.UInt16 or
                TypeCode.UInt32 or
                TypeCode.UInt64 => Convert.ToUInt64(Value, CultureInfo.InvariantCulture),

                _ => throw new InvalidOperationException("Unknown enum type."),
            };

        public static bool IsTwixtInclusive<T>(this T Value, T Lower, T Upper)
            where T : Enum
            => Value.ToUInt64() >= Lower.ToUInt64()
            && Value.ToUInt64() <= Upper.ToUInt64()
            ;

        public static bool? ToNullableBool(this UIUtils.CascadableResult Value)
        {
            if (Value <= UIUtils.CascadableResult.Continue)
                return true;

            if (Value <= UIUtils.CascadableResult.Back)
                return false;

            return null;
        }

        public static bool ToBool(this UIUtils.CascadableResult Value)
            => Value <= UIUtils.CascadableResult.Continue
            ;

        public static bool IsContinue(this UIUtils.CascadableResult Value)
            => Value.IsTwixtInclusive(UIUtils.CascadableResult.Continue, UIUtils.CascadableResult.Continue)
            ;

        public static bool IsBack(this UIUtils.CascadableResult Value)
            => Value.IsTwixtInclusive(UIUtils.CascadableResult.Back, UIUtils.CascadableResult.BackSilent)
            ;

        public static bool IsCancel(this UIUtils.CascadableResult Value)
            => Value >= UIUtils.CascadableResult.Cancel
            ;

        public static bool IsSilent(this UIUtils.CascadableResult Value)
            => ((int)Value % 2) == ((int)UIUtils.CascadableResult.BackSilent % 2)
            ;

        public static UIUtils.CascadableResult ToCascadableResult(this bool? Value, bool Silent)
        {
            if (Value.GetValueOrDefault())
                return UIUtils.CascadableResult.Continue;

            var result = Value.HasValue
                ? UIUtils.CascadableResult.Back
                : UIUtils.CascadableResult.Cancel
                ;

            if (Silent)
                result++;

            return result;
        }

        public static UIUtils.CascadableResult ContinueIfNotCancel(this UIUtils.CascadableResult Value)
            => !Value.IsCancel()
            ? UIUtils.CascadableResult.Continue
            : Value
            ;

        public static TAccumulate Aggregate<TAccumulate>(
            this int Number,
            TAccumulate seed,
            Func<TAccumulate, int, TAccumulate> func
            )
        {
            for (int i = 0; i < Number; i++)
                seed = func(seed, i);

            return seed;
        }

        public static string ThisManyTimes(this string @string, int Times = 1)
            => Times.Aggregate("", (a, n) => a + @string)
            ;

        public static string ThisManyTimes(this char @char, int Times = 1)
            => @char.ToString().ThisManyTimes(Times)
            ;

        public static string Indent(this int Amount, int Factor = 2, int MaxIndent = 12, bool NBSP = false)
        {
            if (!NBSP)
                return Amount > 0
                    ? " ".ThisManyTimes(Math.Min(Amount * Math.Max(1, Factor), MaxIndent * Factor))
                    : null
                    ;
            else
                return Amount > 0
                    ? $"=ud_nbsp:{Math.Min(Amount * Math.Max(1, Factor), MaxIndent * Factor)}=".StartReplace().ToString()
                    : null
                    ;
        }

        [VariableReplacer]
        public static string ud_nbsp(ReplacerContext Context)
        {
            string output = Utils.NBSP;
            if (!Context.Parameters.IsNullOrEmpty()
                && int.TryParse(Context.Parameters[0], out int count))
                output = Utils.NBSP.ThisManyTimes(count);

            return output;
        }

        public static StringBuilder AppendLineEnd(this StringBuilder SB)
            => SB.AppendLine().Append("=ud_nbsp=".StartReplace().ToString())
            ;

        public static TextBuilder AppendLineEnd(this TextBuilder TB)
            => TB.AppendLine().Append("=ud_nbsp=".StartReplace().ToString())
            ;

        public static TextBuilder AppendRules(this TextBuilder TB, Action<TextBuilder> appender)
        {
            TB.Append("\n{{rules|");
            appender(TB);
            TB.Append("}}");
            return TB;
        }

        public static TextBuilder AppendRules(this TextBuilder TB, string text)
            => !text.IsNullOrEmpty()
            ? TB.AppendLine().AppendColored("rules", text)
            : TB
            ;

        public static StringBuilder AppendRule(this StringBuilder SB, object Value)
            => !(Value?.ToString()).IsNullOrEmpty()
            ? SB.AppendColored("rules", Value.ToString())
            : SB
            ;

        public static TextBuilder AppendRule(this TextBuilder TB, object Value)
            => !(Value?.ToString()).IsNullOrEmpty()
            ? TB.AppendColored("rules", Value.ToString())
            : TB
            ;

        public static StringBuilder AppendQuote(this StringBuilder SB, object Value)
            => SB.Append("\"").Append(Value).Append("\"")
            ;

        public static TextBuilder AppendQuote(this TextBuilder TB, object Value)
            => TB.Append("\"").Append(Value).Append("\"")
            ;

        public static StringBuilder AppendBullet(
            this StringBuilder SB,
            string Color = null,
            string Bullet = "\u0007"
            )
        {
            if (Color.IsNullOrEmpty())
                SB.Append(Bullet);
            else
                SB.AppendColored(Color, Bullet);

            return SB.Append(" ");
        }

        public static StringBuilder AppendBulletLine(
            this StringBuilder SB,
            string Color = null,
            string Bullet = "\u0007"
            )
            => SB.AppendLine().AppendBullet(Color, Bullet)
            ;


        public static TextBuilder AppendIndent(this TextBuilder TB, int Amount = 0, int Factor = 2, int MaxIndent = 12, bool AsNBSP = false)
            => TB.Append(Amount.Indent(Factor, MaxIndent, AsNBSP))
            ;

        public static TextBuilder AppendColored(this TextBuilder TB, string color, string text)
            => TB.Append("{{").Append(color).Append("|")
                .Append(text)
                .Append("}}")
            ;

        public static string GetPlural(this bool IsPlural, string Singular, string Plural = null)
            => IsPlural
            ? Plural ?? Singular.Pluralize()
            : Singular
            ;
    }
}
