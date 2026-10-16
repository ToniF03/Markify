using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace Markify.Textify
{
    public sealed partial class MarkifyTextBox
    {
        private sealed class EditorSurface : FrameworkElement
        {
            private static readonly Brush SelectionBackground = FrozenBrush(Color.FromRgb(194, 220, 247));
            private static readonly Brush BracketBackground = FrozenBrush(Color.FromRgb(255, 217, 128));
            private static readonly Brush CaretForeground = FrozenBrush(Color.FromRgb(45, 111, 176));
            private static readonly Brush MarkdownTableBorder = FrozenBrush(Color.FromRgb(190, 198, 207));
            private static readonly Brush MarkdownTableHeader = FrozenBrush(Color.FromRgb(239, 243, 248));
            private static readonly Brush MarkdownMathPreviewBackground = FrozenBrush(Color.FromRgb(247, 249, 252));
            private static readonly Pen MarkdownMathPreviewBorder = FrozenPen(Color.FromRgb(205, 212, 220), 1);
            private static readonly Brush MarkdownMathPreviewLabel = FrozenBrush(Color.FromRgb(105, 116, 129));
            private static readonly Brush MarkdownCodeFenceCopyButtonHoverFill = FrozenBrush(Color.FromRgb(225, 234, 244));
            private static readonly Brush MarkdownCodeFenceCopyButtonPressedFill = FrozenBrush(Color.FromRgb(205, 220, 238));
            private static readonly Pen MarkdownCodeFenceCopyIconPen = FrozenPen(Color.FromRgb(78, 91, 106), 1.35);
            private static readonly Brush MarkdownTaskCheckboxFill = FrozenBrush(Color.FromRgb(248, 250, 252));
            private static readonly Brush MarkdownTaskCheckboxCheckedFill = FrozenBrush(Color.FromRgb(48, 133, 91));
            private static readonly Pen MarkdownTaskCheckboxBorder = FrozenPen(Color.FromRgb(112, 128, 144), 1.15);
            private static readonly Pen MarkdownTaskCheckboxCheckedBorder = FrozenPen(Color.FromRgb(48, 133, 91), 1.15);
            private static readonly Pen MarkdownTaskCheckboxCheckPen = FrozenPen(Colors.White, 1.6);
            private static readonly Pen MarkdownQuotePen = FrozenPen(Color.FromRgb(157, 171, 187), 2);
            private static readonly Brush MarkdownAlertBlue = FrozenBrush(Color.FromRgb(45, 104, 168));
            private static readonly Brush MarkdownAlertGreen = FrozenBrush(Color.FromRgb(45, 125, 78));
            private static readonly Brush MarkdownAlertPurple = FrozenBrush(Color.FromRgb(112, 82, 156));
            private static readonly Brush MarkdownAlertOrange = FrozenBrush(Color.FromRgb(186, 108, 28));
            private static readonly Brush MarkdownAlertRed = FrozenBrush(Color.FromRgb(180, 62, 62));
            private readonly MarkifyTextBox _editor;
            public EditorSurface(MarkifyTextBox editor)
            {
                _editor = editor;
                Focusable = true;
                Cursor = System.Windows.Input.Cursors.IBeam;
                SnapsToDevicePixels = true;
            }

            private static Brush FrozenBrush(Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }

            private static Pen FrozenPen(Color color, double thickness)
            {
                var pen = new Pen(FrozenBrush(color), thickness);
                pen.Freeze();
                return pen;
            }

            protected override Size MeasureOverride(Size availableSize)
            {
                var lines = _editor.Lines;
                double maxWidth = 0;
                bool containsHorizontalRule = false;
                if (!_editor.IsWordWrapEnabled)
                {
                    for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                    {
                        if (_editor.IsMarkdownCodeFenceLine(lineIndex) &&
                            !_editor.IsMarkdownCodeFenceBodyLine(lineIndex) &&
                            !_editor.MarkdownCodeFenceMarkersAreVisible(lineIndex)) continue;
                        if (_editor.IsMarkdownHorizontalRuleLine(lineIndex))
                        {
                            containsHorizontalRule = true;
                            if (!_editor.MarkdownHorizontalRuleSourceIsVisible(lineIndex)) continue;
                        }
                        MarkifyTextBox.MarkdownTableLayout tableLayout;
                        if (_editor.TryGetMarkdownTableLayout(lineIndex, out tableLayout) &&
                            !tableLayout.IsSourceVisible)
                        {
                            maxWidth = Math.Max(maxWidth, _editor.TextOriginForLine(lineIndex) +
                                _editor.MarkdownTableWidth(tableLayout));
                            continue;
                        }
                        if (_editor.MarkdownMathBlockPreviewIsVisible(lineIndex))
                            maxWidth = Math.Max(maxWidth, _editor.TextOriginForLine(lineIndex) +
                                _editor.MarkdownMathBlockPreviewWidth(lineIndex));
                        maxWidth = Math.Max(maxWidth,
                            _editor.MeasureTextWidthForLine(lines[lineIndex].TrimEnd('\r'), lineIndex) +
                            Math.Max(0, _editor.TextOriginForLine(lineIndex) - _editor.TextOrigin));
                        MarkifyTextBox.MarkdownMediaBlock mediaBlock;
                        if (_editor.TryGetMarkdownMediaBlockForLine(lineIndex, out mediaBlock))
                            maxWidth = Math.Max(maxWidth, _editor.TextOriginForLine(lineIndex) +
                                (mediaBlock.IsVideo ? 480 : 480));
                    }
                }
                double width = _editor.IsWordWrapEnabled
                    ? (!double.IsInfinity(availableSize.Width) ? availableSize.Width : Math.Max(_editor.TextOrigin + 20, _editor.ActualWidth))
                    : _editor.TextOrigin + maxWidth + _editor.MeasureTextWidth("  ") + 10;
                if (containsHorizontalRule) width = Math.Max(width, _editor.ActualWidth);
                double height = TopInset * 2 + Math.Max(_editor.LineHeight, _editor.TotalVisualHeight);
                // ScrollViewer measures its content with Infinity along scrollable axes.
                // DesiredSize must stay finite; use the text's natural extent in that case.
                if (!double.IsInfinity(availableSize.Width) && !double.IsNaN(availableSize.Width))
                    width = Math.Max(width, availableSize.Width);
                if (!double.IsInfinity(availableSize.Height) && !double.IsNaN(availableSize.Height))
                    height = Math.Max(height, availableSize.Height);
                return new Size(width, height);
            }

            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                dc.DrawRectangle(_editor.Background, null, new Rect(0, 0, ActualWidth, ActualHeight));
                string[] lines = _editor.Lines;
                double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                int matchingBracket = _editor.EnableBracketHighlighting ? _editor.FindMatchingBracketPosition() : -1;
                int bracketCandidate = _editor.EnableBracketHighlighting && _editor.Caret < _editor.Text.Length && MarkifyTextBox.IsBracket(_editor.Text[_editor.Caret])
                    ? _editor.Caret : (_editor.EnableBracketHighlighting ? _editor.Caret - 1 : -1);
                int firstRow = _editor.FirstVisibleLine;
                int lastRow = Math.Max(firstRow + 1, _editor.VisibleLineEnd);

                for (int row = firstRow; row < lastRow; row++)
                {
                    int documentLine = _editor.DocumentLineAtVisibleRow(row);
                    int offset = _editor.GetLineStart(documentLine);
                    double textOrigin = _editor.TextOriginForLine(documentLine);
                    string fullLine = lines[documentLine].TrimEnd('\r');
                    int segmentStart = _editor.VisibleRowStartColumn(row);
                    int segmentEnd = segmentStart + Math.Min(_editor.VisibleRowLength(row), Math.Max(0, fullLine.Length - segmentStart));
                    int renderedStart = _editor.RenderedStartColumnForRow(row);
                    MarkifyTextBox.RenderedTextSegment renderedSegment =
                        _editor.CreateRenderedTextSegment(documentLine, renderedStart, segmentEnd);
                    string line = renderedSegment.Text;
                    double rowHeight = _editor.LineHeightForVisibleRow(row);
                    bool mathSourceVisible = _editor.MarkdownMathSourceIsVisible(documentLine);
                    bool mathPreviewVisible = _editor.MarkdownMathBlockPreviewIsVisible(documentLine);
                    double sourceRowHeight = mathPreviewVisible
                        ? _editor.MarkdownMathSourceRowHeight(documentLine) : rowHeight;
                    double y = TopInset + _editor.VisibleRowTop(row);
                    if (_editor.HighlightCurrentLine && _editor.LineIndexAt(_editor.Caret) == documentLine)
                        dc.DrawRectangle(_editor.CurrentLineBackground, null, new Rect(0, y, ActualWidth, rowHeight));
                    if (_editor.IsWholeLineSelected(documentLine))
                        dc.DrawRectangle(SelectionBackground, null, new Rect(0, y, ActualWidth, rowHeight));
                    MarkifyTextBox.MarkdownTableLayout tableLayout;
                    if (_editor.TryGetMarkdownTableLayout(documentLine, out tableLayout) &&
                        !tableLayout.IsSourceVisible && !mathSourceVisible)
                    {
                        DrawMarkdownTableRow(dc, documentLine, tableLayout, row, textOrigin, y, rowHeight, dpi);
                        continue;
                    }
                    if (!mathSourceVisible && _editor.IsMarkdownSetextUnderlineLine(documentLine) &&
                        !_editor.MarkdownSetextMarkersAreVisible(documentLine)) continue;
                    if (!mathSourceVisible && _editor.IsMarkdownCodeFenceLine(documentLine))
                    {
                        double codeFenceOrigin = _editor.MarkdownCodeFenceBackgroundOriginForLine(documentLine);
                        double codeFenceLeft = Math.Max(0, codeFenceOrigin - 5);
                        double codeFenceRight = _editor.MarkdownCodeFenceBackgroundRightEdge(documentLine);
                        dc.DrawRectangle(MarkifyTextBox.MarkdownInlineCodeBackground, null,
                            new Rect(codeFenceLeft, y, Math.Max(0, codeFenceRight - codeFenceLeft), rowHeight));
                        Rect copyButtonBounds;
                        if (_editor.TryGetMarkdownCodeFenceCopyButtonBounds(documentLine, y, out copyButtonBounds))
                            DrawMarkdownCodeFenceCopyButton(dc, copyButtonBounds,
                                _editor.MarkdownCodeFenceCopyButtonIsHighlighted(documentLine),
                                _editor.MarkdownCodeFenceCopyButtonIsPressed(documentLine));
                    }
                    else if (!mathSourceVisible && _editor.IsMarkdownIndentedCodeLine(documentLine))
                        dc.DrawRectangle(MarkifyTextBox.MarkdownInlineCodeBackground, null,
                            new Rect(Math.Max(0, textOrigin - 4), y,
                                Math.Max(0, ActualWidth - textOrigin + 4), rowHeight));
                    if (!mathSourceVisible)
                    {
                        DrawMarkdownBlockQuote(dc, documentLine, textOrigin, y, rowHeight);
                        DrawMarkdownInlineCodeBackgrounds(dc, renderedSegment, documentLine, y, rowHeight);
                    }
                    if (!mathSourceVisible && _editor.IsMarkdownHorizontalRuleLine(documentLine) &&
                        !_editor.MarkdownHorizontalRuleSourceIsVisible(documentLine))
                    {
                        if (_editor.GetSelectionSpansForLine(documentLine).Count > 0 && !_editor.IsWholeLineSelected(documentLine))
                            dc.DrawRectangle(SelectionBackground, null,
                                new Rect(textOrigin, y, Math.Max(0, ActualWidth - textOrigin * 2), rowHeight));
                        double ruleY = y + rowHeight / 2;
                        double ruleEnd = Math.Max(textOrigin, ActualWidth - textOrigin);
                        Brush ruleBrush = _editor.Foreground.Clone();
                        ruleBrush.Opacity *= 0.55;
                        if (ruleBrush.CanFreeze) ruleBrush.Freeze();
                        var rulePen = new Pen(ruleBrush, 0.65);
                        if (rulePen.CanFreeze) rulePen.Freeze();
                        dc.DrawLine(rulePen, new Point(textOrigin, ruleY), new Point(ruleEnd, ruleY));
                        continue;
                    }
                    MarkifyTextBox.MarkdownMediaBlock mediaBlock;
                    if (!mathSourceVisible && _editor.TryGetMarkdownMediaBlockForLine(documentLine, out mediaBlock) &&
                        !_editor.MarkdownMediaSourceIsVisible(documentLine, mediaBlock))
                    {
                        if (!mediaBlock.IsVideo)
                        {
                            ImageSource image = _editor.GetMarkdownImageSource(mediaBlock.Source);
                            double imageWidth = 0;
                            double imageHeight = 0;
                            if (image != null && image.Width > 0 && image.Height > 0)
                            {
                                double scale = Math.Min(1, Math.Min(480 / image.Width, 260 / image.Height));
                                imageWidth = image.Width * scale;
                                imageHeight = image.Height * scale;
                            }
                            if (imageWidth > 0 && imageHeight > 0)
                            {
                                dc.DrawImage(image, new Rect(textOrigin, y + 3, imageWidth, imageHeight));
                            }
                            else
                            {
                                var placeholder = new Rect(textOrigin, y + 3, 260, 42);
                                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(242, 244, 247)),
                                    new Pen(new SolidColorBrush(Color.FromRgb(205, 211, 218)), 1), placeholder, 4, 4);
                                string label = string.IsNullOrWhiteSpace(mediaBlock.AltText)
                                    ? "Bild konnte nicht geladen werden" : mediaBlock.AltText;
                                var placeholderText = MakeText(label, new Typeface(_editor.FontFamily, FontStyles.Normal,
                                    FontWeights.Normal, FontStretches.Normal), _editor.Foreground, dpi, _editor.FontSize);
                                dc.DrawText(placeholderText, new Point(textOrigin + 10, y + 13));
                            }
                        }
                        continue;
                    }
                    Brush lineBrush = _editor.Foreground;
                    int headingLevel = mathSourceVisible ? 0 : _editor.MarkdownHeadingLevelForLine(documentLine);
                    double fontSize = mathSourceVisible ? _editor.FontSize : _editor.FontSizeForLine(documentLine);
                    var typeface = new Typeface(!mathSourceVisible && _editor.IsMarkdownIndentedCodeLine(documentLine)
                        ? new FontFamily("Consolas") : _editor.FontFamily, FontStyles.Normal,
                        (headingLevel > 0 || _editor.IsMarkdownDefinitionTermLine(documentLine))
                            ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);

                    int visibleLength = line.Length;
                    int lineEnd = offset + fullLine.Length;
                    foreach (var span in _editor.GetSelectionSpansForLine(documentLine))
                    {
                        int selectionStart = -1;
                        for (int column = 0; column <= visibleLength; column++)
                        {
                            bool selected = column < visibleLength &&
                                offset + renderedSegment.SourceColumns[column] >= span.Start &&
                                offset + renderedSegment.SourceColumns[column] < span.Start + span.Length;
                            if (selected && selectionStart < 0) selectionStart = column;
                            if (!selected && selectionStart >= 0)
                            {
                                double x = textOrigin +
                                    _editor.MeasureRenderedTextWidthForLine(renderedSegment, documentLine, selectionStart);
                                double endX = textOrigin +
                                    _editor.MeasureRenderedTextWidthForLine(renderedSegment, documentLine, column);
                                dc.DrawRectangle(SelectionBackground, null,
                                    new Rect(x, y, endX - x, sourceRowHeight));
                                selectionStart = -1;
                            }
                        }
                    }
                    int matchingBracketColumn = matchingBracket >= offset ?
                        _editor.RenderedCharacterForSourceColumn(renderedSegment, matchingBracket - offset) : -1;
                    int bracketCandidateColumn = bracketCandidate >= offset ?
                        _editor.RenderedCharacterForSourceColumn(renderedSegment, bracketCandidate - offset) : -1;
                    if (matchingBracketColumn >= 0)
                        DrawBracketHighlight(dc, renderedSegment, y, matchingBracketColumn, documentLine, sourceRowHeight);
                    if (bracketCandidateColumn >= 0)
                        DrawBracketHighlight(dc, renderedSegment, y, bracketCandidateColumn, documentLine, sourceRowHeight);
                    if (line.Length > 0)
                    {
                        List<MarkifyTextBox.MarkdownScriptSpan> scriptSpans = mathSourceVisible
                            ? new List<MarkifyTextBox.MarkdownScriptSpan>()
                            : _editor.GetMarkdownScriptSpansForLine(documentLine);
                        if (scriptSpans.Count == 0)
                        {
                            var formatted = MakeText(line, typeface, lineBrush, dpi, fontSize);
                            if (!mathSourceVisible)
                            {
                                if (_editor.EnableSyntaxHighlighting)
                                    _editor.ApplySyntaxHighlighting(formatted, line, documentLine);
                                _editor.ApplyMarkdownBold(formatted, documentLine, renderedSegment.SourceColumns);
                                _editor.ApplyMarkdownItalic(formatted, documentLine, renderedSegment.SourceColumns);
                                _editor.ApplyMarkdownStrikethrough(formatted, documentLine, renderedSegment.SourceColumns);
                                _editor.ApplyMarkdownScriptStyle(formatted, documentLine, renderedSegment.SourceColumns);
                                _editor.ApplyMarkdownInlineCode(formatted, documentLine, renderedSegment.SourceColumns);
                                _editor.ApplyMarkdownCodeFenceStyle(formatted, documentLine, line.Length);
                                _editor.ApplyMarkdownFenceSyntaxHighlighting(formatted, documentLine, renderedSegment.SourceColumns);
                                _editor.ApplyMarkdownTaskMarkerStyle(formatted, documentLine, renderedSegment.SourceColumns);
                                _editor.ApplyMarkdownLinkStyle(formatted, documentLine, renderedSegment.SourceColumns);
                                _editor.ApplyMarkdownFootnoteStyle(formatted, documentLine, renderedSegment.SourceColumns);
                                _editor.ApplyMarkdownListMarkerStyle(formatted, documentLine, renderedSegment.SourceColumns);
                            }
                            dc.DrawText(formatted, new Point(textOrigin,
                                y + Math.Max(0, (sourceRowHeight - formatted.Height) / 2)));
                        }
                        else
                        {
                            var baseMetrics = MakeText("M", typeface, lineBrush, dpi, fontSize);
                            double baseTextY = y + Math.Max(0, (sourceRowHeight - baseMetrics.Height) / 2);
                            int displayStart = 0;
                            while (displayStart < line.Length)
                            {
                                int scriptKind = MarkifyTextBox.MarkdownScriptKindForSourceColumn(
                                    scriptSpans, renderedSegment.SourceColumns[displayStart]);
                                int displayEnd = displayStart + 1;
                                while (displayEnd < line.Length &&
                                    MarkifyTextBox.MarkdownScriptKindForSourceColumn(
                                        scriptSpans, renderedSegment.SourceColumns[displayEnd]) == scriptKind)
                                    displayEnd++;

                                int runLength = displayEnd - displayStart;
                                string runText = line.Substring(displayStart, runLength);
                                var formatted = MakeText(runText, typeface, lineBrush, dpi, fontSize);
                                var runSourceColumns = new int[runLength];
                                Array.Copy(renderedSegment.SourceColumns, displayStart, runSourceColumns, 0, runLength);
                                if (_editor.EnableSyntaxHighlighting)
                                    _editor.ApplySyntaxHighlighting(formatted, line, documentLine, displayStart, runLength);
                                _editor.ApplyMarkdownBold(formatted, documentLine, runSourceColumns);
                                _editor.ApplyMarkdownItalic(formatted, documentLine, runSourceColumns);
                                _editor.ApplyMarkdownStrikethrough(formatted, documentLine, runSourceColumns);
                                _editor.ApplyMarkdownScriptStyle(formatted, documentLine, runSourceColumns);
                                _editor.ApplyMarkdownInlineCode(formatted, documentLine, runSourceColumns);
                                _editor.ApplyMarkdownCodeFenceStyle(formatted, documentLine, runLength);
                                _editor.ApplyMarkdownFenceSyntaxHighlighting(formatted, documentLine, runSourceColumns);
                                _editor.ApplyMarkdownTaskMarkerStyle(formatted, documentLine, runSourceColumns);
                                _editor.ApplyMarkdownLinkStyle(formatted, documentLine, runSourceColumns);
                                _editor.ApplyMarkdownFootnoteStyle(formatted, documentLine, runSourceColumns);
                                _editor.ApplyMarkdownListMarkerStyle(formatted, documentLine, runSourceColumns);

                                double x = textOrigin +
                                    _editor.MeasureRenderedTextWidthForLine(renderedSegment, documentLine, displayStart);
                                double yOffset = baseTextY + baseMetrics.Baseline - formatted.Baseline;
                                if (scriptKind == 1) yOffset -= fontSize * 0.27;
                                else if (scriptKind == 2) yOffset += fontSize * 0.18;
                                dc.DrawText(formatted, new Point(x, yOffset));
                                displayStart = displayEnd;
                            }
                        }
                    }
                    DrawMarkdownMathFractions(dc, renderedSegment, documentLine, textOrigin,
                        y, sourceRowHeight, typeface, lineBrush, dpi, fontSize);
                    if (!mathSourceVisible)
                        DrawMarkdownAlertLabel(dc, documentLine, textOrigin, y, sourceRowHeight, dpi);
                    MarkifyTextBox.MarkdownTaskMarker taskMarker;
                    Rect taskCheckboxBounds;
                    if (!mathSourceVisible && _editor.TryGetMarkdownTaskCheckboxBounds(documentLine, renderedSegment,
                        textOrigin, y, sourceRowHeight, true, out taskMarker, out taskCheckboxBounds))
                        DrawMarkdownTaskCheckbox(dc, taskMarker, taskCheckboxBounds);
                    if (mathPreviewVisible)
                        DrawMarkdownMathBlockPreview(dc, documentLine, textOrigin, y, dpi);
                    if (_editor.HasFocus && _editor.CaretVisible && _editor.SelectionLength == 0 &&
                        (!_editor.IsRectangularSelection || _editor.GetSelectionSpansForLine(documentLine).Count == 0) &&
                        _editor.VisibleRowForPosition(_editor.Caret) == row &&
                        _editor.LineIndexAt(_editor.Caret) == documentLine && _editor.Caret >= offset && _editor.Caret <= lineEnd)
                    {
                        int sourceColumn = _editor.Caret - offset;
                        int col = _editor.RenderedBoundaryForSourceColumn(renderedSegment, sourceColumn);
                        double cx = textOrigin +
                            _editor.MeasureRenderedTextWidthForLine(renderedSegment, documentLine, col);
                        dc.DrawRectangle(CaretForeground, null,
                            new Rect(cx, y + 1, 1.4, sourceRowHeight - 2));
                    }
                    foreach (int secondaryCaret in _editor.AdditionalCaretPositions)
                    {
                        if (!_editor.HasFocus) break;
                        if (_editor.VisibleRowForPosition(secondaryCaret) != row) continue;
                        int localColumn = _editor.RenderedBoundaryForSourceColumn(renderedSegment, secondaryCaret - offset);
                        double x = textOrigin +
                            _editor.MeasureRenderedTextWidthForLine(renderedSegment, documentLine, localColumn);
                        dc.DrawRectangle(CaretForeground, null,
                            new Rect(x, y + 2, 1.2, sourceRowHeight - 4));
                    }
                }
            }

            private void DrawMarkdownInlineCodeBackgrounds(DrawingContext dc, MarkifyTextBox.RenderedTextSegment segment,
                int line, double y, double rowHeight)
            {
                foreach (MarkifyTextBox.MarkdownInlineCodeSpan span in _editor.GetMarkdownInlineCodeSpansForLine(line))
                {
                    int index = 0;
                    while (index < segment.SourceColumns.Length)
                    {
                        while (index < segment.SourceColumns.Length &&
                            (segment.SourceColumns[index] < span.ContentStart ||
                             segment.SourceColumns[index] >= span.ContentStart + span.ContentLength)) index++;
                        int codeStart = index;
                        while (index < segment.SourceColumns.Length &&
                            segment.SourceColumns[index] >= span.ContentStart &&
                            segment.SourceColumns[index] < span.ContentStart + span.ContentLength) index++;
                        if (index <= codeStart) continue;

                        double startX = _editor.TextOriginForLine(line) +
                            _editor.MeasureRenderedTextWidthForLine(segment, line, codeStart);
                        double endX = _editor.TextOriginForLine(line) +
                            _editor.MeasureRenderedTextWidthForLine(segment, line, index);
                        var background = new Rect(startX - 2, y + 2, endX - startX + 4, Math.Max(0, rowHeight - 4));
                        dc.DrawRoundedRectangle(MarkifyTextBox.MarkdownInlineCodeBackground, null, background, 2, 2);
                    }
                }
            }

            private void DrawMarkdownBlockQuote(DrawingContext dc, int line, double textOrigin,
                double y, double rowHeight)
            {
                int depth = _editor.MarkdownQuoteDepthForLine(line);
                if (depth <= 0) return;
                double step = _editor.MeasureTextWidth("  ");
                for (int level = 0; level < depth; level++)
                {
                    double x = _editor.TextOrigin + level * step + step * 0.28;
                    dc.DrawLine(MarkdownQuotePen, new Point(x, y - 0.5),
                        new Point(x, y + rowHeight + 0.5));
                }
            }

            private void DrawMarkdownCodeFenceCopyButton(DrawingContext dc, Rect bounds,
                bool isHighlighted, bool isPressed)
            {
                if (isHighlighted)
                    dc.DrawRoundedRectangle(isPressed ? MarkdownCodeFenceCopyButtonPressedFill :
                        MarkdownCodeFenceCopyButtonHoverFill, null, bounds, 4, 4);
                double iconWidth = Math.Min(8, bounds.Width * 0.36);
                double iconHeight = Math.Min(10, bounds.Height * 0.55);
                double iconLeft = bounds.Left + (bounds.Width - iconWidth) / 2;
                double iconTop = bounds.Top + (bounds.Height - iconHeight) / 2;
                Rect backPage = new Rect(iconLeft + 2.4, iconTop - 2.1, iconWidth - 0.3, iconHeight - 0.2);
                Rect frontPage = new Rect(iconLeft, iconTop, iconWidth - 2.4, iconHeight - 1.8);
                dc.DrawRoundedRectangle(null, MarkdownCodeFenceCopyIconPen, backPage, 1, 1);
                dc.DrawRoundedRectangle(null, MarkdownCodeFenceCopyIconPen, frontPage, 1, 1);
            }

            private void DrawMarkdownAlertLabel(DrawingContext dc, int line, double textOrigin,
                double y, double rowHeight, double dpi)
            {
                string alertType;
                int markerStart;
                int markerEnd;
                if (!_editor.TryGetMarkdownAlertForLine(line, out alertType, out markerStart, out markerEnd) ||
                    _editor.MarkdownAlertMarkersAreVisible(line, markerStart, markerEnd)) return;
                Brush brush = alertType == "NOTE" ? MarkdownAlertBlue :
                    alertType == "TIP" ? MarkdownAlertGreen :
                    alertType == "IMPORTANT" ? MarkdownAlertPurple :
                    alertType == "WARNING" ? MarkdownAlertOrange : MarkdownAlertRed;
                var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                    FontWeights.Bold, FontStretches.Normal);
                FormattedText label = MakeText(alertType, typeface, brush, dpi,
                    Math.Max(9, _editor.FontSize * 0.82));
                dc.DrawText(label, new Point(textOrigin, y + Math.Max(0, (rowHeight - label.Height) / 2)));
            }

            private void DrawMarkdownTableRow(DrawingContext dc, int line,
                MarkifyTextBox.MarkdownTableLayout layout, int visualRow, double textOrigin, double y,
                double rowHeight, double dpi)
            {
                double tableWidth = _editor.MarkdownTableWidth(layout);
                var borderPen = new Pen(MarkdownTableBorder, 0.8);
                if (layout.IsSeparator)
                {
                    double ruleY = y + rowHeight / 2;
                    dc.DrawLine(borderPen, new Point(textOrigin, ruleY), new Point(textOrigin + tableWidth, ruleY));
                    return;
                }

                double x = textOrigin;
                for (int column = 0; column < layout.Cells.Length; column++)
                {
                    double columnWidth = layout.PixelColumnWidths[column];
                    var cellRect = new Rect(x, y, columnWidth, rowHeight);
                    Brush cellBackground = layout.IsHeader ? MarkdownTableHeader :
                        (_editor.HighlightCurrentLine && _editor.LineIndexAt(_editor.Caret) == line
                            ? _editor.CurrentLineBackground : _editor.Background);
                    dc.DrawRectangle(cellBackground, borderPen, cellRect);

                    MarkifyTextBox.MarkdownTableCell cell = layout.Cells[column];
                    MarkifyTextBox.RenderedTextSegment segment =
                        _editor.CreateRenderedTextSegment(line, cell.Start, cell.Start + cell.Length);
                    double contentX = _editor.MarkdownTableCellTextOrigin(line, layout, column, segment);
                    int lineStart = _editor.GetLineStart(line);
                    foreach (MarkifyTextBox.SelectionSpan span in _editor.GetSelectionSpansForLine(line))
                    {
                        int index = 0;
                        while (index < segment.SourceColumns.Length)
                        {
                            while (index < segment.SourceColumns.Length &&
                                (lineStart + segment.SourceColumns[index] < span.Start ||
                                 lineStart + segment.SourceColumns[index] >= span.Start + span.Length)) index++;
                            int selectionStart = index;
                            while (index < segment.SourceColumns.Length &&
                                lineStart + segment.SourceColumns[index] >= span.Start &&
                                lineStart + segment.SourceColumns[index] < span.Start + span.Length) index++;
                            if (index <= selectionStart) continue;
                            double startX = contentX + _editor.MarkdownTableTextWidth(
                                segment, line, selectionStart, layout.IsHeader);
                            double endX = contentX + _editor.MarkdownTableTextWidth(
                                segment, line, index, layout.IsHeader);
                            dc.DrawRectangle(SelectionBackground, null,
                                new Rect(startX, y, Math.Max(0, endX - startX), rowHeight));
                        }
                    }
                    if (segment.Text.Length > 0)
                    {
                        double fontSize = _editor.FontSizeForLine(line);
                        var typeface = new Typeface(_editor.FontFamily, FontStyles.Normal,
                            layout.IsHeader ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                        FormattedText formatted = MakeText(segment.Text, typeface, _editor.Foreground, dpi, fontSize);
                        if (_editor.EnableSyntaxHighlighting)
                            _editor.ApplySyntaxHighlighting(formatted, segment.Text, line);
                        _editor.ApplyMarkdownBold(formatted, line, segment.SourceColumns);
                        _editor.ApplyMarkdownItalic(formatted, line, segment.SourceColumns);
                        _editor.ApplyMarkdownStrikethrough(formatted, line, segment.SourceColumns);
                        _editor.ApplyMarkdownScriptStyle(formatted, line, segment.SourceColumns);
                        _editor.ApplyMarkdownInlineCode(formatted, line, segment.SourceColumns);
                        _editor.ApplyMarkdownTaskMarkerStyle(formatted, line, segment.SourceColumns);
                        _editor.ApplyMarkdownLinkStyle(formatted, line, segment.SourceColumns);
                        _editor.ApplyMarkdownFootnoteStyle(formatted, line, segment.SourceColumns);
                        _editor.ApplyMarkdownListMarkerStyle(formatted, line, segment.SourceColumns);

                        double contentY = y + Math.Max(0, (rowHeight - formatted.Height) / 2);
                        dc.DrawText(formatted, new Point(contentX, contentY));
                        DrawMarkdownMathFractions(dc, segment, line, contentX, y,
                            rowHeight, typeface, _editor.Foreground, dpi, fontSize);
                    }

                    int caretColumn = _editor.Caret - lineStart;
                    if (_editor.HasFocus && _editor.CaretVisible && _editor.SelectionLength == 0 &&
                        _editor.VisibleRowForPosition(_editor.Caret) == visualRow &&
                        _editor.LineIndexAt(_editor.Caret) == line &&
                        caretColumn >= cell.Start && caretColumn <= cell.Start + cell.Length)
                    {
                        int displayColumn = _editor.RenderedBoundaryForSourceColumn(segment, caretColumn);
                        double caretX = contentX + _editor.MarkdownTableTextWidth(
                            segment, line, displayColumn, layout.IsHeader);
                        dc.DrawRectangle(CaretForeground, null,
                            new Rect(caretX, y + 1, 1.4, Math.Max(0, rowHeight - 2)));
                    }
                    foreach (int secondaryCaret in _editor.AdditionalCaretPositions)
                    {
                        if (!_editor.HasFocus || _editor.VisibleRowForPosition(secondaryCaret) != visualRow ||
                            _editor.LineIndexAt(secondaryCaret) != line) continue;
                        int secondaryColumn = secondaryCaret - lineStart;
                        if (secondaryColumn < cell.Start || secondaryColumn > cell.Start + cell.Length) continue;
                        int displayColumn = _editor.RenderedBoundaryForSourceColumn(segment, secondaryColumn);
                        double secondaryX = contentX + _editor.MarkdownTableTextWidth(
                            segment, line, displayColumn, layout.IsHeader);
                        dc.DrawRectangle(CaretForeground, null,
                            new Rect(secondaryX, y + 2, 1.2, Math.Max(0, rowHeight - 4)));
                    }
                    x += columnWidth;
                }
            }

            private void DrawMarkdownTaskCheckbox(DrawingContext dc,
                MarkifyTextBox.MarkdownTaskMarker marker, Rect bounds)
            {
                dc.DrawRoundedRectangle(marker.IsChecked ? MarkdownTaskCheckboxCheckedFill : MarkdownTaskCheckboxFill,
                    marker.IsChecked ? MarkdownTaskCheckboxCheckedBorder : MarkdownTaskCheckboxBorder,
                    bounds, 2.5, 2.5);
                if (!marker.IsChecked) return;
                var first = new Point(bounds.Left + bounds.Width * 0.24, bounds.Top + bounds.Height * 0.52);
                var middle = new Point(bounds.Left + bounds.Width * 0.43, bounds.Top + bounds.Height * 0.72);
                var last = new Point(bounds.Left + bounds.Width * 0.77, bounds.Top + bounds.Height * 0.31);
                dc.DrawLine(MarkdownTaskCheckboxCheckPen, first, middle);
                dc.DrawLine(MarkdownTaskCheckboxCheckPen, middle, last);
            }

            private void DrawBracketHighlight(DrawingContext dc, MarkifyTextBox.RenderedTextSegment segment,
                double y, int column, int documentLine, double rowHeight)
            {
                if (column < 0 || column >= segment.Text.Length) return;
                double x = _editor.TextOriginForLine(documentLine) +
                    _editor.MeasureRenderedTextWidthForLine(segment, documentLine, column);
                double width = _editor.MeasureRenderedTextWidthForLine(segment, documentLine, column + 1) -
                    _editor.MeasureRenderedTextWidthForLine(segment, documentLine, column);
                dc.DrawRectangle(BracketBackground, null, new Rect(x, y + 1, Math.Max(2, width), rowHeight - 2));
            }

            private struct MathSequenceMetrics
            {
                public double Width;
                public double Ascent;
                public double Descent;
            }

            private void DrawMarkdownMathFractions(DrawingContext dc,
                MarkifyTextBox.RenderedTextSegment segment, int line, double textOrigin,
                double y, double rowHeight, Typeface typeface, Brush brush, double dpi, double fontSize)
            {
                if (segment.MathFractions == null || segment.MathFractions.Count == 0) return;
                var baselineText = MakeText("M", typeface, brush, dpi, fontSize);
                double baseline = y + Math.Max(0, (rowHeight - baselineText.Height) / 2) + baselineText.Baseline;
                foreach (MarkifyTextBox.RenderedMathFractionLayout fraction in segment.MathFractions)
                {
                    double x = textOrigin + _editor.MeasureRenderedTextWidthForLine(
                        segment, line, fraction.DisplayStart);
                    DrawStackedMathFraction(dc, fraction.Numerator, fraction.Denominator,
                        x, baseline, fraction.FontSize, brush, dpi);
                }
            }

            private void DrawMarkdownMathBlockPreview(DrawingContext dc, int closingLine,
                double textOrigin, double y, double dpi)
            {
                List<MarkifyTextBox.MarkdownMathPreviewLine> lines =
                    _editor.GetMarkdownMathPreviewLines(closingLine);
                if (lines.Count == 0) return;

                double sourceRowHeight = _editor.MarkdownMathSourceRowHeight(closingLine);
                double previewHeight = _editor.MarkdownMathBlockPreviewHeight(closingLine);
                double previewTop = y + sourceRowHeight + 4;
                double previewWidth = _editor.MarkdownMathBlockPreviewWidth(closingLine);
                var bounds = new Rect(textOrigin, previewTop, previewWidth, Math.Max(1, previewHeight - 4));
                dc.DrawRoundedRectangle(MarkdownMathPreviewBackground, MarkdownMathPreviewBorder,
                    bounds, 5, 5);

                var labelTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                    FontWeights.SemiBold, FontStretches.Normal);
                FormattedText label = MakeText("Preview", labelTypeface,
                    MarkdownMathPreviewLabel, dpi, 10);
                dc.DrawText(label, new Point(bounds.Left + 10, bounds.Top + 4));

                double rowY = bounds.Top + 25;
                foreach (MarkifyTextBox.MarkdownMathPreviewLine line in lines)
                {
                    double fontSize = _editor.FontSize * 1.08;
                    bool hasFraction = line.Characters != null &&
                        line.Characters.Exists(character => character.IsFraction);
                    double mathRowHeight = Math.Ceiling(fontSize * (hasFraction ? 2.7 : 1.5));
                    MathSequenceMetrics metrics = MeasureMathSequence(line.Characters,
                        fontSize, _editor.Foreground, dpi);
                    double baseline = rowY + Math.Max(metrics.Ascent,
                        (mathRowHeight + metrics.Ascent - metrics.Descent) / 2);
                    DrawMathSequence(dc, line.Characters, bounds.Left + 10, baseline,
                        fontSize, _editor.Foreground, dpi);
                    rowY += mathRowHeight + 2;
                }
            }

            private MathSequenceMetrics MeasureMathSequence(
                IList<MarkifyTextBox.MarkdownMathCharacter> characters, double fontSize,
                Brush brush, double dpi)
            {
                var metrics = new MathSequenceMetrics();
                if (characters == null || characters.Count == 0) return metrics;
                var run = new StringBuilder();
                int runScriptKind = -1;
                Typeface mathTypeface = new Typeface(new FontFamily("Cambria Math"),
                    FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                Action flushRun = () =>
                {
                    if (run.Length == 0) return;
                    double runFontSize = fontSize * (runScriptKind == 0 ? 1.0 : 0.72);
                    FormattedText formatted = MakeText(run.ToString(), mathTypeface, brush, dpi, runFontSize);
                    metrics.Width += formatted.WidthIncludingTrailingWhitespace;
                    double upwardShift = runScriptKind == 1 ? fontSize * 0.27 : 0;
                    double downwardShift = runScriptKind == 2 ? fontSize * 0.18 : 0;
                    metrics.Ascent = Math.Max(metrics.Ascent,
                        formatted.Baseline + upwardShift - downwardShift);
                    metrics.Descent = Math.Max(metrics.Descent,
                        formatted.Height - formatted.Baseline + downwardShift - upwardShift);
                    run.Clear();
                };

                foreach (MarkifyTextBox.MarkdownMathCharacter character in characters)
                {
                    if (!character.IsFraction)
                    {
                        if (run.Length > 0 && runScriptKind != character.ScriptKind)
                        {
                            flushRun();
                            runScriptKind = character.ScriptKind;
                        }
                        else if (run.Length == 0)
                            runScriptKind = character.ScriptKind;
                        run.Append(character.Value);
                        continue;
                    }
                    flushRun();
                    runScriptKind = -1;
                    double innerFontSize = fontSize * 0.72;
                    MathSequenceMetrics numerator = MeasureMathSequence(character.Numerator,
                        innerFontSize, brush, dpi);
                    MathSequenceMetrics denominator = MeasureMathSequence(character.Denominator,
                        innerFontSize, brush, dpi);
                    double padding = fontSize * 0.16;
                    metrics.Width += Math.Max(numerator.Width, denominator.Width) + padding * 2;
                    double gap = fontSize * 0.08;
                    double lineThickness = Math.Max(0.65, fontSize * 0.055);
                    metrics.Ascent = Math.Max(metrics.Ascent,
                        numerator.Ascent + numerator.Descent + gap + lineThickness);
                    metrics.Descent = Math.Max(metrics.Descent,
                        denominator.Ascent + denominator.Descent + gap + lineThickness);
                }
                flushRun();
                return metrics;
            }

            private void DrawMathSequence(DrawingContext dc,
                IList<MarkifyTextBox.MarkdownMathCharacter> characters, double x, double baseline,
                double fontSize, Brush brush, double dpi)
            {
                if (characters == null || characters.Count == 0) return;
                var run = new StringBuilder();
                int runScriptKind = -1;
                Typeface mathTypeface = new Typeface(new FontFamily("Cambria Math"),
                    FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                Action flushRun = () =>
                {
                    if (run.Length == 0) return;
                    double runFontSize = fontSize * (runScriptKind == 0 ? 1.0 : 0.72);
                    FormattedText formatted = MakeText(run.ToString(), mathTypeface, brush, dpi, runFontSize);
                    double baselineShift = runScriptKind == 1 ? -fontSize * 0.27 :
                        (runScriptKind == 2 ? fontSize * 0.18 : 0);
                    dc.DrawText(formatted, new Point(x, baseline + baselineShift - formatted.Baseline));
                    x += formatted.WidthIncludingTrailingWhitespace;
                    run.Clear();
                };

                foreach (MarkifyTextBox.MarkdownMathCharacter character in characters)
                {
                    if (!character.IsFraction)
                    {
                        if (run.Length > 0 && runScriptKind != character.ScriptKind)
                        {
                            flushRun();
                            runScriptKind = character.ScriptKind;
                        }
                        else if (run.Length == 0)
                            runScriptKind = character.ScriptKind;
                        run.Append(character.Value);
                        continue;
                    }
                    flushRun();
                    runScriptKind = -1;
                    DrawStackedMathFraction(dc, character.Numerator, character.Denominator,
                        x, baseline, fontSize, brush, dpi);
                    MathSequenceMetrics fraction = MeasureMathSequence(
                        new[] { character }, fontSize, brush, dpi);
                    x += fraction.Width;
                }
                flushRun();
            }

            private void DrawStackedMathFraction(DrawingContext dc,
                IList<MarkifyTextBox.MarkdownMathCharacter> numeratorCharacters,
                IList<MarkifyTextBox.MarkdownMathCharacter> denominatorCharacters,
                double x, double baseline, double fontSize, Brush brush, double dpi)
            {
                double innerFontSize = fontSize * 0.72;
                MathSequenceMetrics numerator = MeasureMathSequence(numeratorCharacters,
                    innerFontSize, brush, dpi);
                MathSequenceMetrics denominator = MeasureMathSequence(denominatorCharacters,
                    innerFontSize, brush, dpi);
                double padding = fontSize * 0.16;
                double width = Math.Max(numerator.Width, denominator.Width) + padding * 2;
                double gap = fontSize * 0.08;
                double thickness = Math.Max(0.65, fontSize * 0.055);
                DrawMathSequence(dc, numeratorCharacters,
                    x + (width - numerator.Width) / 2,
                    baseline - gap - thickness - numerator.Descent,
                    innerFontSize, brush, dpi);
                dc.DrawLine(new Pen(brush, thickness), new Point(x, baseline), new Point(x + width, baseline));
                DrawMathSequence(dc, denominatorCharacters,
                    x + (width - denominator.Width) / 2,
                    baseline + gap + thickness + denominator.Ascent,
                    innerFontSize, brush, dpi);
            }

            private FormattedText MakeText(string value, Typeface typeface, Brush brush, double dpi, double fontSize)
            {
                return new FormattedText(value, CultureInfo.CurrentUICulture, _editor.FlowDirection,
                    typeface, fontSize, brush, dpi);
            }
        }
    }
}
