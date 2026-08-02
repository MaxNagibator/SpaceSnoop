using System.IO;
using System.Runtime.CompilerServices;
using System.Security;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.Views.Behaviors;

public static class PathSuggest
{
    private const int MaxCandidates = 64;

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled",
        typeof(bool),
        typeof(PathSuggest),
        new(false, OnEnabledChanged));

    private static readonly ConditionalWeakTable<TextBox, State> States = [];
    private static bool _suppress;

    public static bool GetEnabled(DependencyObject element)
    {
        return (bool)element.GetValue(EnabledProperty);
    }

    public static void SetEnabled(DependencyObject element, bool value)
    {
        element.SetValue(EnabledProperty, value);
    }

    // TODO: UNC share
    internal static IReadOnlyList<string> Matches(string text)
    {
        var separator = text.LastIndexOfAny(['\\', '/']);

        if (separator < 0)
        {
            return [];
        }

        var directory = text[..(separator + 1)];
        var prefix = text[(separator + 1)..];

        if (prefix.Length == 0)
        {
            return [];
        }

        try
        {
            if (!Directory.Exists(directory))
            {
                return [];
            }

            var matches = Directory.EnumerateDirectories(directory, prefix + "*")
                .Where(match => Path.GetFileName(match).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Take(MaxCandidates)
                .Select(match => directory + Path.GetFileName(match))
                .ToList();

            matches.Sort(StringComparer.OrdinalIgnoreCase);
            return matches;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or SecurityException)
        {
            return [];
        }
    }

    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress || sender is not TextBox box)
        {
            return;
        }

        if (box.SelectionLength != 0 || box.CaretIndex != box.Text.Length)
        {
            return;
        }

        var added = 0;

        foreach (var change in e.Changes)
        {
            added += change.AddedLength;
        }

        if (added == 0)
        {
            States.Remove(box);
            return;
        }

        var text = box.Text;
        var matches = Matches(text);

        if (matches.Count == 0)
        {
            States.Remove(box);
            return;
        }

        var state = new State(text.Length, matches);
        States.AddOrUpdate(box, state);
        Apply(box, state);
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        if (e.Key is Key.Up or Key.Down)
        {
            Cycle(box, e);
        }
        else if (e.Key == Key.Back)
        {
            Backspace(box, e);
        }
    }

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box)
        {
            return;
        }

        box.TextChanged -= OnTextChanged;
        box.PreviewKeyDown -= OnPreviewKeyDown;

        if (e.NewValue is true)
        {
            box.TextChanged += OnTextChanged;
            box.PreviewKeyDown += OnPreviewKeyDown;
        }
    }

    private static void Cycle(TextBox box, KeyEventArgs e)
    {
        if (box.SelectionLength == 0 || !States.TryGetValue(box, out var state) || state.Candidates.Count < 2)
        {
            return;
        }

        var count = state.Candidates.Count;
        state.Index = e.Key == Key.Down ? (state.Index + 1) % count : (state.Index - 1 + count) % count;
        Apply(box, state);
        e.Handled = true;
    }

    private static void Backspace(TextBox box, KeyEventArgs e)
    {
        var reachesEnd = box.SelectionLength == 0
            ? box.CaretIndex == box.Text.Length
            : box.SelectionStart + box.SelectionLength == box.Text.Length;

        if (!reachesEnd)
        {
            return;
        }

        var prefixEnd = box.SelectionLength == 0 ? box.CaretIndex : box.SelectionStart;

        if (prefixEnd == 0)
        {
            return;
        }

        var prefix = box.Text[..(prefixEnd - 1)];
        var matches = Matches(prefix);

        if (matches.Count == 0)
        {
            _suppress = true;
            box.Text = prefix;
            box.CaretIndex = prefix.Length;
            _suppress = false;
            States.Remove(box);
        }
        else
        {
            var state = new State(prefix.Length, matches);
            States.AddOrUpdate(box, state);
            Apply(box, state);
        }

        e.Handled = true;
    }

    private static void Apply(TextBox box, State state)
    {
        var candidate = state.Candidates[state.Index];

        _suppress = true;
        box.Text = candidate;
        box.Select(state.PrefixLength, candidate.Length - state.PrefixLength);
        _suppress = false;
    }

    private sealed class State(int prefixLength, IReadOnlyList<string> candidates)
    {
        public int PrefixLength { get; } = prefixLength;

        public IReadOnlyList<string> Candidates { get; } = candidates;

        public int Index { get; set; }
    }
}
