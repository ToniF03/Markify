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

            public MarkdownCodeFenceBlock(int openingLine, int closingLine, int delimiterLength)
            {
                OpeningLine = openingLine;
                ClosingLine = closingLine;
                DelimiterLength = delimiterLength;
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
        private List<MarkdownCodeFenceBlock> _markdownCodeFenceBlocks = new List<MarkdownCodeFenceBlock>();
        private int[] _markdownCodeFenceByLine = new[] { -1 };
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
        private int _selectionAnchor;
        private int _preferredColumn = -1;
        private bool _caretVisible = true;
        private const double LeftInset = 10;
        private const double TopInset = 8;
        private static readonly FontFamily MarkdownInlineCodeFontFamily = new FontFamily("Consolas");
        private static readonly Brush MarkdownInlineCodeBackground = CreateFrozenBrush(Color.FromRgb(238, 240, 243));
        private static readonly Brush MarkdownInlineCodeForeground = CreateFrozenBrush(Color.FromRgb(91, 49, 47));
        private static readonly Brush MarkdownListMarkerForeground = CreateFrozenBrush(Color.FromRgb(76, 98, 122));

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
            _scroll = new ScrollViewer { Content = _surface, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false,
                Background = Background, Focusable = false };
            _scroll.ScrollChanged += (s, e) => _surface.InvalidateVisual();
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
            Unloaded += DetachHorizontalWheelHook;
            GotKeyboardFocus += (s, e) => { _caretVisible = true; _surface.InvalidateVisual(); };
            LostKeyboardFocus += (s, e) => { _surface.InvalidateVisual(); };
            _surface.MouseDown += HandleMouseDown;
            _surface.MouseMove += HandleMouseMove;
            _surface.MouseUp += (s, e) => _surface.ReleaseMouseCapture();
            _surface.MouseLeave += (s, e) => _surface.Cursor = Cursors.IBeam;
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
        internal double TextOriginForLine(int line) { return TextOrigin + MarkdownListIndentForLine(line); }
        internal bool HasFocus { get { return IsKeyboardFocusWithin; } }
        internal bool CaretVisible { get { return _caretVisible; } }

        internal int MarkdownHeadingLevelForLine(int line)
        {
            if (!EnableMarkdownHeadings || IsMarkdownCodeFenceLine(line) || line < 0 || line >= _lineCache.Length) return 0;
            string value = _lineCache[line].TrimEnd('\r');
            int position = 0;
            while (position < value.Length && position < 3 && value[position] == ' ') position++;
            int level = 0;
            while (position + level < value.Length && value[position + level] == '#') level++;
            if (level < 1 || level > 6) return 0;
            int afterMarkers = position + level;
            if (afterMarkers < value.Length && value[afterMarkers] != ' ' && value[afterMarkers] != '\t') return 0;
            return level;
        }

        internal bool IsMarkdownHorizontalRuleLine(int line)
        {
            if (!EnableMarkdownHorizontalRules || IsMarkdownCodeFenceLine(line) || line < 0 || line >= _lineCache.Length) return false;
            string value = _lineCache[line].TrimEnd('\r');
            int position = 0;
            while (position < value.Length && value[position] == ' ') position++;
            if (position > 3) return false;

            int hyphenCount = 0;
            for (; position < value.Length; position++)
            {
                char character = value[position];
                if (character == '-') hyphenCount++;
                else if (character != ' ' && character != '\t') return false;
            }
            return hyphenCount >= 3;
        }

        internal bool TryGetMarkdownListMarkerForLine(int line, out MarkdownListMarker marker)
        {
            marker = default(MarkdownListMarker);
            if (!EnableMarkdownLists || IsMarkdownCodeFenceLine(line) || line < 0 || line >= _lineCache.Length)
                return false;

            string value = _lineCache[line].TrimEnd('\r');
            int markerStart = 0;
            while (markerStart < value.Length && value[markerStart] == ' ') markerStart++;
            if (markerStart > 3 || markerStart >= value.Length) return false;

            bool isOrdered = false;
            int markerEnd;
            char first = value[markerStart];
            if (first == '-' || first == '+' || first == '*')
            {
                markerEnd = markerStart + 1;
            }
            else if (first >= '0' && first <= '9')
            {
                int digitCount = 0;
                while (markerStart + digitCount < value.Length &&
                    value[markerStart + digitCount] >= '0' && value[markerStart + digitCount] <= '9') digitCount++;
                if (digitCount > 9 || markerStart + digitCount >= value.Length ||
                    (value[markerStart + digitCount] != '.' && value[markerStart + digitCount] != ')')) return false;
                markerEnd = markerStart + digitCount + 1;
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
                if (!TryParseMarkdownCodeFenceOpening(_lineCache[line].TrimEnd('\r'), out delimiterLength))
                {
                    line++;
                    continue;
                }

                int closingLine = -1;
                for (int candidate = line + 1; candidate < _lineCache.Length; candidate++)
                {
                    if (TryParseMarkdownCodeFenceClosing(_lineCache[candidate].TrimEnd('\r'), delimiterLength))
                    {
                        closingLine = candidate;
                        break;
                    }
                }

                int blockIndex = _markdownCodeFenceBlocks.Count;
                _markdownCodeFenceBlocks.Add(new MarkdownCodeFenceBlock(line, closingLine, delimiterLength));
                int lastLine = closingLine >= 0 ? closingLine : _lineCache.Length - 1;
                for (int blockLine = line; blockLine <= lastLine; blockLine++)
                    _markdownCodeFenceByLine[blockLine] = blockIndex;
                line = lastLine + 1;
            }
        }

        private static bool TryParseMarkdownCodeFenceOpening(string value, out int delimiterLength)
        {
            delimiterLength = 0;
            int indent = 0;
            while (indent < value.Length && indent <= 3 && value[indent] == ' ') indent++;
            if (indent > 3 || indent >= value.Length || value[indent] != '`') return false;
            delimiterLength = BacktickRunLength(value, indent);
            if (delimiterLength < 3) return false;
            for (int i = indent + delimiterLength; i < value.Length; i++)
                if (value[i] == '`') return false;
            return true;
        }

        private static bool TryParseMarkdownCodeFenceClosing(string value, int minimumDelimiterLength)
        {
            int indent = 0;
            while (indent < value.Length && indent <= 3 && value[indent] == ' ') indent++;
            if (indent > 3 || indent >= value.Length || value[indent] != '`') return false;
            int delimiterLength = BacktickRunLength(value, indent);
            if (delimiterLength < minimumDelimiterLength) return false;
            for (int i = indent + delimiterLength; i < value.Length; i++)
                if (value[i] != ' ' && value[i] != '\t') return false;
            return true;
        }

        internal List<SelectionSpan> GetMarkdownBoldContentSpansForLine(int line)
        {
            var spans = new List<SelectionSpan>();
            if (!EnableMarkdownBold || IsMarkdownCodeFenceLine(line) || line < 0 || line >= _lineCache.Length) return spans;

            string value = _lineCache[line].TrimEnd('\r');
            int searchFrom = 0;
            while (searchFrom < value.Length)
            {
                int opening = value.IndexOf("**", searchFrom, StringComparison.Ordinal);
                if (opening < 0) break;
                int closing = value.IndexOf("**", opening + 2, StringComparison.Ordinal);
                if (closing < 0) break;
                if (IsMarkdownInlineCodeContentColumn(line, opening))
                {
                    searchFrom = closing + 2;
                    continue;
                }
                if (closing > opening + 2)
                    spans.Add(new SelectionSpan(opening + 2, closing - opening - 2));
                searchFrom = closing + 2;
            }
            return spans;
        }

        internal List<SelectionSpan> GetMarkdownItalicContentSpansForLine(int line)
        {
            var spans = new List<SelectionSpan>();
            if (!EnableMarkdownItalic || IsMarkdownCodeFenceLine(line) || line < 0 || line >= _lineCache.Length) return spans;

            string value = _lineCache[line].TrimEnd('\r');
            int searchFrom = 0;
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

        internal List<MarkdownScriptSpan> GetMarkdownScriptSpansForLine(int line)
        {
            var spans = new List<MarkdownScriptSpan>();
            if (!EnableMarkdownSuperSubscript || IsMarkdownCodeFenceLine(line) || line < 0 || line >= _lineCache.Length)
                return spans;

            string value = _lineCache[line].TrimEnd('\r');
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
                            !IsMarkdownInlineCodeContentColumn(line, closing);
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
            if (!EnableMarkdownInlineCode || IsMarkdownCodeFenceLine(line) || line < 0 || line >= _lineCache.Length) return spans;

            string value = _lineCache[line].TrimEnd('\r');
            int searchFrom = 0;
            while (searchFrom < value.Length)
            {
                int opening = value.IndexOf('`', searchFrom);
                if (opening < 0) break;
                int delimiterLength = BacktickRunLength(value, opening);
                int closing = -1;
                int closingSearch = opening + delimiterLength;
                while (closingSearch < value.Length)
                {
                    int candidate = value.IndexOf('`', closingSearch);
                    if (candidate < 0) break;
                    int candidateLength = BacktickRunLength(value, candidate);
                    if (candidateLength == delimiterLength)
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

        internal RenderedTextSegment CreateRenderedTextSegment(int line, int sourceStartColumn, int sourceEndColumn)
        {
            string value = _lineCache[Math.Max(0, Math.Min(_lineCache.Length - 1, line))].TrimEnd('\r');
            sourceStartColumn = Math.Max(0, Math.Min(value.Length, sourceStartColumn));
            sourceEndColumn = Math.Max(sourceStartColumn, Math.Min(value.Length, sourceEndColumn));
            var hiddenMarkers = new bool[value.Length];
            MarkdownListMarker listMarker;
            bool hasListMarker = TryGetMarkdownListMarkerForLine(line, out listMarker);
            bool listMarkersVisible = hasListMarker && MarkdownListMarkerIsVisible(line, listMarker);
            if (IsMarkdownCodeFenceDelimiterLine(line) && !MarkdownCodeFenceMarkersAreVisible(line))
            {
                for (int i = 0; i < hiddenMarkers.Length; i++) hiddenMarkers[i] = true;
            }
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
            foreach (MarkdownScriptSpan span in GetMarkdownScriptSpansForLine(line))
            {
                if (MarkdownScriptMarkersAreVisible(line, span)) continue;
                int opening = span.ContentStart - 1;
                int closing = span.ContentStart + span.ContentLength;
                if (opening >= 0 && opening < hiddenMarkers.Length) hiddenMarkers[opening] = true;
                if (closing >= 0 && closing < hiddenMarkers.Length) hiddenMarkers[closing] = true;
            }

            var text = new StringBuilder(sourceEndColumn - sourceStartColumn);
            var sourceColumns = new List<int>(sourceEndColumn - sourceStartColumn);
            var sourceBoundaries = new List<int>(sourceEndColumn - sourceStartColumn + 1) { sourceStartColumn };
            for (int column = sourceStartColumn; column < sourceEndColumn; column++)
            {
                if (hiddenMarkers[column])
                {
                    sourceBoundaries[sourceBoundaries.Count - 1] = column + 1;
                    continue;
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
                SourceBoundaries = sourceBoundaries.ToArray()
            };
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

        internal void ApplyMarkdownScriptStyle(FormattedText formatted, int line, int[] sourceColumns)
        {
            if (sourceColumns == null || sourceColumns.Length == 0) return;
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
            if (!IsMarkdownCodeFenceLine(line) || textLength <= 0) return;
            formatted.SetFontFamily(MarkdownInlineCodeFontFamily, 0, textLength);
            formatted.SetForegroundBrush(MarkdownInlineCodeForeground, 0, textLength);
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
            var typeface = new Typeface(FontFamily, FontStyles.Normal,
                headingLevel > 0 ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var formatted = new FormattedText(value, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection, typeface, FontSizeForLine(line), Foreground, dpi);
            var sourceColumns = new int[displayLength];
            Array.Copy(segment.SourceColumns, sourceColumns, displayLength);
            ApplyMarkdownBold(formatted, line, sourceColumns);
            ApplyMarkdownItalic(formatted, line, sourceColumns);
            ApplyMarkdownScriptStyle(formatted, line, sourceColumns);
            ApplyMarkdownInlineCode(formatted, line, sourceColumns);
            ApplyMarkdownCodeFenceStyle(formatted, line, displayLength);
            ApplyMarkdownListMarkerStyle(formatted, line, sourceColumns);
            return formatted.WidthIncludingTrailingWhitespace;
        }

        internal int MarkdownHeadingPrefixLengthForLine(int line)
        {
            int level = MarkdownHeadingLevelForLine(line);
            if (level == 0) return 0;
            string value = _lineCache[line].TrimEnd('\r');
            int position = 0;
            while (position < value.Length && position < 3 && value[position] == ' ') position++;
            position += level;
            while (position < value.Length && (value[position] == ' ' || value[position] == '\t')) position++;
            return position < value.Length ? position : 0;
        }

        internal bool MarkdownHeadingMarkersAreVisible(int line)
        {
            int prefixLength = MarkdownHeadingPrefixLengthForLine(line);
            if (prefixLength <= 0) return false;
            int lineStart = GetLineStart(line);
            int prefixEnd = lineStart + prefixLength;
            if (_caret >= lineStart && _caret <= prefixEnd) return true;
            foreach (int caret in AdditionalCaretPositions)
                if (caret >= lineStart && caret <= prefixEnd) return true;
            foreach (SelectionSpan selection in GetSelectionSpansForLine(line))
                if (selection.Start < prefixEnd && selection.Start + selection.Length > lineStart) return true;
            return false;
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
            return Math.Ceiling(FontSizeForLine(line) * 1.45);
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
            if (level == 0 && !containsBoldText && !containsItalicText && !containsScriptText &&
                !containsCodeText && !replaceListMarkerWithBullet)
                return MeasureTextWidth(value);
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (Math.Abs(_metricDpi - dpi) > 0.001)
            {
                _textWidthCache.Clear();
                _metricDpi = dpi;
            }
            string cacheKey = "\u0001styled" + level + ":" + sourceStartColumn + ":" + boldSignature + ":" +
                italicSignature + ":" + scriptSignature + ":" + codeSignature + ":" + renderedValue;
            double cachedWidth;
            if (_textWidthCache.TryGetValue(cacheKey, out cachedWidth)) return cachedWidth;
            var typeface = new Typeface(FontFamily, FontStyles.Normal,
                level > 0 ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            var formatted = new FormattedText(renderedValue, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection, typeface, FontSizeForLine(line), Foreground, dpi);
            ApplyMarkdownBold(formatted, line, sourceStartColumn, value.Length);
            var sourceColumns = new int[value.Length];
            for (int i = 0; i < sourceColumns.Length; i++) sourceColumns[i] = sourceStartColumn + i;
            ApplyMarkdownItalic(formatted, line, sourceColumns);
            ApplyMarkdownScriptStyle(formatted, line, sourceColumns);
            ApplyMarkdownInlineCode(formatted, line, sourceColumns);
            ApplyMarkdownCodeFenceStyle(formatted, line, value.Length);
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
            RebuildVisibleLineMap();
        }

        private void RebuildVisibleLineMap()
        {
            var visible = new List<int>();
            var starts = new List<int>();
            var lengths = new List<int>();
            _documentToVisibleRow = new int[_lineCache.Length];
            for (int line = 0; line < _lineCache.Length; line++)
            {
                _documentToVisibleRow[line] = visible.Count;
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
        }

        private void AddVisualRowsForLine(int line, List<int> visible, List<int> starts, List<int> lengths)
        {
            string value = _lineCache[line].TrimEnd('\r');
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
            if (!IsWordWrapEnabled || double.IsInfinity(_wrapTextWidth) || value.Length == 0)
            {
                visible.Add(line);
                starts.Add(0);
                lengths.Add(value.Length);
                return;
            }

            double lineWrapWidth = Math.Max(CharacterWidth, _wrapTextWidth - MarkdownListIndentForLine(line));
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
                e.Property == EnableMarkdownListsProperty || e.Property == EnableMarkdownHorizontalRulesProperty)
            {
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
