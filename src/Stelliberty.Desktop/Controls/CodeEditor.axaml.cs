using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace Stelliberty.Desktop.Controls;

// AvaloniaEdit 封装；外观对齐 dialog-input.multiline
public sealed partial class CodeEditor : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<CodeEditor, string?>(
            nameof(Text), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsReadOnly));

    public static readonly StyledProperty<bool> WordWrapProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(WordWrap), true);

    public static readonly StyledProperty<bool> ShowLineNumbersProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(ShowLineNumbers), true);

    public static readonly StyledProperty<string> SearchTextProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(SearchText), string.Empty);

    public static readonly StyledProperty<string> SearchStatusProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(SearchStatus), "0 / 0");

    public static readonly StyledProperty<string?> SyntaxLanguageProperty =
        AvaloniaProperty.Register<CodeEditor, string?>(nameof(SyntaxLanguage));

    public static readonly StyledProperty<IBrush?> SyntaxKeyBrushProperty =
        AvaloniaProperty.Register<CodeEditor, IBrush?>(nameof(SyntaxKeyBrush));

    public static readonly StyledProperty<IBrush?> SyntaxMarkerBrushProperty =
        AvaloniaProperty.Register<CodeEditor, IBrush?>(nameof(SyntaxMarkerBrush));

    public static readonly StyledProperty<IBrush?> SyntaxFunctionBrushProperty =
        AvaloniaProperty.Register<CodeEditor, IBrush?>(nameof(SyntaxFunctionBrush));

    public static readonly StyledProperty<IBrush?> SyntaxBooleanBrushProperty =
        AvaloniaProperty.Register<CodeEditor, IBrush?>(nameof(SyntaxBooleanBrush));

    public static readonly StyledProperty<IBrush?> SyntaxNumberBrushProperty =
        AvaloniaProperty.Register<CodeEditor, IBrush?>(nameof(SyntaxNumberBrush));

    public static readonly StyledProperty<IBrush?> SyntaxStringBrushProperty =
        AvaloniaProperty.Register<CodeEditor, IBrush?>(nameof(SyntaxStringBrush));

    public static readonly StyledProperty<IBrush?> SyntaxCommentBrushProperty =
        AvaloniaProperty.Register<CodeEditor, IBrush?>(nameof(SyntaxCommentBrush));

    private const string PlainTextSyntaxLanguage = "plaintext";
    private const string YamlSyntaxLanguage = "yaml";
    private const string JavaScriptSyntaxLanguage = "javascript";

    private TextEditor _editor = null!;
    private TextBox _searchBox = null!;
    private readonly SearchMatchHighlighter _searchHighlighter = new();
    private readonly List<TextSegment> _matches = [];
    private CodeEditorSyntaxColorizer? _syntaxTransformer;
    private int _currentMatchIndex = -1;
    private string _appliedSyntaxLanguage = PlainTextSyntaxLanguage;
    // 抑制 Text 属性与编辑器互相回写造成的递归。
    private bool _syncing;

    public CodeEditor()
    {
        PreviousCommand = new DelegateCommand(FindPrevious);
        NextCommand = new DelegateCommand(FindNext);
        InitializeComponent();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool WordWrap
    {
        get => GetValue(WordWrapProperty);
        set => SetValue(WordWrapProperty, value);
    }

    public bool ShowLineNumbers
    {
        get => GetValue(ShowLineNumbersProperty);
        set => SetValue(ShowLineNumbersProperty, value);
    }

    public string SearchText
    {
        get => GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value ?? string.Empty);
    }

    public string SearchStatus
    {
        get => GetValue(SearchStatusProperty);
        private set => SetValue(SearchStatusProperty, value);
    }

    public string? SyntaxLanguage
    {
        get => GetValue(SyntaxLanguageProperty);
        set => SetValue(SyntaxLanguageProperty, value);
    }

    public IBrush? SyntaxKeyBrush
    {
        get => GetValue(SyntaxKeyBrushProperty);
        set => SetValue(SyntaxKeyBrushProperty, value);
    }

    public IBrush? SyntaxMarkerBrush
    {
        get => GetValue(SyntaxMarkerBrushProperty);
        set => SetValue(SyntaxMarkerBrushProperty, value);
    }

    public IBrush? SyntaxFunctionBrush
    {
        get => GetValue(SyntaxFunctionBrushProperty);
        set => SetValue(SyntaxFunctionBrushProperty, value);
    }

    public IBrush? SyntaxBooleanBrush
    {
        get => GetValue(SyntaxBooleanBrushProperty);
        set => SetValue(SyntaxBooleanBrushProperty, value);
    }

    public IBrush? SyntaxNumberBrush
    {
        get => GetValue(SyntaxNumberBrushProperty);
        set => SetValue(SyntaxNumberBrushProperty, value);
    }

    public IBrush? SyntaxStringBrush
    {
        get => GetValue(SyntaxStringBrushProperty);
        set => SetValue(SyntaxStringBrushProperty, value);
    }

    public IBrush? SyntaxCommentBrush
    {
        get => GetValue(SyntaxCommentBrushProperty);
        set => SetValue(SyntaxCommentBrushProperty, value);
    }

    public ICommand PreviousCommand { get; }

    public ICommand NextCommand { get; }

    public string SearchAutomationId => $"{GetAutomationId()}.SearchBox";

    public string PreviousAutomationId => $"{GetAutomationId()}.PreviousButton";

    public string NextAutomationId => $"{GetAutomationId()}.NextButton";

    public string SearchStatusAutomationId => $"{GetAutomationId()}.SearchStatusText";

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _editor = this.FindControl<TextEditor>("Editor")!;
        _searchBox = this.FindControl<TextBox>("SearchBox")!;
        Focusable = true;
        _editor.ShowLineNumbers = ShowLineNumbers;
        ApplyLineNumberSpacing();
        _editor.TemplateApplied += (_, _) => ConfigureEditorScrollBars();
        _editor.WordWrap = WordWrap;
        _editor.IsReadOnly = IsReadOnly;
        ApplySyntaxLanguage();
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Options.AllowScrollBelowDocument = false;
        _editor.TextArea.TextView.BackgroundRenderers.Add(_searchHighlighter);
        _editor.TextChanged += OnEditorTextChanged;
        if (Text is { } initial)
        {
            _syncing = true;
            _editor.Text = initial;
            _syncing = false;
        }

        RefreshSearchMatches(selectFirst: false);
        Dispatcher.UIThread.Post(ConfigureEditorScrollBars, DispatcherPriority.Render);
    }

    // 只调左侧 margin 子项，避免正文 Padding 牵动分隔线。
    private void ApplyLineNumberSpacing()
    {
        foreach (var margin in _editor.TextArea.LeftMargins)
        {
            if (margin is Line line && DottedLineMargin.IsDottedLineMargin(line))
            {
                line.Margin = new Thickness(5, 0, 5, 0);
                line.StrokeDashArray = null;
                break;
            }
        }
    }

    // TextEditor 内部 ScrollViewer 不继承外层附加属性，需在模板生成后写入。
    private void ConfigureEditorScrollBars()
    {
        foreach (var scrollViewer in _editor.GetVisualDescendants().OfType<ScrollViewer>())
        {
            scrollViewer.AllowAutoHide = true;
        }

        foreach (var scrollBar in _editor.GetVisualDescendants().OfType<ScrollBar>())
        {
            scrollBar.AllowAutoHide = true;
        }
    }

    private void ApplySyntaxLanguage()
    {
        var language = NormalizeSyntaxLanguage(SyntaxLanguage);
        if (_appliedSyntaxLanguage == language)
        {
            return;
        }

        if (_syntaxTransformer is not null)
        {
            _editor.TextArea.TextView.LineTransformers.Remove(_syntaxTransformer);
            _syntaxTransformer = null;
        }

        _editor.SyntaxHighlighting = null;
        if (language == YamlSyntaxLanguage)
        {
            _syntaxTransformer = new YamlSyntaxColorizer();
        }
        else if (language == JavaScriptSyntaxLanguage)
        {
            _syntaxTransformer = new JavaScriptSyntaxColorizer();
        }

        if (_syntaxTransformer is not null)
        {
            _editor.TextArea.TextView.LineTransformers.Insert(0, _syntaxTransformer);
            RefreshSyntaxPalette();
        }

        _appliedSyntaxLanguage = language;
        _editor.TextArea.TextView.Redraw();
    }

    private void RefreshSyntaxPalette()
    {
        if (_syntaxTransformer is null)
        {
            return;
        }

        _syntaxTransformer.UpdatePalette(ResolveSyntaxPalette());
        _editor.TextArea.TextView.Redraw();
    }

    private SyntaxPalette ResolveSyntaxPalette()
    {
        return new SyntaxPalette
        {
            Key = SyntaxKeyBrush!,
            Marker = SyntaxMarkerBrush!,
            Function = SyntaxFunctionBrush!,
            Boolean = SyntaxBooleanBrush!,
            Number = SyntaxNumberBrush!,
            String = SyntaxStringBrush!,
            Comment = SyntaxCommentBrush!,
        };
    }

    private static string NormalizeSyntaxLanguage(string? syntaxLanguage)
    {
        return syntaxLanguage?.Trim().ToLowerInvariant() switch
        {
            "yaml" or "yml" => YamlSyntaxLanguage,
            "javascript" or "js" => JavaScriptSyntaxLanguage,
            _ => PlainTextSyntaxLanguage
        };
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_editor.IsReadOnly || e.Source is not Visual source)
        {
            return;
        }

        if (source.FindAncestorOfType<TextBox>(includeSelf: true) is not null
            || source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        _editor.TextArea.Focus();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_editor is null)
        {
            return;
        }

        if (change.Property == TextProperty && !_syncing)
        {
            var incoming = change.GetNewValue<string?>() ?? string.Empty;
            if (_editor.Text != incoming)
            {
                _syncing = true;
                _editor.Text = incoming;
                _syncing = false;
                RefreshSearchMatches(selectFirst: false);
            }
        }
        else if (change.Property == IsReadOnlyProperty)
        {
            _editor.IsReadOnly = change.GetNewValue<bool>();
        }
        else if (change.Property == WordWrapProperty)
        {
            _editor.WordWrap = change.GetNewValue<bool>();
        }
        else if (change.Property == SyntaxLanguageProperty)
        {
            ApplySyntaxLanguage();
        }
        else if (change.Property == ShowLineNumbersProperty)
        {
            var showLineNumbers = change.GetNewValue<bool>();
            _editor.ShowLineNumbers = showLineNumbers;
            if (showLineNumbers)
            {
                ApplyLineNumberSpacing();
            }
        }
        else if (change.Property == SearchTextProperty)
        {
            ApplySearchText(selectFirst: true);
        }
        else if (change.Property == SyntaxKeyBrushProperty
            || change.Property == SyntaxMarkerBrushProperty
            || change.Property == SyntaxFunctionBrushProperty
            || change.Property == SyntaxBooleanBrushProperty
            || change.Property == SyntaxNumberBrushProperty
            || change.Property == SyntaxStringBrushProperty
            || change.Property == SyntaxCommentBrushProperty)
        {
            RefreshSyntaxPalette();
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        Text = _editor.Text;
        _syncing = false;
        RefreshSearchMatches(selectFirst: false);
    }

    private void ApplySearchText(bool selectFirst)
    {
        RefreshSearchMatches(selectFirst);
    }

    private void FindPrevious()
    {
        if (string.IsNullOrEmpty(SearchText))
        {
            _searchBox.Focus();
            return;
        }

        RefreshSearchMatches(selectFirst: false);
        if (_matches.Count == 0)
        {
            _searchBox.Focus();
            return;
        }

        var previousIndex = _currentMatchIndex <= 0 ? _matches.Count - 1 : _currentMatchIndex - 1;
        SelectMatch(previousIndex, focusEditor: true);
    }

    private void FindNext()
    {
        if (string.IsNullOrEmpty(SearchText))
        {
            _searchBox.Focus();
            return;
        }

        RefreshSearchMatches(selectFirst: false);
        if (_matches.Count == 0)
        {
            _searchBox.Focus();
            return;
        }

        var nextIndex = _currentMatchIndex < 0 || _currentMatchIndex >= _matches.Count - 1
            ? 0
            : _currentMatchIndex + 1;
        SelectMatch(nextIndex, focusEditor: true);
    }

    private void RefreshSearchMatches(bool selectFirst)
    {
        RebuildMatches();
        if (_matches.Count == 0)
        {
            _currentMatchIndex = -1;
            RefreshSearchHighlight();
            SearchStatus = "0 / 0";
            return;
        }

        if (selectFirst)
        {
            SelectMatch(0, focusEditor: false);
            return;
        }

        _currentMatchIndex = FindCurrentMatchIndex();
        RefreshSearchHighlight();
        RefreshSearchStatus();
    }

    private void RebuildMatches()
    {
        _matches.Clear();
        var keyword = SearchText;
        if (string.IsNullOrEmpty(keyword))
        {
            return;
        }

        var text = _editor.Text;
        var index = 0;
        while ((index = text.IndexOf(keyword, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            _matches.Add(new TextSegment { StartOffset = index, Length = keyword.Length });
            index += keyword.Length;
        }
    }

    private int FindCurrentMatchIndex()
    {
        if (_matches.Count == 0)
        {
            return -1;
        }

        var selectionStart = _editor.SelectionStart;
        for (var index = 0; index < _matches.Count; index++)
        {
            var match = _matches[index];
            if (selectionStart >= match.StartOffset && selectionStart <= match.EndOffset)
            {
                return index;
            }
        }

        return Math.Min(Math.Max(_currentMatchIndex, 0), _matches.Count - 1);
    }

    private void SelectMatch(int index, bool focusEditor)
    {
        if (_matches.Count == 0)
        {
            return;
        }

        _currentMatchIndex = (index + _matches.Count) % _matches.Count;
        var match = _matches[_currentMatchIndex];
        _editor.Select(match.StartOffset, match.Length);
        _editor.ScrollToLine(_editor.Document.GetLineByOffset(match.StartOffset).LineNumber);
        RefreshSearchHighlight();
        RefreshSearchStatus();
        if (focusEditor)
        {
            _editor.TextArea.Focus();
        }
    }

    private void RefreshSearchHighlight()
    {
        _searchHighlighter.SetMatches(_matches);
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private void RefreshSearchStatus()
    {
        SearchStatus = _currentMatchIndex < 0
            ? $"0 / {_matches.Count}"
            : $"{_currentMatchIndex + 1} / {_matches.Count}";
    }

    private string GetAutomationId()
    {
        return AutomationProperties.GetAutomationId(this) ?? "CodeEditor";
    }

    private sealed class SearchMatchHighlighter : IBackgroundRenderer
    {
        private readonly List<TextSegment> _matches = [];
        private readonly IBrush _matchBrush = new SolidColorBrush(Color.FromArgb(80, 255, 214, 0));

        public KnownLayer Layer => KnownLayer.Background;

        public void SetMatches(IEnumerable<TextSegment> matches)
        {
            _matches.Clear();
            foreach (var match in matches)
            {
                _matches.Add(new TextSegment { StartOffset = match.StartOffset, Length = match.Length });
            }
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_matches.Count == 0)
            {
                return;
            }

            textView.EnsureVisualLines();
            var builder = new BackgroundGeometryBuilder
            {
                AlignToWholePixels = true,
                CornerRadius = 2
            };

            foreach (var match in _matches)
            {
                builder.AddSegment(textView, match);
            }

            var geometry = builder.CreateGeometry();
            if (geometry is not null)
            {
                drawingContext.DrawGeometry(_matchBrush, null, geometry);
            }
        }
    }

    private sealed class DelegateCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }
}
