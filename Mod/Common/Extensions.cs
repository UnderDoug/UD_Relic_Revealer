using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

using XRL;
using XRL.Collections;
using XRL.World;
using XRL.World.Capabilities;
using XRL.World.Parts;

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
    }
}
