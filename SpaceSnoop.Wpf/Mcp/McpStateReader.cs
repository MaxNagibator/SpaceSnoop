namespace SpaceSnoop.Wpf.Mcp;

internal sealed class McpStateReader(IScanAutomation scan, ISyncAutomation sync, McpNavigator navigator)
{
    public McpScanState ReadScanState()
    {
        return new(scan.SelectedDrive,
            scan.ResultPath,
            scan.IsScanning,
            scan.HasResult,
            scan.ResultSizeText,
            scan.ResultFileCountText,
            scan.ResultDirCountText,
            scan.MarkedCount,
            Math.Round(scan.LastScanElapsed.TotalSeconds, 2),
            scan.ResultRateText);
    }

    public McpSyncState ReadSyncState()
    {
        return new(sync.LeftPath,
            sync.RightPath,
            sync.Mode,
            sync.Winner,
            sync.Mirror,
            sync.Exclusions,
            sync.IsBusy,
            sync.HasComparison,
            new Dictionary<string, int>
            {
                ["Identical"] = sync.IdenticalCount,
                ["LeftOnly"] = sync.LeftOnlyCount,
                ["RightOnly"] = sync.RightOnlyCount,
                ["Modified"] = sync.ModifiedCount,
                ["Conflict"] = sync.ConflictCount,
            });
    }

    public string DescribeContext()
    {
        return McpDispatch.Run(() =>
        {
            List<string> parts = [$"страница «{McpFormat.DescribePage(navigator.CurrentSectionKey)}»"];

            if (scan.IsScanning)
            {
                parts.Add($"идёт сканирование {scan.SelectedDrive}");
            }
            else if (scan.HasResult)
            {
                parts.Add($"открыт скан {scan.ResultPath} – {scan.ResultSizeText}, файлов {scan.ResultFileCountText}");
            }

            if (scan.MarkedCount > 0)
            {
                parts.Add($"помечено на удаление {scan.MarkedCount}");
            }

            if (sync.HasComparison)
            {
                parts.Add($"открыто сравнение {sync.LeftPath} → {sync.RightPath}, различий {sync.LeftOnlyCount + sync.RightOnlyCount + sync.ModifiedCount + sync.ConflictCount}");
            }

            return $"[Состояние окна SpaceSnoop: {string.Join("; ", parts)}. Это служебная справка, отвечать на неё не надо.]";
        });
    }
}
