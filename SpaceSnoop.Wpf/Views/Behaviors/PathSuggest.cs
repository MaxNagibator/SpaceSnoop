using System.IO;
using System.Runtime.CompilerServices;
using System.Security;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Views.Behaviors;

public static class PathSuggest
{
    private const int MaxCandidates = 64;
    private const int MaxVisibleRows = 9;
    private const double PopupMaxHeight = 236;
    private const double PopupMaxWidth = 560;
    private static readonly long ShareCacheTtlMs = AppDefaults.NetworkDiscoveryCacheSeconds * 1000L;

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled",
        typeof(bool),
        typeof(PathSuggest),
        new(false, OnEnabledChanged));

    private static readonly Func<IReadOnlyList<string>> DefaultHostReader = static () => NetworkDiscovery.ListHosts();
    private static readonly ConditionalWeakTable<TextBox, BoxState> States = [];
    private static readonly Lock ShareGate = new();
    private static readonly Dictionary<string, ShareCacheEntry> Shares = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<Action>> PendingShares = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock LookupGate = new();
    private static readonly Dictionary<string, RootLookup> InFlightLookups = new(StringComparer.OrdinalIgnoreCase);
    private static bool _suppress;

    internal static Func<long> ClockMs { get; set; } = static () => Environment.TickCount64;

    internal static Func<string, string, IReadOnlyList<string>> DirectoryReader { get; set; } = EnumerateMatches;

    internal static Func<IReadOnlyList<string>> HostReader { get; set; } = DefaultHostReader;

    internal static Func<string, IReadOnlyList<string>> ShareReader { get; set; } = NetworkShares.List;

    internal static Func<string, IReadOnlyList<string>> KnownShareReader { get; set; } = static server => NetworkDiscovery.ListKnownShares(server);

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

    internal static bool IsPopupOpen(TextBox box)
    {
        return States.TryGetValue(box, out var boxState) && boxState.Popup?.IsOpen == true;
    }

    internal static IReadOnlyList<string> PopupItems(TextBox box)
    {
        if (!States.TryGetValue(box, out var boxState) || boxState.Popup?.IsOpen != true || boxState.ListBox is null)
        {
            return [];
        }

        return boxState.ListBox.ItemsSource as IReadOnlyList<string> ?? [];
    }

    internal static int PopupSelectedIndex(TextBox box)
    {
        return States.TryGetValue(box, out var boxState) && boxState.ListBox is not null
            ? boxState.ListBox.SelectedIndex
            : -1;
    }

    internal static void ClearCacheForTests()
    {
        lock (ShareGate)
        {
            Shares.Clear();
            PendingShares.Clear();
        }
    }

    private static bool TryParseQuery(string text, out SuggestQuery query)
    {
        query = default;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var normalized = text.Replace('/', '\\');

        if (normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var rest = normalized[2..];
            var firstSlash = rest.IndexOf('\\');

            if (firstSlash < 0)
            {
                if (rest is "?" or ".")
                {
                    return false;
                }

                query = new(QueryKind.Host, @"\\", string.Empty, rest);
                return true;
            }

            if (firstSlash == 0)
            {
                return false;
            }

            var server = rest[..firstSlash];

            if (server is "?" or ".")
            {
                return false;
            }

            var afterServer = rest[(firstSlash + 1)..];
            var secondSlash = afterServer.IndexOf('\\');

            if (secondSlash < 0)
            {
                query = new(QueryKind.Share, $@"\\{server}\", server, afterServer);
                return true;
            }

            if (secondSlash == 0)
            {
                return false;
            }

            var uncLastSlash = normalized.LastIndexOf('\\');
            query = new(QueryKind.Directory, normalized[..(uncLastSlash + 1)], server, normalized[(uncLastSlash + 1)..]);
            return true;
        }

        if (normalized.Length < 3 || !char.IsAsciiLetter(normalized[0]) || normalized[1] != ':' || normalized[2] != '\\')
        {
            return false;
        }

        var lastSlash = normalized.LastIndexOf('\\');
        query = new(QueryKind.Directory, normalized[..(lastSlash + 1)], string.Empty, normalized[(lastSlash + 1)..]);
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

    private static IReadOnlyList<string> FilterHosts(IReadOnlyList<string> hosts, string prefix)
    {
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in hosts)
        {
            var host = raw.Trim().Trim('\\', '/');

            if (host.Length > 0 && host.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                distinct.Add($@"\\{host}");
            }
        }

        return SortAndCap(distinct);
    }

    private static IReadOnlyList<string> FilterShares(IEnumerable<string> shares, string directory, string prefix)
    {
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in shares)
        {
            var share = raw.Trim().Trim('\\', '/');

            if (share.Length > 0 && share.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                distinct.Add(directory + share);
            }
        }

        return SortAndCap(distinct);
    }

    private static IReadOnlyList<string> SortAndCap(HashSet<string> values)
    {
        if (values.Count == 0)
        {
            return [];
        }

        var sorted = values.ToArray();
        Array.Sort(sorted, StringComparer.OrdinalIgnoreCase);
        return sorted.Length <= MaxCandidates ? sorted : sorted[..MaxCandidates];
    }

    private static bool TryGetFreshShares(string server, out IReadOnlyList<string> shares)
    {
        lock (ShareGate)
        {
            if (Shares.TryGetValue(server, out var entry))
            {
                if (ClockMs() - entry.UpdatedMs < ShareCacheTtlMs)
                {
                    shares = entry.Items;
                    return true;
                }

                Shares.Remove(server);
            }
        }

        shares = [];
        return false;
    }

    private static IReadOnlyList<string> CollectImmediateShares(string server, string directory, string prefix)
    {
        var known = KnownShareReader(server);

        if (TryGetFreshShares(server, out var enumerated))
        {
            return FilterShares(known.Concat(enumerated), directory, prefix);
        }

        return FilterShares(known, directory, prefix);
    }

    private static void RequestShares(string server, Action onLoaded)
    {
        var startReader = false;

        lock (ShareGate)
        {
            if (PendingShares.TryGetValue(server, out var callbacks))
            {
                callbacks.Add(onLoaded);
            }
            else
            {
                PendingShares[server] = [onLoaded];
                startReader = true;
            }
        }

        if (!startReader)
        {
            return;
        }

        var reader = ShareReader;

        _ = Task.Run(() =>
        {
            IReadOnlyList<string> shares = [];
            List<Action>? callbacks = null;

            try
            {
                try
                {
                    shares = reader(server);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or SecurityException)
                {
                    shares = [];
                }

                lock (ShareGate)
                {
                    if (shares.Count > 0)
                    {
                        Shares[server] = new(shares, ClockMs());
                    }

                    if (PendingShares.Remove(server, out var registered))
                    {
                        callbacks = registered;
                    }
                }
            }
            finally
            {
                lock (ShareGate)
                {
                    PendingShares.Remove(server);
                }
            }

            if (shares.Count > 0 && callbacks is not null)
            {
                foreach (var callback in callbacks)
                {
                    callback();
                }
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
            ClosePopup(boxState);
            return;
        }

        var added = 0;

        foreach (var change in e.Changes)
        {
            added += change.AddedLength;
        }

        if (added == 0)
        {
            ClosePopup(boxState);
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

        switch (e.Key)
        {
            case Key.Up:
            case Key.Down:
                Cycle(box, e);
                break;

            case Key.Tab:
                AcceptWithTab(box, e);
                break;

            case Key.Enter:
                AcceptWithEnter(box, e);
                break;

            case Key.Escape:
                CloseWithEscape(box, e);
                break;

            case Key.Back:
                Backspace(box, e);
                break;
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
        box.LostKeyboardFocus -= OnLostKeyboardFocus;
        box.IsVisibleChanged -= OnIsVisibleChanged;
        box.Unloaded -= OnUnloaded;

        if (e.NewValue is true)
        {
            box.TextChanged += OnTextChanged;
            box.PreviewKeyDown += OnPreviewKeyDown;
            box.LostKeyboardFocus += OnLostKeyboardFocus;
            box.IsVisibleChanged += OnIsVisibleChanged;
            box.Unloaded += OnUnloaded;
            return;
        }

        Invalidate(box);
    }

    private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box && !box.IsKeyboardFocusWithin)
        {
            Invalidate(box);
        }
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && !box.IsVisible)
        {
            Invalidate(box);
        }
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
            ClosePopup(boxState);
        }
    }

    private static void Cycle(TextBox box, KeyEventArgs e)
    {
        if (!States.TryGetValue(box, out var boxState) || boxState.Active is not { Candidates.Count: > 0 } state)
        {
            return;
        }

        var popupOpen = boxState.Popup?.IsOpen == true;

        if (!popupOpen && (!state.HasPrefix || box.SelectionLength == 0))
        {
            return;
        }

        var count = state.Candidates.Count;

        if (count == 1 && state.Index == 0 && !popupOpen)
        {
            return;
        }

        if (state.Index < 0)
        {
            state.Index = e.Key == Key.Down ? 0 : count - 1;
        }
        else
        {
            state.Index = e.Key == Key.Down ? (state.Index + 1) % count : (state.Index - 1 + count) % count;
        }

        state.HasExplicitSelection = true;

        if (boxState.ListBox is { } listBox)
        {
            listBox.SelectedIndex = state.Index;
            listBox.ScrollIntoView(listBox.SelectedItem);
        }

        if (state.HasPrefix)
        {
            ApplyInline(box, state);
        }

        e.Handled = true;
    }

    private static void AcceptWithTab(TextBox box, KeyEventArgs e)
    {
        if (!States.TryGetValue(box, out var boxState)
            || boxState.Popup?.IsOpen != true
            || boxState.Active is not { Candidates.Count: > 0 } state)
        {
            return;
        }

        var index = state.Index >= 0 ? state.Index : 0;
        CommitCandidate(box, boxState, state, index);
        e.Handled = true;
    }

    private static void AcceptWithEnter(TextBox box, KeyEventArgs e)
    {
        if (!States.TryGetValue(box, out var boxState)
            || boxState.Popup?.IsOpen != true
            || boxState.Active is not { Candidates.Count: > 0 } state)
        {
            return;
        }

        if (state.HasExplicitSelection && state.Index >= 0)
        {
            CommitCandidate(box, boxState, state, state.Index);
            e.Handled = true;
            return;
        }

        ClosePopup(boxState);
        boxState.Active = null;
    }

    private static void CloseWithEscape(TextBox box, KeyEventArgs e)
    {
        if (!States.TryGetValue(box, out var boxState) || boxState.Popup?.IsOpen != true)
        {
            return;
        }

        ClosePopup(boxState);
        boxState.Active = null;
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
        ClosePopup(boxState);

        _suppress = true;
        box.Text = prefix;
        box.CaretIndex = prefix.Length;
        _suppress = false;
        e.Handled = true;

        ScheduleSuggest(box, boxState, prefix, boxState.Generation);
    }

    private static void ScheduleSuggest(TextBox box, BoxState boxState, string text, int generation)
    {
        if (!TryParseQuery(text, out var query))
        {
            ClosePopup(boxState);
            return;
        }

        switch (query.Kind)
        {
            case QueryKind.Host:
                ScheduleHostSuggest(box, boxState, text, generation, query);
                break;

            case QueryKind.Share:
                ScheduleShareSuggest(box, boxState, text, generation, query);
                break;

            case QueryKind.Directory:
                _ = SuggestDirectoryAsync(box, boxState, text, query, generation);
                break;
        }
    }

    private static void ScheduleHostSuggest(
        TextBox box,
        BoxState boxState,
        string requestedText,
        int generation,
        SuggestQuery query)
    {
        var reader = HostReader;
        var hosts = ReferenceEquals(reader, DefaultHostReader)
            ? NetworkDiscovery.ListHosts(() =>
            {
                var updated = FilterHosts(NetworkDiscovery.ListHosts(), query.Prefix);

                if (updated.Count > 0)
                {
                    _ = box.Dispatcher.InvokeAsync(() => ApplyIfCurrent(box, boxState, requestedText, generation, query, updated));
                }
            })
            : reader();

        var matches = FilterHosts(hosts, query.Prefix);

        if (matches.Count == 0)
        {
            ClosePopup(boxState);
            return;
        }

        ApplyResolved(box, boxState, requestedText, query, matches);
    }

    private static void ScheduleShareSuggest(
        TextBox box,
        BoxState boxState,
        string requestedText,
        int generation,
        SuggestQuery query)
    {
        var immediate = CollectImmediateShares(query.Server, query.Directory, query.Prefix);

        if (immediate.Count > 0)
        {
            ApplyResolved(box, boxState, requestedText, query, immediate);
        }

        if (TryGetFreshShares(query.Server, out _))
        {
            if (immediate.Count == 0)
            {
                ClosePopup(boxState);
            }

            return;
        }

        RequestShares(
            query.Server,
            () =>
            {
                var combined = CollectImmediateShares(query.Server, query.Directory, query.Prefix);

                if (combined.Count > 0)
                {
                    _ = box.Dispatcher.InvokeAsync(() => ApplyIfCurrent(box, boxState, requestedText, generation, query, combined));
                }
            });
    }

    private static async Task SuggestDirectoryAsync(
        TextBox box,
        BoxState boxState,
        string text,
        SuggestQuery query,
        int generation)
    {
        var matches = await ReadDirectoryMatchesAsync(query.Directory, query.Prefix).ConfigureAwait(false);

        if (matches.Count == 0)
        {
            _ = box.Dispatcher.InvokeAsync(() => ClosePopupIfCurrent(box, boxState, text, generation));
            return;
        }

        _ = box.Dispatcher.InvokeAsync(() => ApplyIfCurrent(box, boxState, text, generation, query, matches));
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
        if (UncPath.TryParseShareRoot(directory, out var shareRoot))
        {
            return shareRoot;
        }

        try
        {
            return Path.GetPathRoot(directory) is { Length: > 0 } root ? root : directory;
        }
        catch (ArgumentException)
        {
            return directory;
        }
    }

    private static void ClosePopupIfCurrent(TextBox box, BoxState boxState, string requestedText, int generation)
    {
        if (boxState.Generation == generation && string.Equals(box.Text, requestedText, StringComparison.Ordinal))
        {
            ClosePopup(boxState);
        }
    }

    private static void ApplyIfCurrent(
        TextBox box,
        BoxState boxState,
        string requestedText,
        int generation,
        SuggestQuery query,
        IReadOnlyList<string> matches)
    {
        if (!GetEnabled(box) || boxState.Generation != generation)
        {
            return;
        }

        var active = boxState.Active;
        var selected = active is { Index: >= 0 } ? active.Candidates[active.Index] : null;
        var hasInline = selected is not null
            && active!.HasPrefix
            && active.PrefixStartLength == requestedText.Length
            && box.SelectionStart == requestedText.Length
            && box.SelectionLength == selected.Length - requestedText.Length
            && string.Equals(box.Text, selected, StringComparison.Ordinal);

        if (!hasInline
            && (box.SelectionLength != 0
                || box.CaretIndex != requestedText.Length
                || !string.Equals(box.Text, requestedText, StringComparison.Ordinal)))
        {
            return;
        }

        ApplyResolved(box, boxState, requestedText, query, matches, selected, active?.HasExplicitSelection == true);
    }

    private static void ApplyResolved(
        TextBox box,
        BoxState boxState,
        string requestedText,
        SuggestQuery query,
        IReadOnlyList<string> matches,
        string? selected = null,
        bool explicitSelection = false)
    {
        var hasPrefix = query.Prefix.Length > 0;
        var state = new State(query.Kind, requestedText.Length, hasPrefix, matches);

        for (var i = 0; selected is not null && i < matches.Count; i++)
        {
            if (string.Equals(matches[i], selected, StringComparison.OrdinalIgnoreCase))
            {
                state.Index = i;
                state.HasExplicitSelection = explicitSelection;
                break;
            }
        }

        boxState.Active = state;

        if (hasPrefix)
        {
            ApplyInline(box, state);
        }

        ShowPopup(box, boxState, state);
    }

    private static void ApplyInline(TextBox box, State state)
    {
        var candidate = state.Candidates[state.Index];

        _suppress = true;
        box.Text = candidate;

        if (state.PrefixStartLength <= candidate.Length)
        {
            box.Select(state.PrefixStartLength, candidate.Length - state.PrefixStartLength);
        }
        else
        {
            box.CaretIndex = candidate.Length;
        }

        _suppress = false;
    }

    private static void CommitCandidate(TextBox box, BoxState boxState, State state, int index)
    {
        var candidate = state.Candidates[index];
        var isHost = state.Kind == QueryKind.Host;
        var committedText = isHost ? candidate + @"\" : candidate;

        boxState.Generation++;
        boxState.Active = null;
        ClosePopup(boxState);

        _suppress = true;
        box.Text = committedText;
        box.CaretIndex = committedText.Length;
        _suppress = false;

        if (isHost)
        {
            ScheduleSuggest(box, boxState, committedText, boxState.Generation);
        }
    }

    private static bool CanOpenPopup(TextBox box)
    {
        return box.IsKeyboardFocusWithin && PresentationSource.FromVisual(box) is not null;
    }

    private static void ShowPopup(TextBox box, BoxState boxState, State state)
    {
        if (!CanOpenPopup(box))
        {
            ClosePopup(boxState);
            return;
        }

        var (popup, listBox) = EnsurePopup(box, boxState);
        listBox.ItemsSource = state.Candidates;
        listBox.SelectedIndex = state.Index;
        popup.MinWidth = Math.Max(box.ActualWidth, 180);
        popup.IsOpen = true;
    }

    private static void ClosePopup(BoxState boxState)
    {
        if (boxState.Popup is { IsOpen: true } popup)
        {
            popup.IsOpen = false;
        }

        if (boxState.ListBox is { } listBox)
        {
            listBox.ItemsSource = null;
        }
    }

    private static (Popup Popup, ListBox ListBox) EnsurePopup(TextBox box, BoxState boxState)
    {
        if (boxState.Popup is not null && boxState.ListBox is not null)
        {
            return (boxState.Popup, boxState.ListBox);
        }

        var listBox = new ListBox
        {
            Focusable = false,
            Background = Brushes.Transparent,
            BorderThickness = new(0),
            MaxHeight = PopupMaxHeight,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ItemContainerStyle = CreateItemStyle(),
        };

        ScrollViewer.SetVerticalScrollBarVisibility(listBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Disabled);
        KeyboardNavigation.SetDirectionalNavigation(listBox, KeyboardNavigationMode.None);

        listBox.PreviewMouseLeftButtonDown += (_, e) => OnPopupListMouseDown(box, boxState, listBox, e);

        var border = new Border
        {
            BorderThickness = new(1),
            Padding = new(4),
            Margin = new(0, 4, 0, 0),
            MaxWidth = PopupMaxWidth,
            Child = listBox,
        };
        border.SetResourceReference(Border.BackgroundProperty, "Bg.Surface");
        border.SetResourceReference(Border.BorderBrushProperty, "Border.Default");
        border.SetResourceReference(Border.CornerRadiusProperty, "Radius.M");

        var popup = new Popup
        {
            PlacementTarget = box,
            Placement = PlacementMode.Bottom,
            AllowsTransparency = true,
            Focusable = false,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade,
            MaxHeight = PopupMaxHeight + (MaxVisibleRows * 2),
            Child = border,
        };

        boxState.Popup = popup;
        boxState.ListBox = listBox;
        return (popup, listBox);
    }

    private static void OnPopupListMouseDown(TextBox box, BoxState boxState, ListBox listBox, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(listBox, source) is not ListBoxItem item)
        {
            return;
        }

        var index = listBox.ItemContainerGenerator.IndexFromContainer(item);

        if (index < 0 || boxState.Active is not { } state || index >= state.Candidates.Count)
        {
            return;
        }

        CommitCandidate(box, boxState, state, index);
        e.Handled = true;
    }

    private static Style CreateItemStyle()
    {
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "ItemBd";
        borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        borderFactory.SetResourceReference(Border.CornerRadiusProperty, "Radius.S");

        var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        contentFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(contentFactory);

        var template = new ControlTemplate(typeof(ListBoxItem))
        {
            VisualTree = borderFactory,
        };

        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Fg.Primary")));
        style.Setters.Add(new Setter(Control.FontFamilyProperty, new DynamicResourceExtension("Font.Mono")));
        style.Setters.Add(new Setter(Control.FontSizeProperty, new DynamicResourceExtension("Font.Size.S")));

        var hoverTrigger = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
        };
        hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Bg.SurfaceMute")));
        style.Triggers.Add(hoverTrigger);

        var selectedTrigger = new Trigger
        {
            Property = ListBoxItem.IsSelectedProperty,
            Value = true,
        };
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Accent.Soft")));
        selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Fg.Primary")));
        style.Triggers.Add(selectedTrigger);

        return style;
    }

    private enum QueryKind
    {
        None = 0,
        Host = 1,
        Share = 2,
        Directory = 3,
    }

    private readonly record struct SuggestQuery(QueryKind Kind, string Directory, string Server, string Prefix);

    private sealed class BoxState
    {
        public int Generation { get; set; }

        public State? Active { get; set; }

        public Popup? Popup { get; set; }

        public ListBox? ListBox { get; set; }
    }

    private sealed class State(
        QueryKind kind,
        int prefixStartLength,
        bool hasPrefix,
        IReadOnlyList<string> candidates)
    {
        public QueryKind Kind { get; } = kind;

        public int PrefixStartLength { get; } = prefixStartLength;

        public bool HasPrefix { get; } = hasPrefix;

        public IReadOnlyList<string> Candidates { get; } = candidates;

        public int Index { get; set; } = hasPrefix ? 0 : -1;

        public bool HasExplicitSelection { get; set; }
    }

    private sealed record WaitingQuery(
        string Directory,
        string Prefix,
        TaskCompletionSource<IReadOnlyList<string>> Completion);

    private readonly record struct ShareCacheEntry(IReadOnlyList<string> Items, long UpdatedMs);

    private readonly record struct RootLookup(Task<IReadOnlyList<string>> Running, WaitingQuery? Waiting);
}
