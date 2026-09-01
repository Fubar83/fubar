using System;
using System.Collections.Generic;
using System.Linq;

namespace Fubar.Diff.Core.Code;

/// <summary>
/// Compares two structure trees and says what happened to each member.
///
/// Pure and BCL-only, like every other differ here - the compiler front end that produced the trees
/// lives behind <see cref="ICodeStructureParser"/> in Infrastructure, and this side of the line knows
/// nothing about it.
///
/// The whole design is in the MATCHING. Line differs fail on source code in one specific way: they
/// have no notion of a member, so a method moved to the other end of the file is a large deletion and
/// an unrelated large insertion, a renamed method is the same, and a reindented file is every line
/// changed. Each of those reads as work to review and is not. Matching members to members first, and
/// only then asking what differs about the pair, is what turns all three into one short sentence.
///
/// Four passes, most specific first, and the order matters:
///
/// 1. <b>Same kind, same signature.</b> Two siblings cannot share one in a language that compiles, so
///    this is a key rather than a guess.
/// 2. <b>Same kind, same name.</b> Catches a changed parameter list - an overload added to a method
///    that had none is still that method.
/// 3. <b>Same name, any kind.</b> Catches a field promoted to a property, which is one of the
///    commonest real edits and which passes 1 and 2 both miss.
/// 4. <b>Identical body.</b> A rename: everything the member DOES is untouched and only its name
///    moved. Requires the body to be unique on both sides, for the same reason
///    <c>MoveDetector</c> requires it of a moved block - a mark that says "you can skip this" is worse
///    than nothing when it is wrong, and three one-line properties with the same body would otherwise
///    pair up arbitrarily.
///
/// Anything still unmatched genuinely appeared or went away.
/// </summary>
public static class CodeStructureDiffer
{
    /// <summary>
    /// Compares two parsed files. The result is in source order of the RIGHT-hand file, with removed
    /// members appearing where they used to be, so the list reads down the file the user is looking
    /// at rather than in whatever order the matching happened to run.
    /// </summary>
    public static IReadOnlyList<CodeChange> Compare(CodeNode left, CodeNode right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var changes = new List<CodeChange>();

        CompareChildren(left, right, string.Empty, 0, changes);

        return changes;
    }

    /// <summary>One matched pair, and how it was matched - which is what decides a rename.</summary>
    private readonly record struct Pair(int LeftIndex, int RightIndex, bool ByBody);

    private static void CompareChildren(CodeNode left, CodeNode right, string path, int depth, List<CodeChange> changes)
    {
        var pairs = Match(left.Children, right.Children);
        var moved = MovedPairs(pairs);

        var matchedLeft = new bool[left.Children.Count];
        var matchedRight = new bool[right.Children.Count];

        foreach (var pair in pairs)
        {
            matchedLeft[pair.LeftIndex] = true;
            matchedRight[pair.RightIndex] = true;
        }

        // Walked in RIGHT order, with each removed member emitted at the point it used to sit relative
        // to its surviving neighbours. A list that jumped to the end for removals would be describing
        // a file nobody has.
        var byRightIndex = pairs.ToDictionary(p => p.RightIndex);
        var nextLeft = 0;

        for (var rightIndex = 0; rightIndex <= right.Children.Count; rightIndex++)
        {
            if (byRightIndex.TryGetValue(rightIndex, out var pair))
            {
                EmitRemovals(left, path, depth, changes, matchedLeft, ref nextLeft, pair.LeftIndex);

                nextLeft = pair.LeftIndex + 1;

                var leftChild = left.Children[pair.LeftIndex];
                var rightChild = right.Children[pair.RightIndex];
                var childPath = Join(path, rightChild.Signature);

                var kind = Classify(leftChild, rightChild, pair.ByBody);
                var isMoved = moved.Contains(pair);

                if (kind is not null || isMoved)
                {
                    changes.Add(new CodeChange(
                        childPath,
                        kind ?? CodeChangeKind.Moved,
                        Located(leftChild, Join(path, leftChild.Signature)),
                        Located(rightChild, childPath))
                    {
                        IsMoved = isMoved,
                        Depth = depth,
                        Container = path,
                    });
                }

                // Always recursed into, even when the pair itself is identical: a class whose own
                // tokens match can hold a method that does not, and that method is the answer.
                CompareChildren(leftChild, rightChild, childPath, depth + 1, changes);
            }
            else if (rightIndex < right.Children.Count && !matchedRight[rightIndex])
            {
                var added = right.Children[rightIndex];
                var childPath = Join(path, added.Signature);

                changes.Add(new CodeChange(childPath, CodeChangeKind.Added, null, Located(added, childPath)) { Depth = depth, Container = path });
            }
        }

        // Whatever sat past the last surviving member.
        EmitRemovals(left, path, depth, changes, matchedLeft, ref nextLeft, left.Children.Count);
    }

    private static void EmitRemovals(
        CodeNode left,
        string path,
        int depth,
        List<CodeChange> changes,
        bool[] matchedLeft,
        ref int nextLeft,
        int until)
    {
        for (; nextLeft < until && nextLeft < left.Children.Count; nextLeft++)
        {
            if (matchedLeft[nextLeft])
            {
                continue;
            }

            var removed = left.Children[nextLeft];
            var childPath = Join(path, removed.Signature);

            // Not recursed into. A class that went away took its methods with it, and listing every
            // one of them as its own removal buries the fact that it is the class that went.
            changes.Add(new CodeChange(childPath, CodeChangeKind.Removed, Located(removed, childPath), null) { Depth = depth, Container = path });
        }
    }

