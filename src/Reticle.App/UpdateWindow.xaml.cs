using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using Reticle.Core;

namespace Reticle.App;

public partial class UpdateWindow : Window
{
    private static readonly HttpClient Client = new();
    private readonly UpdateChecker checker;
    private readonly CancellationTokenSource lifetime = new();
    private Uri downloadPage = new(UpdateChecker.ReleasesPage);
    private bool checking, closed;

    public UpdateWindow(UpdateChecker? updateChecker = null)
    {
        checker = updateChecker ?? new UpdateChecker(Client);
        InitializeComponent();
        CurrentVersionText.Text = "当前版本  v" + AppVersion.Current;
        IncludePreview.IsChecked = AppVersion.Parsed.IsPrerelease;
        Loaded += async (_, _) => await CheckAsync();
        Closed += (_, _) => { closed = true; lifetime.Cancel(); lifetime.Dispose(); };
    }

    private async void Check_Click(object sender, RoutedEventArgs e) => await CheckAsync();

    private async Task CheckAsync()
    {
        if (checking || closed) return;
        checking = true;
        CheckButton.IsEnabled = IncludePreview.IsEnabled = false;
        StatusText.Text = "正在检查更新…";
        DetailText.Text = "正在获取已发布的版本，请稍候。";
        DownloadButton.Content = "打开发布页";
        downloadPage = new(UpdateChecker.ReleasesPage);
        try
        {
            var release = await checker.GetLatestAsync(IncludePreview.IsChecked == true, lifetime.Token);
            if (closed) return;
            if (release is null)
            {
                StatusText.Text = "暂无可用的发布版本";
                DetailText.Text = "可以稍后重试，或打开发布页查看。";
            }
            else if (release.Version.CompareTo(AppVersion.Parsed) > 0)
            {
                StatusText.Text = "发现新版本  v" + release.Version.Display;
                DetailText.Text = "可前往发布页查看更新说明并下载。";
                downloadPage = release.Page;
                DownloadButton.Content = "前往下载";
            }
            else
            {
                StatusText.Text = "当前已是最新版本";
                DetailText.Text = "未发现比当前版本更新的" + (IncludePreview.IsChecked == true ? "正式版或预览版。" : "正式版。");
            }
        }
        catch (OperationCanceledException) when (closed) { }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException or IOException)
        {
            if (closed) return;
            StatusText.Text = "暂时无法检查更新";
            DetailText.Text = error switch
            {
                OperationCanceledException => "连接超时，请检查网络后重试，也可手动打开发布页。",
                HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests } => "更新服务暂时限制了请求，请稍后重试或手动打开发布页。",
                HttpRequestException => "无法连接更新服务，请检查网络后重试，也可手动打开发布页。",
                _ => "更新服务返回的数据暂时无法识别，请稍后重试。"
            };
        }
        finally
        {
            checking = false;
            if (!closed) CheckButton.IsEnabled = IncludePreview.IsEnabled = true;
        }
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(downloadPage.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, "无法打开浏览器，请手动访问：\n" + downloadPage.AbsoluteUri, "打开发布页", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
