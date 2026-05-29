using System.Text.RegularExpressions;

namespace TicketSistemi.Utils
{
    public static class HtmlSanitizer
    {
        public static string Sanitize(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;

            // Remove script tags and their contents
            string sanitized = Regex.Replace(html, @"<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>", "", RegexOptions.IgnoreCase);

            // Remove inline event handlers (onmouseover, onload, onerror, onclick, etc.)
            sanitized = Regex.Replace(sanitized, @"\s+on\w+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)", "", RegexOptions.IgnoreCase);

            // Remove javascript: links
            sanitized = Regex.Replace(sanitized, @"href\s*=\s*(?:""\s*javascript:[^""]*""|'\s*javascript:[^']*'|javascript:[^\s>]+)", "", RegexOptions.IgnoreCase);

            return sanitized;
        }
    }
}
