using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace Markify.Textify
{
    public sealed partial class MarkifyTextBox
    {
        private void HandleTextInput(object sender, TextCompositionEventArgs e)
        {
            if (IsReadOnly || string.IsNullOrEmpty(e.Text)) return;
            var value = e.Text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (AutoCloseBrackets && value.Length == 1 && _secondaryCarets.Count == 0 &&
                _secondarySelections.Count == 0 && !_isRectangularSelection)
            {
                char ch = value[0];
                char closing = MatchingBracket(ch);
                if (IsOpeningBracket(ch))
                {
                    ReplaceSelection(new string(ch, 1) + new string(closing, 1));
                    _caret = Math.Max(0, _caret - 1);
                    _selectionAnchor = _caret;
                    Changed();
                    e.Handled = true;
                    return;
                }
                if (!IsOpeningBracket(ch) && _caret < Text.Length && Text[_caret] == ch && SelectionLength == 0)
                {
                    MoveTo(_caret + 1, false);
                    e.Handled = true;
                    return;
                }
            }
            ReplaceSelection(value);
            e.Handled = true;
        }

        private void HandleKeyDown(object sender, KeyEventArgs e)
        {
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            if (EnableWordWrapShortcut && alt && e.Key == Key.Z)
            { IsWordWrapEnabled = !IsWordWrapEnabled; e.Handled = true; return; }
            if (EnableLineOperations && control && shift && e.Key == Key.D7)
            { ToggleLineComment(); e.Handled = true; return; }
            if (EnableLineOperations && control && e.Key == Key.OemQuestion)
            { ToggleLineComment(); e.Handled = true; return; }
            if (EnableMultipleCarets && control && e.Key == Key.D)
            { SelectNextOccurrence(); e.Handled = true; return; }
            if (EnableLineOperations && control && shift && e.Key == Key.Up)
            { MoveCurrentLine(-1); e.Handled = true; return; }
            if (EnableLineOperations && control && shift && e.Key == Key.Down)
            { MoveCurrentLine(1); e.Handled = true; return; }
            if (EnableMultipleCarets && control && alt && e.Key == Key.Up)
            { AddCaretOnAdjacentVisibleLine(-1); e.Handled = true; return; }
            if (EnableMultipleCarets && control && alt && e.Key == Key.Down)
            { AddCaretOnAdjacentVisibleLine(1); e.Handled = true; return; }
            if (control)
            {
                switch (e.Key)
                {
                    case Key.A:
                        _isRectangularSelection = false;
                        _secondaryCarets.Clear();
                        _secondarySelections.Clear();
                        _selectionAnchor = 0; _caret = Text.Length;
                        Changed(); e.Handled = true; return;
                    case Key.C: Copy(); e.Handled = true; return;
                    case Key.X: if (!IsReadOnly) { Copy(); ReplaceSelection(string.Empty); } e.Handled = true; return;
                    case Key.V: if (!IsReadOnly) Paste(); e.Handled = true; return;
                    case Key.Z: if (!IsReadOnly) Undo(); e.Handled = true; return;
                    case Key.Y: if (!IsReadOnly) Redo(); e.Handled = true; return;
                    case Key.Home: MoveTo(0, shift); e.Handled = true; return;
                    case Key.End: MoveTo(Text.Length, shift); e.Handled = true; return;
                }
            }
            if (e.Key == Key.Back && !IsReadOnly) { Backspace(); e.Handled = true; return; }
            if (e.Key == Key.Delete && !IsReadOnly) { Delete(); e.Handled = true; return; }
            if (e.Key == Key.Enter && !IsReadOnly)
            {
                ReplaceSelection("\n");
                e.Handled = true; return;
            }
            if (e.Key == Key.Tab && EnableTabIndentation && !IsReadOnly)
            {
                if (shift) IndentSelectedLines(false);
                else if (SelectionLength > 0) IndentSelectedLines(true);
                else ReplaceSelection("    ");
                e.Handled = true; return;
            }
            if (e.Key == Key.Left) { MoveHorizontal(-1, shift, control); e.Handled = true; return; }
            if (e.Key == Key.Right) { MoveHorizontal(1, shift, control); e.Handled = true; return; }
            if (e.Key == Key.Home) { MoveTo(LineStart(_caret), shift); e.Handled = true; return; }
            if (e.Key == Key.End) { MoveTo(LineEnd(_caret), shift); e.Handled = true; return; }
            if (e.Key == Key.Up || e.Key == Key.Down) { MoveVertical(e.Key == Key.Up ? -1 : 1, shift); e.Handled = true; return; }
            if (e.Key == Key.PageUp || e.Key == Key.PageDown)
            {
                _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset + (e.Key == Key.PageUp ? -1 : 1) * _scroll.ViewportHeight);
                MoveVertical(e.Key == Key.PageUp ? -Math.Max(1, (int)(_scroll.ViewportHeight / LineHeight)) : Math.Max(1, (int)(_scroll.ViewportHeight / LineHeight)), shift);
                e.Handled = true; return;
            }
        }

        private void ReplaceSelection(string insertion)
        {
            if (_isRectangularSelection || _secondaryCarets.Count > 0 || _secondarySelections.Count > 0)
            {
                ReplaceMultipleCarets(insertion);
                return;
            }
            int start = SelectionStart, length = SelectionLength;
            string updated = Text.Remove(start, length).Insert(start, insertion);
            if (!string.Equals(updated, Text, StringComparison.Ordinal))
            {
                SaveUndo();
                Text = updated;
            }
            _caret = start + insertion.Length;
            _selectionAnchor = _caret;
            Changed();
        }

        private void Backspace()
        {
            if (_isRectangularSelection || _secondaryCarets.Count > 0 || _secondarySelections.Count > 0)
            { DeleteAtMultipleCarets(true); return; }
            if (SelectionLength > 0) { ReplaceSelection(string.Empty); return; }
            if (_caret == 0) return;
            if (IsMarkdownTablePipe(_caret - 1)) return;
            if (_caret > 1 && Text[_caret - 1] == '\n' && Text[_caret - 2] == '\r') _caret--;
            _selectionAnchor = _caret - 1; ReplaceSelection(string.Empty);
        }

        private void Delete()
        {
            if (_isRectangularSelection || _secondaryCarets.Count > 0 || _secondarySelections.Count > 0)
            { DeleteAtMultipleCarets(false); return; }
            if (SelectionLength > 0) { ReplaceSelection(string.Empty); return; }
            if (_caret >= Text.Length) return;
            if (IsMarkdownTablePipe(_caret)) return;
            _selectionAnchor = _caret + 1; ReplaceSelection(string.Empty);
        }

        private void MoveHorizontal(int direction, bool extend, bool byWord)
        {
            if (_secondaryCarets.Count > 0 || _secondarySelections.Count > 0 || _isRectangularSelection)
            {
                _secondaryCarets.Clear();
                _secondarySelections.Clear();
                _isRectangularSelection = false;
                if (!extend) _selectionAnchor = _caret;
            }
            if (!extend && SelectionLength > 0) { MoveTo(direction < 0 ? SelectionStart : SelectionStart + SelectionLength, false); return; }
            int tableDestination;
            if (!byWord && TryGetMarkdownTableHorizontalDestination(_caret, direction, out tableDestination))
            {
                MoveTo(tableDestination, extend);
                return;
            }
            int target = _caret + direction;
            if (byWord && target >= 0 && target <= Text.Length)
            {
                while (target > 0 && target < Text.Length && char.IsWhiteSpace(Text[target])) target += direction;
                while (target > 0 && target < Text.Length && !char.IsWhiteSpace(Text[target - (direction < 0 ? 1 : 0)])) target += direction;
            }
            MoveTo(Math.Max(0, Math.Min(Text.Length, target)), extend);
        }

        private void MoveVertical(int delta, bool extend)
        {
            int currentLine = LineIndexAt(_caret);
            int start = GetLineStart(currentLine);
            int currentRow = VisibleRowForPosition(_caret);
            if (_preferredColumn < 0)
            {
                _preferredColumn = Math.Max(0, _caret - start - RenderedStartColumnForRow(currentRow));
            }

            int targetRow = Math.Max(0, Math.Min(VisibleLineCount - 1, currentRow + delta));
            int targetLine = DocumentLineAtVisibleRow(targetRow);
            MarkdownTableLayout targetTable;
            while (targetRow != currentRow && TryGetMarkdownTableLayout(targetLine, out targetTable) &&
                targetTable.IsSeparator)
            {
                int nextRow = Math.Max(0, Math.Min(VisibleLineCount - 1, targetRow + (delta < 0 ? -1 : 1)));
                if (nextRow == targetRow) break;
                targetRow = nextRow;
                targetLine = DocumentLineAtVisibleRow(targetRow);
            }

            MarkdownTableLayout currentTable;
            if (TryGetMarkdownTableLayout(currentLine, out currentTable) && !currentTable.IsSeparator &&
                TryGetMarkdownTableLayout(targetLine, out targetTable) && !targetTable.IsSeparator)
            {
                int currentColumn = _caret - GetLineStart(currentLine);
                for (int column = 0; column < currentTable.Cells.Length; column++)
                {
                    MarkdownTableCell currentCell = currentTable.Cells[column];
                    if (currentColumn < currentCell.Start || currentColumn > currentCell.Start + currentCell.Length)
                        continue;
                    RenderedTextSegment currentSegment = MarkdownTableCellSegment(currentLine, currentTable, column);
                    int displayColumn = RenderedBoundaryForSourceColumn(currentSegment, currentColumn);
                    int tableTargetColumn = Math.Min(column, targetTable.Cells.Length - 1);
                    RenderedTextSegment targetSegment = MarkdownTableCellSegment(targetLine, targetTable, tableTargetColumn);
                    int targetDisplayColumn = Math.Min(displayColumn, targetSegment.Text.Length);
                    MoveTo(GetLineStart(targetLine) + targetSegment.SourceBoundaries[targetDisplayColumn], extend,
                        preservePreferredColumn: true);
                    return;
                }
            }
            int targetLineStart = GetLineStart(targetLine);
            int targetSegmentStart = Math.Max(VisibleRowStartColumn(targetRow), RenderedStartColumnForRow(targetRow));
            int targetSegmentLength = VisibleRowLength(targetRow);
            int targetSegmentEnd = targetSegmentStart + targetSegmentLength;
            int targetColumn = Math.Min(targetSegmentEnd, targetSegmentStart + _preferredColumn);
            MoveTo(targetLineStart + targetColumn, extend, preservePreferredColumn: true);
        }

        private void MoveTo(int position, bool extend, bool preservePreferredColumn = false)
        {
            _secondaryCarets.Clear();
            _secondarySelections.Clear();
            _isRectangularSelection = false;
            int bounded = Math.Max(0, Math.Min(Text.Length, position));
            int normalized = NormalizeMarkdownTableCaretPosition(bounded, _caret);
            if (extend)
                normalized = ClampMarkdownTablePositionToCell(_selectionAnchor, normalized);
            _caret = normalized;
            if (!extend)
                _selectionAnchor = _caret;
            if (!preservePreferredColumn) _preferredColumn = -1;
            Changed(); EnsureCaretVisible();
        }

        private int LineStart(int index) { return GetLineStart(LineIndexAt(index)); }
        private int LineEnd(int index) { return GetLineEndAtLine(LineIndexAt(index)); }

        private void SaveUndo()
        {
            _undo.Add(CaptureEditSnapshot());
            if (_undo.Count > 300) _undo.RemoveAt(0);
            _redo.Clear();
        }

        private EditSnapshot CaptureEditSnapshot()
        {
            return new EditSnapshot(Text, _caret, _selectionAnchor,
                new List<int>(_secondaryCarets), new List<SelectionSpan>(_secondarySelections),
                _isRectangularSelection, _rectangleAnchorLine, _rectangleAnchorColumn,
                _rectangleEndLine, _rectangleEndColumn);
        }

        private void Undo()
        {
            if (_undo.Count == 0) return;
            _redo.Add(CaptureEditSnapshot());
            var state = _undo[_undo.Count - 1]; _undo.RemoveAt(_undo.Count - 1);
            Restore(state);
        }

        private void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Add(CaptureEditSnapshot());
            var state = _redo[_redo.Count - 1]; _redo.RemoveAt(_redo.Count - 1);
            Restore(state);
        }

        private void Restore(EditSnapshot state)
        {
            Text = state.Text;
            RebuildLineIndex();
            _caret = state.Caret; _selectionAnchor = state.Anchor;
            _secondaryCarets.Clear();
            if (EnableMultipleCarets && state.SecondaryCarets != null) _secondaryCarets.AddRange(state.SecondaryCarets);
            _secondarySelections.Clear();
            if (EnableMultipleCarets && state.SecondarySelections != null) _secondarySelections.AddRange(state.SecondarySelections);
            _isRectangularSelection = EnableRectangularSelection && state.IsRectangularSelection;
            _rectangleAnchorLine = state.RectangleAnchorLine;
            _rectangleAnchorColumn = state.RectangleAnchorColumn;
            _rectangleEndLine = state.RectangleEndLine;
            _rectangleEndColumn = state.RectangleEndColumn;
            Changed(); EnsureCaretVisible();
        }

        private void Copy()
        {
            if (HasSelection) Clipboard.SetText(GetSelectedText());
        }

        private void Paste()
        {
            if (!Clipboard.ContainsText()) return;
            ReplaceSelection(Clipboard.GetText().Replace("\r\n", "\n").Replace('\r', '\n'));
        }

        private void HandleMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            ModifierKeys modifiers = Keyboard.Modifiers;
            bool extendSelection = (modifiers & ModifierKeys.Shift) != 0;
            bool rectangle = EnableRectangularSelection &&
                (modifiers & (ModifierKeys.Alt | ModifierKeys.Shift)) == (ModifierKeys.Alt | ModifierKeys.Shift);
            bool addCaret = EnableMultipleCarets &&
                (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == (ModifierKeys.Control | ModifierKeys.Alt);
            Focus(); _surface.Focus();
            Point point = e.GetPosition(_surface);
            if (rectangle)
            {
                int position = PositionFromPoint(point);
                _isRectangularSelection = true;
                _secondaryCarets.Clear();
                _secondarySelections.Clear();
                _rectangleAnchorLine = _rectangleEndLine = LineIndexAt(position);
                _rectangleAnchorColumn = _rectangleEndColumn = position - GetLineStart(_rectangleAnchorLine);
                _selectionAnchor = _caret = position;
                _preferredColumn = -1;
                _surface.CaptureMouse(); Changed(); e.Handled = true; return;
            }
            if (addCaret)
            {
                int position = PositionFromPoint(point);
                _isRectangularSelection = false;
                AddCaret(position);
                _surface.CaptureMouse(); e.Handled = true; return;
            }
            _isRectangularSelection = false;
            _secondaryCarets.Clear();
            _secondarySelections.Clear();
            _preferredColumn = -1;
            _caret = PositionFromPoint(point);
            if (extendSelection)
                _caret = ClampMarkdownTablePositionToCell(_selectionAnchor, _caret);
            if (!extendSelection)
                _selectionAnchor = _caret;
            _surface.CaptureMouse(); Changed(); e.Handled = true;
        }

        private void HandleMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _surface.IsMouseCaptured)
            {
                _surface.Cursor = Cursors.IBeam;
                Point point = e.GetPosition(_surface);
                if (_isRectangularSelection)
                {
                    UpdateRectangleFromPosition(PositionFromPoint(point));
                    RefreshMarkdownTableSourceVisibility();
                    return;
                }
                _caret = ClampMarkdownTablePositionToCell(_selectionAnchor, PositionFromPoint(point));
                RefreshMarkdownTableSourceVisibility();
                _surface.InvalidateMeasure();
                _surface.InvalidateVisual();
                UpdateMarkdownVideoPlayerLayout();
                PublishSelectionState();
            }
            else
            {
                _surface.Cursor = Cursors.IBeam;
            }
        }

        private int PositionFromPoint(Point point)
        {
            int row = VisibleRowAtY(point.Y);
            int line = DocumentLineAtVisibleRow(row);
            int segmentStart = VisibleRowStartColumn(row);
            int segmentEnd = segmentStart + VisibleRowLength(row);
            int renderedStart = RenderedStartColumnForRow(row);
            int lineStart = GetLineStart(line);
            if (IsMarkdownHorizontalRuleLine(line) && !MarkdownHorizontalRuleSourceIsVisible(line))
            {
                int sourceLength = Math.Max(0, GetLineEndAtLine(line) - GetLineStart(line));
                double ruleWidth = Math.Max(1, _surface.ActualWidth - TextOrigin * 2);
                double relativePosition = Math.Max(0, Math.Min(1, (point.X - TextOrigin) / ruleWidth));
                return lineStart + (int)Math.Round(relativePosition * sourceLength);
            }
            MarkdownTableLayout tableLayout;
            if (TryGetMarkdownTableLayout(line, out tableLayout) && !tableLayout.IsSourceVisible)
                return PositionFromMarkdownTablePoint(line, point.X, tableLayout);
            int clampedSegmentEnd = Math.Min(segmentEnd, GetLineEndAtLine(line) - lineStart);
            RenderedTextSegment renderedSegment = CreateRenderedTextSegment(line, renderedStart, clampedSegmentEnd);
            string visibleLine = renderedSegment.Text;
            double targetX = Math.Max(0, point.X - TextOriginForLine(line));
            int low = 0, high = visibleLine.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (MeasureRenderedTextWidthForLine(renderedSegment, line, mid) <= targetX) low = mid;
                else high = mid - 1;
            }
            int col = low;
            if (col < visibleLine.Length)
            {
                double left = MeasureRenderedTextWidthForLine(renderedSegment, line, col);
                double right = MeasureRenderedTextWidthForLine(renderedSegment, line, col + 1);
                if (targetX - left > (right - left) / 2) col++;
            }
            return lineStart + renderedSegment.SourceBoundaries[col];
        }

        private void HandleMouseUp(object sender, MouseButtonEventArgs e) { _surface.ReleaseMouseCapture(); }

        private void Changed()
        {
            _caretVisible = true; _surface.InvalidateMeasure(); _surface.InvalidateVisual();
            RefreshMarkdownTableSourceVisibility();
            UpdateMarkdownVideoPlayerLayout(); EnsureCaretVisible(); PublishSelectionState();
        }

        private void EnsureCaretVisible()
        {
            if (_surface == null || _scroll == null) return;
            int lineStart = LineStart(_caret);
            int lineEnd = LineEnd(lineStart);
            int visibleRow = VisibleRowForPosition(_caret);
            int line = LineIndexAt(_caret);
            int renderedStart = RenderedStartColumnForRow(visibleRow);
            int segmentEnd = Math.Min(VisibleRowStartColumn(visibleRow) + VisibleRowLength(visibleRow), lineEnd - lineStart);
            RenderedTextSegment renderedSegment = CreateRenderedTextSegment(line, renderedStart, segmentEnd);
            int displayColumn = RenderedBoundaryForSourceColumn(renderedSegment, _caret - lineStart);
            double x = TextOriginForLine(line) + MeasureRenderedTextWidthForLine(renderedSegment, line, displayColumn);
            double y = TopInset + VisibleRowTop(visibleRow);
            double rowHeight = LineHeightForVisibleRow(visibleRow);
            if (x < _scroll.HorizontalOffset) _scroll.ScrollToHorizontalOffset(x);
            else if (x + CharacterWidth > _scroll.HorizontalOffset + _scroll.ViewportWidth) _scroll.ScrollToHorizontalOffset(x + CharacterWidth - _scroll.ViewportWidth);
            if (y < _scroll.VerticalOffset) _scroll.ScrollToVerticalOffset(y);
            else if (y + rowHeight > _scroll.VerticalOffset + _scroll.ViewportHeight) _scroll.ScrollToVerticalOffset(y + rowHeight - _scroll.ViewportHeight);
        }
    }
}
