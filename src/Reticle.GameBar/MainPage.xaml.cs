using System;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Reticle.GameBar
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            InitializeComponent();
            ShowMode();
        }

        private void ShowMode()
        {
            ImportStatus.Text = ConfigurationReader.UseDesktopSync ? "当前模式：桌面实时同步。" : "当前模式：使用独立导入的配置。";
        }

        private async void Import_Click(object sender, RoutedEventArgs args)
        {
            ImportButton.IsEnabled = false;
            try
            {
                var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
                picker.FileTypeFilter.Add(".json");
                var selected = await picker.PickSingleFileAsync();
                if (selected == null) return;
                string text = await ConfigurationReader.ReadBoundedAsync(selected);
                ConfigurationReader.ParseImported(text);
                var local = await ApplicationData.Current.LocalFolder.CreateFileAsync(ConfigurationReader.ImportFileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(local, text);
                ConfigurationReader.UseDesktopSync = false;
                ImportStatus.Text = "已导入。已打开的准星组件会自动更新；再次启用桌面同步可切回编辑器。";
            }
            catch (Exception error) { ImportStatus.Text = "导入失败，保留之前的准星：" + error.Message; }
            finally { ImportButton.IsEnabled = true; }
        }

        private void Sync_Click(object sender, RoutedEventArgs args)
        {
            ConfigurationReader.UseDesktopSync = true;
            ShowMode();
        }
    }
}