    /// <summary>What differs about a matched pair, or null when nothing does.</summary>
    private static CodeChangeKind? Classify(CodeNode left, CodeNode right, bool matchedByBody)
    {
        if (matchedByBody || (left.Name != right.Name && left.BodyTokens == right.BodyTokens && left.BodyTokens.Length > 0))
        {
            return CodeChangeKind.Renamed;
        }

        if (left.Tokens != right.Tokens)
        {
            return CodeChangeKind.Modified;
        }

        // Same tokens, different characters: spacing, wrapping, or a comment. The one answer a line
        // differ cannot give.
        return left.Text != right.Text ? CodeChangeKind.Cosmetic : null;
    }

    private static IReadOnlyList<Pair> Match(IReadOnlyList<CodeNode> left, IReadOnlyList<CodeNode> right)
    {
        var pairs = new List<Pair>();
        var usedLeft = new bool[left.Count];
        var usedRight = new bool[right.Count];

        MatchBy(left, right, usedLeft, usedRight, pairs, byBody: false, n => $"{(int)n.Kind}{n.Signature}");
        MatchBy(left, right, usedLeft, usedRight, pairs, byBody: false, n => $"{(int)n.Kind}{n.Name}");
        MatchBy(left, right, usedLeft, usedRight, pairs, byBody: false, n => $"{n.Name}");
        MatchBy(left, right, usedLeft, usedRight, pairs, byBody: true, n => n.BodyTokens.Length > 0 ? n.BodyTokens : null);

        pairs.Sort((a, b) => a.LeftIndex.CompareTo(b.LeftIndex));

        return pairs;
    }

    /// <summary>
    /// Pairs up whatever is still unmatched by a key, and only where that key is UNIQUE on both sides.
    ///
    /// The uniqueness rule is what keeps every pass safe to run after the one before it. A key shared
    /// by two members says nothing about which of them is which, and pairing them in the order they
    /// happen to appear would produce a confident, wrong answer - the failure mode a structural diff
    /// is supposed to remove rather than add.
    /// </summary>
    private static void MatchBy(
        IReadOnlyList<CodeNode> left,
        IReadOnlyList<CodeNode> right,
        bool[] usedLeft,
        bool[] usedRight,
        List<Pair> pairs,
        bool byBody,
        Func<CodeNode, string?> key)
    {
        var leftByKey = UniqueByKey(left, usedLeft, key);
        var rightByKey = UniqueByKey(right, usedRight, key);

        foreach (var (k, leftIndex) in leftByKey)
        {
            if (!rightByKey.TryGetValue(k, out var rightIndex))
            {
                continue;
            }

            usedLeft[leftIndex] = true;
            usedRight[rightIndex] = true;

            pairs.Add(new Pair(leftIndex, rightIndex, byBody));
        }
    }

    private static Dictionary<string, int> UniqueByKey(
        IReadOnlyList<CodeNode> nodes,
        bool[] used,
        Func<CodeNode, string?> key)
    {
        var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < nodes.Count; i++)
        {
            if (used[i] || key(nodes[i]) is not { } k)
            {
                continue;
            }

            if (!byKey.TryAdd(k, i))
            {
                duplicates.Add(k);
            }
        }

        foreach (var duplicate in duplicates)
        {
            byKey.Remove(duplicate);
        }

        return byKey;
    }

    /// <summary>
    /// Which matched pairs actually changed position, as opposed to being pushed along by something
    /// else moving.
    ///
    /// The pairs are already in left order, so what is wanted is the longest run of them that is also
    /// in right order - everything in that run stayed put, and everything else moved. Without this,
    /// inserting one method at the top of a file marks every method below it as moved, which is both
    /// wrong and exactly the noise this feature exists to remove.
    /// </summary>
    private static HashSet<Pair> MovedPairs(IReadOnlyList<Pair> pairs)
    {
        var moved = new HashSet<Pair>();

        if (pairs.Count < 2)
        {
            return moved;
        }

        // Patience-style longest increasing subsequence over the right indices: tails[l] is the
        // smallest right index that can end an increasing run of length l+1, and previous[] threads
        // the chosen run back together.
        var tails = new List<int>();
        var tailIndex = new List<int>();
        var previous = new int[pairs.Count];

        for (var i = 0; i < pairs.Count; i++)
        {
            var value = pairs[i].RightIndex;
            var position = LowerBound(tails, value);

            previous[i] = position > 0 ? tailIndex[position - 1] : -1;

            if (position == tails.Count)
            {
                tails.Add(value);
                tailIndex.Add(i);
            }
            else
            {
                tails[position] = value;
                tailIndex[position] = i;
            }
        }

        var stayed = new HashSet<int>();

        for (var i = tailIndex.Count == 0 ? -1 : tailIndex[^1]; i >= 0; i = previous[i])
        {
            stayed.Add(i);
        }

        for (var i = 0; i < pairs.Count; i++)
        {
            if (!stayed.Contains(i))
            {
                moved.Add(pairs[i]);
            }
        }

        return moved;
    }

    private static int LowerBound(List<int> tails, int value)
    {
        var low = 0;
        var high = tails.Count;

        while (low < high)
        {
            var middle = (low + high) / 2;

            if (tails[middle] < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static CodeNode Located(CodeNode node, string path) => node with { Path = path };

    private static string Join(string path, string name) =>
        path.Length == 0 ? name : $"{path}.{name}";
}
