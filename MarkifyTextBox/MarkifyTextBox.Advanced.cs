using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Markify.Textify
{
    public sealed partial class MarkifyTextBox
    {
        private bool _publishingSelectionState;

        internal struct SelectionSpan
        {
            public int Start;
            public int Length;
            public SelectionSpan(int start, int length) { Start = start; Length = length; }
        }

        private void InitializeAdvancedUi()
        {
            if (_surface == null) return;
            if (!EnableEditorContextMenu)
            {
                _surface.ContextMenu = null;
                return;
            }
            var menu = new ContextMenu();
            AddMenuItem(menu, "Copy", Copy);
            AddMenuItem(menu, "Copy line", CopyCurrentLine);
            if (EnableLineOperations)
            {
                AddMenuItem(menu, "Move line up", () => MoveCurrentLine(-1));
                AddMenuItem(menu, "Move line down", () => MoveCurrentLine(1));
                AddMenuItem(menu, "Toggle line comment", ToggleLineComment);
            }
            menu.Items.Add(new Separator());
            var wrapItem = new MenuItem { Header = "Word wrap", IsCheckable = true };
            wrapItem.Click += (s, e) => IsWordWrapEnabled = wrapItem.IsChecked;
            menu.Items.Add(wrapItem);
            var horizontalScrollItem = new MenuItem { Header = "Horizontal scrolling", IsCheckable = true };
            horizontalScrollItem.Click += (s, e) => IsHorizontalScrollEnabled = horizontalScrollItem.IsChecked;
            menu.Items.Add(horizontalScrollItem);
            menu.Opened += (s, e) =>
            {
                foreach (var item in menu.Items)
                {
                    var menuItem = item as MenuItem;
                    if (menuItem == null) continue;
                    if ((string)menuItem.Header == "Copy") menuItem.IsEnabled = HasSelection;
                    else if ((string)menuItem.Header == "Word wrap") menuItem.IsChecked = IsWordWrapEnabled;
                    else if ((string)menuItem.Header == "Horizontal scrolling") menuItem.IsChecked = IsHorizontalScrollEnabled;
                    else if ((string)menuItem.Header == "Move line up" || (string)menuItem.Header == "Move line down" ||
                        (string)menuItem.Header == "Toggle line comment") menuItem.IsEnabled = EnableLineOperations && !IsReadOnly;
                }
            };
            _surface.ContextMenu = menu;
        }

        internal IList<int> SecondaryCarets { get { return _secondaryCarets; } }

        internal IList<int> AdditionalCaretPositions
        {
            get
            {
                var positions = new List<int>(_secondaryCarets);
                if (_isRectangularSelection && _rectangleAnchorColumn == _rectangleEndColumn)
                {
                    int first = Math.Min(_rectangleAnchorLine, _rectangleEndLine);
                    int last = Math.Max(_rectangleAnchorLine, _rectangleEndLine);
                    for (int line = first; line <= last; line++)
                    {
                        int lineStart = GetLineStart(line);
                        int position = lineStart + Math.Min(_rectangleEndColumn, GetLineEndAtLine(line) - lineStart);
                        if (position != _caret && !positions.Contains(position)) positions.Add(position);
                    }
                }
                return positions;
            }
        }

        internal void ApplySyntaxHighlighting(FormattedText formatted, string line, int documentLine)
        {
            if (!EnableSyntaxHighlighting) return;
            foreach (SyntaxHighlightRule rule in SyntaxHighlighting)
            {
                foreach (Match match in rule.Matcher.Matches(line))
                {
                    if (match.Length > 0 && match.Index + match.Length <= line.Length)
                        formatted.SetForegroundBrush(rule.Foreground, match.Index, match.Length);
                }
            }
        }

        private static void AddMenuItem(ContextMenu menu, string title, Action action)
        {
            var item = new MenuItem { Header = title };
            item.Click += (s, e) => action();
            menu.Items.Add(item);
        }

        public bool HasSelection
        {
            get
            {
                return SelectionLength > 0 || _secondarySelections.Count > 0 ||
                    (_isRectangularSelection && (_rectangleAnchorLine != _rectangleEndLine || _rectangleAnchorColumn != _rectangleEndColumn));
            }
        }

        internal bool IsWholeLineSelected(int line)
        {
            if (SelectionLength == 0 || line < 0 || line >= LineCount) return false;
            int lineStart = GetLineStart(line);
            int lineEnd = line + 1 < LineCount ? GetLineStart(line + 1) : Text.Length;
            return SelectionStart <= lineStart && SelectionEnd >= lineEnd;
        }

        public bool IsRectangularSelection { get { return _isRectangularSelection; } }
        public int SelectionEnd { get { return SelectionStart + SelectionLength; } }
        public IList<int> SecondaryCaretOffsets { get { return _secondaryCarets.AsReadOnly(); } }

        public void Select(int start, int length)
        {
            int boundedStart = Math.Max(0, Math.Min(Text.Length, start));
            int boundedEnd = Math.Max(boundedStart, Math.Min(Text.Length, start + length));
            _selectionAnchor = boundedStart;
            _caret = boundedEnd;
            _isRectangularSelection = false;
            _secondaryCarets.Clear();
            _secondarySelections.Clear();
            Changed();
        }

        public void AddCaret(int offset)
        {
            if (!EnableMultipleCarets) return;
            int position = Math.Max(0, Math.Min(Text.Length, offset));
            if (position == _caret || _secondaryCarets.Contains(position)) return;
            _isRectangularSelection = false;
            _secondaryCarets.Add(position);
            _secondaryCarets.Sort();
            _surface.InvalidateMeasure();
            _surface.InvalidateVisual();
            PublishSelectionState();
        }

        public void ClearSecondaryCarets()
        {
            if (_secondaryCarets.Count == 0 && _secondarySelections.Count == 0) return;
            _secondaryCarets.Clear();
            _secondarySelections.Clear();
            _surface.InvalidateMeasure();
            _surface.InvalidateVisual();
            PublishSelectionState();
        }

        private void SetCaret(int position, bool extend)
        {
            _isRectangularSelection = false;
            MoveTo(position, extend);
        }

        private void PublishSelectionState()
        {
            if (_publishingSelectionState) return;
            _publishingSelectionState = true;
            try
            {
                if (_lastPublishedCaret != _caret)
                {
                    _lastPublishedCaret = _caret;
                    if (CaretPositionChanged != null) CaretPositionChanged(this, EventArgs.Empty);
                }
                int secondarySelectionCount = _secondaryCarets.Count + _secondarySelections.Count;
                var secondaryStateBuilder = new StringBuilder();
                foreach (int position in _secondaryCarets) secondaryStateBuilder.Append(position).Append(',');
                secondaryStateBuilder.Append('|');
                foreach (SelectionSpan span in _secondarySelections)
                    secondaryStateBuilder.Append(span.Start).Append(':').Append(span.Length).Append(',');
                string secondaryState = secondaryStateBuilder.ToString();
                if (_lastPublishedSelectionStart != SelectionStart || _lastPublishedSelectionLength != SelectionLength ||
                    _lastPublishedSecondaryCaretCount != secondarySelectionCount || _lastPublishedSecondaryState != secondaryState ||
                    _lastPublishedRectangle != _isRectangularSelection)
                {
                    _lastPublishedSelectionStart = SelectionStart;
                    _lastPublishedSelectionLength = SelectionLength;
                    _lastPublishedSecondaryCaretCount = secondarySelectionCount;
                    _lastPublishedSecondaryState = secondaryState;
                    _lastPublishedRectangle = _isRectangularSelection;
                    if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
                }
            }
            finally { _publishingSelectionState = false; }
        }

        internal List<SelectionSpan> GetSelectionSpansForLine(int line)
        {
            var spans = new List<SelectionSpan>();
            if (_isRectangularSelection)
            {
                int firstLine = Math.Min(_rectangleAnchorLine, _rectangleEndLine);
                int lastLine = Math.Max(_rectangleAnchorLine, _rectangleEndLine);
                if (line < firstLine || line > lastLine) return spans;
                int firstColumn = Math.Min(_rectangleAnchorColumn, _rectangleEndColumn);
                int lastColumn = Math.Max(_rectangleAnchorColumn, _rectangleEndColumn);
                int length = GetLineEndAtLine(line) - GetLineStart(line);
                int start = GetLineStart(line) + Math.Min(firstColumn, length);
                int end = GetLineStart(line) + Math.Min(lastColumn, length);
                if (end > start) spans.Add(new SelectionSpan(start, end - start));
                return spans;
            }
            if (SelectionLength > 0)
            {
                int lineStart = GetLineStart(line);
                int lineEnd = GetLineEndAtLine(line);
                int start = Math.Max(lineStart, SelectionStart);
                int end = Math.Min(lineEnd, SelectionStart + SelectionLength);
                if (end > start) spans.Add(new SelectionSpan(start, end - start));
            }
            foreach (SelectionSpan secondary in _secondarySelections)
            {
                int lineStart = GetLineStart(line), lineEnd = GetLineEndAtLine(line);
                int start = Math.Max(lineStart, secondary.Start);
                int end = Math.Min(lineEnd, secondary.Start + secondary.Length);
                if (end > start) spans.Add(new SelectionSpan(start, end - start));
            }
            return spans;
        }

        public string GetSelectedText()
        {
            if (!_isRectangularSelection)
            {
                var selected = new List<string>();
                if (SelectionLength > 0) selected.Add(Text.Substring(SelectionStart, SelectionLength));
                selected.AddRange(_secondarySelections.OrderBy(span => span.Start).Select(span => Text.Substring(span.Start, span.Length)));
                return string.Join(Environment.NewLine, selected);
            }
            var parts = new List<string>();
            for (int line = Math.Min(_rectangleAnchorLine, _rectangleEndLine); line <= Math.Max(_rectangleAnchorLine, _rectangleEndLine); line++)
            {
                List<SelectionSpan> spans = GetSelectionSpansForLine(line);
                parts.Add(spans.Count == 0 ? string.Empty : Text.Substring(spans[0].Start, spans[0].Length));
            }
            return string.Join(Environment.NewLine, parts);
        }

        public bool SelectNextOccurrence()
        {
            if (!EnableMultipleCarets || _isRectangularSelection || SelectionLength == 0) return false;
            string needle = Text.Substring(SelectionStart, SelectionLength);
            int from = Math.Min(Text.Length, SelectionStart + SelectionLength);
            if (_secondarySelections.Count > 0)
                from = Math.Max(from, _secondarySelections.Max(span => span.Start + span.Length));
            int index = from < Text.Length ? Text.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase) : -1;
            if (index < 0) index = Text.IndexOf(needle, 0, StringComparison.OrdinalIgnoreCase);
            if (index < 0 || index == SelectionStart) return false;
            if (!_secondarySelections.Any(span => span.Start == index && span.Length == needle.Length))
                _secondarySelections.Add(new SelectionSpan(index, needle.Length));
            _surface.InvalidateVisual();
            PublishSelectionState();
            return true;
        }

        private void CopyCurrentLine()
        {
            int line = LineIndexAt(_caret);
            Clipboard.SetText(Text.Substring(GetLineStart(line), GetLineEndAtLine(line) - GetLineStart(line)));
        }

        private void MoveCurrentLine(int delta)
        {
            if (!EnableLineOperations || IsReadOnly) return;
            int line = LineIndexAt(_caret), target = line + delta;
            if (target < 0 || target >= LineCount) return;
            int previousLine = Math.Min(line, target);
            int start = GetLineStart(previousLine);
            int end = Math.Max(line, target) + 1 < LineCount ? GetLineStart(Math.Max(line, target) + 1) : Text.Length;
            int currentStart = GetLineStart(line);
            int targetStart = GetLineStart(target);
            string currentContent = Text.Substring(currentStart, GetLineEndAtLine(line) - currentStart);
            string targetContent = Text.Substring(targetStart, GetLineEndAtLine(target) - targetStart);
            string currentSeparator = line + 1 < LineCount
                ? Text.Substring(GetLineEndAtLine(line), GetLineStart(line + 1) - GetLineEndAtLine(line)) : string.Empty;
            string targetSeparator = target + 1 < LineCount
                ? Text.Substring(GetLineEndAtLine(target), GetLineStart(target + 1) - GetLineEndAtLine(target)) : string.Empty;
            string result = delta < 0
                ? currentContent + targetSeparator + targetContent + currentSeparator
                : targetContent + currentSeparator + currentContent + targetSeparator;
            string updated = Text.Remove(start, end - start).Insert(start, result);
            if (string.Equals(updated, Text, StringComparison.Ordinal)) return;
            int column = _caret - currentStart;
            SaveUndo();
            Text = updated;
            int movedLineStart = delta < 0 ? start : start + targetContent.Length + currentSeparator.Length;
            _caret = Math.Max(0, Math.Min(Text.Length, movedLineStart + column));
            _selectionAnchor = _caret;
            Changed();
        }

        private void AddCaretOnAdjacentVisibleLine(int delta)
        {
            int currentRow = VisibleRowForPosition(_caret);
            int targetRow = Math.Max(0, Math.Min(VisibleLineCount - 1, currentRow + delta));
            int targetLine = DocumentLineAtVisibleRow(targetRow);
            int currentColumn = _caret - GetLineStart(LineIndexAt(_caret)) - VisibleRowStartColumn(currentRow);
            int targetColumn = VisibleRowStartColumn(targetRow) + Math.Min(VisibleRowLength(targetRow), currentColumn);
            int targetPosition = GetLineStart(targetLine) + Math.Min(GetLineEndAtLine(targetLine) - GetLineStart(targetLine), targetColumn);
            AddCaret(targetPosition);
        }

        private void ToggleLineComment()
        {
            if (!EnableLineOperations || IsReadOnly) return;
            int firstLine = SelectionLength == 0 ? LineIndexAt(_caret) : LineIndexAt(SelectionStart);
            int lastLine = SelectionLength == 0 ? firstLine : LineIndexAt(SelectionStart + SelectionLength);
            if (lastLine > firstLine && SelectionStart + SelectionLength == GetLineStart(lastLine)) lastLine--;
            bool remove = true;
            for (int line = firstLine; line <= lastLine; line++)
            {
                string value = _lineCache[line].TrimStart();
                if (!value.StartsWith("//", StringComparison.Ordinal)) { remove = false; break; }
            }
            var edits = new List<SelectionSpan>();
            for (int line = firstLine; line <= lastLine; line++)
            {
                int start = GetLineStart(line);
                string raw = _lineCache[line];
                int indent = raw.Length - raw.TrimStart().Length;
                edits.Add(new SelectionSpan(start + indent, remove ? 2 : 0));
            }
            ApplyEditSpans(edits, remove ? string.Empty : "//", _caret);
        }

        private int FindMatchingBracketPosition()
        {
            if (ReferenceEquals(_matchingBracketCacheText, Text) && _matchingBracketCacheCaret == _caret)
                return _matchingBracketCacheResult;
            int candidate = _caret < Text.Length && IsBracket(Text[_caret]) ? _caret : _caret - 1;
            if (candidate < 0 || candidate >= Text.Length || !IsBracket(Text[candidate]))
            {
                _matchingBracketCacheText = Text;
                _matchingBracketCacheCaret = _caret;
                _matchingBracketCacheResult = -1;
                return -1;
            }
            bool forward = IsOpeningBracket(Text[candidate]);
            int direction = forward ? 1 : -1;
            var stack = new List<char>();
            int result = -1;
            for (int i = candidate; i >= 0 && i < Text.Length; i += direction)
            {
                char current = Text[i];
                if (forward && IsOpeningBracket(current)) stack.Add(current);
                else if (forward && IsClosingBracket(current))
                {
                    if (stack.Count == 0 || MatchingBracket(stack[stack.Count - 1]) != current) continue;
                    stack.RemoveAt(stack.Count - 1);
                    if (stack.Count == 0) { result = i; break; }
                }
                else if (!forward && IsClosingBracket(current)) stack.Add(current);
                else if (!forward && IsOpeningBracket(current))
                {
                    if (stack.Count == 0 || MatchingBracket(current) != stack[stack.Count - 1]) continue;
                    stack.RemoveAt(stack.Count - 1);
                    if (stack.Count == 0) { result = i; break; }
                }
            }
            _matchingBracketCacheText = Text;
            _matchingBracketCacheCaret = _caret;
            _matchingBracketCacheResult = result;
            return result;
        }

        private static bool IsBracket(char c) { return c == '(' || c == ')' || c == '[' || c == ']' || c == '{' || c == '}'; }
        private static bool IsOpeningBracket(char c) { return c == '(' || c == '[' || c == '{'; }
        private static bool IsClosingBracket(char c) { return c == ')' || c == ']' || c == '}'; }
        private static char MatchingBracket(char c)
        {
            switch (c) { case '(': return ')'; case ')': return '('; case '[': return ']'; case ']': return '['; case '{': return '}'; default: return '{'; }
        }

        private void UpdateRectangleFromPosition(int position)
        {
            _rectangleEndLine = LineIndexAt(position);
            _rectangleEndColumn = position - GetLineStart(_rectangleEndLine);
            _caret = position;
            _surface.InvalidateVisual();
            PublishSelectionState();
        }

        private void ApplyEditSpans(List<SelectionSpan> spans, string insertion, int activePosition)
        {
            if (spans == null || spans.Count == 0) return;
            spans = spans.Where(s => s.Start >= 0 && s.Start <= Text.Length && s.Length >= 0)
                .GroupBy(s => s.Start).Select(g => new SelectionSpan(g.Key, g.Max(s => s.Length)))
                .OrderBy(s => s.Start).ToList();
            int primaryStart = spans.OrderBy(s => Math.Abs(s.Start - activePosition)).First().Start;
            int primaryIndex = spans.FindIndex(s => s.Start == primaryStart);
            if (primaryIndex < 0) primaryIndex = 0;
            var builder = new StringBuilder(Text);
            for (int i = spans.Count - 1; i >= 0; i--)
            {
                SelectionSpan span = spans[i];
                int length = Math.Min(span.Length, builder.Length - span.Start);
                builder.Remove(span.Start, length);
                builder.Insert(span.Start, insertion);
            }
            string updated = builder.ToString();
            int low = spans[0].Start;
            int high = spans.Max(s => s.Start + s.Length);
            int insertedEnd = high;
            foreach (SelectionSpan span in spans) insertedEnd += insertion.Length - span.Length;
            insertedEnd = Math.Max(low, Math.Min(updated.Length, insertedEnd));
            if (!string.Equals(updated, Text, StringComparison.Ordinal))
            {
                SaveUndo();
                Text = updated;
            }

            var newCarets = new List<int>();
            for (int i = 0; i < spans.Count; i++)
            {
                int shift = 0;
                for (int j = 0; j < i; j++) shift += insertion.Length - spans[j].Length;
                newCarets.Add(Math.Max(0, Math.Min(Text.Length, spans[i].Start + shift + insertion.Length)));
            }
            _caret = newCarets[primaryIndex];
            _selectionAnchor = _caret;
            _isRectangularSelection = false;
            _rectangleAnchorLine = _rectangleEndLine = 0;
            _secondaryCarets.Clear();
            _secondarySelections.Clear();
            for (int i = 0; i < newCarets.Count; i++)
                if (i != primaryIndex && newCarets[i] != _caret && !_secondaryCarets.Contains(newCarets[i]))
                    _secondaryCarets.Add(newCarets[i]);
            _secondaryCarets.Sort();
            Changed();
        }

        private void ReplaceMultipleCarets(string insertion)
        {
            var spans = new List<SelectionSpan>();
            if (_isRectangularSelection)
            {
                int firstLine = Math.Min(_rectangleAnchorLine, _rectangleEndLine);
                int lastLine = Math.Max(_rectangleAnchorLine, _rectangleEndLine);
                int firstColumn = Math.Min(_rectangleAnchorColumn, _rectangleEndColumn);
                int lastColumn = Math.Max(_rectangleAnchorColumn, _rectangleEndColumn);
                for (int line = firstLine; line <= lastLine; line++)
                {
                    int lineLength = GetLineEndAtLine(line) - GetLineStart(line);
                    int start = GetLineStart(line) + Math.Min(firstColumn, lineLength);
                    int end = GetLineStart(line) + Math.Min(lastColumn, lineLength);
                    spans.Add(new SelectionSpan(start, end - start));
                }
            }
            else
            {
                if (SelectionLength > 0) spans.Add(new SelectionSpan(SelectionStart, SelectionLength));
                else spans.Add(new SelectionSpan(_caret, 0));
                spans.AddRange(_secondarySelections);
                foreach (int position in _secondaryCarets) spans.Add(new SelectionSpan(position, 0));
            }
            if (spans.Count == 0) return;
            ApplyEditSpans(spans, insertion, _caret);
        }

        private void DeleteAtMultipleCarets(bool backward)
        {
            var spans = new List<SelectionSpan>();
            if (_isRectangularSelection)
            {
                int firstLine = Math.Min(_rectangleAnchorLine, _rectangleEndLine);
                int lastLine = Math.Max(_rectangleAnchorLine, _rectangleEndLine);
                int firstColumn = Math.Min(_rectangleAnchorColumn, _rectangleEndColumn);
                int lastColumn = Math.Max(_rectangleAnchorColumn, _rectangleEndColumn);
                for (int line = firstLine; line <= lastLine; line++)
                {
                    int lineStart = GetLineStart(line), lineLength = GetLineEndAtLine(line) - lineStart;
                    int from = Math.Min(firstColumn, lineLength), to = Math.Min(lastColumn, lineLength);
                    if (to > from) spans.Add(new SelectionSpan(lineStart + from, to - from));
                    else if (backward && from > 0) spans.Add(new SelectionSpan(lineStart + from - 1, 1));
                    else if (!backward && from < lineLength) spans.Add(new SelectionSpan(lineStart + from, 1));
                }
            }
            else
            {
                if (SelectionLength > 0) spans.Add(new SelectionSpan(SelectionStart, SelectionLength));
                else if (backward && _caret > 0) spans.Add(new SelectionSpan(_caret - 1, 1));
                else if (!backward && _caret < Text.Length) spans.Add(new SelectionSpan(_caret, 1));
                foreach (int position in _secondaryCarets)
                {
                    if (backward && position > 0) spans.Add(new SelectionSpan(position - 1, 1));
                    else if (!backward && position < Text.Length) spans.Add(new SelectionSpan(position, 1));
                }
                spans.AddRange(_secondarySelections);
            }
            ApplyEditSpans(spans, string.Empty, _caret);
        }

        private void IndentSelectedLines(bool indent)
        {
            int firstLine = _isRectangularSelection ? Math.Min(_rectangleAnchorLine, _rectangleEndLine) :
                (SelectionLength == 0 ? LineIndexAt(_caret) : LineIndexAt(SelectionStart));
            int lastLine = _isRectangularSelection ? Math.Max(_rectangleAnchorLine, _rectangleEndLine) :
                (SelectionLength == 0 ? firstLine : LineIndexAt(SelectionStart + SelectionLength));
            if (lastLine > firstLine && SelectionStart + SelectionLength == GetLineStart(lastLine)) lastLine--;
            var edits = new List<SelectionSpan>();
            for (int line = firstLine; line <= lastLine; line++)
            {
                int start = GetLineStart(line);
                if (indent) edits.Add(new SelectionSpan(start, 0));
                else
                {
                    string value = _lineCache[line];
                    int count = value.StartsWith("\t", StringComparison.Ordinal) ? 1 :
                        Math.Min(4, value.TakeWhile(ch => ch == ' ').Count());
                    if (count > 0) edits.Add(new SelectionSpan(start, count));
                }
            }
            if (edits.Count == 0) return;
            int oldCaret = _caret, oldAnchor = _selectionAnchor;
            bool wasRectangle = _isRectangularSelection;
            ApplyEditSpans(edits, indent ? "    " : string.Empty, oldCaret);
            if (!wasRectangle)
            {
                _caret = TranslatePositionAfterEdits(oldCaret, edits, indent);
                _selectionAnchor = TranslatePositionAfterEdits(oldAnchor, edits, indent);
                _secondaryCarets.Clear();
                Changed();
            }
        }

        private int TranslatePositionAfterEdits(int position, IList<SelectionSpan> edits, bool indent)
        {
            int translated = position;
            foreach (SelectionSpan edit in edits)
            {
                int delta = indent ? 4 : -edit.Length;
                if (edit.Start < position || (edit.Start == position && delta > 0)) translated += delta;
            }
            return Math.Max(0, Math.Min(Text.Length, translated));
        }
    }
}
