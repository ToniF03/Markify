using System;
using System.Collections.Generic;
using System.Globalization;
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
                        maxWidth = Math.Max(maxWidth,
                            _editor.MeasureTextWidthForLine(lines[lineIndex].TrimEnd('\r'), lineIndex) +
                            _editor.MarkdownListIndentForLine(lineIndex));
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
                    double y = TopInset + _editor.VisibleRowTop(row);
                    if (_editor.HighlightCurrentLine && _editor.LineIndexAt(_editor.Caret) == documentLine)
                        dc.DrawRectangle(_editor.CurrentLineBackground, null, new Rect(0, y, ActualWidth, rowHeight));
                    if (_editor.IsWholeLineSelected(documentLine))
                        dc.DrawRectangle(SelectionBackground, null, new Rect(0, y, ActualWidth, rowHeight));
                    MarkifyTextBox.MarkdownTableLayout tableLayout;
                    if (_editor.TryGetMarkdownTableLayout(documentLine, out tableLayout) &&
                        !tableLayout.IsSourceVisible)
                    {
                        DrawMarkdownTableRow(dc, documentLine, tableLayout, row, textOrigin, y, rowHeight, dpi);
                        continue;
                    }
                    if (_editor.IsMarkdownCodeFenceLine(documentLine))
                        dc.DrawRectangle(MarkifyTextBox.MarkdownInlineCodeBackground, null,
                            new Rect(Math.Max(0, _editor.TextOrigin - 4), y,
                                Math.Max(0, ActualWidth - _editor.TextOrigin + 4), rowHeight));
                    DrawMarkdownInlineCodeBackgrounds(dc, renderedSegment, documentLine, y, rowHeight);
                    if (_editor.IsMarkdownHorizontalRuleLine(documentLine) &&
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
                    if (_editor.TryGetMarkdownMediaBlockForLine(documentLine, out mediaBlock) &&
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
                    int headingLevel = _editor.MarkdownHeadingLevelForLine(documentLine);
                    double fontSize = _editor.FontSizeForLine(documentLine);
                    var typeface = new Typeface(_editor.FontFamily, FontStyles.Normal,
                        headingLevel > 0 ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);

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
                                dc.DrawRectangle(SelectionBackground, null, new Rect(x, y, endX - x, rowHeight));
                                selectionStart = -1;
                            }
                        }
                    }
                    int matchingBracketColumn = matchingBracket >= offset ?
                        _editor.RenderedCharacterForSourceColumn(renderedSegment, matchingBracket - offset) : -1;
                    int bracketCandidateColumn = bracketCandidate >= offset ?
                        _editor.RenderedCharacterForSourceColumn(renderedSegment, bracketCandidate - offset) : -1;
                    if (matchingBracketColumn >= 0)
                        DrawBracketHighlight(dc, renderedSegment, y, matchingBracketColumn, documentLine, rowHeight);
                    if (bracketCandidateColumn >= 0)
                        DrawBracketHighlight(dc, renderedSegment, y, bracketCandidateColumn, documentLine, rowHeight);
                    if (line.Length > 0)
                    {
                        List<MarkifyTextBox.MarkdownScriptSpan> scriptSpans =
                            _editor.GetMarkdownScriptSpansForLine(documentLine);
                        if (scriptSpans.Count == 0)
                        {
                            var formatted = MakeText(line, typeface, lineBrush, dpi, fontSize);
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
                            _editor.ApplyMarkdownListMarkerStyle(formatted, documentLine, renderedSegment.SourceColumns);
                            dc.DrawText(formatted, new Point(textOrigin, y + Math.Max(0, (rowHeight - formatted.Height) / 2)));
                        }
                        else
                        {
                            var baseMetrics = MakeText("M", typeface, lineBrush, dpi, fontSize);
                            double baseTextY = y + Math.Max(0, (rowHeight - baseMetrics.Height) / 2);
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
                            new Rect(cx, y + 1, 1.4, rowHeight - 2));
                    }
                    foreach (int secondaryCaret in _editor.AdditionalCaretPositions)
                    {
                        if (!_editor.HasFocus) break;
                        if (_editor.VisibleRowForPosition(secondaryCaret) != row) continue;
                        int localColumn = _editor.RenderedBoundaryForSourceColumn(renderedSegment, secondaryCaret - offset);
                        double x = textOrigin +
                            _editor.MeasureRenderedTextWidthForLine(renderedSegment, documentLine, localColumn);
                        dc.DrawRectangle(CaretForeground, null, new Rect(x, y + 2, 1.2, rowHeight - 4));
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
                        _editor.ApplyMarkdownListMarkerStyle(formatted, line, segment.SourceColumns);

                        double contentY = y + Math.Max(0, (rowHeight - formatted.Height) / 2);
                        dc.DrawText(formatted, new Point(contentX, contentY));
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

            private FormattedText MakeText(string value, Typeface typeface, Brush brush, double dpi, double fontSize)
            {
                return new FormattedText(value, CultureInfo.CurrentUICulture, _editor.FlowDirection,
                    typeface, fontSize, brush, dpi);
            }
        }
    }
}
