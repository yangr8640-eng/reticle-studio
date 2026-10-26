using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;

namespace Reticle.GameBar
{
    // Local diagnostics contain only this widget's rendering state, never the game or other windows.
    internal sealed class WidgetDiagnostics
    {
        private bool writing;
        private DateTimeOffset lastWrite;

        internal async void Record(JsonObject snapshot, FrameworkElement reticle)
        {
            if (writing || DateTimeOffset.UtcNow - lastWrite < TimeSpan.FromSeconds(2)) return;
            writing = true;
            lastWrite = DateTimeOffset.UtcNow;
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var output = await folder.CreateFileAsync("widget-diagnostics.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(output, snapshot.Stringify());
                // A developer/user can request a capture of the reticle visual only for pixel verification.
                var request = await folder.TryGetItemAsync("capture-widget.request");
                if (request != null)
                {
                    await request.DeleteAsync();
                    try
                    {
                        await CaptureAsync(reticle, folder);
                        snapshot["captureResult"] = JsonValue.CreateStringValue("widget-reticle.png");
                    }
                    catch (Exception error) { snapshot["captureResult"] = JsonValue.CreateStringValue(error.Message); }
                    await FileIO.WriteTextAsync(output, snapshot.Stringify());
                }
            }
            catch (Exception) { /* A diagnostic write must not stop the crosshair. */ }
            finally { writing = false; }
        }

        private static async Task CaptureAsync(FrameworkElement visual, StorageFolder folder)
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(visual);
            if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) throw new InvalidOperationException("The widget has no rendered layout yet.");
            var buffer = await bitmap.GetPixelsAsync();
            var pixels = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);
            var file = await folder.CreateFileAsync("widget-reticle.png", CreationCollisionOption.ReplaceExisting);
            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
                await encoder.FlushAsync();
            }
        }
    }
}
