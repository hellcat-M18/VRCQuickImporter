# VRCQuickImporter

BOOTHライブラリからUnityへの取り込みを支援する、VRChat向けUnity Editor拡張のプロトタイプです。

> **重要: 本ツールはBOOTH（pixiv株式会社）の非公式支援ツールです。**
> 公式の提供物ではなく、BOOTHおよびpixivは本ツールのサポート対象外です。
> BOOTHの[利用規約](https://policies.pixiv.net/)・[ガイドライン](https://booth.pm/guidelines)を尊重し、**常識的な範囲**でご利用ください。

## 位置づけと利用上の留意（必読）

- **参照するのは「あなた自身の購入履歴」のみ**です。ログイン済みの自分のセッションを使い、自分が買った商品一覧を表示します。他人のデータや公開カタログを無差別に収集するものではありません。
- BOOTHのガイドラインは「クローラー等による**商品の収集**」「サーバへの**極端な負荷**」を禁止しています。本ツールはこれを避けるため、以下の設計としています。
  - **全ページ自動巡回は行いません。** 1ページ目を取得したあとは、ユーザーが明示的に「もっと読み込む」を押したときだけ次の1ページを取得します。
  - ページ取得のたびに**待機間隔（2秒）**を挟み、サーバへの負荷が極端にならないようにしています。
  - 1アクション = 1ページ取得（ヘルパープロセス1回起動）で、バックグラウンドでの連続巡回はしません。
- 取得したデータは**このUnityプロジェクトの `Library/` 以下にのみ保存**します。第三者への提供・再配布・営利目的の利用は想定していません。
- 本ツールは購入履歴というログイン必須の情報を扱うため、規約上はグレーゾーンになり得ます。**利用は自己責任**でお願いします。BOOTH側の判断で利用が制限される場合があります。

## 方針

- WebView2は **BOOTHログインと同期処理用** に限定して使います。
- BOOTHのライブラリ画面や商品ページをUnity内にそのまま表示する方針ではありません。
- ログイン後のライブラリ一覧・ダウンロード・展開・Unityへのインポート操作は、Unity EditorWindow上の自作UIで行います。
- Chrome/Edgeなど既存ブラウザのCookieは流用しません。
- BOOTHログイン状態は、この拡張専用のWebView2プロファイルに保存します。

## 現在の状態

プロトタイプ段階です。

現在実装済み:

- Unity EditorWindow（BOOTHスキリスト風カードグリッド）
- WebView2 helper exe 起動（BOOTHログイン・ライブラリ同期用）
  - プロジェクトローカルのログイン用プロファイル（`Library/VRCQuickImporter/webview-profile`）
  - ログイン用WebViewのHTML保存ボタン
- BOOTHライブラリ同期
  - 1ページ目の取得（「BOOTHと同期」）
  - **「もっと読み込む」による1ページずつの追加取得**（ページごとに待機間隔あり）
  - DOM構造ベースの商品/ショップ/ファイル名抽出
  - 結果を `Library/VRCQuickImporter/database.json` に保存（重複除外）
  - バックグラウンド（非表示）同期と、可視ウインドウ同期の切替
  - 同期時の抽出JSON/HTMLを `Library/VRCQuickImporter/logs/` に保存
- サムネイル画像表示（取得＆ローカルキャッシュ）

未実装:

- 実ファイルダウンロード
- 取得ファイルの展開・Unityへのインポート
  - 現時点では「インポート（未実装）」の導線のみ

## 開き方

Unityメニュー:

```text
Tools/VRCQuickImporter/開く
```

ログイン画面を直接開く場合:

```text
Tools/VRCQuickImporter/プロトタイプ/BOOTHログイン画面を開く
```

## 使い方（ライブラリ同期）

1. 「BOOTHログイン画面を開く」でBOOTHにログイン（初回のみ）。
2. 「BOOTHと同期」で1ページ目を取得。
3. 必要に応じて「もっと読み込む」で次のページを追加取得。
   - 全ページ自動取得はしません。サーバ負荷を避けるため、明示的な操作で1ページずつ読み込みます。

## データ保存先

実行時データは以下に保存します。

```text
Library/VRCQuickImporter/
  cache/
  thumbnails/      サムネイル画像キャッシュ
  downloads/
  extracted/
  logs/
  webview-profile/ ログイン用プロファイル
  database.json    同期済みライブラリ
  pending-page.json 取得途中の1ページ分（取り込み後に削除）
```

`Library/VRCQuickImporter/` は削除しても再生成されます。削除した場合、BOOTHへの再ログインが必要になります。

## 注意

WebView2はUnityプロセス内ではなく、別プロセスのhelper exeとして起動します。

helper実体:

```text
Editor/Helpers~/WebView2Host/win-x64/VRCQuickImporter.WebView2Host.exe
```

Windows上で WebView2 Evergreen Runtime と .NET 8 Desktop Runtime が必要です。
