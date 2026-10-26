# Reticle Studio Game Bar component

Traditional C# 7.3 UWP XAML project, separate from the .NET 10 WPF application. Build with Visual Studio MSBuild and the UWP/.NET Native tools plus Windows 10 SDK 19041 or later. Package signing and installation are handled by the repository's Game Bar build/install workflow.

Identity: `ReticleStudio.GameBar`, publisher `CN=ReticleStudio`, application `App`, extension `ReticleWidget`.

The widget reads `ApplicationData.Current.LocalFolder/overlay-state.json` every 250 ms. The desktop editor writes a bounded, atomically replaced UTF-8 document:

```json
{"schemaVersion":1,"enabled":true,"updatedAtUtc":"2026-10-26T12:00:00Z","settings":{"Shape":"Cross","Color":"#67E8B0"}}
```

`settings` uses the existing PascalCase settings properties; missing fields use defaults. Input is bounded to 64 KiB, typed fields are checked, numeric values are finite/clamped, and unrecognized shape/color values are rejected. After a valid live state has been received, a heartbeat older than 15 seconds (or more than 5 seconds in the future) hides the crosshair. Invalid or temporarily unavailable data preserves the most recent valid settings and still observes this timeout.

The ordinary launcher lets users import an existing JSON through FileOpenPicker. This stores `imported-crosshair.json` locally and sets `UseDesktopSync=false` in LocalSettings; the widget switches to this standalone configuration with no timeout. Clicking “使用桌面同步” switches back. No network, broad filesystem, game process, or injection capability is requested.

Default widget content size is 400×400 DIP. Geometry and offsets use physical pixels by dividing by `RawPixelsPerViewPixel`. Large offsets request a larger transparent window before centering; Game Bar can decline that request, in which case the foreground UI explains the possible clipping and offers a retry. Game Bar owns the display/monitor placement. The selected WPF `Monitor` value is not used to move Game Bar to another monitor.

Game Bar's foreground UI explains pinning and click-through. Only the crosshair remains in `PinnedOnly` mode. Pinning and click-through remain user-controlled. This component targets Game Bar-compatible fullscreen presentation and must be tested in the target CS installation; it does not establish support for every exclusive-fullscreen surface.

`RequestedOpacity` is used as a 0..1 XAML opacity, following Microsoft's actual WidgetAdvSampleCS implementation. Only the editor's 0..100 setting is divided by 100. A local `widget-diagnostics.json` records the observed raw value and final alpha, layout and primitive counts. Creating `capture-widget.request` in LocalFolder requests `widget-reticle.png` of this widget's visual only, with no game/desktop capture. These files are local and overwritten, not uploaded.
