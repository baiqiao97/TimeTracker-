using System.Text.RegularExpressions;

namespace TimeTracker
{
    public static class UrlParser
    {
        private static readonly Dictionary<string, Regex> BrowserPatterns = new()
        {
            ["chrome"] = new Regex(@"^(.+?)\s*[-–—]\s*Google Chrome$", RegexOptions.IgnoreCase),
            ["msedge"] = new Regex(@"^(.+?)\s*[-–—]\s*Microsoft[\s\u00A0]Edge$", RegexOptions.IgnoreCase),
            ["firefox"] = new Regex(@"^(.+?)\s*[-–—]\s*Mozilla Firefox$", RegexOptions.IgnoreCase),
            ["brave"] = new Regex(@"^(.+?)\s*[-–—]\s*Brave$", RegexOptions.IgnoreCase),
            ["opera"] = new Regex(@"^(.+?)\s*[-–—]\s*Opera$", RegexOptions.IgnoreCase),
        };

        public static string? TryExtractDomain(string processName, string windowTitle)
        {
            var key = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim().ToLower();
            if (!BrowserPatterns.TryGetValue(key, out var regex))
                return null;

            var match = regex.Match(windowTitle);
            if (!match.Success) return null;

            var title = match.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(title)) return null;

            // 如果标题是URL格式，提取域名
            if (Uri.TryCreate(title, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
                return uri.Host;

            // 否则返回清理后的标题
            return title.Length > 50 ? title[..47] + "…" : title;
        }
    }
}
