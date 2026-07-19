# Third Party Notices

## Microsoft Edge WebView2 SDK

This tool includes files from the NuGet package `Microsoft.Web.WebView2`:

- `Microsoft.Web.WebView2.Core.dll`
- `Microsoft.Web.WebView2.WinForms.dll`
- `Microsoft.Web.WebView2.Wpf.dll`
- `WebView2Loader.dll`

Package: https://www.nuget.org/packages/Microsoft.Web.WebView2
Documentation: https://learn.microsoft.com/en-us/microsoft-edge/webview2/

The WebView2 Evergreen Runtime itself is not bundled. When it is not installed, the tool asks for consent before downloading and running Microsoft's official bootstrapper; users can also install it manually.

## Noto Sans JP (Noto CJK)

This package bundles the following font files, which are static instances
instantiated from the Noto Sans JP variable font:

- `Editor/Fonts/NotoSansJP-Regular.ttf`
- `Editor/Fonts/NotoSansJP-Bold.ttf`

These fonts are licensed under the [SIL Open Font License, Version 1.1](https://openfontlicense.org).
The full license text is included at `Editor/Fonts/LICENSE-OFL.txt`.

Copyright 2014-2024 Adobe (http://www.adobe.com/), with Reserved Font Name "Source".
Copyright 2019 Google LLC (https://www.google.com/), with Reserved Font Name "Noto".

Noto is a trademark of Google LLC.

Source: https://github.com/notofonts/noto-cjk
