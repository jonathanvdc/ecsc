using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Compiler;

namespace Flame.Ecs.Diagnostics
{
    /// <summary>
    /// Contains functionality that suggestes names for "Did you mean ...?" messages.
    /// </summary>
    public static class NameSuggestionHelpers
    {
        /// <summary>
        /// Suggests a name from the given sequence of names by picking the
        /// name that is the most similar to the spelled name.
        /// </summary>
        /// <param name="PossibleNames">The name from which a name is to be picked.</param>
        /// <param name="ActualNames">The sequence of names from which a name is to be suggested.</param>
        /// <returns>The name from the sequence of names which is closest to the spelled name;
        /// <c>null</c> if the sequence of possible names is empty.</returns>
        public static string SuggestName(
            string SpelledName, IEnumerable<string> PossibleNames)
        {
            string closestName = null;
            int closestNameDistance = int.MaxValue;
            foreach (var name in PossibleNames)
            {
                int dist = LevenshteinDistance(SpelledName, name);
                if (dist < closestNameDistance)
                {
                    closestNameDistance = dist;
                    closestName = name;
                }
            }
            return closestName;
        }

        private static int Min(int a, int b, int c)
        {
            return Math.Min(Math.Min(a, b), c);
        }

        /// <summary>
        /// Computes the Levenshtein distance between the given strings.
        /// </summary>
        /// <param name="s">The first string.</param>
        /// <param name="t">The second string.</param>
        /// <returns>The Levenshtein distance.</returns>
        private static int LevenshteinDistance(string s, string t)
        {
            // This algorithm is based on the code from Wikipedia
            // (https://en.wikipedia.org/wiki/Levenshtein_distance), which was
            // originally presented in "Fast, memory efficient Levenshtein algorithm"
            // by Sten Hjelmqvist.
            // (https://www.codeproject.com/Articles/13525/Fast-memory-efficient-Levenshtein-algorithm)

            // Handle degenerate cases.
            if (s.Length == 0)
                return t.Length;

            if (t.Length == 0)
                return s.Length;

            // Create two work vectors of integer distances.
            var v0 = new int[t.Length + 1];
            var v1 = new int[t.Length + 1];

            // Initialize v0 (the previous row of distances).
            // This row is A[0][i]: edit distance for an empty s.
            // The distance is just the number of characters to delete from t.
            for (int i = 0; i < v0.Length; i++)
            {
                v0[i] = i;
            }

            for (int i = 0; i < s.Length; i++)
            {
                // Calculate v1 (current row distances) from the previous row v0.

                // First element of v1 is A[i+1][0]
                // Edit distance is delete (i+1) chars from s to match empty t.
                v1[0] = i + 1;

                // Use formula to fill in the rest of the row.
                for (int j = 0; j < t.Length; j++)
                {
                    var cost = (s[i] == t[j]) ? 0 : 1;
                    v1[j + 1] = Min(v1[j] + 1, v0[j + 1] + 1, v0[j] + cost);
                }

                // Copy v1 (current row) to v0 (previous row) for next iteration.
                for (int j = 0; j < v0.Length; j++)
                {
                    v0[j] = v1[j];
                }
            }

            return v1[t.Length];
        }
    }
}

