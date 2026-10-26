using System.Net;
using System.Net.Http;
using System.Text.Json;
using Reticle.Core;

internal static class UpdateChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Release versions use SemVer precedence, not text or publish date", () =>
        {
            string[] ordered = ["0.2.1", "0.3.0-alpha", "0.3.0-alpha.1", "0.3.0-beta", "0.3.0-preview.2", "0.3.0-preview.10", "0.3.0", "0.10.0", "1.0.0"];
            for (int i = 1; i < ordered.Length; i++) Assert(Parse(ordered[i]).CompareTo(Parse(ordered[i - 1])) > 0);
            Assert(Parse("v0.3.0+build.1").CompareTo(Parse("0.3.0+build.2")) == 0);
            Assert(Parse("0.3.0-preview.999999999999999999999").CompareTo(Parse("0.3.0-preview.10")) > 0);
        });
        check("Malformed release versions are rejected", () =>
        {
            foreach (var value in new string?[] { null, "", "0.3", "01.3.0", "0.3.0-preview.01", "0.3.0-", "0.3.0-.", "0.3.0\n", "latest", "0.3.0+" })
                Assert(!ReleaseVersion.TryParse(value, out _));
        });
        check("Release check filters drafts and preview channels and ignores untrusted URLs", () =>
        {
            using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Json(new[]
            {
                Release("v0.3.0-preview.10", true), Release("v0.3.0-preview.2", true),
                Release("v99.0.0", false, true), Release("v0.2.1"), Release("v0.2.0"),
                Release("v4.0.0-preview.1"), Release("invalid")
            }))));
            var checker = new UpdateChecker(client);
            var stable = checker.GetLatestAsync(false).GetAwaiter().GetResult()!;
            var preview = checker.GetLatestAsync(true).GetAwaiter().GetResult()!;
            Assert(stable.Version.Display == "0.2.1" && preview.Version.Display == "4.0.0-preview.1");
            Assert(stable.Page.AbsoluteUri == UpdateChecker.ReleasesPage + "/tag/v0.2.1");
            Assert(stable.Version.CompareTo(Parse("0.3.0-preview.3")) < 0); // No downgrade prompt.
        });
        check("Release check follows pages and sends anonymous API headers", () =>
        {
            int calls = 0;
            using var client = new HttpClient(new Handler((request, _) =>
            {
                calls++;
                Assert(request.Headers.UserAgent.Count > 0 && request.Headers.Authorization is null);
                Assert(request.RequestUri!.Query.Contains("page=" + calls));
                return Task.FromResult(calls == 1 ? Json(Enumerable.Repeat(Release("v0.1.0"), 100)) : Json(new[] { Release("v0.2.1") }));
            }));
            Assert(new UpdateChecker(client).GetLatestAsync(false).GetAwaiter().GetResult()!.Version.Display == "0.2.1" && calls == 2);
        });
        check("Independent releases have their own update channel", () =>
        {
            using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Json(new[]
            {
                Release("v0.3.0-independent.1", true), Release("v0.3.0-independent.2", true),
                Release("v0.3.0-preview.99", true), Release("v9.0.0"), Release("v0.2.1")
            }))));
            var independent = new UpdateChecker(client, ReleaseChannel.Independent).GetLatestAsync(true).GetAwaiter().GetResult()!;
            Assert(independent.Version.Display == "0.3.0-independent.2" && independent.Version.IsIndependent);
            Assert(new UpdateChecker(client).GetLatestAsync(true).GetAwaiter().GetResult()!.Version.Display == "9.0.0");
            Assert(new UpdateChecker(client, ReleaseChannel.Independent).GetLatestAsync(false).GetAwaiter().GetResult() is null);
        });
        check("Empty release list produces no release", () =>
        {
            using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Json(Array.Empty<object>()))));
            Assert(new UpdateChecker(client).GetLatestAsync(true).GetAwaiter().GetResult() is null);
        });
        check("HTTP failures remain failures rather than reporting up to date", () =>
        {
            foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable })
            {
                using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status))));
                bool failed = false;
                try { new UpdateChecker(client).GetLatestAsync(true).GetAwaiter().GetResult(); }
                catch (HttpRequestException error) { failed = error.StatusCode == status; }
                Assert(failed);
            }
        });
        check("Malformed API responses remain failures", () =>
        {
            foreach (string json in new[] { "{broken", "{}", "null" })
            {
                using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) })));
                bool failed = false;
                try { new UpdateChecker(client).GetLatestAsync(true).GetAwaiter().GetResult(); }
                catch (Exception error) when (error is JsonException or InvalidDataException) { failed = true; }
                Assert(failed);
            }
        });
        check("Closing an update check cancels an in-flight request", () =>
        {
            using var cancel = new CancellationTokenSource();
            using var client = new HttpClient(new Handler(async (_, token) =>
            {
                cancel.Cancel();
                await Task.Delay(Timeout.Infinite, token);
                return Json(Array.Empty<object>());
            }));
            bool canceled = false;
            try { new UpdateChecker(client).GetLatestAsync(true, cancel.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { canceled = true; }
            Assert(canceled);
        });
    }

    private static object Release(string tag, bool preview = false, bool draft = false) => new { tag_name = tag, prerelease = preview, draft, html_url = "https://example.invalid/untrusted" };
    private static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data)) };
    private static ReleaseVersion Parse(string version) => ReleaseVersion.TryParse(version, out var parsed) ? parsed : throw new Exception("Invalid test version");
    private static void Assert(bool value) { if (!value) throw new Exception("Update assertion failed"); }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
