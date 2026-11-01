using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;

namespace Clio;

/// <summary>
/// Indenting lists the Google Docs way (RTC's request, 2026-11-01).
///
/// Tab on a bullet, numbered or checklist line indents it; Shift+Tab, or
/// Backspace at the start of an indented line, or Enter on an empty indented
/// line, brings it back out.
///
/// Bullets nest as real nested lists, with Docs' marker per level
/// (disc, circle, square). Checklist lines are paragraphs, so they indent by
/// margin. Numbers are the awkward one: WPF numbers a nested list from 1 again
/// and has no "1.1" style, so a nested list under a numbered list draws no
/// marker and each item carries its outline label as text ("1.2.1 "). Renumber
/// rewrites every label after each structural change.
/// </summary>
public partial class NoteWindow
{
    private const double IndentStep = 24;
    private const int MaxDepth = 6;

    /// <summary>An outline label at the start of a line: "1.2" or "3.1.4", then a space.</summary>
    private static readonly Regex Label = new(@"^\d+(?:\.\d+)+ ", RegexOptions.Compiled);

    private static readonly TextMarkerStyle[] BulletLevels =
        { TextMarkerStyle.Disc, TextMarkerStyle.Circle, TextMarkerStyle.Square };

    private static bool IsNumbered(List list) => list.MarkerStyle is TextMarkerStyle.Decimal
        or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin
        or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman;

    /// <summary>How many lists deep this paragraph sits (0 when it is not in a list).</summary>
    private static int ListDepth(Paragraph para)
    {
        var d = 0;
        for (DependencyObject? x = para.Parent; x is not null; x = (x as TextElement)?.Parent)
            if (x is List) d++;
        return d;
    }

    private static List? RootList(Paragraph para)
    {
        List? root = null;
        for (DependencyObject? x = para.Parent; x is not null; x = (x as TextElement)?.Parent)
            if (x is List l) root = l;
        return root;
    }

    /// <summary>Is the line indented (nested in a list, or a checklist line moved in)?</summary>
    private static bool Indented(Paragraph para) =>
        ListDepth(para) > 1 || (ListDepth(para) == 0 && para.Margin.Left > 0);

    /// <summary>Characters at the start of the line that are list furniture, not writing.</summary>
    private static int Furniture(Paragraph para)
    {
        var text = Plain(para);
        var n = Label.Match(text) is { Success: true } m ? m.Length : 0;
        if (n < text.Length && (text[n] == Open || text[n] == Done))
            n += n + 1 < text.Length && text[n + 1] == ' ' ? 2 : 1;
        return n;
    }

    /// <summary>
    /// The line's own writing. TextRange from ContentStart also reports a list
    /// line's drawn marker ("1." and a tab), which hid every label from the
    /// match and stacked them up; starting at the first insertion position
    /// leaves the marker out.
    /// </summary>
    private static string Plain(Paragraph para) =>
        string.Concat(para.Inlines.Select(i => i is Run r ? r.Text : i is LineBreak ? "\n" : "￼"));

    /// <summary>Caret steps from the start of the line's writing to this position.</summary>
    private static int StepsTo(Paragraph para, TextPointer to)
    {
        var n = 0;
        for (var p = para.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
             p.CompareTo(to) < 0 && p.GetNextInsertionPosition(LogicalDirection.Forward) is { } next; p = next)
            n++;
        return n;
    }

    private static bool EmptyLine(Paragraph para) => Plain(para)[Furniture(para)..].Trim().Length == 0;

    /// <summary>Tab (+1) or Shift+Tab (-1) on a list line. False when the line is not a list line.</summary>
    private bool IndentLine(int direction)
    {
        if (Ed.CaretPosition.Paragraph is not { } para) return false;
        var inList = para.Parent is ListItem;
        if (!inList && !Boxed(para)) return false;
        if (direction > 0 && (inList ? ListDepth(para) : (int)(para.Margin.Left / IndentStep) + 1) >= MaxDepth) return true;

        Ed.BeginChange();
        try
        {
            if (inList)
            {
                if (direction < 0 && ListDepth(para) <= 1) return true;   // Docs: nothing further out than the list
                (direction > 0 ? EditingCommands.IncreaseIndentation : EditingCommands.DecreaseIndentation).Execute(null, Ed);
            }
            else
            {
                var left = Math.Max(0, para.Margin.Left + direction * IndentStep);
                para.Margin = new Thickness(left, para.Margin.Top, para.Margin.Right, para.Margin.Bottom);
            }
            Renumber();
        }
        finally
        {
            Ed.EndChange();
        }
        return true;
    }

