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
        internal bool HasFocus { get { return IsKeyboardFocusWithin; } }
        internal bool CaretVisible { get { return _caretVisible; } }

        internal int MarkdownHeadingLevelForLine(int line)
        {
            if (!EnableMarkdownHeadings || line < 0 || line >= _lineCache.Length) return 0;
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
            if (!EnableMarkdownHorizontalRules || line < 0 || line >= _lineCache.Length) return false;
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

        internal List<SelectionSpan> GetMarkdownBoldContentSpansForLine(int line)
        {
            var spans = new List<SelectionSpan>();
            if (!EnableMarkdownBold || line < 0 || line >= _lineCache.Length) return spans;

            string value = _lineCache[line].TrimEnd('\r');
            int searchFrom = 0;
            while (searchFrom < value.Length)
            {
                int opening = value.IndexOf("**", searchFrom, StringComparison.Ordinal);
                if (opening < 0) break;
                int closing = value.IndexOf("**", opening + 2, StringComparison.Ordinal);
                if (closing < 0) break;
                if (closing > opening + 2)
                    spans.Add(new SelectionSpan(opening + 2, closing - opening - 2));
                searchFrom = closing + 2;
            }
            return spans;
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

        internal RenderedTextSegment CreateRenderedTextSegment(int line, int sourceStartColumn, int sourceEndColumn)
        {
            string value = _lineCache[Math.Max(0, Math.Min(_lineCache.Length - 1, line))].TrimEnd('\r');
            sourceStartColumn = Math.Max(0, Math.Min(value.Length, sourceStartColumn));
            sourceEndColumn = Math.Max(sourceStartColumn, Math.Min(value.Length, sourceEndColumn));
            var hiddenMarkers = new bool[value.Length];
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
                text.Append(value[column]);
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
            if (level == 0 && !containsBoldText) return MeasureTextWidth(value);
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (Math.Abs(_metricDpi - dpi) > 0.001)
            {
                _textWidthCache.Clear();
                _metricDpi = dpi;
            }
            string cacheKey = "\u0001styled" + level + ":" + sourceStartColumn + ":" + boldSignature + ":" + value;
            double cachedWidth;
            if (_textWidthCache.TryGetValue(cacheKey, out cachedWidth)) return cachedWidth;
            var typeface = new Typeface(FontFamily, FontStyles.Normal,
                level > 0 ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            var formatted = new FormattedText(value, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection, typeface, FontSizeForLine(line), Foreground, dpi);
            ApplyMarkdownBold(formatted, line, sourceStartColumn, value.Length);
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

            int column = 0;
            while (column < value.Length)
            {
                int low = column, high = value.Length;
                while (low < high)
                {
                    int middle = (low + high + 1) / 2;
                    if (MeasureTextWidthForLine(value.Substring(column, middle - column), line, column) <= _wrapTextWidth) low = middle;
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
                e.Property == EnableMarkdownHorizontalRulesProperty)
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
