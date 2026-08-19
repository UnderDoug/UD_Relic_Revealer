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
    }
}
