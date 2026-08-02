using KeepShell.Services.Modal;
using Microsoft.Extensions.DependencyInjection;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class GalleryDialogs
{
    public const string Confirm = "confirm";
    public const string Diff = "diff";
    public const string Delete = "delete";
    public const string Archive = "archive";
    public const string Git = "git";
    public const string Batch = "batch";

    private const int GitFolderSample = 3;
    private const string TextSample = "заметки.md";

    public static IReadOnlyList<string> All { get; } = [Confirm, Diff, Delete, Archive, Git, Batch];

    public static string Section(string key)
    {
        return key switch
        {
            Delete or Archive => SectionKey.Scan,
            Batch => SectionKey.Schedule,
            _ => SectionKey.Sync,
        };
    }

    public static void Open(string key, IServiceProvider services, GalleryFixture fixture, ModalHostViewModel modals)
    {
        switch (key)
        {
            case Confirm:
                Run(services.GetRequiredService<SyncViewModel>().SyncCommand, "«Синхронизировать» недоступна – сравнение не дало плана.");
                break;

            case Diff:
                OpenDiff(services);
                break;

            case Delete:
                OpenDelete(services);
                break;

            case Archive:
                OpenArchive(services);
                break;

            case Git:
                _ = modals.ShowAsync(new GitFolderPromptViewModel(GitFolderSample));
                break;

            case Batch:
                _ = modals.ShowAsync(CreateBatch(services, fixture));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, "Неизвестный диалог галереи.");
        }
    }

    public static void Cleanup(string key, IServiceProvider services)
    {
        if (key is not Delete)
        {
            return;
        }

        var root = services.GetRequiredService<ScanViewModel>().Roots.FirstOrDefault();
        root?.UnmarkContentsCommand.Execute(null);
    }

    private static void OpenDiff(IServiceProvider services)
    {
        var sync = services.GetRequiredService<SyncViewModel>();
        var tree = sync.Rows.FlatView;
        SyncNodeViewModel? row;

        sync.Rows.FlatView = true;

        try
        {
            var modified = sync.Rows.Rows.Where(static row => row.IsFile && row.Status == ComparisonStatus.Modified).ToList();
            row = modified.Find(static row => row.Name.EndsWith(TextSample, StringComparison.OrdinalIgnoreCase)) ?? modified.FirstOrDefault();
        }
        finally
        {
            sync.Rows.FlatView = tree;
        }

        if (row is null)
        {
            throw new InvalidOperationException("В сравнении фикстуры нет изменённого файла – нечего показать в diff.");
        }

        Run(row.CompareContentCommand, "Сравнение содержимого недоступно для выбранной строки.");
    }

    private static void OpenDelete(IServiceProvider services)
    {
        var scan = services.GetRequiredService<ScanViewModel>();
        var root = Root(scan);

        root.MarkContentsDeletedCommand.Execute(null);
        Run(scan.DeleteMarkedCommand, "«Удалить помеченное» недоступна – пометки не встали.");
    }

    private static void OpenArchive(IServiceProvider services)
    {
        var root = Root(services.GetRequiredService<ScanViewModel>());

        var dir = root.Children.FirstOrDefault(static child => child.IsDirectory)
            ?? throw new InvalidOperationException("В дереве фикстуры нет подкаталога – нечего архивировать.");

        Run(dir.ArchiveToZipCommand, "Архивация недоступна для выбранного узла.");
    }

    private static ScanNodeViewModel Root(ScanViewModel scan)
    {
        var root = scan.Roots.FirstOrDefault()
            ?? throw new InvalidOperationException("Дерево сканирования пусто – фикстура не отсканирована.");

        root.EnsureLoaded();

        return root;
    }

    private static BatchCreateProfilesDialogViewModel CreateBatch(IServiceProvider services, GalleryFixture fixture)
    {
        return new(services.GetRequiredService<ISettingsStore>())
        {
            SourceParent = fixture.Left,
            DestParent = fixture.Right,
        };
    }

    private static void Run(System.Windows.Input.ICommand command, string unavailable)
    {
        if (!command.CanExecute(null))
        {
            throw new InvalidOperationException(unavailable);
        }

        command.Execute(null);
    }
}
