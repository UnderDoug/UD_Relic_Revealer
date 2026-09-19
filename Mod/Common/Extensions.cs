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
using System.Reflection;
using HarmonyLib;
using System.Reflection.Emit;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization.Formatters;
using System.Runtime.InteropServices;
using XRL.Rules;

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

        public static TextBuilder AppendPair<TKey, TValue>(this TextBuilder TB, TKey Key, TValue Value)
            => TB.Append(Key).Append(": ").Append(Value)
            ;

        public static TextBuilder AppendPair<TKey, TValue>(this TextBuilder TB, KeyValuePair<TKey, TValue> KVP)
            => TB.AppendPair(KVP.Key, KVP.Value)
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

        public static V SetFieldNaughty<T, V>(this T Object, string Field, V Value)
        {
            var type = typeof(T);
            var valueType = typeof(V);
            try
            {
                if (Field.IsNullOrEmpty())
                    throw new ArgumentException(nameof(Field), "Cannot be null or empty string");

                var field = type.GetField(Field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    ?? throw new ArgumentOutOfRangeException(nameof(Field), $"Field \"{Field}\" was not found in {nameof(Type)} {type}");

                if (!field.FieldType.IsAssignableFrom(valueType))
                    throw new InvalidCastException($"{Field} field in {nameof(Type)} {type} is {field.FieldType}, to which {valueType} cannot be cast");

                field.SetValue(Object, Value);
                if (field.GetValue(Object) is V value)
                    Utils.Info($"{type}.{nameof(SetFieldNaughty)}({nameof(Field)}: {Field})");

                return Value;
            }
            catch (ArgumentOutOfRangeException x)
            {
                Utils.Error(nameof(SetFieldNaughty), x);
                return default;
            }
            catch (InvalidCastException x)
            {
                Utils.Error(nameof(SetFieldNaughty), x);
                return default;
            }
        }

        public static bool TryGetFieldNaughty<T, V>(this T Object, string Field, out V Value)
        {
            Value = default;
            var type = typeof(T);
            var valueType = typeof(V);
            try
            {
                if (Field.IsNullOrEmpty())
                    throw new ArgumentException(nameof(Field), "Cannot be null or empty string");

                var field = type.GetField(Field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    ?? throw new ArgumentOutOfRangeException(nameof(Field), $"Field \"{Field}\" was not found in {nameof(Type)} {type}");

                if (!field.FieldType.IsAssignableFrom(valueType))
                    throw new InvalidCastException($"{Field} field in {nameof(Type)} {type} is {field.FieldType}, to which {valueType} cannot be cast");

                Value = (V)field.GetValue(Object);
                Utils.Info($"{type}.{nameof(TryGetFieldNaughty)}({nameof(Field)}: {Field}, out {valueType})");
                return true;
            }
            catch (ArgumentOutOfRangeException x)
            {
                Utils.Error(nameof(TryGetFieldNaughty), x);
                return default;
            }
            catch (InvalidCastException x)
            {
                Utils.Error(nameof(TryGetFieldNaughty), x);
                return default;
            }
        }

        public static string ToLiteral(this string String, bool Quotes = false)
        {
            if (String.IsNullOrEmpty())
                return null;

            string output = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(String, false);

            if (Quotes)
                output = $"\"{output}\"";

            return output;
        }

        public static string CorruptText(this string Text)
        {
            if (Text.IsNullOrEmpty())
                return Text;

            Text = Text.Strip();

            using var corruptions = ScopeDisposedList<string>.GetFromPool();
            while (corruptions.IsNullOrEmpty()
                || corruptions.Aggregate(0, (a, n) => a + n.Length) <= Text.Length)
            {
                string corruption = TextFilters.GenerateCrypticWord();
                if (corruption.Length < 8)
                    corruptions.Add(corruption);
            }

            int corruptionsLength = corruptions.Aggregate(0, (a, n) => a + n.Length);
            int diff = corruptionsLength - Text.Length;
            int startDiff = (int)Math.Ceiling(diff / 2.0);
            int endDiff = (int)Math.Floor(diff / 2.0);

            int startAt = 0;
            if (corruptions.Aggregate(0, (a, n) => a + n.Length) > Text.Length)
                startAt = Stat.RandomCosmetic(0, diff);

            string firstCorruption = null;
            if (startDiff > 0)
                firstCorruption = corruptions.TakeAt(0);

            string lastCorruption = null;
            if (endDiff > 0
                && corruptions.Count > 0)
                lastCorruption = corruptions.TakeAt(corruptions.Count - 1);

            if (!firstCorruption.IsNullOrEmpty()
                && startDiff > 0
                && startDiff < firstCorruption.Length)
                corruptions.Insert(0, firstCorruption[startDiff..]);

            if (!lastCorruption.IsNullOrEmpty()
                && endDiff > 0
                && endDiff < lastCorruption.Length)
                corruptions.Add(lastCorruption[endDiff..]);

            corruptionsLength = corruptions.Aggregate(0, (a, n) => a + n.Length);

            int originalStart = startAt;
            int moduloOffset = Stat.RandomCosmetic(0, 6999);

            using var descriptions = ScopeDisposedList<string>.GetFromPool();
            int startPos = 0;
            foreach (string corruption in corruptions)
            {
                int endPos = Math.Min(startPos + corruption.Length, Text.Length);

                if (startPos < endPos)
                    descriptions.Add(Text[startPos..endPos]);

                startPos = endPos;
            }

            int modOffset = Stat.RandomCosmetic(0, 6999);

            string text = descriptions.Count.Aggregate(
                seed: "",
                func: delegate (string text, int i)
                {
                    string next = ((i + moduloOffset) % 2 == 0)
                        ? descriptions[i]
                        : corruptions[i];
                    return text + next;
                });

            using var fragments = ScopeDisposedList<string>.GetFromPool();
            int colorOffset = Stat.RandomCosmetic(1, 3);
            if (50.in100())
                colorOffset *= -1;

            startPos = 0;
            for (int i = 0; i < descriptions.Count; i++)
            {
                if (descriptions[i] is string description)
                {
                    if (i > 0)
                        startPos = Math.Clamp(startPos, 0, Text.Length);

                    int endPos = Math.Clamp(startPos + description.Length + colorOffset, 0, Text.Length);

                    if (startPos < endPos)
                        fragments.Add(text[startPos..endPos]);

                    if (i == descriptions.Count - 1
                        && endPos < Text.Length)
                        fragments.Add(text[endPos..]);

                    startPos = endPos;
                }
            }

            var colorBag = new BallBag<string>
            {
                { "k", 10 },
                { "K", 50 },
                { "C", 100 },
                { "c", 100 },
                { "Y", 50 },
            };

            text = fragments.Count.Aggregate(
                seed: "",
                func: delegate (string text, int i)
                {
                    int index = (int)Math.Floor(i / 2.0);
                    if ((i + moduloOffset) % 2 == 0)
                        return text + fragments[i];
                    return text + fragments[i].Color(colorBag.PeekOne());
                });

            return text;
        }

        #region Transpilation

        public static bool IsEndOfSection(this OpCode OpCode)
            => OpCode.ToString() is not string opCodeString
            || opCodeString.StartsWith("pop")
            || opCodeString.StartsWith("br")
            || opCodeString.StartsWith("be")
            || opCodeString.StartsWith("bg")
            || opCodeString.StartsWith("bl")
            || opCodeString.StartsWith("leave")
            || opCodeString.StartsWith("ret")
            || opCodeString.StartsWith("st")
            || opCodeString.StartsWith("throw")
            || opCodeString.StartsWith("endfinally")
            ;

        public static LocalBuilder GetLocalBuilderAtIndex(this MethodBase MethodBase, int Index)
            => MethodBase.GetMethodBody().LocalVariables[Index] as LocalBuilder
            ;

        public static CodeInstruction Vomit(
            this CodeInstruction Instruction,
            int Pos,
            int PosPadding,
            Dictionary<Label, int> LabelInstructions = null,
            List<int> Offsets = null,
            bool HaveILGen = false,
            bool IncludeEnd = false,
            bool Do = false
            )
        {
            if (!Do)
                return Instruction;

            string operandString = Instruction?.operand?.VomitOperand(PosPadding, LabelInstructions, Offsets, HaveILGen, Do);

            int loc = !Offsets.IsNullOrEmpty() ? Offsets[Pos] : Pos;
            string labelString = $"[{Pos.ToString().PadLeft(PosPadding, '0')}]";
            if (HaveILGen)
                labelString = $"IL_{loc:X4}";


            Utils.Log($"{labelString} {Instruction.opcode,-10} {operandString}");
            if (IncludeEnd
                && Instruction.opcode.IsEndOfSection())
                Utils.Log("");

            return Instruction;
        }

        public static CodeMatch Vomit(
            this CodeMatch CodeMatch,
            int Pos,
            int PosPadding,
            Dictionary<Label, int> LabelInstructions = null,
            List<int> Offsets = null,
            bool HaveILGen = false,
            bool IncludeEnd = false,
            int Indent = 0,
            bool Do = false
            )
        {
            if (!Do)
                return CodeMatch;

            string operandString = CodeMatch?.operand?.VomitOperand(PosPadding, LabelInstructions, Offsets, HaveILGen, Do);
            string labelString = $"[{Pos.ToString().PadLeft(PosPadding, '0')}]";
            if (HaveILGen)
                labelString = $"IL_{Pos:X4}:";

            Utils.Log($"{Indent.Indent()}{labelString} {CodeMatch.opcode,-10} {operandString}");
            if (IncludeEnd
                && CodeMatch.opcode.IsEndOfSection())
                Utils.Log("");

            return CodeMatch;
        }

        public static CodeMatch[] Vomit(
            this CodeMatch[] CodeMatchs,
            string Context = null,
            string EndContext = null,
            Dictionary<Label, int> LabelInstructions = null,
            bool HaveILGen = false,
            bool IncludeEnd = false,
            bool Do = false
            )
        {
            if (!Do)
                return CodeMatchs;

            int num = 0;
            int posPadding = Math.Max(4, (CodeMatchs.Length + 1).ToString().Length);
            if (!Context.IsNullOrEmpty())
                Utils.Log(Context);

            for (int i = 0; i < CodeMatchs.Length; i++)
                CodeMatchs[i].Vomit(
                    IncludeEnd: num < CodeMatchs.Length - 1 && IncludeEnd,
                    Pos: num++,
                    PosPadding: posPadding,
                    LabelInstructions: LabelInstructions,
                    HaveILGen: HaveILGen,
                    Do: Do);

            if (!EndContext.IsNullOrEmpty())
                Utils.Log(EndContext);

            return CodeMatchs;
        }

        public static CodeInstruction[] Vomit(
            this CodeInstruction[] CodeInstructions,
            string Context = null,
            string EndContext = null,
            Dictionary<Label, int> LabelInstructions = null,
            bool HaveILGen = false,
            bool IncludeEnd = false,
            bool Do = false
            )
        {
            if (!Do)
                return CodeInstructions;

            int num = 0;
            int posPadding = Math.Max(4, (CodeInstructions.Length + 1).ToString().Length);
            if (!Context.IsNullOrEmpty())
                Utils.Log(Context);

            for (int i = 0; i < CodeInstructions.Length; i++)
                CodeInstructions[i].Vomit(
                    IncludeEnd: num < CodeInstructions.Length - 1 && IncludeEnd,
                    Pos: num++,
                    PosPadding: posPadding,
                    LabelInstructions: LabelInstructions,
                    HaveILGen: HaveILGen,
                    Do: Do);

            if (!EndContext.IsNullOrEmpty())
                Utils.Log(EndContext);

            return CodeInstructions;
        }

        public static string VomitOperand(
            this object Operand,
            int PosPadding,
            Dictionary<Label, int> LabelInstructions = null,
            List<int> Offsets = null,
            bool HaveILGen = false,
            bool Do = false
            )
        {
            if (!Do)
                return null;

            string result = Operand?.ToString();
            if (Operand?.GetType() == typeof(string))
                result = Operand?.ToString()?.ToLiteral(Quotes: true);
            else
            if (Operand is MethodInfo methodOp)
                result = $"{(methodOp.ReturnType.IsValueType ? "null" : "class ")}{methodOp.ReturnType} {methodOp.DeclaringType}:{methodOp.Name}({methodOp.GetParameters().Select(p => $"{(p.ParameterType.IsValueType ? "null" : "class ")}{p.ParameterType}").Aggregate((string)null, Utils.CommaSpaceDelimitedAggregator)})";
            else
            if (Operand is FieldInfo fieldOp)
                result = $"{(fieldOp.FieldType.IsValueType ? "null" : "class ")}{fieldOp.FieldType} {fieldOp.DeclaringType}:{fieldOp.Name}";
            else
            if (Operand is PropertyInfo propertyOp)
                result = $"{(propertyOp.PropertyType.IsValueType ? "null" : "class ")}{propertyOp.PropertyType} {propertyOp.DeclaringType}:{propertyOp.Name}";
            else
            if (Operand is Label key)
            {
                result = "[????]";
                if (!LabelInstructions.IsNullOrEmpty()
                    && LabelInstructions.ContainsKey(key))
                {
                    int labelPos = LabelInstructions[key];
                    int loc = !Offsets.IsNullOrEmpty() ? Offsets[labelPos] : labelPos;
                    result = $"[{LabelInstructions[key].ToString().PadLeft(PosPadding, '0')}]";
                    if (HaveILGen)
                        result = $"IL_{labelPos:X4}";
                }
            }

            return result;
        }

        public static CodeMatcher Vomit(
            this CodeMatcher CodeMatcher,
            ILGenerator Generator,
            bool Do = false,
            int? From = null,
            int? To = null
            )
        {
            if (Do)
            {
                bool haveGenerator = false; // Generator != null;
                var positionsByLabel = new Dictionary<Label, int>();
                int pos = CodeMatcher.Pos;

                var offsets = new List<int>();
                int offset = 0;

                CodeMatcher.Start();
                do
                {
                    var instruction = CodeMatcher.Instruction;

                    /*offset += instruction.opcode.Size;
                    if (instruction.operand != null)
                        offset += Marshal.SizeOf(instruction.operand);
                    offsets.Add(offset);*/

                    if (instruction.labels.IsNullOrEmpty())
                        continue;

                    foreach (var label in instruction.labels)
                        positionsByLabel[label] = CodeMatcher.Pos;
                }
                while (CodeMatcher.Advance(1).IsValid);

                int posPadding = Math.Max(4, (CodeMatcher.Instructions().Count + 1).ToString().Length);
                int from = From ?? 0;
                int to = To ?? CodeMatcher.Length - 1;
                CodeMatcher.Start();
                do
                {
                    if (CodeMatcher.Pos < from)
                        continue;
                    if (CodeMatcher.Pos > to)
                        break;

                    CodeMatcher.Instruction?.Vomit(CodeMatcher.Pos, posPadding, positionsByLabel, offsets, haveGenerator || !offsets.IsNullOrEmpty(), IncludeEnd: true, Do);
                }
                while (CodeMatcher.Advance(1).IsValid);

                CodeMatcher.Start().Advance(pos);
            }

            return CodeMatcher;
        }

        public static CodeMatcher Vomit(
            this CodeMatcher CodeMatcher,
            bool Do,
            int? From,
            int? To
            )
            => CodeMatcher.Vomit(
                Generator: null,
                Do: Do,
                From: From,
                To: To)
            ;

        public static CodeMatcher Vomit(
            this CodeMatcher CodeMatcher,
            ILGenerator Generator,
            bool Do,
            int PosMargin
            )
            => CodeMatcher.Vomit(
                Generator: Generator,
                Do: Do,
                From: PosMargin >= 0 ? Math.Max(0, (CodeMatcher?.Pos ?? 0) - PosMargin) : null,
                To: PosMargin >= 0 ? Math.Min((CodeMatcher?.Pos ?? 0) + PosMargin, (CodeMatcher?.Length ?? 1) - 1) : null)
            ;

        public static CodeMatcher Vomit(
            this CodeMatcher CodeMatcher,
            bool Do = false,
            int PosMargin = -1
            )
            => CodeMatcher.Vomit(
                Generator: null,
                Do: Do,
                PosMargin: PosMargin)
            ;

        public static CodeMatcher Vomit(
            this CodeMatcher CodeMatcher,
            bool Do = false
            )
            => CodeMatcher.Vomit(
                Generator: null,
                Do: Do,
                From: null,
                To: null)
            ;

        public static IEnumerable<CodeInstruction> Vomit(this IEnumerable<CodeInstruction> Instructions, bool Do = false)
            => new CodeMatcher(Instructions).Vomit(Do).InstructionEnumeration()
            ;

        #endregion
    }
}
