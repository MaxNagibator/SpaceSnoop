namespace SpaceSnoop;

public sealed class SyncHelpForm : Form
{
    private static readonly Color LeftOnlyColor = Color.Green;
    private static readonly Color RightOnlyColor = Color.DodgerBlue;
    private static readonly Color ModifiedColor = Color.DarkOrange;
    private static readonly Color ConflictColor = Color.Red;
    private static readonly Color IdenticalColor = Color.Gray;

    private static readonly (Color Color, string Text)[] ColorLegend =
    [
        (LeftOnlyColor, "● Зелёный – файл/папка только слева"),
        (RightOnlyColor, "● Синий – файл/папка только справа"),
        (ModifiedColor, "● Оранжевый – файл изменён (разный размер или дата)"),
        (ConflictColor, "● Красный – конфликт (двусторонний режим)"),
        (IdenticalColor, "● Серый – файлы идентичны"),
    ];

    private static readonly Section[] Sections =
    [
        new TextSection("СРАВНЕНИЕ", """
                                     Укажите левую и правую директории и нажмите «Сравнить».
                                     Файлы сравниваются по размеру и дате модификации.
                                     Кнопка «Проверить хешем» дополнительно вычисляет SHA-256
                                     для изменённых файлов – совпавшие будут помечены как идентичные.
                                     """),
        new ColorSection("ЦВЕТОВАЯ КОДИРОВКА", ColorLegend),
        new TextSection("ДЕЙСТВИЯ (центральная колонка)", """
                                                          → Копировать слева направо
                                                          ← Копировать справа налево
                                                          ⊘ Пропустить
                                                          ⊗ Удалить
                                                          ⚡ Не выбрано действие (требуется разрешение)
                                                          """),
        new TextSection("УПРАВЛЕНИЕ", """
                                      • Клик по иконке действия – циклическое переключение
                                      • Правый клик по файлу – контекстное меню для файла
                                      • Правый клик по папке – массовые действия для всей папки
                                        (рекурсивно применяются ко всем её файлам)
                                      • Клик по папке – свернуть/развернуть
                                      """),
        new TextSection("МАССОВОЕ РАЗРЕШЕНИЕ", """
                                               Кнопка «Разрешить конфликты ▾» назначает действие сразу всем
                                               нерешённым элементам (со значком ⚡). Активна, когда есть
                                               неподтверждённые элементы; синхронизация заблокирована, пока
                                               они не разрешены.
                                               """),
        new TextSection("РЕЖИМЫ СИНХРОНИЗАЦИИ", """
                                                • Левая → Правая: скопировать недостающее/изменённое вправо
                                                • Правая → Левая: скопировать недостающее/изменённое влево
                                                • Двусторонняя: новейший файл побеждает; при равных датах
                                                  и односторонних элементах действие требуется указать вручную
                                                """),
        new TextSection("ФИЛЬТРЫ", """
                                   Маски через запятую: *.tmp, .git, *.bak
                                   Поддерживаются подстановочные знаки (* и ?).
                                   """),
    ];

    private abstract record Section(string Heading);
    private sealed record TextSection(string Heading, string Body) : Section(Heading);
    private sealed record ColorSection(string Heading, (Color Color, string Text)[] Lines) : Section(Heading);

    private readonly RichTextBox _textBox;

    public SyncHelpForm()
    {
        Text = "Справка";
        Size = new(640, 560);
        MinimumSize = new(480, 360);
        StartPosition = FormStartPosition.CenterParent;
        ShowIcon = false;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = true;

        _textBox = new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            BackColor = SystemColors.Window,
            Font = new("Segoe UI", 9.75f),
            Margin = new(12),
        };

        var closeButton = new Button
        {
            Text = "Закрыть",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = new(6),
            UseVisualStyleBackColor = true,
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new(6),
        };
        buttonPanel.Controls.Add(closeButton);

        var textPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new(12),
        };
        textPanel.Controls.Add(_textBox);

        Controls.Add(textPanel);
        Controls.Add(buttonPanel);

        AcceptButton = closeButton;
        CancelButton = closeButton;

        FillContent();
    }

    private void FillContent()
    {
        var regularFont = _textBox.Font;
        var headingFont = new Font(regularFont.FontFamily, regularFont.Size + 1.5f, FontStyle.Bold);

        AppendText("Синхронизация директорий", headingFont, Color.FromArgb(40, 80, 160));
        AppendText(Environment.NewLine + Environment.NewLine, regularFont, SystemColors.ControlText);

        for (var i = 0; i < Sections.Length; i++)
        {
            var section = Sections[i];

            AppendText(section.Heading, headingFont, Color.FromArgb(70, 70, 70));
            AppendText(Environment.NewLine, regularFont, SystemColors.ControlText);

            switch (section)
            {
                case TextSection text:
                    AppendText(text.Body, regularFont, SystemColors.ControlText);
                    break;

                case ColorSection color:
                    for (var j = 0; j < color.Lines.Length; j++)
                    {
                        var (lineColor, lineText) = color.Lines[j];
                        AppendText(lineText, regularFont, lineColor);

                        if (j < color.Lines.Length - 1)
                        {
                            AppendText(Environment.NewLine, regularFont, SystemColors.ControlText);
                        }
                    }

                    break;
            }

            if (i < Sections.Length - 1)
            {
                AppendText(Environment.NewLine + Environment.NewLine, regularFont, SystemColors.ControlText);
            }
        }

        _textBox.SelectionStart = 0;
        _textBox.SelectionLength = 0;
    }

    private void AppendText(string text, Font font, Color color)
    {
        _textBox.SelectionStart = _textBox.TextLength;
        _textBox.SelectionLength = 0;
        _textBox.SelectionFont = font;
        _textBox.SelectionColor = color;
        _textBox.AppendText(text);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _textBox.Font.Dispose();
        }

        base.Dispose(disposing);
    }
}
