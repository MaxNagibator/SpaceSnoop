using System.ComponentModel;

namespace SpaceSnoop.Controls;

public sealed class SyncDiffView : UserControl
{
    private static readonly Color LeftOnlyColor = Color.Green;
    private static readonly Color RightOnlyColor = Color.DodgerBlue;
    private static readonly Color ModifiedColor = Color.DarkOrange;
    private static readonly Color ConflictColor = Color.Red;
    private static readonly Color IdenticalColor = Color.Gray;
    private static readonly Color AbsentColor = Color.FromArgb(200, 200, 200);
    private static readonly Color DirBackColor = Color.FromArgb(245, 245, 250);
    private static readonly Color ActionColBackColor = Color.FromArgb(248, 248, 248);
    private static readonly Color SeparatorColor = Color.FromArgb(220, 220, 220);
    private static readonly Color HoverColor = Color.FromArgb(230, 240, 255);

    private static readonly Dictionary<SyncAction, (string Symbol, Color Color)> ActionStyles = new()
    {
        [SyncAction.CopyToRight] = ("\u2192", Color.Green),
        [SyncAction.CopyToLeft] = ("\u2190", Color.DodgerBlue),
        [SyncAction.Skip] = ("\u2298", Color.Gray),
        [SyncAction.DeleteLeft] = ("\u2297", Color.Red),
        [SyncAction.DeleteRight] = ("\u2297", Color.Red),
        [SyncAction.None] = ("\u26A1", Color.Red),
    };

    private static readonly SyncAction[] LeftOnlyCycle = [SyncAction.CopyToRight, SyncAction.Skip, SyncAction.DeleteLeft];
    private static readonly SyncAction[] RightOnlyCycle = [SyncAction.CopyToLeft, SyncAction.Skip, SyncAction.DeleteRight];
    private static readonly SyncAction[] LeftOnlyCycleNoDelete = [SyncAction.CopyToRight, SyncAction.Skip];
    private static readonly SyncAction[] RightOnlyCycleNoDelete = [SyncAction.CopyToLeft, SyncAction.Skip];
    private static readonly SyncAction[] BothSidesCycle = [SyncAction.CopyToRight, SyncAction.CopyToLeft, SyncAction.Skip];
    private static readonly SyncAction[] ConflictCycle = [SyncAction.Skip];

    private const string DeleteBlockedHint = "противоположную сторону обошли не полностью";
    private const string TypeConflictHint = "слева и справа объекты разного вида";

    private readonly VScrollBar _scrollBar;
    private readonly Font _boldFont;
    private readonly Font _actionFont;
    private readonly Font _strikeoutFont;

