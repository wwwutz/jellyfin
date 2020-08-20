#pragma warning disable CS1591
#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Emby.Naming.Video
{
    // run all regexes through string one after the other, do not bail out
    public static class CleanStringParser
    {
        public static bool TryClean(string name, IReadOnlyList<Regex> expressions, out ReadOnlySpan<char> newName)
        {
            var tryname = name;
            for (int i = 0; i < expressions.Count; i++)
            {
                Console.Error.WriteLine("TryClean(" + tryname + ", " + expressions[i] + ")");
                tryname = expressions[i].Replace(tryname, string.Empty);
                Console.Error.WriteLine("TryClean= " + tryname);
            }

            newName = tryname;

            return true;
        }
    }
}
