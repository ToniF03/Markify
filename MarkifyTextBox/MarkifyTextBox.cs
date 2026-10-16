using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Markify.Textify
{
    /// <summary>A small, self-contained WPF text editor. It does not derive from TextBox.</summary>
    public sealed partial class MarkifyTextBox : Grid
    {
        internal sealed class RenderedTextSegment
        {
            public string Text;
            public int[] SourceColumns;
            public int[] SourceBoundaries;
            public List<RenderedMathFractionLayout> MathFractions;
        }

        internal struct MarkdownInlineCodeSpan
        {
            public int ContentStart;
            public int ContentLength;
            public int DelimiterLength;

            public MarkdownInlineCodeSpan(int contentStart, int contentLength, int delimiterLength)
            {
                ContentStart = contentStart;
                ContentLength = contentLength;
                DelimiterLength = delimiterLength;
            }
        }

        internal struct MarkdownCodeFenceBlock
        {
            public int OpeningLine;
            public int ClosingLine;
            public int DelimiterLength;
            public char DelimiterCharacter;
            public string Language;
            public int OpeningQuoteDepth;

            public MarkdownCodeFenceBlock(int openingLine, int closingLine, int delimiterLength,
                char delimiterCharacter, string language, int openingQuoteDepth)
            {
                OpeningLine = openingLine;
                ClosingLine = closingLine;
                DelimiterLength = delimiterLength;
                DelimiterCharacter = delimiterCharacter;
                Language = language;
                OpeningQuoteDepth = openingQuoteDepth;
            }
        }

        internal struct MarkdownMathBlock
        {
            public int OpeningLine;
            public int ClosingLine;
            public int OpeningColumn;
            public int ClosingColumn;

            public MarkdownMathBlock(int openingLine, int closingLine, int openingColumn, int closingColumn)
            {
                OpeningLine = openingLine;
                ClosingLine = closingLine;
                OpeningColumn = openingColumn;
                ClosingColumn = closingColumn;
            }
        }

        internal struct MarkdownListMarker
        {
            public int MarkerStart;
            public int MarkerEnd;
            public int ContentStart;
            public bool IsOrdered;

            public MarkdownListMarker(int markerStart, int markerEnd, int contentStart, bool isOrdered)
            {
                MarkerStart = markerStart;
                MarkerEnd = markerEnd;
                ContentStart = contentStart;
                IsOrdered = isOrdered;
            }
        }

        internal struct MarkdownScriptSpan
        {
            public int ContentStart;
            public int ContentLength;
            public bool IsSuperscript;

            public MarkdownScriptSpan(int contentStart, int contentLength, bool isSuperscript)
            {
                ContentStart = contentStart;
                ContentLength = contentLength;
                IsSuperscript = isSuperscript;
            }
        }

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(string.Empty, OnTextChanged));
        public static readonly DependencyProperty ShowBorderProperty = DependencyProperty.Register(
            nameof(ShowBorder), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(true, OnBorderVisibilityChanged));
        public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
            nameof(FontFamily), typeof(FontFamily), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(new FontFamily("Consolas"), OnVisualOptionChanged));
        public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
            nameof(FontSize), typeof(double), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(14d, OnVisualOptionChanged));
        public static readonly DependencyProperty EnableMarkdownHeadingsProperty = DependencyProperty.Register(
            nameof(EnableMarkdownHeadings), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(true, OnVisualOptionChanged));
        public static readonly DependencyProperty EnableMarkdownBoldProperty = DependencyProperty.Register(
            nameof(EnableMarkdownBold), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(true, OnVisualOptionChanged));
        public static readonly DependencyProperty EnableMarkdownItalicProperty = DependencyProperty.Register(
            nameof(EnableMarkdownItalic), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(true, OnVisualOptionChanged));
        public static readonly DependencyProperty EnableMarkdownSuperSubscriptProperty = DependencyProperty.Register(
            nameof(EnableMarkdownSuperSubscript), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(true, OnVisualOptionChanged));
        public static readonly DependencyProperty EnableMarkdownInlineCodeProperty = DependencyProperty.Register(
            nameof(EnableMarkdownInlineCode), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(true, OnVisualOptionChanged));
        public static readonly DependencyProperty EnableMarkdownCodeBlocksProperty = DependencyProperty.Register(
            nameof(EnableMarkdownCodeBlocks), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(true, OnVisualOptionChanged));
        public static readonly DependencyProperty EnableMarkdownListsProperty = DependencyProperty.Register(
            nameof(EnableMarkdownLists), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(true, OnVisualOptionChanged));
        public static readonly DependencyProperty EnableMarkdownHorizontalRulesProperty = DependencyProperty.Register(
            nameof(EnableMarkdownHorizontalRules), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(true, OnVisualOptionChanged));
        public static readonly DependencyProperty EnableMarkdownMathProperty = DependencyProperty.Register(
            nameof(EnableMarkdownMath), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(true, OnVisualOptionChanged));
        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
            nameof(Foreground), typeof(Brush), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(Brushes.DarkSlateGray, OnVisualOptionChanged));
        public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
            nameof(IsReadOnly), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(false));
        public static readonly DependencyProperty IsWordWrapEnabledProperty = DependencyProperty.Register(
            nameof(IsWordWrapEnabled), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(false, OnWordWrapChanged));
        public static readonly DependencyProperty IsHorizontalScrollEnabledProperty = DependencyProperty.Register(
            nameof(IsHorizontalScrollEnabled), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(true, OnHorizontalScrollChanged));
        public static readonly DependencyProperty HighlightCurrentLineProperty = DependencyProperty.Register(
            nameof(HighlightCurrentLine), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(false, OnVisualOptionChanged));
        public static readonly DependencyProperty CurrentLineBackgroundProperty = DependencyProperty.Register(
            nameof(CurrentLineBackground), typeof(Brush), typeof(MarkifyTextBox),
            new FrameworkPropertyMetadata(CreateFrozenBrush(Color.FromRgb(246, 248, 251)), OnVisualOptionChanged));
        public static readonly DependencyProperty AutoCloseBracketsProperty = DependencyProperty.Register(
            nameof(AutoCloseBrackets), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(false));
        public static readonly DependencyProperty EnableSyntaxHighlightingProperty = DependencyProperty.Register(
            nameof(EnableSyntaxHighlighting), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(false, OnFeatureOptionChanged));
        public static readonly DependencyProperty EnableBracketHighlightingProperty = DependencyProperty.Register(
            nameof(EnableBracketHighlighting), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(false, OnFeatureOptionChanged));
        public static readonly DependencyProperty EnableMultipleCaretsProperty = DependencyProperty.Register(
            nameof(EnableMultipleCarets), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(false, OnFeatureOptionChanged));
        public static readonly DependencyProperty EnableRectangularSelectionProperty = DependencyProperty.Register(
            nameof(EnableRectangularSelection), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(false, OnFeatureOptionChanged));
        public static readonly DependencyProperty EnableEditorContextMenuProperty = DependencyProperty.Register(
            nameof(EnableEditorContextMenu), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(false, OnFeatureOptionChanged));
        public static readonly DependencyProperty EnableLineOperationsProperty = DependencyProperty.Register(
            nameof(EnableLineOperations), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(false, OnFeatureOptionChanged));
        public static readonly DependencyProperty EnableWordWrapShortcutProperty = DependencyProperty.Register(
            nameof(EnableWordWrapShortcut), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(false));
        public static readonly DependencyProperty EnableTabIndentationProperty = DependencyProperty.Register(
            nameof(EnableTabIndentation), typeof(bool), typeof(MarkifyTextBox), new FrameworkPropertyMetadata(false));

        private readonly ScrollViewer _scroll;
        private readonly EditorSurface _surface;
        private readonly Border _border;
        private HwndSource _windowSource;
        private readonly List<EditSnapshot> _undo = new List<EditSnapshot>();
        private readonly List<EditSnapshot> _redo = new List<EditSnapshot>();
        private readonly Dictionary<string, double> _textWidthCache = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly List<int> _lineStarts = new List<int> { 0 };
        private readonly List<int> _secondaryCarets = new List<int>();
        private readonly List<SelectionSpan> _secondarySelections = new List<SelectionSpan>();
        private string[] _lineCache = new[] { string.Empty };
        private MarkdownListMarker[] _markdownListMarkersByLine = new MarkdownListMarker[1];
        private bool[] _markdownListMarkersParsed = new bool[1];
        private List<MarkdownCodeFenceBlock> _markdownCodeFenceBlocks = new List<MarkdownCodeFenceBlock>();
        private int[] _markdownCodeFenceByLine = new[] { -1 };
        private List<MarkdownMathBlock> _markdownMathBlocks = new List<MarkdownMathBlock>();
        private int[] _markdownMathBlockByLine = new[] { -1 };
        private bool[] _markdownMathContainsFractionByLine = new bool[0];
        private bool[] _markdownMathFractionCheckedByLine = new bool[0];
        private bool[] _markdownMathEnvironmentControlLineCollapsed = new bool[0];
        private bool[] _markdownMathPreviewVisibleByLine = new bool[0];
        private bool[] _markdownIndentedCodeLines = new[] { false };
        private bool[] _markdownSetextMarkerVisibilityByLine = new[] { false };
        private readonly HashSet<string> _markdownFootnoteDefinitions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, MarkdownTableLayout> _markdownTableLayouts =
            new Dictionary<int, MarkdownTableLayout>();
        private int[] _visibleLines = new[] { 0 };
        private int[] _documentToVisibleRow = new[] { 0 };
        private int[] _visibleRowStartColumns = new[] { 0 };
        private int[] _visibleRowLengths = new[] { 0 };
        private double[] _visibleRowTops = new[] { 0d };
        private double _totalVisualHeight;
        private double _wrapTextWidth = double.PositiveInfinity;
        private bool _isRectangularSelection;
        private int _rectangleAnchorLine;
        private int _rectangleAnchorColumn;
        private int _rectangleEndLine;
        private int _rectangleEndColumn;
        private int _lastPublishedCaret = -1;
        private int _lastPublishedSelectionStart = -1;
        private int _lastPublishedSelectionLength = -1;
        private int _lastPublishedSecondaryCaretCount = -1;
        private string _lastPublishedSecondaryState;
        private bool _lastPublishedRectangle;
        private string _matchingBracketCacheText;
        private int _matchingBracketCacheCaret = -1;
        private int _matchingBracketCacheResult = -1;
        private double _metricDpi = -1;
        private readonly DispatcherTimer _caretTimer;
        private int _caret;
        private int _hoveredMarkdownCodeFenceCopyButtonLine = -1;
        private int _pressedMarkdownCodeFenceCopyButtonLine = -1;
        private int _selectionAnchor;
        private int _taskCheckboxDisplaySuppressedAt = -1;
        private int _preferredColumn = -1;
        private bool _caretVisible = true;
        private const double LeftInset = 10;
        private const double TopInset = 8;
        private static readonly FontFamily MarkdownInlineCodeFontFamily = new FontFamily("Consolas");
        private static readonly Brush MarkdownInlineCodeBackground = CreateFrozenBrush(Color.FromRgb(238, 240, 243));
        private static readonly Brush MarkdownInlineCodeForeground = CreateFrozenBrush(Color.FromRgb(91, 49, 47));
        private static readonly Brush MarkdownListMarkerForeground = CreateFrozenBrush(Color.FromRgb(76, 98, 122));
        private static readonly Brush MarkdownLinkForeground = CreateFrozenBrush(Color.FromRgb(0, 102, 204));
        private static readonly Brush MarkdownTaskForeground = CreateFrozenBrush(Color.FromRgb(54, 115, 78));
        private static readonly Brush MarkdownTaskUncheckedForeground = CreateFrozenBrush(Color.FromRgb(92, 105, 119));
        private static readonly SyntaxDefinition MarkdownSyntaxDefinition =
            SyntaxDefinition.LoadFromResource("Markify.Textify.Syntaxes.markdown.syntax.xml");

        private static Brush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public string Text { get { return (string)GetValue(TextProperty); } set { SetValue(TextProperty, value ?? string.Empty); } }
        public bool ShowBorder { get { return (bool)GetValue(ShowBorderProperty); } set { SetValue(ShowBorderProperty, value); } }
        public FontFamily FontFamily { get { return (FontFamily)GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }
        public double FontSize { get { return (double)GetValue(FontSizeProperty); } set { SetValue(FontSizeProperty, value); } }
        public bool EnableMarkdownHeadings { get { return (bool)GetValue(EnableMarkdownHeadingsProperty); } set { SetValue(EnableMarkdownHeadingsProperty, value); } }
        public bool EnableMarkdownBold { get { return (bool)GetValue(EnableMarkdownBoldProperty); } set { SetValue(EnableMarkdownBoldProperty, value); } }
        public bool EnableMarkdownItalic { get { return (bool)GetValue(EnableMarkdownItalicProperty); } set { SetValue(EnableMarkdownItalicProperty, value); } }
        public bool EnableMarkdownSuperSubscript { get { return (bool)GetValue(EnableMarkdownSuperSubscriptProperty); } set { SetValue(EnableMarkdownSuperSubscriptProperty, value); } }
        public bool EnableMarkdownInlineCode { get { return (bool)GetValue(EnableMarkdownInlineCodeProperty); } set { SetValue(EnableMarkdownInlineCodeProperty, value); } }
        public bool EnableMarkdownCodeBlocks { get { return (bool)GetValue(EnableMarkdownCodeBlocksProperty); } set { SetValue(EnableMarkdownCodeBlocksProperty, value); } }
        public bool EnableMarkdownLists { get { return (bool)GetValue(EnableMarkdownListsProperty); } set { SetValue(EnableMarkdownListsProperty, value); } }
        public bool EnableMarkdownHorizontalRules { get { return (bool)GetValue(EnableMarkdownHorizontalRulesProperty); } set { SetValue(EnableMarkdownHorizontalRulesProperty, value); } }
        public bool EnableMarkdownMath { get { return (bool)GetValue(EnableMarkdownMathProperty); } set { SetValue(EnableMarkdownMathProperty, value); } }
        public Brush Foreground { get { return (Brush)GetValue(ForegroundProperty); } set { SetValue(ForegroundProperty, value); } }
        public bool IsReadOnly { get { return (bool)GetValue(IsReadOnlyProperty); } set { SetValue(IsReadOnlyProperty, value); } }
        public bool IsWordWrapEnabled { get { return (bool)GetValue(IsWordWrapEnabledProperty); } set { SetValue(IsWordWrapEnabledProperty, value); } }
        public bool IsHorizontalScrollEnabled { get { return (bool)GetValue(IsHorizontalScrollEnabledProperty); } set { SetValue(IsHorizontalScrollEnabledProperty, value); } }
        public bool HighlightCurrentLine { get { return (bool)GetValue(HighlightCurrentLineProperty); } set { SetValue(HighlightCurrentLineProperty, value); } }
        public Brush CurrentLineBackground { get { return (Brush)GetValue(CurrentLineBackgroundProperty); } set { SetValue(CurrentLineBackgroundProperty, value); } }
        public bool AutoCloseBrackets { get { return (bool)GetValue(AutoCloseBracketsProperty); } set { SetValue(AutoCloseBracketsProperty, value); } }
        public bool EnableSyntaxHighlighting { get { return (bool)GetValue(EnableSyntaxHighlightingProperty); } set { SetValue(EnableSyntaxHighlightingProperty, value); } }
        public bool EnableBracketHighlighting { get { return (bool)GetValue(EnableBracketHighlightingProperty); } set { SetValue(EnableBracketHighlightingProperty, value); } }
        public bool EnableMultipleCarets { get { return (bool)GetValue(EnableMultipleCaretsProperty); } set { SetValue(EnableMultipleCaretsProperty, value); } }
        public bool EnableRectangularSelection { get { return (bool)GetValue(EnableRectangularSelectionProperty); } set { SetValue(EnableRectangularSelectionProperty, value); } }
        public bool EnableEditorContextMenu { get { return (bool)GetValue(EnableEditorContextMenuProperty); } set { SetValue(EnableEditorContextMenuProperty, value); } }
        public bool EnableLineOperations { get { return (bool)GetValue(EnableLineOperationsProperty); } set { SetValue(EnableLineOperationsProperty, value); } }
        public bool EnableWordWrapShortcut { get { return (bool)GetValue(EnableWordWrapShortcutProperty); } set { SetValue(EnableWordWrapShortcutProperty, value); } }
        public bool EnableTabIndentation { get { return (bool)GetValue(EnableTabIndentationProperty); } set { SetValue(EnableTabIndentationProperty, value); } }
        public ObservableCollection<SyntaxHighlightRule> SyntaxHighlighting { get; private set; }

        public event EventHandler CaretPositionChanged;
        public event EventHandler SelectionChanged;

        public MarkifyTextBox()
        {
            SyntaxHighlighting = new ObservableCollection<SyntaxHighlightRule>();
            SyntaxHighlighting.CollectionChanged += OnSyntaxHighlightingChanged;
            ClipToBounds = true;
            Focusable = true;
            Cursor = Cursors.IBeam;
            Background = Brushes.White;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _border = new Border
            {
                BorderBrush = CreateFrozenBrush(Color.FromRgb(210, 215, 221)),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                SnapsToDevicePixels = true
            };
            Grid.SetRowSpan(_border, RowDefinitions.Count);
            _surface = new EditorSurface(this);
            _markdownMediaCanvas = new Canvas { IsHitTestVisible = true };
            var content = new Grid();
            content.Children.Add(_surface);
            content.Children.Add(_markdownMediaCanvas);
            _scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false,
                Background = Background, Focusable = false };
            _scroll.ScrollChanged += (s, e) => { _surface.InvalidateVisual(); UpdateMarkdownVideoPlayerLayout(); };
            _scroll.SizeChanged += OnEditorViewportSizeChanged;
            Grid.SetRow(_scroll, 1);
            Children.Add(_scroll);
            InitializeAdvancedUi();
            Children.Add(_border);
            _caretTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
            _caretTimer.Tick += (s, e) => { _caretVisible = !_caretVisible; _surface.InvalidateVisual(); };
            _caretTimer.Start();
            PreviewKeyDown += HandleKeyDown;
            TextInput += HandleTextInput;
            Loaded += AttachHorizontalWheelHook;
            Loaded += (s, e) => UpdateMarkdownVideoPlayerLayout();
            Unloaded += DetachHorizontalWheelHook;
            GotKeyboardFocus += (s, e) => { _caretVisible = true; _surface.InvalidateVisual(); };
            LostKeyboardFocus += (s, e) => { _surface.InvalidateVisual(); };
            _surface.MouseDown += HandleMouseDown;
            _surface.MouseMove += HandleMouseMove;
            _surface.MouseUp += HandleMouseUp;
            _surface.MouseLeave += HandleSurfaceMouseLeave;
        }

        internal int Caret { get { return _caret; } }
        public int CaretOffset { get { return _caret; } set { SetCaret(value, false); } }
        public int SelectionStart { get { return Math.Min(_selectionAnchor, _caret); } }
        public int SelectionLength { get { return Math.Abs(_selectionAnchor - _caret); } }
        internal double LineHeight { get { return Math.Ceiling(FontSize * 1.45); } }
        internal double LineHeightForVisibleRow(int row) { return LineHeightForDocumentLine(DocumentLineAtVisibleRow(row)); }
        internal double VisibleRowTop(int row) { return _visibleRowTops[Math.Max(0, Math.Min(_visibleRowTops.Length - 1, row))]; }
        internal double TotalVisualHeight { get { return _totalVisualHeight; } }
        internal double CharacterWidth { get { return Math.Ceiling(FontSize * 0.61); } }
        internal double TextOrigin { get { return LeftInset; } }
        internal double TextOriginForLine(int line)
        {
            return TextOrigin + MarkdownListIndentForLine(line) +
                MarkdownQuoteDepthForLine(line) * MeasureTextWidth("  ") +
                MarkdownDefinitionListIndentForLine(line);
        }
        internal double MarkdownCodeFenceBackgroundOriginForLine(int line)
        {
            MarkdownCodeFenceBlock block;
            if (!TryGetMarkdownCodeFenceBlock(line, out block)) return TextOriginForLine(line);
            int lineQuoteDepth = MarkdownQuoteDepthForLine(line);
            int backgroundQuoteDepth = block.OpeningQuoteDepth > 0
                ? Math.Max(block.OpeningQuoteDepth, lineQuoteDepth) : 0;
            return TextOriginForLine(line) -
                (lineQuoteDepth - backgroundQuoteDepth) * MeasureTextWidth("  ");
        }
        internal double MarkdownCodeFenceBackgroundRightEdge(int line)
        {
            double left = Math.Max(0, MarkdownCodeFenceBackgroundOriginForLine(line) - 5);
            double viewportWidth = _scroll == null || _scroll.ViewportWidth <= 0 ||
                double.IsInfinity(_scroll.ViewportWidth) ? ActualWidth : _scroll.ViewportWidth;
            return Math.Max(left, viewportWidth - 8);
        }
        internal bool TryGetMarkdownCodeFenceCopyButtonBounds(int line, double y, out Rect bounds)
        {
            bounds = Rect.Empty;
            MarkdownCodeFenceBlock block;
            if (!TryGetMarkdownCodeFenceBlock(line, out block) || line != block.OpeningLine) return false;
            double left = Math.Max(0, MarkdownCodeFenceBackgroundOriginForLine(line) - 5);
            double right = MarkdownCodeFenceBackgroundRightEdge(line);
            double availableWidth = right - left - 12;
            if (availableWidth < 18) return false;
            double size = Math.Min(24, availableWidth);
            bounds = new Rect(right - size - 6, y + 2, size, 20);
            return true;
        }
        internal bool MarkdownCodeFenceCopyButtonIsHighlighted(int line)
        {
            return line == _hoveredMarkdownCodeFenceCopyButtonLine ||
                line == _pressedMarkdownCodeFenceCopyButtonLine;
        }
        internal bool MarkdownCodeFenceCopyButtonIsPressed(int line)
        {
            return line == _pressedMarkdownCodeFenceCopyButtonLine;
        }
        internal string MarkdownCodeFenceContent(int line)
        {
            MarkdownCodeFenceBlock block;
            if (!TryGetMarkdownCodeFenceBlock(line, out block)) return string.Empty;
            int lastContentLine = block.ClosingLine >= 0 ? block.ClosingLine : _lineCache.Length;
            var contentLines = new List<string>();
            for (int contentLine = block.OpeningLine + 1; contentLine < lastContentLine; contentLine++)
            {
                string value = _lineCache[contentLine].TrimEnd('\r');
                if (block.OpeningQuoteDepth > 0)
                {
                    int prefixLength = 0;
                    int depth = 0;
                    while (depth < block.OpeningQuoteDepth)
                    {
                        int markerPosition = prefixLength;
                        int indent = 0;
                        while (markerPosition < value.Length && indent < 3 && value[markerPosition] == ' ')
                        { markerPosition++; indent++; }
                        if (markerPosition >= value.Length || value[markerPosition] != '>') break;
                        prefixLength = markerPosition + 1;
                        if (prefixLength < value.Length && (value[prefixLength] == ' ' || value[prefixLength] == '\t'))
                            prefixLength++;
                        depth++;
                    }
                    if (depth == block.OpeningQuoteDepth) value = value.Substring(prefixLength);
                }
                contentLines.Add(value);
            }
            return string.Join(Environment.NewLine, contentLines);
        }
        internal bool HasFocus { get { return IsKeyboardFocusWithin; } }
        internal bool CaretVisible { get { return _caretVisible; } }

        internal int MarkdownHeadingLevelForLine(int line)
        {
            if (!EnableMarkdownHeadings || IsMarkdownCodeFenceLine(line) || IsMarkdownIndentedCodeLine(line) ||
                line < 0 || line >= _lineCache.Length) return 0;
            string value = _lineCache[line].TrimEnd('\r');
            int quotePrefix = MarkdownQuotePrefixLengthForLine(line);
            value = value.Substring(Math.Min(quotePrefix, value.Length));
            int position = 0;
            while (position < value.Length && position < 3 && value[position] == ' ') position++;
            int level = 0;
            while (position + level < value.Length && value[position + level] == '#') level++;
            if (level == 0) return MarkdownSetextHeadingLevelForTitleLine(line);
            if (level > 6) return 0;
            int afterMarkers = position + level;
            if (afterMarkers < value.Length && value[afterMarkers] != ' ' && value[afterMarkers] != '\t') return 0;
            return level;
        }

        private int MarkdownSetextHeadingLevelForTitleLine(int line)
        {
            if (line < 0 || line + 1 >= _lineCache.Length || IsMarkdownCodeFenceLine(line) ||
                IsMarkdownIndentedCodeLine(line)) return 0;
            int titleQuotePrefix = MarkdownQuotePrefixLengthForLine(line);
            int underlineQuotePrefix = MarkdownQuotePrefixLengthForLine(line + 1);
            if (MarkdownQuoteDepthForLine(line) != MarkdownQuoteDepthForLine(line + 1)) return 0;
            string title = _lineCache[line].TrimEnd('\r').Substring(titleQuotePrefix);
            if (string.IsNullOrWhiteSpace(title)) return 0;
            string titleContent = title.TrimStart(' ');
            int headingMarkers = 0;
            while (headingMarkers < titleContent.Length && titleContent[headingMarkers] == '#') headingMarkers++;
            if (headingMarkers > 0 && headingMarkers <= 6 &&
                (headingMarkers == titleContent.Length || char.IsWhiteSpace(titleContent[headingMarkers]))) return 0;
            MarkdownListMarker listMarker;
            if (TryParseMarkdownListMarker(title, out listMarker) || IsThematicBreakSource(title)) return 0;
            string underline = _lineCache[line + 1].TrimEnd('\r').Substring(underlineQuotePrefix);
            int indent = LeadingWhitespaceLength(underline);
            if (indent > 3) return 0;
            string marker = underline.Substring(indent).TrimEnd(' ', '\t');
            if (marker.Length == 0) return 0;
            char delimiter = marker[0];
            if (delimiter != '=' && delimiter != '-') return 0;
            for (int index = 0; index < marker.Length; index++)
                if (marker[index] != delimiter) return 0;
            if (delimiter == '=' && marker.Length >= 1) return 1;
            if (delimiter == '-' && marker.Length >= 1) return 2;
            return 0;
        }

        private static bool IsThematicBreakSource(string value)
        {
            int position = 0;
            while (position < value.Length && position < 3 && value[position] == ' ') position++;
            if (position > 3 || position >= value.Length) return false;
            char delimiter = value[position];
            if (delimiter != '-' && delimiter != '*' && delimiter != '_') return false;
            int count = 0;
            for (; position < value.Length; position++)
            {
                char character = value[position];
                if (character == ' ' || character == '\t') continue;
                if (character != delimiter) return false;
                count++;
            }
            return count >= 3;
        }

        internal bool IsMarkdownSetextUnderlineLine(int line)
        {
            return EnableMarkdownHeadings && line > 0 && line < _lineCache.Length &&
                MarkdownSetextHeadingLevelForTitleLine(line - 1) > 0;
        }

        internal bool MarkdownSetextMarkersAreVisible(int line)
        {
            if (!IsMarkdownSetextUnderlineLine(line)) return false;
            int start = GetLineStart(line);
            int end = GetLineEndAtLine(line);
            if (_caret >= start && _caret <= end) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= start && caret <= end) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < end && selection.Start + selection.Length > start) return true;
            return false;
        }

        internal bool RefreshMarkdownSetextSourceVisibility()
        {
            bool changed = _markdownSetextMarkerVisibilityByLine.Length != _lineCache.Length;
            if (changed) _markdownSetextMarkerVisibilityByLine = new bool[_lineCache.Length];
            for (int line = 0; line < _lineCache.Length; line++)
            {
                bool visible = IsMarkdownSetextUnderlineLine(line) && MarkdownSetextMarkersAreVisible(line);
                if (_markdownSetextMarkerVisibilityByLine[line] == visible) continue;
                _markdownSetextMarkerVisibilityByLine[line] = visible;
                changed = true;
            }
            if (changed) RebuildVisibleLineMap();
            return changed;
        }

        internal int MarkdownQuoteDepthForLine(int line)
        {
            if (line < 0 || line >= _lineCache.Length) return 0;
            int depth;
            GetMarkdownQuotePrefix(_lineCache[line].TrimEnd('\r'), out depth);
            return IsMarkdownIndentedCodeLine(line) && depth == 0 ? 0 : depth;
        }

        private static int GetMarkdownQuotePrefix(string value, out int depth)
        {
            int position = 0;
            while (position < value.Length && position < 3 && value[position] == ' ') position++;
            depth = 0;
            while (position < value.Length && value[position] == '>')
            {
                depth++;
                position++;
                if (position < value.Length && (value[position] == ' ' || value[position] == '\t')) position++;
                int nestedMarker = position;
                while (nestedMarker < value.Length && value[nestedMarker] == ' ') nestedMarker++;
                if (nestedMarker < value.Length && value[nestedMarker] == '>') position = nestedMarker;
                else break;
            }
            return depth == 0 ? 0 : position;
        }

        internal int MarkdownQuotePrefixLengthForLine(int line)
        {
            int depth = MarkdownQuoteDepthForLine(line);
            if (depth == 0) return 0;
            int parsedDepth;
            return GetMarkdownQuotePrefix(_lineCache[line].TrimEnd('\r'), out parsedDepth);
        }

        internal bool MarkdownQuoteMarkersAreVisible(int line)
        {
            int prefixLength = MarkdownQuotePrefixLengthForLine(line);
            if (prefixLength <= 0) return false;
            int start = GetLineStart(line);
            int end = start + prefixLength;
            if (_caret >= start && _caret <= end) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= start && caret <= end) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < end && selection.Start + selection.Length > start) return true;
            return false;
        }

        internal bool IsMarkdownIndentedCodeLine(int line)
        {
            return EnableMarkdownCodeBlocks && line >= 0 && line < _markdownIndentedCodeLines.Length &&
                _markdownIndentedCodeLines[line];
        }

        internal bool IsMarkdownCodeBlockLine(int line)
        {
            return IsMarkdownCodeFenceBodyLine(line) || IsMarkdownIndentedCodeLine(line);
        }

        internal int MarkdownIndentedCodePrefixLength(int line)
        {
            if (!IsMarkdownIndentedCodeLine(line)) return 0;
            string value = _lineCache[line].TrimEnd('\r');
            int quotePrefix = GetMarkdownQuotePrefix(value, out int quoteDepth);
            string code = value.Substring(quotePrefix);
            int count = 0;
            while (count < code.Length && code[count] == ' ' && count < 4) count++;
            return count >= 4 ? 4 : (count == 0 && code.Length > 0 && code[0] == '\t' ? 1 : 0);
        }

        internal int MarkdownIndentedCodePrefixStart(int line)
        {
            if (!IsMarkdownIndentedCodeLine(line)) return 0;
            int quoteDepth;
            return GetMarkdownQuotePrefix(_lineCache[line].TrimEnd('\r'), out quoteDepth);
        }

        private bool IsMarkdownEscapablePunctuation(int line, int position)
        {
            if (line < 0 || line >= _lineCache.Length) return false;
            string value = _lineCache[line].TrimEnd('\r');
            if (position < 0 || position + 1 >= value.Length || value[position] != '\\' ||
                IsMarkdownCodeFenceLine(line) || IsMarkdownIndentedCodeLine(line)) return false;
            const string escapable = "\\`*_{}[]()#+-.!|>~$^";
            int precedingSlashes = 0;
            for (int previous = position - 1; previous >= 0 && value[previous] == '\\'; previous--) precedingSlashes++;
            return precedingSlashes % 2 == 0 && escapable.IndexOf(value[position + 1]) >= 0 &&
                !IsMarkdownInlineCodeContentColumn(line, position);
        }

        private void RebuildMarkdownIndentedCodeLines()
        {
            _markdownIndentedCodeLines = new bool[_lineCache.Length];
            if (!EnableMarkdownCodeBlocks) return;
            for (int line = 0; line < _lineCache.Length; line++)
            {
                if (IsMarkdownCodeFenceLine(line)) continue;
                string value = _lineCache[line].TrimEnd('\r');
                int quoteDepth;
                int quotePrefix = GetMarkdownQuotePrefix(value, out quoteDepth);
                string code = value.Substring(quotePrefix);
                int indent = 0;
                while (indent < code.Length && code[indent] == ' ') indent++;
                _markdownIndentedCodeLines[line] = indent >= 4 ||
                    (code.Length > 0 && code[0] == '\t');
            }
        }

        internal double MarkdownDefinitionListIndentForLine(int line)
        {
            if (line < 0 || line >= _lineCache.Length || IsMarkdownCodeFenceLine(line)) return 0;
            string value = _lineCache[line].TrimEnd('\r');
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(value, @"^ {0,3}:\s+");
            bool hasTerm = line > 0 && !string.IsNullOrWhiteSpace(_lineCache[line - 1].TrimEnd('\r'));
            return (match.Success && hasTerm) || MarkdownFootnoteDefinitionPrefixLength(line) > 0
                ? MeasureTextWidth("  ") : 0;
        }

        internal int MarkdownDefinitionListPrefixLengthForLine(int line)
        {
            if (line < 0 || line >= _lineCache.Length || IsMarkdownCodeFenceLine(line)) return 0;
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
                _lineCache[line].TrimEnd('\r'), @"^ {0,3}:\s+");
            return match.Success && line > 0 &&
                !string.IsNullOrWhiteSpace(_lineCache[line - 1].TrimEnd('\r')) ? match.Length : 0;
        }

        internal bool IsMarkdownDefinitionTermLine(int line)
        {
            if (line < 0 || line + 1 >= _lineCache.Length || IsMarkdownCodeFenceLine(line)) return false;
            string next = _lineCache[line + 1].TrimEnd('\r');
            return !string.IsNullOrWhiteSpace(_lineCache[line].TrimEnd('\r')) &&
                System.Text.RegularExpressions.Regex.IsMatch(next, @"^ {0,3}:\s+");
        }

        internal bool IsMarkdownHorizontalRuleLine(int line)
        {
            if (!EnableMarkdownHorizontalRules || IsMarkdownCodeFenceLine(line) || IsMarkdownIndentedCodeLine(line) ||
                line < 0 || line >= _lineCache.Length || IsMarkdownSetextUnderlineLine(line)) return false;
            string value = _lineCache[line].TrimEnd('\r');
            int position = MarkdownQuotePrefixLengthForLine(line);
            while (position < value.Length && value[position] == ' ') position++;
            if (position - MarkdownQuotePrefixLengthForLine(line) > 3) return false;
            char delimiter = '\0';
            int delimiterCount = 0;
            for (; position < value.Length; position++)
            {
                char character = value[position];
                if (character == ' ' || character == '\t') continue;
                if (character != '-' && character != '*' && character != '_') return false;
                if (delimiter == '\0') delimiter = character;
                else if (delimiter != character) return false;
                delimiterCount++;
            }
            return delimiterCount >= 3;
        }

        internal bool TryGetMarkdownListMarkerForLine(int line, out MarkdownListMarker marker)
        {
            marker = default(MarkdownListMarker);
            if (!EnableMarkdownLists || IsMarkdownCodeFenceLine(line) || IsMarkdownIndentedCodeLine(line) ||
                line < 0 || line >= _lineCache.Length)
                return false;

            if (!_markdownListMarkersParsed[line])
            {
                _markdownListMarkersParsed[line] = true;
                string lineValue = _lineCache[line].TrimEnd('\r');
                int quotePrefix = MarkdownQuotePrefixLengthForLine(line);
                string listContent = lineValue.Substring(Math.Min(quotePrefix, lineValue.Length));
                MarkdownListMarker parsedMarker;
                if (TryParseMarkdownListMarker(listContent, out parsedMarker) &&
                    (GetMarkdownIndentWidth(listContent, parsedMarker.MarkerStart) <= 3 ||
                     HasMarkdownListParent(line, new MarkdownListMarker(
                         parsedMarker.MarkerStart + quotePrefix, parsedMarker.MarkerEnd + quotePrefix,
                         parsedMarker.ContentStart + quotePrefix, parsedMarker.IsOrdered))))
                {
                    _markdownListMarkersByLine[line] = new MarkdownListMarker(
                        parsedMarker.MarkerStart + quotePrefix, parsedMarker.MarkerEnd + quotePrefix,
                        parsedMarker.ContentStart + quotePrefix, parsedMarker.IsOrdered);
                }
            }

            marker = _markdownListMarkersByLine[line];
            return marker.MarkerEnd > marker.MarkerStart;
        }

        private bool HasMarkdownListParent(int line, MarkdownListMarker marker)
        {
            string currentValue = _lineCache[line].TrimEnd('\r');
            int currentIndent = GetMarkdownIndentWidth(currentValue, marker.MarkerStart);
            for (int previousLine = line - 1; previousLine >= 0; previousLine--)
            {
                if (IsMarkdownCodeFenceLine(previousLine)) break;

                string previousValue = _lineCache[previousLine].TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(previousValue)) continue;

                MarkdownListMarker parentMarker;
                if (TryGetMarkdownListMarkerForLine(previousLine, out parentMarker))
                {
                    int parentIndent = GetMarkdownIndentWidth(previousValue, parentMarker.MarkerStart);
                    if (parentIndent < currentIndent &&
                        currentIndent >= parentIndent + 2 &&
                        LinesAreIndentedWithinMarkdownList(previousLine + 1, line - 1, parentIndent + 2))
                        return true;
                }
                else if (GetMarkdownIndentWidth(previousValue, LeadingWhitespaceLength(previousValue)) == 0)
                {
                    break;
                }
            }
            return false;
        }

        private bool LinesAreIndentedWithinMarkdownList(int firstLine, int lastLine, int minimumIndent)
        {
            for (int line = firstLine; line <= lastLine; line++)
            {
                if (IsMarkdownCodeFenceLine(line)) return false;
                string value = _lineCache[line].TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (GetMarkdownIndentWidth(value, LeadingWhitespaceLength(value)) < minimumIndent) return false;
            }
            return true;
        }

        private static bool TryParseMarkdownListMarker(string value, out MarkdownListMarker marker)
        {
            marker = default(MarkdownListMarker);
            int markerStart = LeadingWhitespaceLength(value);
            if (markerStart >= value.Length) return false;

            bool isOrdered = false;
            int markerEnd;
            char first = value[markerStart];
            if (first == '-' || first == '+' || first == '*')
            {
                markerEnd = markerStart + 1;
            }
            else if (first >= '0' && first <= '9')
            {
                int digitsEnd = markerStart;
                while (digitsEnd < value.Length && value[digitsEnd] >= '0' && value[digitsEnd] <= '9') digitsEnd++;
                if (digitsEnd == markerStart || digitsEnd >= value.Length ||
                    (value[digitsEnd] != '.' && value[digitsEnd] != ')')) return false;
                markerEnd = digitsEnd + 1;
                isOrdered = true;
            }
            else
            {
                return false;
            }

            if (markerEnd >= value.Length || (value[markerEnd] != ' ' && value[markerEnd] != '\t')) return false;
            int contentStart = markerEnd;
            while (contentStart < value.Length && (value[contentStart] == ' ' || value[contentStart] == '\t')) contentStart++;
            marker = new MarkdownListMarker(markerStart, markerEnd, contentStart, isOrdered);
            return true;
        }

        private static int LeadingWhitespaceLength(string value)
        {
            int length = 0;
            while (length < value.Length && (value[length] == ' ' || value[length] == '\t')) length++;
            return length;
        }

        private static int GetMarkdownIndentWidth(string value, int endColumn)
        {
            int width = 0;
            int end = Math.Max(0, Math.Min(value.Length, endColumn));
            for (int column = 0; column < end; column++)
            {
                if (value[column] == '\t') width += 4 - (width % 4);
                else if (value[column] == ' ') width++;
            }
            return width;
        }

        internal double MarkdownListIndentForLine(int line)
        {
            MarkdownListMarker marker;
            return TryGetMarkdownListMarkerForLine(line, out marker) ? MeasureTextWidth("  ") : 0;
        }

        internal bool MarkdownListMarkerIsVisible(int line, MarkdownListMarker marker)
        {
            int lineStart = GetLineStart(line);
            int prefixEnd = lineStart + marker.ContentStart;
            if (_caret >= lineStart && _caret <= prefixEnd) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= lineStart && caret <= prefixEnd) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < prefixEnd && selection.Start + selection.Length > lineStart) return true;
            return false;
        }

        internal bool MarkdownHorizontalRuleSourceIsVisible(int line)
        {
            if (!IsMarkdownHorizontalRuleLine(line)) return false;
            int lineStart = GetLineStart(line);
            int lineEnd = GetLineEndAtLine(line);
            if (_caret >= lineStart && _caret <= lineEnd) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= lineStart && caret <= lineEnd) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < lineEnd && selection.Start + selection.Length > lineStart) return true;
            return false;
        }

        internal bool IsMarkdownCodeFenceLine(int line)
        {
            return EnableMarkdownCodeBlocks && line >= 0 && line < _markdownCodeFenceByLine.Length &&
                _markdownCodeFenceByLine[line] >= 0;
        }

        internal bool IsMarkdownCodeFenceBodyLine(int line)
        {
            MarkdownCodeFenceBlock block;
            return TryGetMarkdownCodeFenceBlock(line, out block) && line != block.OpeningLine && line != block.ClosingLine;
        }

        private bool IsMarkdownCodeFenceDelimiterLine(int line)
        {
            MarkdownCodeFenceBlock block;
            return TryGetMarkdownCodeFenceBlock(line, out block) &&
                (line == block.OpeningLine || line == block.ClosingLine);
        }

        private bool TryGetMarkdownCodeFenceBlock(int line, out MarkdownCodeFenceBlock block)
        {
            block = default(MarkdownCodeFenceBlock);
            if (!IsMarkdownCodeFenceLine(line)) return false;
            block = _markdownCodeFenceBlocks[_markdownCodeFenceByLine[line]];
            return true;
        }

        internal bool MarkdownCodeFenceMarkersAreVisible(int line)
        {
            MarkdownCodeFenceBlock block;
            if (!TryGetMarkdownCodeFenceBlock(line, out block)) return false;
            int lastLine = block.ClosingLine >= 0 ? block.ClosingLine : _lineCache.Length - 1;
            int blockStart = GetLineStart(block.OpeningLine);
            int blockEnd = block.ClosingLine >= 0 ? GetLineEndAtLine(block.ClosingLine) : Text.Length;
            if (_caret >= blockStart && _caret <= blockEnd) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= blockStart && caret <= blockEnd) return true;
            for (int selectedLine = block.OpeningLine; selectedLine <= lastLine; selectedLine++)
                foreach (SelectionSpan selection in GetSelectionSpansForLine(selectedLine))
                    if (selection.Start < blockEnd && selection.Start + selection.Length > blockStart) return true;
            return false;
        }

        private void RebuildMarkdownCodeFenceBlocks()
        {
            _markdownCodeFenceBlocks = new List<MarkdownCodeFenceBlock>();
            _markdownCodeFenceByLine = new int[_lineCache.Length];
            for (int i = 0; i < _markdownCodeFenceByLine.Length; i++) _markdownCodeFenceByLine[i] = -1;

            int line = 0;
            while (line < _lineCache.Length)
            {
                int delimiterLength;
                char delimiterCharacter;
                string language;
                string openingValue = _lineCache[line].TrimEnd('\r');
                int openingQuoteDepth;
                int openingQuotePrefix = GetMarkdownQuotePrefix(openingValue, out openingQuoteDepth);
                if (!TryParseMarkdownCodeFenceOpening(openingValue.Substring(openingQuotePrefix),
                    out delimiterLength, out delimiterCharacter, out language))
                {
                    line++;
                    continue;
                }

                int closingLine = -1;
                for (int candidate = line + 1; candidate < _lineCache.Length; candidate++)
                {
                    string candidateValue = _lineCache[candidate].TrimEnd('\r');
                    int candidateQuoteDepth;
                    int candidateQuotePrefix = GetMarkdownQuotePrefix(candidateValue, out candidateQuoteDepth);
                    string fenceCandidate = candidateQuoteDepth >= openingQuoteDepth
                        ? candidateValue.Substring(candidateQuotePrefix) : candidateValue;
                    if (TryParseMarkdownCodeFenceClosing(fenceCandidate,
                        delimiterLength, delimiterCharacter))
                    {
                        closingLine = candidate;
                        break;
                    }
                }

                int blockIndex = _markdownCodeFenceBlocks.Count;
                _markdownCodeFenceBlocks.Add(new MarkdownCodeFenceBlock(line, closingLine,
                    delimiterLength, delimiterCharacter, language, openingQuoteDepth));
                int lastLine = closingLine >= 0 ? closingLine : _lineCache.Length - 1;
                for (int blockLine = line; blockLine <= lastLine; blockLine++)
                    _markdownCodeFenceByLine[blockLine] = blockIndex;
                line = lastLine + 1;
            }
        }

        private static bool TryParseMarkdownCodeFenceOpening(string value, out int delimiterLength,
            out char delimiterCharacter, out string language)
        {
            delimiterLength = 0;
            delimiterCharacter = '\0';
            language = string.Empty;
            int indent = 0;
            while (indent < value.Length && indent <= 3 && value[indent] == ' ') indent++;
            if (indent > 3 || indent >= value.Length || (value[indent] != '`' && value[indent] != '~')) return false;
            delimiterCharacter = value[indent];
            delimiterLength = RunLength(value, indent, delimiterCharacter);
            if (delimiterLength < 3) return false;
            if (delimiterCharacter == '`')
                for (int i = indent + delimiterLength; i < value.Length; i++)
                    if (value[i] == '`') return false;
            int infoStart = indent + delimiterLength;
            while (infoStart < value.Length && char.IsWhiteSpace(value[infoStart])) infoStart++;
            if (infoStart < value.Length)
            {
                int infoEnd = infoStart;
                while (infoEnd < value.Length && !char.IsWhiteSpace(value[infoEnd])) infoEnd++;
                language = value.Substring(infoStart, infoEnd - infoStart).ToLowerInvariant();
            }
            return true;
        }

        private static bool TryParseMarkdownCodeFenceClosing(string value, int minimumDelimiterLength,
            char delimiterCharacter)
        {
            int indent = 0;
            while (indent < value.Length && indent <= 3 && value[indent] == ' ') indent++;
            if (indent > 3 || indent >= value.Length || value[indent] != delimiterCharacter) return false;
            int delimiterLength = RunLength(value, indent, delimiterCharacter);
            if (delimiterLength < minimumDelimiterLength) return false;
            for (int i = indent + delimiterLength; i < value.Length; i++)
                if (value[i] != ' ' && value[i] != '\t') return false;
            return true;
        }

        private static int RunLength(string value, int start, char character)
        {
            int end = start;
            while (end < value.Length && value[end] == character) end++;
            return end - start;
        }

        internal List<SelectionSpan> GetMarkdownBoldContentSpansForLine(int line)
        {
            var spans = new List<SelectionSpan>();
            if (!EnableMarkdownBold || IsMarkdownCodeFenceLine(line) || IsMarkdownIndentedCodeLine(line) ||
                line < 0 || line >= _lineCache.Length) return spans;

            string value = _lineCache[line].TrimEnd('\r');
            int searchFrom = 0;
            while (searchFrom < value.Length)
            {
                int opening = searchFrom;
                while (opening < value.Length && value[opening] != '*' && value[opening] != '_') opening++;
                if (opening >= value.Length) break;
                char delimiter = value[opening];
                int runLength = MarkdownEmphasisDelimiterRunLength(value, opening, delimiter);
                if (runLength < 2 || IsMarkdownEscaped(value, opening) ||
                    (delimiter == '_' && opening > 0 && char.IsLetterOrDigit(value[opening - 1])) ||
                    IsMarkdownInlineCodeContentColumn(line, opening))
                {
                    searchFrom = opening + runLength;
                    continue;
                }
                int closing = opening + runLength;
                bool found = false;
                while (closing < value.Length)
                {
                    if (value[closing] != delimiter)
                    {
                        closing++;
                        continue;
                    }
                    int closingRun = MarkdownEmphasisDelimiterRunLength(value, closing, delimiter);
                    if (closingRun >= 2 && opening + runLength < value.Length &&
                        closing > opening + runLength && !IsMarkdownEscaped(value, closing) &&
                        !char.IsWhiteSpace(value[opening + runLength]) &&
                        !char.IsWhiteSpace(value[closing - 1]) &&
                        (delimiter != '_' || closing + closingRun >= value.Length ||
                         !char.IsLetterOrDigit(value[closing + closingRun])) &&
                        !IsMarkdownInlineCodeContentColumn(line, closing))
                    {
                        const int delimiterLength = 2;
                        spans.Add(new SelectionSpan(opening + delimiterLength,
                            closing - opening - delimiterLength));
                        searchFrom = closing + delimiterLength;
                        found = true;
                        break;
                    }
                    closing += closingRun;
                }
                if (!found) searchFrom = opening + runLength;
            }
            return spans;
        }

        internal List<SelectionSpan> GetMarkdownItalicContentSpansForLine(int line)
        {
            var spans = new List<SelectionSpan>();
            if (!EnableMarkdownItalic || IsMarkdownCodeFenceLine(line) || IsMarkdownIndentedCodeLine(line) ||
                line < 0 || line >= _lineCache.Length) return spans;

            string value = _lineCache[line].TrimEnd('\r');
            int searchFrom = 0;
            foreach (char delimiter in new[] { '*', '_' })
            {
                string triple = new string(delimiter, 3);
                int opening = 0;
                while ((opening = value.IndexOf(triple, opening, StringComparison.Ordinal)) >= 0)
                {
                    int closing = value.IndexOf(triple, opening + 3, StringComparison.Ordinal);
                    if (closing >= opening + 6 && !IsMarkdownEscaped(value, opening) &&
                        !IsMarkdownEscaped(value, closing) &&
                        (delimiter != '_' || opening == 0 || !char.IsLetterOrDigit(value[opening - 1])) &&
                        (delimiter != '_' || closing + 3 >= value.Length || !char.IsLetterOrDigit(value[closing + 3])) &&
                        !IsMarkdownInlineCodeContentColumn(line, opening))
                    {
                        spans.Add(new SelectionSpan(opening + 3, closing - opening - 6));
                        opening = closing + 3;
                    }
                    else opening += 3;
                }
            }
            while (searchFrom < value.Length)
            {
                int opening = searchFrom;
                while (opening < value.Length && value[opening] != '*' && value[opening] != '_') opening++;
                if (opening >= value.Length) break;

                char delimiter = value[opening];
                int openingRunLength = MarkdownEmphasisDelimiterRunLength(value, opening, delimiter);
                bool openingIsValid = openingRunLength == 1 && opening + 1 < value.Length &&
                    !char.IsWhiteSpace(value[opening + 1]) && !IsMarkdownEscaped(value, opening) &&
                    (delimiter != '_' || opening == 0 || !char.IsLetterOrDigit(value[opening - 1])) &&
                    !IsMarkdownInlineCodeContentColumn(line, opening);
                if (!openingIsValid)
                {
                    searchFrom = opening + openingRunLength;
                    continue;
                }

                int closing = opening + 1;
                bool foundClosing = false;
                while (closing < value.Length)
                {
                    if (value[closing] != delimiter)
                    {
                        closing++;
                        continue;
                    }

                    int closingRunLength = MarkdownEmphasisDelimiterRunLength(value, closing, delimiter);
                    bool closingIsValid = closingRunLength == 1 && closing > opening + 1 &&
                        !char.IsWhiteSpace(value[closing - 1]) && !IsMarkdownEscaped(value, closing) &&
                        (delimiter != '_' || closing + 1 >= value.Length || !char.IsLetterOrDigit(value[closing + 1])) &&
                        !IsMarkdownInlineCodeContentColumn(line, closing);
                    if (closingIsValid)
                    {
                        spans.Add(new SelectionSpan(opening + 1, closing - opening - 1));
                        searchFrom = closing + 1;
                        foundClosing = true;
                        break;
                    }
                    closing += closingRunLength;
                }

                if (!foundClosing) searchFrom = opening + 1;
            }
            return spans;
        }

        internal List<SelectionSpan> GetMarkdownStrikethroughContentSpansForLine(int line)
        {
            var spans = new List<SelectionSpan>();
            if (IsMarkdownCodeFenceLine(line) || IsMarkdownIndentedCodeLine(line) ||
                line < 0 || line >= _lineCache.Length) return spans;

            string value = _lineCache[line].TrimEnd('\r');
            int searchFrom = 0;
            while (searchFrom < value.Length)
            {
                int opening = value.IndexOf("~~", searchFrom, StringComparison.Ordinal);
                if (opening < 0) break;
                if (MarkdownEmphasisDelimiterRunLength(value, opening, '~') != 2 ||
                    IsMarkdownEscaped(value, opening) || IsMarkdownInlineCodeContentColumn(line, opening))
                {
                    searchFrom = opening + MarkdownEmphasisDelimiterRunLength(value, opening, '~');
                    continue;
                }

                int closing = opening + 2;
                bool foundClosing = false;
                while (closing < value.Length)
                {
                    int candidate = value.IndexOf("~~", closing, StringComparison.Ordinal);
                    if (candidate < 0) break;
                    int runLength = MarkdownEmphasisDelimiterRunLength(value, candidate, '~');
                    if (runLength == 2 && candidate > opening + 2 &&
                        !char.IsWhiteSpace(value[opening + 2]) && !char.IsWhiteSpace(value[candidate - 1]) &&
                        !IsMarkdownEscaped(value, candidate) && !IsMarkdownInlineCodeContentColumn(line, candidate))
                    {
                        spans.Add(new SelectionSpan(opening + 2, candidate - opening - 2));
                        searchFrom = candidate + 2;
                        foundClosing = true;
                        break;
                    }
                    closing = candidate + Math.Max(1, runLength);
                }

                if (!foundClosing) searchFrom = opening + 2;
            }
            return spans;
        }

        internal List<MarkdownScriptSpan> GetMarkdownScriptSpansForLine(int line)
        {
            var spans = new List<MarkdownScriptSpan>();
            if (IsMarkdownCodeFenceLine(line) || IsMarkdownIndentedCodeLine(line) ||
                line < 0 || line >= _lineCache.Length)
                return spans;

            string value = _lineCache[line].TrimEnd('\r');
            if (EnableMarkdownSuperSubscript)
            {
                char[] delimiters = { '^', '~' };
                foreach (char delimiter in delimiters)
                {
                    int searchFrom = 0;
                    while (searchFrom < value.Length)
                    {
                        int opening = searchFrom;
                        while (opening < value.Length && value[opening] != delimiter) opening++;
                        if (opening >= value.Length) break;

                        int openingRunLength = MarkdownEmphasisDelimiterRunLength(value, opening, delimiter);
                        bool openingIsValid = openingRunLength == 1 && opening + 1 < value.Length &&
                            !char.IsWhiteSpace(value[opening + 1]) && !IsMarkdownEscaped(value, opening) &&
                            !IsMarkdownInlineCodeContentColumn(line, opening) &&
                            !IsMarkdownMathContentColumn(line, opening);
                        if (!openingIsValid)
                        {
                            searchFrom = opening + openingRunLength;
                            continue;
                        }

                        int closing = opening + 1;
                        bool foundClosing = false;
                        while (closing < value.Length)
                        {
                            if (value[closing] != delimiter)
                            {
                                closing++;
                                continue;
                            }

                            int closingRunLength = MarkdownEmphasisDelimiterRunLength(value, closing, delimiter);
                            bool closingIsValid = closingRunLength == 1 && closing > opening + 1 &&
                                !char.IsWhiteSpace(value[closing - 1]) && !IsMarkdownEscaped(value, closing) &&
                                !IsMarkdownInlineCodeContentColumn(line, closing) &&
                                !IsMarkdownMathContentColumn(line, closing);
                            if (closingIsValid)
                            {
                                spans.Add(new MarkdownScriptSpan(opening + 1, closing - opening - 1, delimiter == '^'));
                                searchFrom = closing + 1;
                                foundClosing = true;
                                break;
                            }
                            closing += closingRunLength;
                        }

                        if (!foundClosing) searchFrom = opening + 1;
                    }
                }
            }
            if (EnableMarkdownMath) spans.AddRange(GetMarkdownMathScriptSpansForLine(line));
            return spans;
        }

        private static int MarkdownEmphasisDelimiterRunLength(string value, int start, char delimiter)
        {
            int end = start;
            while (end < value.Length && value[end] == delimiter) end++;
            return end - start;
        }

        private static bool IsMarkdownEscaped(string value, int position)
        {
            int backslashCount = 0;
            for (int index = position - 1; index >= 0 && value[index] == '\\'; index--) backslashCount++;
            return backslashCount % 2 != 0;
        }

        internal List<MarkdownInlineCodeSpan> GetMarkdownInlineCodeSpansForLine(int line)
        {
            var spans = new List<MarkdownInlineCodeSpan>();
            if (!EnableMarkdownInlineCode || IsMarkdownCodeFenceLine(line) || IsMarkdownIndentedCodeLine(line) ||
                line < 0 || line >= _lineCache.Length) return spans;

            string value = _lineCache[line].TrimEnd('\r');
            int searchFrom = 0;
            while (searchFrom < value.Length)
            {
                int opening = value.IndexOf('`', searchFrom);
                if (opening < 0) break;
                int delimiterLength = BacktickRunLength(value, opening);
                if (IsMarkdownEscaped(value, opening))
                {
                    searchFrom = opening + delimiterLength;
                    continue;
                }
                int closing = -1;
                int closingSearch = opening + delimiterLength;
                while (closingSearch < value.Length)
                {
                    int candidate = value.IndexOf('`', closingSearch);
                    if (candidate < 0) break;
                    int candidateLength = BacktickRunLength(value, candidate);
                    if (candidateLength == delimiterLength && !IsMarkdownEscaped(value, candidate))
                    {
                        closing = candidate;
                        break;
                    }
                    closingSearch = candidate + candidateLength;
                }

                if (closing < 0)
                {
                    searchFrom = opening + delimiterLength;
                    continue;
                }

                int contentStart = opening + delimiterLength;
                spans.Add(new MarkdownInlineCodeSpan(contentStart, closing - contentStart, delimiterLength));
                searchFrom = closing + delimiterLength;
            }
            return spans;
        }

        private static int BacktickRunLength(string value, int start)
        {
            int end = start;
            while (end < value.Length && value[end] == '`') end++;
            return end - start;
        }

        private bool IsMarkdownInlineCodeContentColumn(int line, int sourceColumn)
        {
            foreach (MarkdownInlineCodeSpan span in GetMarkdownInlineCodeSpansForLine(line))
                if (sourceColumn >= span.ContentStart && sourceColumn < span.ContentStart + span.ContentLength)
                    return true;
            return false;
        }

        internal bool MarkdownInlineCodeMarkersAreVisible(int line, MarkdownInlineCodeSpan span)
        {
            int lineStart = GetLineStart(line);
            int pairStart = lineStart + span.ContentStart - span.DelimiterLength;
            int pairEnd = lineStart + span.ContentStart + span.ContentLength + span.DelimiterLength;
            if (_caret >= pairStart && _caret <= pairEnd) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= pairStart && caret <= pairEnd) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < pairEnd && selection.Start + selection.Length > pairStart) return true;
            return false;
        }

        internal bool MarkdownBoldMarkersAreVisible(int line, SelectionSpan contentSpan)
        {
            int lineStart = GetLineStart(line);
            int pairStart = lineStart + contentSpan.Start - 2;
            int pairEnd = lineStart + contentSpan.Start + contentSpan.Length + 2;
            if (_caret >= pairStart && _caret <= pairEnd) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= pairStart && caret <= pairEnd) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < pairEnd && selection.Start + selection.Length > pairStart) return true;
            return false;
        }

        internal bool MarkdownItalicMarkersAreVisible(int line, SelectionSpan contentSpan)
        {
            int lineStart = GetLineStart(line);
            int pairStart = lineStart + contentSpan.Start - 1;
            int pairEnd = lineStart + contentSpan.Start + contentSpan.Length + 1;
            if (_caret >= pairStart && _caret <= pairEnd) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= pairStart && caret <= pairEnd) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < pairEnd && selection.Start + selection.Length > pairStart) return true;
            return false;
        }

        internal bool MarkdownStrikethroughMarkersAreVisible(int line, SelectionSpan contentSpan)
        {
            int lineStart = GetLineStart(line);
            int pairStart = lineStart + contentSpan.Start - 2;
            int pairEnd = lineStart + contentSpan.Start + contentSpan.Length + 2;
            if (_caret >= pairStart && _caret <= pairEnd) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= pairStart && caret <= pairEnd) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < pairEnd && selection.Start + selection.Length > pairStart) return true;
            return false;
        }

        internal bool MarkdownScriptMarkersAreVisible(int line, MarkdownScriptSpan span)
        {
            int lineStart = GetLineStart(line);
            int pairStart = lineStart + span.ContentStart - 1;
            int pairEnd = lineStart + span.ContentStart + span.ContentLength + 1;
            if (_caret >= pairStart && _caret <= pairEnd) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= pairStart && caret <= pairEnd) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < pairEnd && selection.Start + selection.Length > pairStart) return true;
            return false;
        }

        private double MeasureMarkdownMathTextWidth(string value, double fontSize)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var typeface = new Typeface(new FontFamily("Cambria Math"), FontStyles.Normal,
                FontWeights.Normal, FontStretches.Normal);
            var formatted = new FormattedText(value, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection, typeface, Math.Max(1, fontSize), Foreground, dpi);
            return formatted.WidthIncludingTrailingWhitespace;
        }

        private double MeasureMarkdownMathSequenceWidth(List<MarkdownMathCharacter> characters,
            double fontSize)
        {
            if (characters == null || characters.Count == 0) return 0;
            var plainText = new StringBuilder();
            double width = 0;
            int runScriptKind = -1;
            for (int index = 0; index < characters.Count; index++)
            {
                MarkdownMathCharacter character = characters[index];
                if (!character.IsFraction)
                {
                    if (plainText.Length > 0 && runScriptKind != character.ScriptKind)
                    {
                        width += MeasureMarkdownMathTextWidth(plainText.ToString(),
                            fontSize * (runScriptKind == 0 ? 1.0 : 0.72));
                        plainText.Clear();
                    }
                    runScriptKind = character.ScriptKind;
                    plainText.Append(character.Value);
                    continue;
                }
                width += MeasureMarkdownMathTextWidth(plainText.ToString(),
                    fontSize * (runScriptKind == 0 ? 1.0 : 0.72));
                plainText.Clear();
                runScriptKind = -1;
                double innerFontSize = fontSize * 0.72;
                width += Math.Max(MeasureMarkdownMathSequenceWidth(character.Numerator, innerFontSize),
                    MeasureMarkdownMathSequenceWidth(character.Denominator, innerFontSize)) + fontSize * 0.32;
            }
            width += MeasureMarkdownMathTextWidth(plainText.ToString(),
                fontSize * (runScriptKind == 0 ? 1.0 : 0.72));
            return width;
        }

        internal bool MarkdownMathLineContainsFraction(int line)
        {
            if (line < 0 || line >= _lineCache.Length || !EnableMarkdownMath) return false;
            if (_markdownMathFractionCheckedByLine.Length == _lineCache.Length &&
                _markdownMathFractionCheckedByLine[line])
                return _markdownMathContainsFractionByLine[line];
            bool containsFraction = false;
            foreach (MarkdownMathSpan span in GetMarkdownMathSpansForLine(line))
            {
                string value = _lineCache[line].TrimEnd('\r').Substring(span.ContentStart, span.ContentLength);
                List<MarkdownMathCharacter> characters = RenderMarkdownMath(value, span.ContentStart);
                for (int index = 0; index < characters.Count; index++)
                    if (characters[index].IsFraction)
                    {
                        containsFraction = true;
                        break;
                    }
                if (containsFraction) break;
            }
            if (_markdownMathFractionCheckedByLine.Length == _lineCache.Length)
            {
                _markdownMathFractionCheckedByLine[line] = true;
                _markdownMathContainsFractionByLine[line] = containsFraction;
            }
            return containsFraction;
        }

        internal RenderedTextSegment CreateRenderedTextSegment(int line, int sourceStartColumn, int sourceEndColumn)
        {
            string value = _lineCache[Math.Max(0, Math.Min(_lineCache.Length - 1, line))].TrimEnd('\r');
            sourceStartColumn = Math.Max(0, Math.Min(value.Length, sourceStartColumn));
            sourceEndColumn = Math.Max(sourceStartColumn, Math.Min(value.Length, sourceEndColumn));
            var hiddenMarkers = new bool[value.Length];
            MarkdownListMarker listMarker;
            bool hasListMarker = TryGetMarkdownListMarkerForLine(line, out listMarker);
            bool listMarkersVisible = hasListMarker && MarkdownListMarkerIsVisible(line, listMarker);
            MarkdownTaskMarker taskMarker;
            bool hasTaskMarker = TryGetMarkdownTaskMarkerForLine(line, out taskMarker);
            bool taskMarkersVisible = hasTaskMarker && MarkdownTaskMarkerIsVisible(line, taskMarker);
            MarkdownHardBreakMarker hardBreakMarker;
            bool hasHardBreakMarker = TryGetMarkdownHardBreakMarkerForLine(line, out hardBreakMarker);
            bool hardBreakMarkerVisible = hasHardBreakMarker && MarkdownHardBreakMarkerIsVisible(line, hardBreakMarker);
            int quotePrefixLength = MarkdownQuotePrefixLengthForLine(line);
            bool quoteMarkersVisible = quotePrefixLength > 0 && MarkdownQuoteMarkersAreVisible(line);
            int codeIndentStart = MarkdownIndentedCodePrefixStart(line);
            int codeIndentLength = MarkdownIndentedCodePrefixLength(line);
            bool codeIndentVisible = codeIndentLength > 0 &&
                IsMarkdownBlockMarkerVisible(line, codeIndentStart, codeIndentStart + codeIndentLength);
            bool setextUnderline = IsMarkdownSetextUnderlineLine(line);
            bool setextMarkersVisible = setextUnderline && MarkdownSetextMarkersAreVisible(line);
            MarkdownMediaBlock mediaBlock;
            bool hasMediaBlock = TryGetMarkdownMediaBlockForLine(line, out mediaBlock);
            bool showMediaPreview = hasMediaBlock && !MarkdownMediaSourceIsVisible(line, mediaBlock);
            bool mathBlockSourceVisible = MarkdownMathSourceIsVisible(line);
            List<MarkdownMathSpan> mathSpans = GetMarkdownMathSpansForLine(line);
            if (showMediaPreview)
                for (int i = 0; i < hiddenMarkers.Length; i++) hiddenMarkers[i] = true;
            if (IsMarkdownMathBlockDelimiterLine(line) && !MarkdownMathBlockMarkersAreVisible(line))
                for (int column = 0; column < hiddenMarkers.Length; column++) hiddenMarkers[column] = true;
            foreach (MarkdownMathSpan mathSpan in mathSpans)
            {
                if (mathSpan.DelimiterLength <= 0 || MarkdownMathMarkersAreVisible(line, mathSpan)) continue;
                for (int marker = mathSpan.OpeningStart;
                    marker < mathSpan.OpeningStart + mathSpan.DelimiterLength && marker < hiddenMarkers.Length; marker++)
                    if (marker >= 0) hiddenMarkers[marker] = true;
                for (int marker = mathSpan.ClosingStart;
                    marker < mathSpan.ClosingStart + mathSpan.DelimiterLength && marker < hiddenMarkers.Length; marker++)
                    if (marker >= 0) hiddenMarkers[marker] = true;
            }
            if (hasHardBreakMarker && !hardBreakMarkerVisible)
                for (int column = hardBreakMarker.Start; column < hardBreakMarker.End; column++)
                    if (column >= 0 && column < hiddenMarkers.Length) hiddenMarkers[column] = true;
            if (quotePrefixLength > 0 && !quoteMarkersVisible)
                for (int column = 0; column < quotePrefixLength && column < hiddenMarkers.Length; column++)
                    hiddenMarkers[column] = true;
            if (codeIndentLength > 0 && !codeIndentVisible)
                for (int column = codeIndentStart; column < codeIndentStart + codeIndentLength && column < hiddenMarkers.Length; column++)
                    hiddenMarkers[column] = true;
            if (setextUnderline && !setextMarkersVisible)
                for (int column = 0; column < hiddenMarkers.Length; column++) hiddenMarkers[column] = true;
            if (IsMarkdownReferenceDefinitionLine(line) && !MarkdownReferenceDefinitionIsVisible(line))
                for (int column = 0; column < hiddenMarkers.Length; column++) hiddenMarkers[column] = true;
            foreach (MarkdownLinkSpan span in GetMarkdownLinksForLine(line))
            {
                if (MarkdownLinkMarkersAreVisible(line, span)) continue;
                for (int i = span.Start; i < span.LabelStart && i < hiddenMarkers.Length; i++)
                    if (i >= 0) hiddenMarkers[i] = true;
                int labelEnd = span.LabelStart + span.LabelLength;
                for (int i = labelEnd; i < span.End && i < hiddenMarkers.Length; i++)
                    if (i >= 0) hiddenMarkers[i] = true;
            }
            if (IsMarkdownCodeFenceDelimiterLine(line) && !MarkdownCodeFenceMarkersAreVisible(line))
            {
                for (int i = 0; i < hiddenMarkers.Length; i++) hiddenMarkers[i] = true;
            }
            int definitionPrefixLength = MarkdownDefinitionListPrefixLengthForLine(line);
            if (definitionPrefixLength > 0 &&
                !IsMarkdownBlockMarkerVisible(line, 0, definitionPrefixLength))
                for (int column = 0; column < definitionPrefixLength && column < hiddenMarkers.Length; column++)
                    hiddenMarkers[column] = true;
            int footnoteDefinitionPrefixLength = MarkdownFootnoteDefinitionPrefixLength(line);
            if (footnoteDefinitionPrefixLength > 0 &&
                !IsMarkdownBlockMarkerVisible(line, 0, footnoteDefinitionPrefixLength))
                for (int column = 0; column < footnoteDefinitionPrefixLength && column < hiddenMarkers.Length; column++)
                    hiddenMarkers[column] = true;
            string alertType;
            int alertMarkerStart;
            int alertMarkerEnd;
            if (TryGetMarkdownAlertForLine(line, out alertType, out alertMarkerStart, out alertMarkerEnd) &&
                !MarkdownAlertMarkersAreVisible(line, alertMarkerStart, alertMarkerEnd))
                for (int column = alertMarkerStart; column < alertMarkerEnd && column < hiddenMarkers.Length; column++)
                    if (column >= 0) hiddenMarkers[column] = true;
            foreach (MarkdownFootnoteReference reference in GetMarkdownFootnoteReferencesForLine(line))
            {
                if (MarkdownFootnoteMarkersAreVisible(line, reference)) continue;
                if (reference.Start >= 0 && reference.Start < hiddenMarkers.Length) hiddenMarkers[reference.Start] = true;
                if (reference.LabelStart > reference.Start && reference.LabelStart - reference.Start > 1)
                    for (int column = reference.Start + 1; column < reference.LabelStart; column++)
                        hiddenMarkers[column] = true;
                if (reference.End > 0 && reference.End - 1 < hiddenMarkers.Length)
                    hiddenMarkers[reference.End - 1] = true;
            }
            int headingClosingStart;
            int headingClosingEnd;
            if (!MarkdownHeadingMarkersAreVisible(line) &&
                TryGetMarkdownAtxClosingMarkerSpan(line, out headingClosingStart, out headingClosingEnd))
                for (int i = headingClosingStart; i < headingClosingEnd && i < hiddenMarkers.Length; i++)
                    hiddenMarkers[i] = true;
            foreach (MarkdownInlineCodeSpan span in GetMarkdownInlineCodeSpansForLine(line))
            {
                if (MarkdownInlineCodeMarkersAreVisible(line, span)) continue;
                int opening = span.ContentStart - span.DelimiterLength;
                int closing = span.ContentStart + span.ContentLength;
                for (int i = opening; i < span.ContentStart; i++)
                    if (i >= 0 && i < hiddenMarkers.Length) hiddenMarkers[i] = true;
                for (int i = closing; i < closing + span.DelimiterLength; i++)
                    if (i >= 0 && i < hiddenMarkers.Length) hiddenMarkers[i] = true;
            }
            foreach (SelectionSpan span in GetMarkdownBoldContentSpansForLine(line))
            {
                if (MarkdownBoldMarkersAreVisible(line, span)) continue;
                int opening = span.Start - 2;
                int closing = span.Start + span.Length;
                for (int i = opening; i < opening + 2 && i < hiddenMarkers.Length; i++)
                    if (i >= 0) hiddenMarkers[i] = true;
                for (int i = closing; i < closing + 2 && i < hiddenMarkers.Length; i++)
                    if (i >= 0) hiddenMarkers[i] = true;
            }
            foreach (SelectionSpan span in GetMarkdownItalicContentSpansForLine(line))
            {
                if (MarkdownItalicMarkersAreVisible(line, span)) continue;
                int opening = span.Start - 1;
                int closing = span.Start + span.Length;
                if (opening >= 0 && opening < hiddenMarkers.Length) hiddenMarkers[opening] = true;
                if (closing >= 0 && closing < hiddenMarkers.Length) hiddenMarkers[closing] = true;
            }
            foreach (SelectionSpan span in GetMarkdownStrikethroughContentSpansForLine(line))
            {
                if (MarkdownStrikethroughMarkersAreVisible(line, span)) continue;
                int opening = span.Start - 2;
                int closing = span.Start + span.Length;
                for (int i = opening; i < opening + 2 && i < hiddenMarkers.Length; i++)
                    if (i >= 0) hiddenMarkers[i] = true;
                for (int i = closing; i < closing + 2 && i < hiddenMarkers.Length; i++)
                    if (i >= 0) hiddenMarkers[i] = true;
            }
            foreach (MarkdownScriptSpan span in GetMarkdownScriptSpansForLine(line))
            {
                if (MarkdownScriptMarkersAreVisible(line, span)) continue;
                int opening = span.ContentStart - 1;
                int closing = span.ContentStart + span.ContentLength;
                if (opening >= 0 && opening < hiddenMarkers.Length) hiddenMarkers[opening] = true;
                if (closing >= 0 && closing < hiddenMarkers.Length) hiddenMarkers[closing] = true;
            }
            if (!IsMarkdownCodeFenceLine(line) && !IsMarkdownIndentedCodeLine(line))
            {
                for (int column = sourceStartColumn; column < sourceEndColumn; column++)
                    if (IsMarkdownEscapablePunctuation(line, column) &&
                        !IsMarkdownBlockMarkerVisible(line, column, column + 2))
                        hiddenMarkers[column] = true;
            }

            var text = new StringBuilder(sourceEndColumn - sourceStartColumn);
            var sourceColumns = new List<int>(sourceEndColumn - sourceStartColumn);
            var sourceBoundaries = new List<int>(sourceEndColumn - sourceStartColumn + 1) { sourceStartColumn };
            var mathFractions = new List<RenderedMathFractionLayout>();
            for (int column = sourceStartColumn; column < sourceEndColumn; column++)
            {
                if (mathBlockSourceVisible)
                {
                    sourceBoundaries[sourceBoundaries.Count - 1] = column;
                    text.Append(value[column]);
                    sourceColumns.Add(column);
                    sourceBoundaries.Add(column + 1);
                    continue;
                }
                MarkdownMathSpan activeMathSpan = default(MarkdownMathSpan);
                bool isMathContent = false;
                foreach (MarkdownMathSpan mathSpan in mathSpans)
                    if (column >= mathSpan.ContentStart &&
                        column < mathSpan.ContentStart + mathSpan.ContentLength)
                    {
                        activeMathSpan = mathSpan;
                        isMathContent = true;
                        break;
                    }
                if (isMathContent)
                {
                    int mathStart = column;
                    int mathEnd = Math.Min(sourceEndColumn,
                        activeMathSpan.ContentStart + activeMathSpan.ContentLength);
                    if (IsMarkdownMathEnvironmentControlLine(line) &&
                        MarkdownMathEnvironmentControlLineIsVisible(line))
                    {
                        for (int sourceColumn = mathStart; sourceColumn < mathEnd; sourceColumn++)
                        {
                            sourceBoundaries[sourceBoundaries.Count - 1] = sourceColumn;
                            text.Append(value[sourceColumn]);
                            sourceColumns.Add(sourceColumn);
                            sourceBoundaries.Add(sourceColumn + 1);
                        }
                        column = mathEnd - 1;
                        continue;
                    }
                    string mathValue = value.Substring(mathStart, mathEnd - mathStart);
                    List<MarkdownMathCharacter> renderedMath = RenderMarkdownMath(mathValue, mathStart);
                    foreach (MarkdownMathCharacter renderedCharacter in renderedMath)
                    {
                        sourceBoundaries[sourceBoundaries.Count - 1] = renderedCharacter.SourceStart;
                        if (renderedCharacter.IsFraction)
                        {
                            double mathFontSize = FontSizeForLine(line) *
                                (activeMathSpan.IsDisplay || activeMathSpan.IsDisplayBlockBody ? 1.08 : 1.0);
                            double fractionWidth = MeasureMarkdownMathSequenceWidth(
                                new List<MarkdownMathCharacter> { renderedCharacter }, mathFontSize);
                            double spaceWidth = Math.Max(1, MeasureMarkdownMathTextWidth("\u2002", mathFontSize));
                            int placeholderLength = Math.Max(1, (int)Math.Ceiling(fractionWidth / spaceWidth));
                            string placeholder = new string('\u2002', placeholderLength);
                            double placeholderWidth = MeasureMarkdownMathTextWidth(placeholder, mathFontSize);
                            int displayStart = text.Length;
                            for (int placeholderIndex = 0; placeholderIndex < placeholderLength; placeholderIndex++)
                            {
                                text.Append('\u2002');
                                sourceColumns.Add(renderedCharacter.SourceStart);
                                sourceBoundaries.Add(renderedCharacter.SourceStart);
                            }
                            sourceBoundaries[sourceBoundaries.Count - 1] = renderedCharacter.SourceEnd;
                            mathFractions.Add(new RenderedMathFractionLayout
                            {
                                DisplayStart = displayStart,
                                DisplayLength = placeholderLength,
                                Numerator = renderedCharacter.Numerator,
                                Denominator = renderedCharacter.Denominator,
                                Width = fractionWidth,
                                PlaceholderWidth = placeholderWidth,
                                FontSize = mathFontSize
                            });
                            continue;
                        }
                        text.Append(renderedCharacter.Value);
                        sourceColumns.Add(renderedCharacter.SourceStart);
                        sourceBoundaries.Add(renderedCharacter.SourceEnd);
                    }
                    sourceBoundaries[sourceBoundaries.Count - 1] = mathEnd;
                    column = mathEnd - 1;
                    continue;
                }
                if (hasTaskMarker && !taskMarkersVisible && column == taskMarker.Start &&
                    taskMarker.End <= sourceEndColumn)
                {
                    sourceBoundaries[sourceBoundaries.Count - 1] = column;
                    text.Append(taskMarker.IsChecked ? '\u2611' : '\u2610');
                    sourceColumns.Add(column);
                    sourceBoundaries.Add(taskMarker.End);
                    column = taskMarker.End - 1;
                    continue;
                }
                if (hiddenMarkers[column])
                {
                    sourceBoundaries[sourceBoundaries.Count - 1] = column + 1;
                    continue;
                }
                if (value[column] == '&' && !IsMarkdownCodeFenceLine(line) && !IsMarkdownIndentedCodeLine(line) &&
                    !IsMarkdownInlineCodeContentColumn(line, column) &&
                    !IsMarkdownBlockMarkerVisible(line, column, Math.Min(value.Length, column + 34)))
                {
                    int entityEnd = value.IndexOf(';', column + 1);
                    if (entityEnd > column + 1 && entityEnd - column <= 33 && entityEnd < sourceEndColumn)
                    {
                        string entity = value.Substring(column, entityEnd - column + 1);
                        string decoded = System.Net.WebUtility.HtmlDecode(entity);
                        if (!string.Equals(decoded, entity, StringComparison.Ordinal))
                        {
                            sourceBoundaries[sourceBoundaries.Count - 1] = column;
                            text.Append(decoded);
                            for (int decodedIndex = 0; decodedIndex < decoded.Length; decodedIndex++)
                            {
                                sourceColumns.Add(column);
                                sourceBoundaries.Add(decodedIndex == decoded.Length - 1 ? entityEnd + 1 : column);
                            }
                            column = entityEnd;
                            continue;
                        }
                    }
                }
                text.Append(hasListMarker && !listMarkersVisible && !listMarker.IsOrdered && column == listMarker.MarkerStart
                    ? '\u2022' : value[column]);
                sourceColumns.Add(column);
                sourceBoundaries.Add(column + 1);
            }
            return new RenderedTextSegment
            {
                Text = text.ToString(),
                SourceColumns = sourceColumns.ToArray(),
                SourceBoundaries = sourceBoundaries.ToArray(),
                MathFractions = mathFractions
            };
        }

        private bool IsMarkdownBlockMarkerVisible(int line, int markerStart, int markerEnd)
        {
            int absoluteStart = GetLineStart(line) + markerStart;
            int absoluteEnd = GetLineStart(line) + markerEnd;
            if (_caret >= absoluteStart && _caret <= absoluteEnd) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= absoluteStart && caret <= absoluteEnd) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < absoluteEnd && selection.Start + selection.Length > absoluteStart) return true;
            return false;
        }

        internal int RenderedBoundaryForSourceColumn(RenderedTextSegment segment, int sourceColumn)
        {
            int[] boundaries = segment.SourceBoundaries;
            int low = 0, high = boundaries.Length - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (boundaries[middle] < sourceColumn) low = middle + 1;
                else high = middle;
            }
            if (low > 0 && Math.Abs(boundaries[low - 1] - sourceColumn) <= Math.Abs(boundaries[low] - sourceColumn))
                return low - 1;
            return low;
        }

        internal int RenderedCharacterForSourceColumn(RenderedTextSegment segment, int sourceColumn)
        {
            for (int i = 0; i < segment.SourceColumns.Length; i++)
                if (segment.SourceColumns[i] == sourceColumn) return i;
            return -1;
        }

        internal void ApplyMarkdownBold(FormattedText formatted, int line, int sourceStartColumn, int visibleLength)
        {
            if (!EnableMarkdownBold || visibleLength <= 0) return;
            int sourceEndColumn = sourceStartColumn + visibleLength;
            foreach (SelectionSpan span in GetMarkdownBoldContentSpansForLine(line))
            {
                int start = Math.Max(span.Start, sourceStartColumn);
                int end = Math.Min(span.Start + span.Length, sourceEndColumn);
                if (end > start)
                    formatted.SetFontWeight(FontWeights.Bold, start - sourceStartColumn, end - start);
            }
        }

        internal void ApplyMarkdownBold(FormattedText formatted, int line, int[] sourceColumns)
        {
            if (!EnableMarkdownBold || sourceColumns == null || sourceColumns.Length == 0) return;
            foreach (SelectionSpan span in GetMarkdownBoldContentSpansForLine(line))
            {
                int index = 0;
                while (index < sourceColumns.Length)
                {
                    while (index < sourceColumns.Length &&
                        (sourceColumns[index] < span.Start || sourceColumns[index] >= span.Start + span.Length)) index++;
                    int start = index;
                    while (index < sourceColumns.Length &&
                        sourceColumns[index] >= span.Start && sourceColumns[index] < span.Start + span.Length) index++;
                    if (index > start) formatted.SetFontWeight(FontWeights.Bold, start, index - start);
                }
            }
        }

        internal void ApplyMarkdownItalic(FormattedText formatted, int line, int[] sourceColumns)
        {
            if (sourceColumns == null || sourceColumns.Length == 0) return;
            foreach (SelectionSpan span in GetMarkdownItalicContentSpansForLine(line))
            {
                int index = 0;
                while (index < sourceColumns.Length)
                {
                    while (index < sourceColumns.Length &&
                        (sourceColumns[index] < span.Start || sourceColumns[index] >= span.Start + span.Length)) index++;
                    int start = index;
                    while (index < sourceColumns.Length &&
                        sourceColumns[index] >= span.Start && sourceColumns[index] < span.Start + span.Length) index++;
                    if (index > start) formatted.SetFontStyle(FontStyles.Italic, start, index - start);
                }
            }
        }

        internal void ApplyMarkdownStrikethrough(FormattedText formatted, int line, int[] sourceColumns)
        {
            if (sourceColumns == null || sourceColumns.Length == 0) return;
            foreach (SelectionSpan span in GetMarkdownStrikethroughContentSpansForLine(line))
            {
                int index = 0;
                while (index < sourceColumns.Length)
                {
                    while (index < sourceColumns.Length &&
                        (sourceColumns[index] < span.Start || sourceColumns[index] >= span.Start + span.Length)) index++;
                    int start = index;
                    while (index < sourceColumns.Length &&
                        sourceColumns[index] >= span.Start && sourceColumns[index] < span.Start + span.Length) index++;
                    if (index > start)
                        formatted.SetTextDecorations(TextDecorations.Strikethrough, start, index - start);
                }
            }
        }

        internal void ApplyMarkdownScriptStyle(FormattedText formatted, int line, int[] sourceColumns)
        {
            if (MarkdownMathSourceIsVisible(line) || sourceColumns == null || sourceColumns.Length == 0) return;
            ApplyMarkdownMathStyle(formatted, line, sourceColumns);
            double scriptFontSize = Math.Max(1, FontSizeForLine(line) * 0.72);
            foreach (MarkdownScriptSpan span in GetMarkdownScriptSpansForLine(line))
            {
                int index = 0;
                while (index < sourceColumns.Length)
                {
                    while (index < sourceColumns.Length &&
                        (sourceColumns[index] < span.ContentStart || sourceColumns[index] >= span.ContentStart + span.ContentLength)) index++;
                    int start = index;
                    while (index < sourceColumns.Length &&
                        sourceColumns[index] >= span.ContentStart && sourceColumns[index] < span.ContentStart + span.ContentLength) index++;
                    if (index > start) formatted.SetFontSize(scriptFontSize, start, index - start);
                }
            }
        }

        internal static int MarkdownScriptKindForSourceColumn(IList<MarkdownScriptSpan> spans, int sourceColumn)
        {
            MarkdownScriptSpan? innermost = null;
            foreach (MarkdownScriptSpan span in spans)
                if (sourceColumn >= span.ContentStart && sourceColumn < span.ContentStart + span.ContentLength &&
                    (!innermost.HasValue || span.ContentLength < innermost.Value.ContentLength))
                    innermost = span;
            return !innermost.HasValue ? 0 : (innermost.Value.IsSuperscript ? 1 : 2);
        }

        internal void ApplyMarkdownInlineCode(FormattedText formatted, int line, int[] sourceColumns)
        {
            if (!EnableMarkdownInlineCode || sourceColumns == null || sourceColumns.Length == 0) return;
            foreach (MarkdownInlineCodeSpan span in GetMarkdownInlineCodeSpansForLine(line))
            {
                int index = 0;
                while (index < sourceColumns.Length)
                {
                    while (index < sourceColumns.Length &&
                        (sourceColumns[index] < span.ContentStart || sourceColumns[index] >= span.ContentStart + span.ContentLength)) index++;
                    int codeStart = index;
                    while (index < sourceColumns.Length &&
                        sourceColumns[index] >= span.ContentStart && sourceColumns[index] < span.ContentStart + span.ContentLength) index++;
                    if (index <= codeStart) continue;

                    formatted.SetFontFamily(MarkdownInlineCodeFontFamily, codeStart, index - codeStart);
                    formatted.SetForegroundBrush(MarkdownInlineCodeForeground, codeStart, index - codeStart);
                }
            }
        }

        internal void ApplyMarkdownCodeFenceStyle(FormattedText formatted, int line, int textLength)
        {
            if (!(IsMarkdownCodeFenceLine(line) || IsMarkdownIndentedCodeLine(line)) || textLength <= 0) return;
            formatted.SetFontFamily(MarkdownInlineCodeFontFamily, 0, textLength);
            formatted.SetForegroundBrush(MarkdownInlineCodeForeground, 0, textLength);
        }

        internal void ApplyMarkdownFenceSyntaxHighlighting(FormattedText formatted, int line, int[] sourceColumns)
        {
            MarkdownCodeFenceBlock block;
            if (sourceColumns == null || sourceColumns.Length == 0 || !IsMarkdownCodeFenceBodyLine(line) ||
                !TryGetMarkdownCodeFenceBlock(line, out block) ||
                !MarkdownSyntaxDefinition.MatchesLanguage(block.Language)) return;

            string value = _lineCache[line].TrimEnd('\r');
            foreach (SyntaxHighlightRule rule in MarkdownSyntaxDefinition.Rules)
            {
                foreach (System.Text.RegularExpressions.Match match in rule.Matcher.Matches(value))
                {
                    if (match.Length == 0) continue;
                    int index = 0;
                    while (index < sourceColumns.Length)
                    {
                        while (index < sourceColumns.Length &&
                            (sourceColumns[index] < match.Index || sourceColumns[index] >= match.Index + match.Length)) index++;
                        int start = index;
                        while (index < sourceColumns.Length &&
                            sourceColumns[index] >= match.Index && sourceColumns[index] < match.Index + match.Length) index++;
                        if (index > start) formatted.SetForegroundBrush(rule.Foreground, start, index - start);
                    }
                }
            }
        }

        internal void ApplyMarkdownListMarkerStyle(FormattedText formatted, int line, int[] sourceColumns)
        {
            MarkdownListMarker marker;
            if (sourceColumns == null || sourceColumns.Length == 0 ||
                !TryGetMarkdownListMarkerForLine(line, out marker)) return;
            int index = 0;
            while (index < sourceColumns.Length)
            {
                while (index < sourceColumns.Length &&
                    (sourceColumns[index] < marker.MarkerStart || sourceColumns[index] >= marker.MarkerEnd)) index++;
                int start = index;
                while (index < sourceColumns.Length &&
                    sourceColumns[index] >= marker.MarkerStart && sourceColumns[index] < marker.MarkerEnd) index++;
                if (index > start) formatted.SetForegroundBrush(MarkdownListMarkerForeground, start, index - start);
            }
        }

        internal double MeasureRenderedTextWidthForLine(RenderedTextSegment segment, int line, int displayLength)
        {
            displayLength = Math.Max(0, Math.Min(segment.Text.Length, displayLength));
            if (displayLength == 0) return 0;
            string value = segment.Text.Substring(0, displayLength);
            int headingLevel = MarkdownHeadingLevelForLine(line);
            var typeface = new Typeface(IsMarkdownIndentedCodeLine(line) ? MarkdownInlineCodeFontFamily : FontFamily,
                FontStyles.Normal, (headingLevel > 0 || IsMarkdownDefinitionTermLine(line))
                    ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var formatted = new FormattedText(value, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection, typeface, FontSizeForLine(line), Foreground, dpi);
            var sourceColumns = new int[displayLength];
            Array.Copy(segment.SourceColumns, sourceColumns, displayLength);
            ApplyMarkdownBold(formatted, line, sourceColumns);
            ApplyMarkdownItalic(formatted, line, sourceColumns);
            ApplyMarkdownStrikethrough(formatted, line, sourceColumns);
            ApplyMarkdownScriptStyle(formatted, line, sourceColumns);
            ApplyMarkdownInlineCode(formatted, line, sourceColumns);
            ApplyMarkdownCodeFenceStyle(formatted, line, displayLength);
            ApplyMarkdownTaskMarkerStyle(formatted, line, sourceColumns);
            ApplyMarkdownLinkStyle(formatted, line, sourceColumns);
            ApplyMarkdownFootnoteStyle(formatted, line, sourceColumns);
            ApplyMarkdownListMarkerStyle(formatted, line, sourceColumns);
            return Math.Max(0, formatted.WidthIncludingTrailingWhitespace +
                MarkdownMathFractionWidthAdjustment(segment, displayLength));
        }

        internal double MarkdownMathFractionWidthAdjustment(RenderedTextSegment segment, int displayLength)
        {
            double adjustment = 0;
            if (segment.MathFractions != null)
                foreach (RenderedMathFractionLayout fraction in segment.MathFractions)
                {
                    int fractionEnd = fraction.DisplayStart + fraction.DisplayLength;
                    if (displayLength <= fraction.DisplayStart) continue;
                    double visibleFraction = Math.Min(1, (displayLength - fraction.DisplayStart) /
                        (double)Math.Max(1, fraction.DisplayLength));
                    if (displayLength >= fractionEnd) visibleFraction = 1;
                    adjustment += (fraction.Width - fraction.PlaceholderWidth) * visibleFraction;
                }
            return adjustment;
        }

        internal int MarkdownHeadingPrefixLengthForLine(int line)
        {
            int level = MarkdownHeadingLevelForLine(line);
            if (level == 0) return 0;
            string value = _lineCache[line].TrimEnd('\r');
            int quotePrefix = MarkdownQuotePrefixLengthForLine(line);
            int position = quotePrefix;
            int indent = 0;
            while (position < value.Length && indent < 3 && value[position] == ' ')
            { position++; indent++; }
            if (position >= value.Length || value[position] != '#') return 0;
            position += level;
            while (position < value.Length && (value[position] == ' ' || value[position] == '\t')) position++;
            return position < value.Length ? position : 0;
        }

        internal bool MarkdownHeadingMarkersAreVisible(int line)
        {
            int prefixLength = MarkdownHeadingPrefixLengthForLine(line);
            int lineStart = GetLineStart(line);
            int prefixEnd = lineStart + prefixLength;
            int closingStart;
            int closingEnd;
            bool hasClosing = TryGetMarkdownAtxClosingMarkerSpan(line, out closingStart, out closingEnd);
            if (prefixLength > 0 && _caret >= lineStart && _caret <= prefixEnd) return true;
            if (hasClosing && _caret >= lineStart + closingStart && _caret <= lineStart + closingEnd) return true;
            foreach (int caret in AdditionalCaretPositions)
                if ((prefixLength > 0 && caret >= lineStart && caret <= prefixEnd) ||
                    (hasClosing && caret >= lineStart + closingStart && caret <= lineStart + closingEnd)) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if ((prefixLength > 0 && selection.Start < prefixEnd &&
                    selection.Start + selection.Length > lineStart) ||
                    (hasClosing && selection.Start < lineStart + closingEnd &&
                    selection.Start + selection.Length > lineStart + closingStart)) return true;
            return false;
        }

        internal bool TryGetMarkdownAtxClosingMarkerSpan(int line, out int start, out int end)
        {
            start = end = 0;
            if (MarkdownHeadingLevelForLine(line) == 0 || MarkdownSetextHeadingLevelForTitleLine(line) > 0)
                return false;
            string value = _lineCache[line].TrimEnd('\r');
            int contentStart = MarkdownHeadingPrefixLengthForLine(line);
            if (contentStart <= 0 || contentStart >= value.Length) return false;
            int markerEnd = value.Length;
            while (markerEnd > contentStart && char.IsWhiteSpace(value[markerEnd - 1])) markerEnd--;
            int markerStart = markerEnd;
            while (markerStart > contentStart && value[markerStart - 1] == '#') markerStart--;
            if (markerStart == markerEnd || markerStart <= contentStart ||
                (value[markerStart - 1] != ' ' && value[markerStart - 1] != '\t')) return false;
            start = markerStart;
            end = markerEnd;
            return true;
        }

        internal int RenderedStartColumnForLine(int line)
        {
            return MarkdownHeadingMarkersAreVisible(line) ? 0 : MarkdownHeadingPrefixLengthForLine(line);
        }

        internal int RenderedStartColumnForRow(int row)
        {
            int line = DocumentLineAtVisibleRow(row);
            int segmentStart = VisibleRowStartColumn(row);
            int segmentEnd = segmentStart + VisibleRowLength(row);
            int renderedStart = RenderedStartColumnForLine(line);
            return Math.Max(segmentStart, Math.Min(segmentEnd, renderedStart));
        }

        internal double FontSizeForLine(int line)
        {
            switch (MarkdownHeadingLevelForLine(line))
            {
                case 1: return FontSize * 1.8;
                case 2: return FontSize * 1.55;
                case 3: return FontSize * 1.35;
                case 4: return FontSize * 1.2;
                case 5: return FontSize * 1.1;
                default: return FontSize;
            }
        }

        private double LineHeightForDocumentLine(int line)
        {
            if (IsMarkdownSetextUnderlineLine(line) && !MarkdownSetextMarkersAreVisible(line)) return 2;
            double textHeight = MarkdownMathSourceRowHeight(line);
            if (MarkdownMathBlockPreviewIsVisible(line))
                return textHeight + MarkdownMathBlockPreviewHeight(line);
            if (MarkdownMathLineContainsFraction(line) && !MarkdownMathSourceIsVisible(line))
                textHeight = Math.Max(textHeight, Math.Ceiling(FontSizeForLine(line) * 2.7));
            MarkdownTableLayout tableLayout;
            if (TryGetMarkdownTableLayout(line, out tableLayout) && tableLayout.IsSeparator &&
                !tableLayout.IsSourceVisible) return 2;
            MarkdownMediaBlock media;
            if (TryGetMarkdownMediaBlockForLine(line, out media))
                return Math.Max(textHeight, media.IsVideo ? MarkdownVideoHeight + 8 : MarkdownImageMaxHeight + 8);
            return textHeight;
        }

        internal double MeasureTextWidthForLine(string value, int line, int sourceStartColumn = 0)
        {
            int level = MarkdownHeadingLevelForLine(line);
            MarkdownListMarker listMarker;
            bool hasListMarker = TryGetMarkdownListMarkerForLine(line, out listMarker);
            bool replaceListMarkerWithBullet = hasListMarker && !listMarker.IsOrdered &&
                !MarkdownListMarkerIsVisible(line, listMarker) &&
                listMarker.MarkerStart >= sourceStartColumn && listMarker.MarkerStart < sourceStartColumn + value.Length;
            string renderedValue = replaceListMarkerWithBullet
                ? value.Substring(0, listMarker.MarkerStart - sourceStartColumn) + "\u2022" +
                    value.Substring(listMarker.MarkerStart - sourceStartColumn + 1)
                : value;
            List<SelectionSpan> boldSpans = GetMarkdownBoldContentSpansForLine(line);
            bool containsBoldText = false;
            int sourceEndColumn = sourceStartColumn + value.Length;
            string boldSignature = string.Empty;
            foreach (SelectionSpan span in boldSpans)
            {
                int boldStart = Math.Max(span.Start, sourceStartColumn);
                int boldEnd = Math.Min(span.Start + span.Length, sourceEndColumn);
                if (boldEnd > boldStart)
                {
                    containsBoldText = true;
                    boldSignature += (boldStart - sourceStartColumn) + "," + (boldEnd - boldStart) + ";";
                }
            }
            bool containsCodeText = false;
            string codeSignature = string.Empty;
            foreach (MarkdownInlineCodeSpan span in GetMarkdownInlineCodeSpansForLine(line))
            {
                int codeStart = Math.Max(span.ContentStart, sourceStartColumn);
                int codeEnd = Math.Min(span.ContentStart + span.ContentLength, sourceEndColumn);
                if (codeEnd > codeStart)
                {
                    containsCodeText = true;
                    codeSignature += (codeStart - sourceStartColumn) + "," + (codeEnd - codeStart) + ";";
                }
            }
            bool containsItalicText = false;
            string italicSignature = string.Empty;
            foreach (SelectionSpan span in GetMarkdownItalicContentSpansForLine(line))
            {
                int italicStart = Math.Max(span.Start, sourceStartColumn);
                int italicEnd = Math.Min(span.Start + span.Length, sourceEndColumn);
                if (italicEnd > italicStart)
                {
                    containsItalicText = true;
                    italicSignature += (italicStart - sourceStartColumn) + "," + (italicEnd - italicStart) + ";";
                }
            }
            bool containsStrikethroughText = false;
            string strikethroughSignature = string.Empty;
            foreach (SelectionSpan span in GetMarkdownStrikethroughContentSpansForLine(line))
            {
                int strikeStart = Math.Max(span.Start, sourceStartColumn);
                int strikeEnd = Math.Min(span.Start + span.Length, sourceEndColumn);
                if (strikeEnd > strikeStart)
                {
                    containsStrikethroughText = true;
                    strikethroughSignature += (strikeStart - sourceStartColumn) + "," + (strikeEnd - strikeStart) + ";";
                }
            }
            bool containsScriptText = false;
            string scriptSignature = string.Empty;
            foreach (MarkdownScriptSpan span in GetMarkdownScriptSpansForLine(line))
            {
                int scriptStart = Math.Max(span.ContentStart, sourceStartColumn);
                int scriptEnd = Math.Min(span.ContentStart + span.ContentLength, sourceEndColumn);
                if (scriptEnd > scriptStart)
                {
                    containsScriptText = true;
                    scriptSignature += (span.IsSuperscript ? "^" : "~") +
                        (scriptStart - sourceStartColumn) + "," + (scriptEnd - scriptStart) + ";";
                }
            }
            if (level == 0 && !containsBoldText && !containsItalicText && !containsStrikethroughText && !containsScriptText &&
                !containsCodeText && !replaceListMarkerWithBullet)
                return MeasureTextWidth(value);
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (Math.Abs(_metricDpi - dpi) > 0.001)
            {
                _textWidthCache.Clear();
                _metricDpi = dpi;
            }
            string cacheKey = "\u0001styled" + level + ":" + sourceStartColumn + ":" + boldSignature + ":" +
                italicSignature + ":" + strikethroughSignature + ":" + scriptSignature + ":" + codeSignature + ":" + renderedValue;
            double cachedWidth;
            if (_textWidthCache.TryGetValue(cacheKey, out cachedWidth)) return cachedWidth;
            var typeface = new Typeface(IsMarkdownIndentedCodeLine(line) ? MarkdownInlineCodeFontFamily : FontFamily,
                FontStyles.Normal, (level > 0 || IsMarkdownDefinitionTermLine(line))
                    ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            var formatted = new FormattedText(renderedValue, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection, typeface, FontSizeForLine(line), Foreground, dpi);
            ApplyMarkdownBold(formatted, line, sourceStartColumn, value.Length);
            var sourceColumns = new int[value.Length];
            for (int i = 0; i < sourceColumns.Length; i++) sourceColumns[i] = sourceStartColumn + i;
            ApplyMarkdownItalic(formatted, line, sourceColumns);
            ApplyMarkdownStrikethrough(formatted, line, sourceColumns);
            ApplyMarkdownScriptStyle(formatted, line, sourceColumns);
            ApplyMarkdownInlineCode(formatted, line, sourceColumns);
            ApplyMarkdownCodeFenceStyle(formatted, line, value.Length);
            ApplyMarkdownTaskMarkerStyle(formatted, line, sourceColumns);
            ApplyMarkdownLinkStyle(formatted, line, sourceColumns);
            ApplyMarkdownFootnoteStyle(formatted, line, sourceColumns);
            ApplyMarkdownListMarkerStyle(formatted, line, sourceColumns);
            double width = formatted.WidthIncludingTrailingWhitespace;
            if (_textWidthCache.Count >= 4096) _textWidthCache.Clear();
            _textWidthCache[cacheKey] = width;
            return width;
        }

        internal int VisibleRowAtY(double contentY)
        {
            double rowY = Math.Max(0, contentY - TopInset);
            int row = Array.BinarySearch(_visibleRowTops, rowY);
            if (row < 0) row = ~row - 1;
            return Math.Max(0, Math.Min(VisibleLineCount - 1, row));
        }

        internal double MeasureTextWidth(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (Math.Abs(_metricDpi - dpi) > 0.001)
            {
                _textWidthCache.Clear();
                _metricDpi = dpi;
            }
            double cachedWidth;
            if (_textWidthCache.TryGetValue(value, out cachedWidth)) return cachedWidth;
            var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var formatted = new FormattedText(value, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection, typeface, FontSize, Foreground, dpi);
            double width = formatted.WidthIncludingTrailingWhitespace;
            if (_textWidthCache.Count >= 4096) _textWidthCache.Clear();
            _textWidthCache[value] = width;
            return width;
        }

        internal string[] Lines { get { return _lineCache; } }
        internal int LineCount { get { return _lineStarts.Count; } }
        internal int FirstVisibleLine { get { return VisibleRowAtY(_scroll.VerticalOffset); } }
        internal int VisibleLineEnd
        {
            get
            {
                double height = Math.Max(LineHeight, _scroll.ViewportHeight);
                return Math.Min(VisibleLineCount, VisibleRowAtY(_scroll.VerticalOffset + height) + 1);
            }
        }
        internal int VisibleLineCount { get { return _visibleLines.Length; } }
        internal int DocumentLineAtVisibleRow(int row) { return _visibleLines[Math.Max(0, Math.Min(_visibleLines.Length - 1, row))]; }
        internal int VisibleRowStartColumn(int row) { return _visibleRowStartColumns[Math.Max(0, Math.Min(_visibleRowStartColumns.Length - 1, row))]; }
        internal int VisibleRowLength(int row) { return _visibleRowLengths[Math.Max(0, Math.Min(_visibleRowLengths.Length - 1, row))]; }
        internal int VisibleRowForDocumentLine(int line) { return _documentToVisibleRow[Math.Max(0, Math.Min(_documentToVisibleRow.Length - 1, line))]; }
        internal int GetLineEndAtLine(int lineIndex)
        {
            int start = GetLineStart(lineIndex);
            int end = lineIndex + 1 < LineCount ? GetLineStart(lineIndex + 1) - 1 : Text.Length;
            if (end > start && Text[end - 1] == '\r') end--;
            return end;
        }
        internal int LineIndexAt(int position)
        {
            int bounded = Math.Max(0, Math.Min(Text.Length, position));
            int found = _lineStarts.BinarySearch(bounded);
            return found >= 0 ? found : Math.Max(0, ~found - 1);
        }
        internal int GetLineStart(int lineIndex) { return _lineStarts[Math.Max(0, Math.Min(_lineStarts.Count - 1, lineIndex))]; }

        private void RebuildLineIndex()
        {
            _lineStarts.Clear();
            _lineStarts.Add(0);
            string text = Text ?? string.Empty;
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '\n') _lineStarts.Add(i + 1);
            _lineCache = text.Split('\n');
            RebuildMarkdownCodeFenceBlocks();
            RebuildMarkdownIndentedCodeLines();
            RebuildMarkdownMathBlocks();
            RebuildMarkdownFootnoteDefinitionCache();
            RebuildMarkdownListMarkerCache();
            RebuildMarkdownTableCache();
            RebuildVisibleLineMap();
            RebuildMarkdownVideoPlayers();
        }

        private void RebuildMarkdownListMarkerCache()
        {
            _markdownListMarkersByLine = new MarkdownListMarker[_lineCache.Length];
            _markdownListMarkersParsed = new bool[_lineCache.Length];
            if (!EnableMarkdownLists) return;
            for (int line = 0; line < _lineCache.Length; line++)
            {
                MarkdownListMarker marker;
                TryGetMarkdownListMarkerForLine(line, out marker);
            }
        }

        private void RebuildVisibleLineMap()
        {
            var visible = new List<int>();
            var starts = new List<int>();
            var lengths = new List<int>();
            _documentToVisibleRow = new int[_lineCache.Length];
            _markdownMathEnvironmentControlLineCollapsed = new bool[_lineCache.Length];
            _markdownMathPreviewVisibleByLine = new bool[_lineCache.Length];
            for (int line = 0; line < _lineCache.Length; line++)
            {
                _documentToVisibleRow[line] = visible.Count;
                _markdownMathPreviewVisibleByLine[line] = MarkdownMathBlockPreviewIsVisible(line);
                AddVisualRowsForLine(line, visible, starts, lengths);
            }
            _visibleLines = visible.ToArray();
            _visibleRowStartColumns = starts.ToArray();
            _visibleRowLengths = lengths.ToArray();
            if (_visibleLines.Length == 0)
            {
                _visibleLines = new[] { 0 };
                _visibleRowStartColumns = new[] { 0 };
                _visibleRowLengths = new[] { 0 };
            }
            _visibleRowTops = new double[_visibleLines.Length];
            _totalVisualHeight = 0;
            for (int row = 0; row < _visibleLines.Length; row++)
            {
                _visibleRowTops[row] = _totalVisualHeight;
                _totalVisualHeight += LineHeightForDocumentLine(_visibleLines[row]);
            }
            UpdateMarkdownVideoPlayerLayout();
        }

        private void AddVisualRowsForLine(int line, List<int> visible, List<int> starts, List<int> lengths)
        {
            string value = _lineCache[line].TrimEnd('\r');
            if (IsMarkdownMathEnvironmentControlLine(line) &&
                !MarkdownMathBlockMarkersAreVisible(line))
            {
                _markdownMathEnvironmentControlLineCollapsed[line] = true;
                return;
            }
            MarkdownMathBlock mathBlock;
            if (TryGetMarkdownMathBlock(line, out mathBlock))
            {
                visible.Add(line);
                starts.Add(0);
                lengths.Add(value.Length);
                return;
            }
            if (IsMarkdownSetextUnderlineLine(line) && !MarkdownSetextMarkersAreVisible(line))
            {
                visible.Add(line);
                starts.Add(0);
                lengths.Add(0);
                return;
            }
            if (IsMarkdownCodeFenceDelimiterLine(line) && !MarkdownCodeFenceMarkersAreVisible(line))
            {
                visible.Add(line);
                starts.Add(0);
                lengths.Add(value.Length);
                return;
            }
            if (IsMarkdownHorizontalRuleLine(line))
            {
                visible.Add(line);
                starts.Add(0);
                lengths.Add(value.Length);
                return;
            }
            MarkdownMediaBlock mediaBlock;
            if (TryGetMarkdownMediaBlockForLine(line, out mediaBlock))
            {
                visible.Add(line);
                starts.Add(0);
                lengths.Add(value.Length);
                return;
            }
            MarkdownTableLayout tableLayout;
            if (TryGetMarkdownTableLayout(line, out tableLayout))
            {
                visible.Add(line);
                starts.Add(0);
                lengths.Add(value.Length);
                return;
            }
            if (MarkdownMathSourceIsVisible(line) || !IsWordWrapEnabled ||
                double.IsInfinity(_wrapTextWidth) || value.Length == 0)
            {
                visible.Add(line);
                starts.Add(0);
                lengths.Add(value.Length);
                return;
            }

            double lineWrapWidth = Math.Max(CharacterWidth,
                _wrapTextWidth - Math.Max(0, TextOriginForLine(line) - TextOrigin));
            int column = 0;
            while (column < value.Length)
            {
                int low = column, high = value.Length;
                while (low < high)
                {
                    int middle = (low + high + 1) / 2;
                    if (MeasureTextWidthForLine(value.Substring(column, middle - column), line, column) <= lineWrapWidth) low = middle;
                    else high = middle - 1;
                }
                int end = low;
                if (end == column) end++;
                if (end < value.Length)
                {
                    int wordBreak = value.LastIndexOf(' ', end - 1, end - column);
                    if (wordBreak >= column) end = wordBreak + 1;
                }
                visible.Add(line);
                starts.Add(column);
                lengths.Add(end - column);
                column = end;
            }
        }

        internal bool RefreshMarkdownMathEnvironmentVisibility()
        {
            if (_markdownMathEnvironmentControlLineCollapsed.Length != _lineCache.Length ||
                _markdownMathPreviewVisibleByLine.Length != _lineCache.Length)
            {
                RebuildVisibleLineMap();
                return true;
            }
            for (int line = 0; line < _lineCache.Length; line++)
            {
                bool collapsed = IsMarkdownMathEnvironmentControlLine(line) &&
                    !MarkdownMathBlockMarkersAreVisible(line);
                bool previewVisible = MarkdownMathBlockPreviewIsVisible(line);
                if (_markdownMathEnvironmentControlLineCollapsed[line] == collapsed &&
                    _markdownMathPreviewVisibleByLine[line] == previewVisible) continue;
                RebuildVisibleLineMap();
                return true;
            }
            return false;
        }

        internal int VisibleRowForPosition(int position)
        {
            int line = LineIndexAt(position);
            int row = VisibleRowForDocumentLine(line);
            if (DocumentLineAtVisibleRow(row) != line) return row;
            int column = Math.Max(0, position - GetLineStart(line));
            while (row + 1 < VisibleLineCount && DocumentLineAtVisibleRow(row + 1) == line &&
                column >= VisibleRowStartColumn(row) + VisibleRowLength(row))
                row++;
            return row;
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = (MarkifyTextBox)d;
            editor._taskCheckboxDisplaySuppressedAt = -1;
            editor._secondaryCarets.Clear();
            editor._secondarySelections.Clear();
            editor._isRectangularSelection = false;
            editor.RebuildLineIndex();
            editor.UpdateWrapWidth();
            editor._caret = Math.Min(editor._caret, editor.Text.Length);
            editor._selectionAnchor = Math.Min(editor._selectionAnchor, editor.Text.Length);
            editor.InvalidateEditor();
            editor.PublishSelectionState();
        }

        private static void OnVisualOptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = (MarkifyTextBox)d;
            if (e.Property == FontFamilyProperty || e.Property == FontSizeProperty ||
                e.Property == EnableMarkdownHeadingsProperty || e.Property == EnableMarkdownBoldProperty ||
                e.Property == EnableMarkdownItalicProperty || e.Property == EnableMarkdownSuperSubscriptProperty ||
                e.Property == EnableMarkdownInlineCodeProperty || e.Property == EnableMarkdownCodeBlocksProperty ||
                e.Property == EnableMarkdownListsProperty || e.Property == EnableMarkdownHorizontalRulesProperty ||
                e.Property == EnableMarkdownMathProperty)
            {
                if (e.Property == EnableMarkdownCodeBlocksProperty)
                {
                    editor.RebuildMarkdownCodeFenceBlocks();
                    editor.RebuildMarkdownIndentedCodeLines();
                    editor.RebuildMarkdownMathBlocks();
                    editor.RebuildMarkdownFootnoteDefinitionCache();
                    editor.RebuildMarkdownTableCache();
                }
                if (e.Property == EnableMarkdownMathProperty)
                    editor.RebuildMarkdownMathBlocks();
                if (e.Property == EnableMarkdownCodeBlocksProperty || e.Property == EnableMarkdownListsProperty)
                    editor.RebuildMarkdownListMarkerCache();
                editor._textWidthCache.Clear();
                editor.UpdateWrapWidth();
                editor.RebuildVisibleLineMap();
            }
            editor.InvalidateEditor();
        }

        private static void OnBorderVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = (MarkifyTextBox)d;
            if (editor._border != null)
                editor._border.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void OnFeatureOptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = (MarkifyTextBox)d;
            if (e.Property == EnableMultipleCaretsProperty && !editor.EnableMultipleCarets)
            {
                editor._secondaryCarets.Clear();
                editor._secondarySelections.Clear();
            }
            if (e.Property == EnableRectangularSelectionProperty && !editor.EnableRectangularSelection)
            {
                editor._isRectangularSelection = false;
                editor._selectionAnchor = editor._caret;
            }
            if (e.Property == EnableEditorContextMenuProperty || e.Property == EnableLineOperationsProperty)
                editor.InitializeAdvancedUi();
            editor.InvalidateEditor();
            editor.PublishSelectionState();
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == Panel.BackgroundProperty) InvalidateEditor();
            if (e.Property == FlowDirectionProperty) _textWidthCache.Clear();
        }

        private void InvalidateEditor()
        {
            if (_surface == null) return;
            _surface.InvalidateMeasure();
            _surface.InvalidateVisual();
        }

        private void OnEditorViewportSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateWrapWidth();
        }

        private void UpdateWrapWidth()
        {
            if (!IsWordWrapEnabled || _scroll == null) return;
            double width = Math.Max(CharacterWidth, _scroll.ViewportWidth - TextOrigin - 8);
            if (Math.Abs(width - _wrapTextWidth) < 1) return;
            _wrapTextWidth = width;
            RebuildVisibleLineMap();
            InvalidateEditor();
        }

        private static void OnWordWrapChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = (MarkifyTextBox)d;
            editor.UpdateHorizontalScrollBar();
            if (!(bool)e.NewValue) editor._wrapTextWidth = double.PositiveInfinity;
            else editor.UpdateWrapWidth();
            editor.RebuildVisibleLineMap();
            editor.InvalidateEditor();
        }

        private static void OnHorizontalScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((MarkifyTextBox)d).UpdateHorizontalScrollBar();
        }

        private void UpdateHorizontalScrollBar()
        {
            if (_scroll == null) return;
            _scroll.HorizontalScrollBarVisibility = IsWordWrapEnabled || !IsHorizontalScrollEnabled
                ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        }

        private void AttachHorizontalWheelHook(object sender, RoutedEventArgs e)
        {
            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source == null || ReferenceEquals(source, _windowSource)) return;
            DetachHorizontalWheelHook(this, e);
            _windowSource = source;
            _windowSource.AddHook(HandleHorizontalWheelMessage);
        }

        private void DetachHorizontalWheelHook(object sender, RoutedEventArgs e)
        {
            if (_windowSource == null) return;
            _windowSource.RemoveHook(HandleHorizontalWheelMessage);
            _windowSource = null;
        }

        private IntPtr HandleHorizontalWheelMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int wmMouseHWheel = 0x020E;
            if (message != wmMouseHWheel || !IsHorizontalScrollEnabled || IsWordWrapEnabled || !IsMouseOver)
                return IntPtr.Zero;

            int delta = (short)((wParam.ToInt64() >> 16) & 0xffff);
            if (delta == 0) return IntPtr.Zero;

            _scroll.ScrollToHorizontalOffset(_scroll.HorizontalOffset + delta);
            handled = true;
            return IntPtr.Zero;
        }

        private void OnSyntaxHighlightingChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_surface != null) _surface.InvalidateVisual();
        }

        private struct EditSnapshot
        {
            public string Text;
            public int Caret;
            public int Anchor;
            public List<int> SecondaryCarets;
            public List<SelectionSpan> SecondarySelections;
            public bool IsRectangularSelection;
            public int RectangleAnchorLine;
            public int RectangleAnchorColumn;
            public int RectangleEndLine;
            public int RectangleEndColumn;

            public EditSnapshot(string text, int caret, int anchor,
                List<int> secondaryCarets, List<SelectionSpan> secondarySelections,
                bool isRectangularSelection, int rectangleAnchorLine, int rectangleAnchorColumn,
                int rectangleEndLine, int rectangleEndColumn)
            {
                Text = text; Caret = caret; Anchor = anchor;
                SecondaryCarets = secondaryCarets; SecondarySelections = secondarySelections;
                IsRectangularSelection = isRectangularSelection;
                RectangleAnchorLine = rectangleAnchorLine; RectangleAnchorColumn = rectangleAnchorColumn;
                RectangleEndLine = rectangleEndLine; RectangleEndColumn = rectangleEndColumn;
            }
        }
    }
}
