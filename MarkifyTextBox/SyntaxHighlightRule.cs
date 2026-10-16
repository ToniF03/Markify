using System;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace Markify.Textify
{
    /// <summary>A regular-expression based foreground rule applied to each visible line.</summary>
    public sealed class SyntaxHighlightRule
    {
        internal Regex Matcher { get; private set; }
        public Brush Foreground { get; private set; }

        public SyntaxHighlightRule(string pattern, Brush foreground)
        {
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            if (foreground == null) throw new ArgumentNullException(nameof(foreground));
            Matcher = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
            Foreground = foreground;
        }
    }
}
