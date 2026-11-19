using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.Views.Behaviors;

public static class PathSuggest
{
    private const int MaxCandidates = 64;

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(PathSuggest),
        new(false, OnEnabledChanged));

    private static readonly ConditionalWeakTable<TextBox, BoxState> States = [];
    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> Shares = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, bool> PendingShares = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock LookupGate = new();
    private static readonly Dictionary<string, RootLookup> InFlightLookups = new(StringComparer.OrdinalIgnoreCase);
    private static bool _suppress;

    internal static Func<string, string, IReadOnlyList<string>> DirectoryReader { get; set; } = EnumerateMatches;

    public static bool GetEnabled(DependencyObject element)
    {
        return (bool)element.GetValue(EnabledProperty);
    }

    public static void SetEnabled(DependencyObject element, bool value)
    {
        element.SetValue(EnabledProperty, value);
    }

    internal static string? ServerName(string directory)
    {
        if (directory.Length < 3 || directory[0] is not ('\\' or '/') || directory[1] is not ('\\' or '/'))
        {
            return null;
        }

        var body = directory[2..];
        var end = body.IndexOfAny(['\\', '/']);

        return end > 0 && end == body.Length - 1 ? body[..end] : null;
    }

    private static bool TrySplitQuery(string text, out string directory, out string prefix)
    {
        directory = string.Empty;
        prefix = string.Empty;

        var separator = text.LastIndexOfAny(['\\', '/']);

        if (separator < 0 || (separator + 1) >= text.Length)
        {
            return false;
        }

        directory = text[..(separator + 1)];
        prefix = text[(separator + 1)..];
        return true;
    }

    private static IReadOnlyList<string> EnumerateMatches(string directory, string prefix)
    {
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

    private static IReadOnlyList<string> FilterShares(IReadOnlyList<string> shares, string directory, string prefix)
    {
        var matches = shares
            .Where(share => share.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Take(MaxCandidates)
            .Select(share => directory + share)
            .ToList();

        matches.Sort(StringComparer.OrdinalIgnoreCase);
        return matches;
    }

    private static void RequestShares(string server, Action onLoaded)
    {
        if (!PendingShares.TryAdd(server, true))
        {
            return;
        }

        _ = Task.Run(() =>
        {
            var shares = NetworkShares.List(server);

            if (shares.Count > 0)
            {
                Shares[server] = shares;
                onLoaded();
            }
            else
            {
                PendingShares.TryRemove(server, out _);
            }
        });
    }

    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress || sender is not TextBox box)
        {
            return;
        }

        var boxState = States.GetOrCreateValue(box);
        boxState.Generation++;
        boxState.Active = null;

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
            return;
        }

        ScheduleSuggest(box, boxState, box.Text, boxState.Generation);
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
        box.Unloaded -= OnUnloaded;

        if (e.NewValue is true)
        {
            box.TextChanged += OnTextChanged;
            box.PreviewKeyDown += OnPreviewKeyDown;
            box.Unloaded += OnUnloaded;
            return;
        }

        Invalidate(box);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            Invalidate(box);
        }
    }

    private static void Invalidate(TextBox box)
    {
        if (States.TryGetValue(box, out var boxState))
        {
            boxState.Generation++;
            boxState.Active = null;
        }
    }

    private static void Cycle(TextBox box, KeyEventArgs e)
    {
        if (box.SelectionLength == 0
            || !States.TryGetValue(box, out var boxState)
            || boxState.Active is not { Candidates.Count: >= 2 } state)
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
        var boxState = States.GetOrCreateValue(box);
        boxState.Generation++;
        boxState.Active = null;

        _suppress = true;
        box.Text = prefix;
        box.CaretIndex = prefix.Length;
        _suppress = false;
        e.Handled = true;

        ScheduleSuggest(box, boxState, prefix, boxState.Generation);
    }

    private static void ScheduleSuggest(TextBox box, BoxState boxState, string text, int generation)
    {
        if (!TrySplitQuery(text, out var directory, out var prefix))
        {
            return;
        }

        if (ServerName(directory) is { } server)
        {
            if (Shares.TryGetValue(server, out var shares))
            {
                var immediate = FilterShares(shares, directory, prefix);

                if (immediate.Count > 0)
                {
                    var state = new State(text.Length, immediate);
                    boxState.Active = state;
                    Apply(box, state);
                }

                return;
            }

            RequestShares(
                server,
                () =>
                {
                    if (Shares.TryGetValue(server, out var loaded))
                    {
                        var matches = FilterShares(loaded, directory, prefix);

                        if (matches.Count > 0)
                        {
                            _ = box.Dispatcher.InvokeAsync(() => ApplyIfCurrent(box, boxState, text, generation, matches));
                        }
                    }
                });

            return;
        }

        _ = SuggestDirectoryAsync(box, boxState, text, directory, prefix, generation);
    }

    private static async Task SuggestDirectoryAsync(
        TextBox box,
        BoxState boxState,
        string text,
        string directory,
        string prefix,
        int generation)
    {
        var matches = await ReadDirectoryMatchesAsync(directory, prefix).ConfigureAwait(false);

        if (matches.Count == 0)
        {
            return;
        }

        _ = box.Dispatcher.InvokeAsync(() => ApplyIfCurrent(box, boxState, text, generation, matches));
    }

    private static Task<IReadOnlyList<string>> ReadDirectoryMatchesAsync(string directory, string prefix)
    {
        var root = RootKey(directory);
        var reader = DirectoryReader;

        lock (LookupGate)
        {
            if (!InFlightLookups.TryGetValue(root, out var slot) || slot.Running.IsCompleted)
            {
                var task = Task.Run(() => RunLookupLoop(root, directory, prefix, reader));
                InFlightLookups[root] = new(task, null);
                return task;
            }

            slot.Waiting?.Completion.TrySetResult([]);

            var waiting = new WaitingQuery(directory, prefix, new(TaskCreationOptions.RunContinuationsAsynchronously));
            InFlightLookups[root] = new(slot.Running, waiting);
            return waiting.Completion.Task;
        }
    }

    private static IReadOnlyList<string> RunLookupLoop(
        string root,
        string directory,
        string prefix,
        Func<string, string, IReadOnlyList<string>> reader)
    {
        var first = SafeInvokeReader(reader, directory, prefix);

        while (true)
        {
            WaitingQuery? next;

            lock (LookupGate)
            {
                if (!InFlightLookups.TryGetValue(root, out var slot) || slot.Waiting is null)
                {
                    InFlightLookups.Remove(root);
                    return first;
                }

                next = slot.Waiting;
                InFlightLookups[root] = new(slot.Running, null);
            }

            next.Completion.TrySetResult(SafeInvokeReader(reader, next.Directory, next.Prefix));
        }
    }

    private static IReadOnlyList<string> SafeInvokeReader(
        Func<string, string, IReadOnlyList<string>> reader,
        string directory,
        string prefix)
    {
        try
        {
            return reader(directory, prefix);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or SecurityException)
        {
            return [];
        }
    }

    private static string RootKey(string directory)
    {
        try
        {
            return Path.GetPathRoot(directory) is { Length: > 0 } root ? root : directory;
        }
        catch (ArgumentException)
        {
            return directory;
        }
    }

    private static void ApplyIfCurrent(
        TextBox box,
        BoxState boxState,
        string requestedText,
        int generation,
        IReadOnlyList<string> matches)
    {
        if (!GetEnabled(box)
            || boxState.Generation != generation
            || box.SelectionLength != 0
            || box.CaretIndex != requestedText.Length
            || !string.Equals(box.Text, requestedText, StringComparison.Ordinal))
        {
            return;
        }

        var state = new State(requestedText.Length, matches);
        boxState.Active = state;
        Apply(box, state);
    }

    private static void Apply(TextBox box, State state)
    {
        var candidate = state.Candidates[state.Index];

        _suppress = true;
        box.Text = candidate;
        box.Select(state.PrefixLength, candidate.Length - state.PrefixLength);
        _suppress = false;
    }

    private sealed class BoxState
    {
        public int Generation { get; set; }

        public State? Active { get; set; }
    }

    private sealed class State(int prefixLength, IReadOnlyList<string> candidates)
    {
        public int PrefixLength { get; } = prefixLength;

        public IReadOnlyList<string> Candidates { get; } = candidates;

        public int Index { get; set; }
    }

    private sealed record WaitingQuery(
        string Directory,
        string Prefix,
        TaskCompletionSource<IReadOnlyList<string>> Completion);

    private readonly record struct RootLookup(Task<IReadOnlyList<string>> Running, WaitingQuery? Waiting);
}