    private readonly StringFormat _leftAlign = new()
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap,
    };

    private readonly StringFormat _rightAlign = new()
    {
        Alignment = StringAlignment.Far,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap,
    };

    private readonly StringFormat _centerAlign = new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
    };

    private readonly HashSet<DirectoryComparison> _expanded = [];
    private ContextMenuStrip? _contextMenu;
    private DirectoryComparison? _root;
    private int _rowHeight = 22;
    private int _indentWidth = 20;
    private int _actionColumnWidth = 40;
    private int _iconWidth = 16;
    private int _sizeColumnWidth = 80;
    private int _padding = 4;
    private List<RowData> _rows = [];
    private int _hoverRow = -1;
    private bool _showIdentical;
    private bool _showSizes = true;
    private bool _showAbsentAsEmpty;

    public SyncDiffView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        ScaleForDpi();

        _boldFont = new(Font, FontStyle.Bold);
        _actionFont = new("Segoe UI", 10f, FontStyle.Bold);
        _strikeoutFont = new(Font, FontStyle.Strikeout);

        _scrollBar = new()
        { Dock = DockStyle.Right };

        _scrollBar.Scroll += (_, _) => Invalidate();
        Controls.Add(_scrollBar);
    }

    public event EventHandler? ActionChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowIdentical
    {
        get => _showIdentical;
        set
        {
            _showIdentical = value;
            Rebuild();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowSizes
    {
        get => _showSizes;
        set
        {
            _showSizes = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowAbsentAsEmpty
    {
        get => _showAbsentAsEmpty;
        set
        {
            _showAbsentAsEmpty = value;
            Invalidate();
        }
    }

    public void SetData(ComparisonResult? result)
    {
        _expanded.Clear();
        _root = result?.Root;

        if (result != null)
        {
            ExpandAll(result.Root);
        }

        Rebuild();
    }

    public void RefreshView()
    {
        Rebuild();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        var scrollOffset = _scrollBar.Value;
        var drawWidth = ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0);
        var sideWidth = (drawWidth - _actionColumnWidth) / 2;
        var actionX = sideWidth;

        using (var headerBrush = new SolidBrush(Color.FromArgb(250, 250, 250)))
        {
            g.FillRectangle(headerBrush, 0, 0, drawWidth, _rowHeight);
        }

        using (var headerFont = new Font(Font, FontStyle.Bold))
        {
            var headerRect = new Rectangle(_padding, 0, sideWidth - _padding * 2, _rowHeight);
            g.DrawString("Левая", headerFont, Brushes.DimGray, headerRect, _leftAlign);

            headerRect = new(actionX + _actionColumnWidth + _padding, 0, sideWidth - _padding * 2, _rowHeight);
            g.DrawString("Правая", headerFont, Brushes.DimGray, headerRect, _leftAlign);
        }

        using var sepPen = new Pen(SeparatorColor);
        g.DrawLine(sepPen, 0, _rowHeight, drawWidth, _rowHeight);

        using (var actionBg = new SolidBrush(ActionColBackColor))
        {
            g.FillRectangle(actionBg, actionX, _rowHeight, _actionColumnWidth, ClientSize.Height - _rowHeight);
        }

        g.DrawLine(sepPen, actionX, 0, actionX, ClientSize.Height);
        g.DrawLine(sepPen, actionX + _actionColumnWidth, 0, actionX + _actionColumnWidth, ClientSize.Height);

        var startRow = Math.Max(0, (scrollOffset - _rowHeight) / _rowHeight);
        var endRow = Math.Min(_rows.Count, (scrollOffset + ClientSize.Height) / _rowHeight + 1);

        for (var i = startRow; i < endRow; i++)
        {
            var y = _rowHeight + i * _rowHeight - scrollOffset;

            if (y + _rowHeight < _rowHeight || y > ClientSize.Height)
            {
                continue;
            }

            var row = _rows[i];

            if (i == _hoverRow)
            {
                using var hoverBrush = new SolidBrush(HoverColor);
                g.FillRectangle(hoverBrush, 0, y, sideWidth, _rowHeight);
                g.FillRectangle(hoverBrush, actionX + _actionColumnWidth, y, sideWidth, _rowHeight);
            }

            if (row.Directory != null)
            {
                DrawDirectoryRow(g, row, y, sideWidth, actionX, drawWidth);
            }
            else if (row.File != null)
            {
                DrawFileRow(g, row, y, sideWidth, actionX, drawWidth);
            }

            g.DrawLine(sepPen, 0, y + _rowHeight, drawWidth, y + _rowHeight);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var newHover = GetRowAtY(e.Y);

        if (newHover == _hoverRow)
        {
            return;
        }

        _hoverRow = newHover;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hoverRow == -1)
        {
            return;
        }

        _hoverRow = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);

        var rowIndex = GetRowAtY(e.Y);

        if (rowIndex < 0 || rowIndex >= _rows.Count)
        {
            return;
        }

        var row = _rows[rowIndex];

        if (e.Button == MouseButtons.Left)
        {
            HandleLeftClick(row, e.X);
        }
        else if (e.Button == MouseButtons.Right)
        {
            HandleRightClick(row, e.Location);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (!_scrollBar.Visible)
        {
            return;
        }

        var delta = e.Delta > 0 ? -_rowHeight * 3 : _rowHeight * 3;
        var newValue = Math.Clamp(_scrollBar.Value + delta, _scrollBar.Minimum, Math.Max(0, _scrollBar.Maximum - _scrollBar.LargeChange));
        _scrollBar.Value = newValue;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollBar();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _boldFont.Dispose();
            _actionFont.Dispose();
            _strikeoutFont.Dispose();
            _leftAlign.Dispose();
            _rightAlign.Dispose();
            _centerAlign.Dispose();
            _contextMenu?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static void CycleAction(FileComparison file)
    {
        var cycle = CycleFor(file);
        var currentIndex = Array.IndexOf(cycle, file.Action);
        file.Action = currentIndex < 0
            ? cycle[0]
            : cycle[(currentIndex + 1) % cycle.Length];
    }

    private static SyncAction[] CycleFor(FileComparison file)
    {
        if (file.TypeConflict != FileTypeConflict.None)
        {
            return ConflictCycle;
        }

        return file.Status switch
        {
            ComparisonStatus.LeftOnly => file.DeleteLeftBlocked ? LeftOnlyCycleNoDelete : LeftOnlyCycle,
            ComparisonStatus.RightOnly => file.DeleteRightBlocked ? RightOnlyCycleNoDelete : RightOnlyCycle,
            _ => BothSidesCycle,
        };
    }

    private static bool DeleteBlocked(FileComparison file, SyncAction action)
    {
        return action switch
        {
            SyncAction.DeleteLeft => file.DeleteLeftBlocked,
            SyncAction.DeleteRight => file.DeleteRightBlocked,
            _ => false,
        };
    }

    private static bool DeleteBlocked(DirectoryComparison dir, SyncAction action)
    {
        return action switch
        {
            SyncAction.DeleteLeft => dir.DeleteLeftBlocked || dir.RightIncomplete,
            SyncAction.DeleteRight => dir.DeleteRightBlocked || dir.LeftIncomplete,
            _ => false,
        };
    }

    private static SyncAction DirActionFor(DirectoryComparison dir, SyncAction requested)
    {
        return dir.Status == ComparisonStatus.LeftOnly
            ? requested switch
            {
                SyncAction.CopyToRight => SyncAction.CopyToRight,
                SyncAction.DeleteLeft => SyncAction.DeleteLeft,
                _ => SyncAction.Skip,
            }
            : requested switch
            {
                SyncAction.CopyToLeft => SyncAction.CopyToLeft,
                SyncAction.DeleteRight => SyncAction.DeleteRight,
                _ => SyncAction.Skip,
            };
    }

    private static long CalcDirectorySize(DirectoryComparison dir, bool isLeft)
    {
        long total = 0;

        foreach (var file in dir.Files)
        {
            var size = isLeft ? file.LeftSize : file.RightSize;

            if (size.HasValue)
            {
                total += size.Value;
            }
        }

        foreach (var sub in dir.SubDirectories)
        {
            total += CalcDirectorySize(sub, isLeft);
        }

        return total;
    }

    private static Color GetStatusColor(ComparisonStatus status)
    {
        return status switch
        {
            ComparisonStatus.LeftOnly => LeftOnlyColor,
            ComparisonStatus.RightOnly => RightOnlyColor,
            ComparisonStatus.Modified => ModifiedColor,
            ComparisonStatus.Conflict => ConflictColor,
            ComparisonStatus.Identical => IdenticalColor,
            _ => Color.Black,
        };
    }

    private static void ApplyActionRecursive(DirectoryComparison dir, SyncAction action)
    {
        if (dir.Status is ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly && !DeleteBlocked(dir, action))
        {
            dir.Action = DirActionFor(dir, action);
        }

        foreach (var file in dir.Files)
        {
            if (file.Status == ComparisonStatus.Identical || DeleteBlocked(file, action))
            {
                continue;
            }

            if (file.TypeConflict != FileTypeConflict.None && action != SyncAction.Skip)
            {
                continue;
            }

            file.Action = action;
        }

        foreach (var sub in dir.SubDirectories)
        {
            ApplyActionRecursive(sub, action);
        }
    }

    private void HandleLeftClick(RowData row, int x)
    {
        if (row.Directory != null)
        {
            if (row.IsExpanded)
            {
                _expanded.Remove(row.Directory);
            }
            else
            {
                _expanded.Add(row.Directory);
            }

            Rebuild();
            return;
        }

        if (row.File == null || row.File.Status == ComparisonStatus.Identical)
        {
            return;
        }

        var drawWidth = ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0);
        var actionX = (drawWidth - _actionColumnWidth) / 2;

        if (x < actionX || x >= actionX + _actionColumnWidth)
        {
            return;
        }

        CycleAction(row.File);
        Invalidate();
        ActionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleRightClick(RowData row, Point location)
    {
        if (row.File != null && row.File.Status != ComparisonStatus.Identical)
        {
            ShowContextMenu(row.File, location);
        }
        else if (row.Directory != null && row.Directory.Status != ComparisonStatus.Identical)
        {
            ShowDirectoryContextMenu(row.Directory, location);
        }
    }

    private void ScaleForDpi()
    {
        var scale = DeviceDpi / 96f;
        _rowHeight = (int)(22 * scale);
        _indentWidth = (int)(20 * scale);
        _actionColumnWidth = (int)(40 * scale);
        _iconWidth = (int)(16 * scale);
        _sizeColumnWidth = (int)(80 * scale);
        _padding = (int)(4 * scale);
    }

    private void Rebuild()
    {
        _rows = [];

        if (_root != null)
        {
            FlattenDirectory(_root, 0);
        }

        UpdateScrollBar();
        Invalidate();
    }

    private void FlattenDirectory(DirectoryComparison dir, int indent)
    {
        foreach (var sub in dir.SubDirectories)
        {
            if (!_showIdentical && sub.Status == ComparisonStatus.Identical)
            {
                continue;
            }

            var isExpanded = _expanded.Contains(sub);
            _rows.Add(new(sub, indent, isExpanded));

            if (isExpanded)
            {
                FlattenDirectory(sub, indent + 1);
            }
        }

        foreach (var file in dir.Files)
        {
            if (!_showIdentical && file.Status == ComparisonStatus.Identical)
            {
                continue;
            }

            _rows.Add(new(file, indent));
        }
    }

    private void ExpandAll(DirectoryComparison dir)
    {
        _expanded.Add(dir);

        foreach (var sub in dir.SubDirectories)
        {
            ExpandAll(sub);
        }
    }

    private void UpdateScrollBar()
    {
        var totalHeight = _rows.Count * _rowHeight;
        var visibleHeight = ClientSize.Height;

        if (totalHeight <= visibleHeight)
        {
            _scrollBar.Visible = false;
            _scrollBar.Value = 0;
        }
        else
        {
            _scrollBar.Visible = true;
            _scrollBar.Minimum = 0;
            _scrollBar.Maximum = totalHeight;
            _scrollBar.LargeChange = Math.Max(1, visibleHeight);
            _scrollBar.SmallChange = _rowHeight;

            if (_scrollBar.Value > totalHeight - visibleHeight)
            {
                _scrollBar.Value = Math.Max(0, totalHeight - visibleHeight);
            }
        }
    }

    private void DrawDirectoryRow(Graphics g, RowData row, int y, int sideWidth, int actionX, int drawWidth)
    {
        var dir = row.Directory!;

        using (var dirBg = new SolidBrush(DirBackColor))
        {
            g.FillRectangle(dirBg, 0, y, sideWidth, _rowHeight);
            g.FillRectangle(dirBg, actionX + _actionColumnWidth, y, sideWidth, _rowHeight);
        }

        var indent = row.Indent * _indentWidth;
        var expandIcon = row.IsExpanded ? "\u25BC" : "\u25B6";
        var statusColor = GetStatusColor(dir.Status);
        var sizeReserve = _showSizes ? _sizeColumnWidth + _padding : _padding;

        DrawDirectoryLeft(g, dir, y, sideWidth, indent, sizeReserve, expandIcon, statusColor);

        if (dir.Status == ComparisonStatus.LeftOnly && _showAbsentAsEmpty)
        {
            return;
        }

        DrawDirectoryRight(g, dir, y, actionX, drawWidth, indent, sizeReserve, expandIcon,
            statusColor);
    }

    private void DrawFileRow(Graphics g, RowData row, int y, int sideWidth, int actionX, int drawWidth)
    {
        var file = row.File!;
        var statusColor = GetStatusColor(file.Status);
        var indent = row.Indent * _indentWidth + _iconWidth + _padding;
        var sizeReserve = _showSizes ? _sizeColumnWidth + _padding : _padding;

        DrawFileLeft(g, file, y, sideWidth, indent, sizeReserve, statusColor);
        DrawFileAction(g, file, y, actionX);
        DrawFileRight(g, file, y, drawWidth, actionX, row.Indent, sizeReserve, statusColor);
    }

    private void DrawDirectoryLeft(
        Graphics g,
        DirectoryComparison dir,
        int y,
        int sideWidth,
        int indent,
        int sizeReserve,
        string expandIcon,
        Color statusColor)
    {
        var leftIndent = indent + _padding;
        var absent = dir.Status == ComparisonStatus.RightOnly;

        if (!absent || !_showAbsentAsEmpty)
        {
            using var brush = new SolidBrush(Color.Gray);
            g.DrawString(expandIcon, Font, brush, leftIndent, y + (_rowHeight - Font.Height) / 2f);
        }

        leftIndent += _iconWidth;

        if (absent)
        {
            if (!_showAbsentAsEmpty)
            {
                DrawName(g, dir.Name, y, leftIndent, sideWidth - leftIndent - _padding, AbsentColor, _strikeoutFont);
            }

            return;
        }

        DrawName(g, dir.Name, y, leftIndent, sideWidth - leftIndent - sizeReserve, statusColor, _boldFont);
        DrawDirectorySize(g, dir, y, sideWidth, true);
    }

    private void DrawDirectoryRight(
        Graphics g,
        DirectoryComparison dir,
        int y,
        int actionX,
        int drawWidth,
        int indent,
        int sizeReserve,
        string expandIcon,
        Color statusColor)
    {
        var rightIndent = actionX + _actionColumnWidth + indent + _padding;
        var absent = dir.Status == ComparisonStatus.LeftOnly;

        if (!absent || !_showAbsentAsEmpty)
        {
            using var brush = new SolidBrush(Color.Gray);
            g.DrawString(expandIcon, Font, brush, rightIndent, y + (_rowHeight - Font.Height) / 2f);
        }

        rightIndent += _iconWidth;

        if (absent)
        {
            if (!_showAbsentAsEmpty)
            {
                DrawName(g, dir.Name, y, rightIndent, drawWidth - rightIndent - _padding, AbsentColor, _strikeoutFont);
            }

            return;
        }

        var rightEnd = drawWidth - _padding;
        DrawName(g, dir.Name, y, rightIndent, rightEnd - rightIndent - (sizeReserve - _padding), statusColor, _boldFont);
        DrawDirectorySize(g, dir, y, drawWidth, false);
    }

    private void DrawName(Graphics g, string name, int y, int x, int width, Color color, Font font)
    {
        using var brush = new SolidBrush(color);
        g.DrawString(name, font, brush, new Rectangle(x, y, width, _rowHeight), _leftAlign);
    }

    private void DrawDirectorySize(Graphics g, DirectoryComparison dir, int y, int width, bool left)
    {
        if (!_showSizes)
        {
            return;
        }

        var size = CalcDirectorySize(dir, left);
        var x = left
            ? width - _sizeColumnWidth - _padding
            : width - _sizeColumnWidth - _padding * 2;

        var sizeRect = new Rectangle(x, y, _sizeColumnWidth, _rowHeight);

        using var brush = new SolidBrush(Color.FromArgb(140, 140, 140));
        g.DrawString(SizeFormatter.Format(size), Font, brush, sizeRect, _rightAlign);
    }

    private void DrawFileLeft(Graphics g, FileComparison file, int y, int sideWidth, int indent, int sizeReserve, Color statusColor)
    {
        if (file.Status == ComparisonStatus.RightOnly)
        {
            if (!_showAbsentAsEmpty)
            {
                DrawName(g, file.Name, y, indent, sideWidth - indent - _padding, AbsentColor, _strikeoutFont);
            }

            return;
        }

        DrawName(g, file.Name, y, indent, sideWidth - indent - sizeReserve, statusColor, Font);

        if (_showSizes && file.LeftSize.HasValue)
        {
            DrawFileSize(g, file.LeftSize.Value, y, sideWidth);
        }
    }

    private void DrawFileAction(Graphics g, FileComparison file, int y, int actionX)
    {
        var actionRect = new Rectangle(actionX, y, _actionColumnWidth, _rowHeight);

        if (file.Status == ComparisonStatus.Identical)
        {
            using var brush = new SolidBrush(Color.DarkGray);
            g.DrawString("=", Font, brush, actionRect, _centerAlign);
        }
        else if (ActionStyles.TryGetValue(file.Action, out var style))
        {
            using var brush = new SolidBrush(style.Color);
            g.DrawString(style.Symbol, _actionFont, brush, actionRect, _centerAlign);
        }
    }

    private void DrawFileRight(Graphics g, FileComparison file, int y, int drawWidth, int actionX, int indent, int sizeReserve, Color statusColor)
    {
        var rightIndent = actionX + _actionColumnWidth + indent * _indentWidth + _iconWidth + _padding;

        if (file.Status == ComparisonStatus.LeftOnly)
        {
            if (!_showAbsentAsEmpty)
            {
                DrawName(g, file.Name, y, rightIndent, drawWidth - rightIndent - _padding, AbsentColor, _strikeoutFont);
            }

            return;
        }

        var rightEnd = drawWidth - _padding;
        DrawName(g, file.Name, y, rightIndent, rightEnd - rightIndent - sizeReserve, statusColor, Font);

        if (_showSizes && file.RightSize.HasValue)
        {
            DrawFileSize(g, file.RightSize.Value, y, rightEnd);
        }
    }

    private void DrawFileSize(Graphics g, long size, int y, int x)
    {
        var sizeRect = new Rectangle(x - _sizeColumnWidth - _padding, y, _sizeColumnWidth, _rowHeight);

        using var brush = new SolidBrush(Color.FromArgb(140, 140, 140));
        g.DrawString(SizeFormatter.Format(size), Font, brush, sizeRect, _rightAlign);
    }

    private void ShowContextMenu(FileComparison file, Point location)
    {
        _contextMenu?.Dispose();
        var menu = new ContextMenuStrip();
        _contextMenu = menu;

        var copyRight = menu.Items.Add("Копировать \u2192", null, (_, _) => AssignAction(file, SyncAction.CopyToRight));
        var copyLeft = menu.Items.Add("\u2190 Копировать", null, (_, _) => AssignAction(file, SyncAction.CopyToLeft));

        if (file.TypeConflict != FileTypeConflict.None)
        {
            Block(copyRight, TypeConflictHint);
            Block(copyLeft, TypeConflictHint);
        }

        menu.Items.Add("Пропустить", null, (_, _) => AssignAction(file, SyncAction.Skip));

        if (file.TypeConflict == FileTypeConflict.None && file.Status is ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly)
        {
            var deleteSide = file.Status == ComparisonStatus.LeftOnly
                ? SyncAction.DeleteLeft
                : SyncAction.DeleteRight;

            var delete = menu.Items.Add("Удалить", null, (_, _) => AssignAction(file, deleteSide));

            if (DeleteBlocked(file, deleteSide))
            {
                Block(delete, DeleteBlockedHint);
            }
        }

        menu.Show(this, location);
    }

    private void ShowDirectoryContextMenu(DirectoryComparison dir, Point location)
    {
        _contextMenu?.Dispose();
        var menu = new ContextMenuStrip();
        _contextMenu = menu;

        menu.Items.Add($"Папка «{dir.Name}»:").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Всё копировать \u2192", null, (_, _) => ApplyActionToDirectory(dir, SyncAction.CopyToRight)).Enabled = dir.Status != ComparisonStatus.RightOnly;
        menu.Items.Add("\u2190 Всё копировать", null, (_, _) => ApplyActionToDirectory(dir, SyncAction.CopyToLeft)).Enabled = dir.Status != ComparisonStatus.LeftOnly;
        menu.Items.Add("Всё пропустить", null, (_, _) => ApplyActionToDirectory(dir, SyncAction.Skip));

        if (dir.Status is ComparisonStatus.LeftOnly)
        {
            AddDirectoryDelete(menu, dir, SyncAction.DeleteLeft, "Всё удалить слева");
        }
        else if (dir.Status is ComparisonStatus.RightOnly)
        {
            AddDirectoryDelete(menu, dir, SyncAction.DeleteRight, "Всё удалить справа");
        }

        menu.Show(this, location);
    }

    private void ApplyActionToDirectory(DirectoryComparison dir, SyncAction action)
    {
        ApplyActionRecursive(dir, action);
        Invalidate();
        ActionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AssignAction(FileComparison file, SyncAction action)
    {
        file.Action = action;
        Invalidate();
        ActionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddDirectoryDelete(ContextMenuStrip menu, DirectoryComparison dir, SyncAction action, string header)
    {
        var item = menu.Items.Add(header, null, (_, _) => ApplyActionToDirectory(dir, action));

        if (DeleteBlocked(dir, action))
        {
            Block(item, DeleteBlockedHint);
        }
    }

    private static void Block(ToolStripItem item, string reason)
    {
        item.Text = $"{item.Text} – {reason}";
        item.Enabled = false;
    }

    private int GetRowAtY(int y)
    {
        var scrollOffset = _scrollBar.Value;
        return (y - _rowHeight + scrollOffset) / _rowHeight;
    }

    private sealed record RowData
    {
        public RowData(DirectoryComparison dir, int indent, bool isExpanded)
        {
            Directory = dir;
            Indent = indent;
            IsExpanded = isExpanded;
        }

        public RowData(FileComparison file, int indent)
        {
            File = file;
            Indent = indent;
        }

        public DirectoryComparison? Directory { get; }
        public FileComparison? File { get; }
        public int Indent { get; }
        public bool IsExpanded { get; }
    }
}
