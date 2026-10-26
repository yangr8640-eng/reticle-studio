using System;
using System.Collections.Generic;
using Microsoft.Gaming.XboxGameBar;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Reticle.GameBar
{
    sealed partial class App : Application
    {
        // Each Game Bar view owns its widget and dispatcher; a launcher may coexist with it.
        private readonly Dictionary<Window, WidgetPage> widgetPages = new Dictionary<Window, WidgetPage>();

        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;
            Resuming += OnResuming;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var frame = Window.Current.Content as Frame;
            if (frame == null) Window.Current.Content = frame = new Frame();
            if (frame.Content == null) frame.Navigate(typeof(MainPage));
            Window.Current.Activate();
        }

        protected override void OnActivated(IActivatedEventArgs args)
        {
            var activation = args as XboxGameBarWidgetActivatedEventArgs;
            if (activation == null || activation.AppExtensionId != "ReticleWidget") return;
            // Do not replace Game Bar's IPC object on repeat activation.
            if (!activation.IsLaunchActivation) return;
            var window = Window.Current;
            var frame = new Frame();
            window.Content = frame;
            var widget = new XboxGameBarWidget(activation, window.CoreWindow, frame);
            frame.Navigate(typeof(WidgetPage), widget);
            var page = (WidgetPage)frame.Content;
            lock (widgetPages) widgetPages[window] = page;
            window.Closed += (sender, e) =>
            {
                page.Dispose();
                lock (widgetPages) widgetPages.Remove(window);
            };
            window.Activate();
        }

        private async void OnSuspending(object sender, SuspendingEventArgs args)
        {
            var deferral = args.SuspendingOperation.GetDeferral();
            try
            {
                List<WidgetPage> pages;
                lock (widgetPages) pages = new List<WidgetPage>(widgetPages.Values);
                foreach (var page in pages)
                    await page.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, page.Pause);
            }
            finally { deferral.Complete(); }
        }

        private async void OnResuming(object sender, object args)
        {
            List<WidgetPage> pages;
            lock (widgetPages) pages = new List<WidgetPage>(widgetPages.Values);
            foreach (var page in pages)
                await page.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, page.Resume);
        }
    }
}
