using System.Net.Http;
using System.Text.Json;

namespace Reticle.Core;

public sealed record ReleaseInfo(ReleaseVersion Version, Uri Page);

public enum ReleaseChannel { Standard, Independent }

public sealed class UpdateChecker(HttpClient client, ReleaseChannel channel = ReleaseChannel.Standard)
{
    public const string ReleasesPage = "https://github.com/yangr8640-eng/reticle-studio/releases";
    private const string Api = "https://api.github.com/repos/yangr8640-eng/reticle-studio/releases";

    public async Task<ReleaseInfo?> GetLatestAsync(bool includePrereleases, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        ReleaseInfo? latest = null;
        // Releases are ordered by publication, which need not match version precedence.
        for (int page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Api}?per_page=100&page={page}");
            request.Headers.UserAgent.ParseAdd("ReticleStudio-UpdateChecker/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("更新服务返回了无效的版本列表。");
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False
                    || !item.TryGetProperty("prerelease", out var pre) || pre.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                    || !item.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String
                    || !ReleaseVersion.TryParse(tag.GetString(), out var version)) continue;
                if (!includePrereleases && (pre.GetBoolean() || version.IsPrerelease)) continue;
                if (version.IsIndependent != (channel == ReleaseChannel.Independent)) continue;
                if (latest is null || version.CompareTo(latest.Version) > 0)
                    // Only navigate to this project's release pages, never an arbitrary response URL.
                    latest = new(version, new Uri(ReleasesPage + "/tag/" + Uri.EscapeDataString(tag.GetString()!)));
            }
            if (document.RootElement.GetArrayLength() < 100) return latest;
        }
    }
}
