using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace Fubar.Controls;

/// <summary>
/// Brace-based folding for JSON ({ }, [ ]), string-literal aware. AvaloniaEdit ships a folding
/// strategy only for XML out of the box, so this fills the same role for the request/response
/// body editors. Used by <see cref="JsonEditor"/>.
/// </summary>
public static class JsonFoldingStrategy
{
    public static void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        var newFoldings = new List<NewFolding>();
        var starts = new Stack<int>();
        var text = document.Text;
        var inString = false;
        var escaped = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;

                case '{' or '[':
                    starts.Push(i);
                    break;

                case '}' or ']' when starts.Count > 0:
                {
                    var start = starts.Pop();
                    if (document.GetLineByOffset(start).LineNumber != document.GetLineByOffset(i).LineNumber)
                    {
                        newFoldings.Add(new NewFolding(start, i + 1));
                    }

                    break;
                }
            }
        }

        newFoldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        manager.UpdateFoldings(newFoldings, -1);
    }
}
