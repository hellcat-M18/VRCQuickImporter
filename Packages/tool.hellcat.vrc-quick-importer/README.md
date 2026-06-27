# VRCQuickImporter

BOOTHライブラリからUnityへの取り込みを支援する、VRChat向けUnity Editor拡張のプロトタイプです。

## 方針

- WebView2は **BOOTHログインと同期処理用** に限定して使います。
- BOOTHのライブラリ画面や商品ページをUnity内にそのまま表示する方針ではありません。
- ログイン後のライブラリ一覧、ダウンロード、展開、Unityへのインポート操作は、Unity EditorWindow上の自作UIで表示・操作します。
- Chrome/Edgeなど既存ブラウザのCookieは流用しません。
- BOOTHログイン状態は、この拡張専用のWebView2プロファイルに保存します。

## 現在の状態

プロトタイプ段階です。

現在実装済み:

- Unity EditorWindow
- WebView2 helper exe 起動
- BOOTHログイン画面表示
- プロジェクトローカルのログイン用プロファイル
  - `Library/VRCQuickImporter/webview-profile`
- ログイン用WebViewのHTML保存ボタン
- ライブラリ一覧UI
  - BOOTHスキリスト風のカードグリッド（サムネ、カテゴリ/タグ、VRCHATバッジ、商品名、ショップ名、価格/いいね、ダウンロードファイル選択ドロップダウン、インポート導線）
  - `database.json` が無い場合はサンプルデータ表示
- BOOTHライブラリ同期の初期テスト
  - WebView2 helperがログイン済みprofileでBOOTHライブラリページを開き、DOMから商品候補を抽出
  - 結果を `Library/VRCQuickImporter/database.json` に保存
  - 同期時の抽出JSON/HTMLを `Library/VRCQuickImporter/logs/` に保存
  - 現時点では商品一覧抽出の運用テスト段階。実ファイル一覧取得・実ダウンロード・実インポートは未実装

## 開き方

Unityメニュー:

```text
Tools/VRCQuickImporter/開く
```

ログイン画面を直接開く場合:

```text
Tools/VRCQuickImporter/プロトタイプ/BOOTHログイン画面を開く
```

## データ保存先

実行時データは以下に保存します。

```text
Library/VRCQuickImporter/
  cache/
  downloads/
  extracted/
  logs/
  webview-profile/
  database.json
```

`Library/VRCQuickImporter/` は削除しても再生成されます。削除した場合、BOOTHへの再ログインが必要になります。

## 注意

WebView2はUnityプロセス内ではなく、別プロセスのhelper exeとして起動します。

helper実体:

```text
Editor/Helpers~/WebView2Host/win-x64/VRCQuickImporter.WebView2Host.exe
```

Windows上で WebView2 Evergreen Runtime と .NET 8 Desktop Runtime が必要です。