    /// <summary>Backspace at the very start of an indented line's writing brings the line out one level.</summary>
    private bool BackInList()
    {
        if (!Ed.Selection.IsEmpty || Ed.CaretPosition.Paragraph is not { } para || !Indented(para)) return false;
        if (StepsTo(para, Ed.CaretPosition) != Furniture(para)) return false;
        return IndentLine(-1);
    }

    /// <summary>Enter on an empty indented line: out one level, as Docs does. Then Enter's usual rules.</summary>
    private bool EnterIndented()
    {
        if (!Ed.Selection.IsEmpty || Ed.CaretPosition.Paragraph is not { } para) return false;
        if (Indented(para) && EmptyLine(para)) return IndentLine(-1);

        // A new line in an outline-numbered list needs its own label.
        if (para.Parent is ListItem && ListDepth(para) > 1 && RootList(para) is { } root && IsNumbered(root))
        {
            Ed.BeginChange();
            try
            {
                EditingCommands.EnterParagraphBreak.Execute(null, Ed);
                Renumber();
            }
            finally
            {
                Ed.EndChange();
            }
            return true;
        }
        return false;
    }

    /// <summary>Rewrites every nested label and bullet style, then puts the caret back where it was in the writing.</summary>
    private void Renumber()
    {
        // The caret is remembered as "n characters into the writing" of its
        // line, because the labels in front of it change length.
        var para = Ed.CaretPosition.Paragraph;
        var into = 0;
        if (para is not null)
        {
            into = Math.Max(0, StepsTo(para, Ed.CaretPosition) - Furniture(para));
        }

        foreach (var list in Ed.Document.Blocks.OfType<List>().ToList())
            Walk(list, "", 1, IsNumbered(list));

        // A line that has left the outline keeps no stale label.
        if (para is not null && !(ListDepth(para) > 1 && RootList(para) is { } r && IsNumbered(r)))
            SetLabel(para, null);

        if (para is not null)
        {
            var start = para.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            var target = Furniture(para) + into;
            var pos = start;
            for (var i = 0; i < target && pos.GetNextInsertionPosition(LogicalDirection.Forward) is { } next
                 && next.CompareTo(para.ContentEnd) <= 0; i++)
                pos = next;
            Ed.CaretPosition = pos;
        }
    }

    private void Walk(List list, string parent, int depth, bool numbered)
    {
        if (depth > 1)
        {
            if (numbered) list.MarkerStyle = TextMarkerStyle.None;
            else if (list.MarkerStyle != TextMarkerStyle.None) list.MarkerStyle = BulletLevels[(depth - 1) % BulletLevels.Length];
        }

        var n = depth == 1 ? list.StartIndex : 1;
        foreach (var item in list.ListItems.ToList())
        {
            var label = parent.Length == 0 ? n.ToString() : $"{parent}.{n}";
            if (item.Blocks.FirstBlock is Paragraph first)
                SetLabel(first, numbered && depth > 1 ? label : null);
            foreach (var nested in item.Blocks.OfType<List>().ToList())
                Walk(nested, numbered ? label : "", depth + 1, numbered);
            n++;
        }
    }

    /// <summary>Sets (or, with null, removes) the outline label at the start of a line, touching it only if it differs.</summary>
    private static void SetLabel(Paragraph para, string? label)
    {
        var old = Label.Match(Plain(para)) is { Success: true } m ? m.Value : "";
        var want = label is null ? "" : label + " ";
        if (old == want) return;

        // A label is typed in one go, so it sits inside the first run.
        if (para.Inlines.FirstInline is Run first && first.Text.StartsWith(old))
            first.Text = want + first.Text[old.Length..];
        else if (want.Length == 0)
            return;
        else if (para.Inlines.FirstInline is null)
            para.Inlines.Add(new Run(want));
        else
            para.Inlines.InsertBefore(para.Inlines.FirstInline, new Run(want));
    }
}
