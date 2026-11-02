using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Clio.Core;

/// <summary>
/// Asks GitHub, once per start, whether a newer Clio release exists. It never
/// downloads or installs anything: the hub shows a dot, and clicking it opens
/// the release page. Off with Settings, Check for updates.
///
/// Notes live in %APPDATA%\Clio, apart from the exe, so replacing the exe with
/// a newer one keeps every note; that is what makes a manual update safe.
/// </summary>
internal static class Updates
{
    private const string Latest = "https://api.github.com/repos/XRTC5/clio/releases/latest";

    public static Version Current =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);

    /// <summary>The newer release's version and page, or null (none newer, offline, or any failure).</summary>
    public static async Task<(Version Version, string Page)?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"Clio/{Current}");
            using var doc = JsonDocument.Parse(await http.GetStringAsync(Latest).ConfigureAwait(false));
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var page = doc.RootElement.GetProperty("html_url").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
            // Compare as major.minor.build; the assembly carries a fourth part.
            var mine = new Version(Current.Major, Current.Minor, Math.Max(0, Current.Build));
            var theirs = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
            return theirs > mine && page.StartsWith("https://github.com/") ? (theirs, page) : null;
        }
        catch (Exception ex)
        {
            Log.Info($"update check skipped: {ex.GetType().Name}");
            return null;
        }
    }
}
