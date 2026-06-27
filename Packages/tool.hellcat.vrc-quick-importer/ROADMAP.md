# VRCQuickImporter Roadmap

## 目的

VRCQuickImporter は、VRChat/Unity ユーザーが BOOTH ライブラリから購入済み素材を取得し、Unity プロジェクトへ安全かつ少ない手順でインポートできるようにする Unity Editor 拡張です。

Chrome 拡張や既存ブラウザの Cookie には依存せず、VPM package 内に同梱した helper と Unity Editor 拡張で完結することを目標にします。

## 配布方針

- パッケージ名: **VRCQuickImporter**
- 配布形態: **VCC/VPM package**
- 主対象:
  - VRChat Creator Companion 利用者
  - Unity 2022.3.22f1
  - Windows 環境
- 可能なら対応:
  - Unity 6 / 6000 系
- 後回し:
  - Linux
  - macOS

## 想定 package 情報

正式値は公開前に確定します。

```jsonc
{
  "name": "com.yourname.vrc-quick-importer",
  "displayName": "VRCQuickImporter",
  "version": "0.1.0",
  "unity": "2022.3",
  "description": "BOOTH library downloader/importer for VRChat Unity projects.",
  "vpmDependencies": {}
}
```

## 基本方針

### BOOTH ログイン

- Chrome/Edge など既存ブラウザの Cookie やログインセッションは流用しない
- WebView2 は **BOOTHログイン専用** に使う
- WebView2 は Unity プロセス内ではなく、VPM package に同梱した別プロセス helper exe として起動する
- WebView2 profile をプロジェクト内に保存し、セッションを維持する
- WebView2 の画面表示はログイン時のみを基本とし、BOOTHページ閲覧用UIとしては使わない

### BOOTH ライブラリ同期/表示

- BOOTHのライブラリ画面や商品ページをUnity内にそのまま表示する方針ではない
- ログイン後のライブラリ一覧、ダウンロード、展開、Unityへのインポート操作は Unity EditorWindow の自作 UI に表示する
- BOOTH 画面に直接ボタンを足す方式にはしない
- ライブラリはページ単位で手動同期する
- 自動巡回・常時クロールはしない
- キャッシュを前提にし、過度なアクセスを避ける

> 現状の進捗: ライブラリ一覧UIの **モック/雛形**（サンプルデータ）のみ実装済み。実際のBOOTH同期・取得は「ログイン済みのWebView2 helper」を経由して今後実装する。

### 保存場所

プロジェクト内に保存します。

```text
<Project>/Library/VRCQuickImporter/
  cache/
  downloads/
  extracted/
  logs/
  webview-profile/
  database.json
```

`Library/VRCQuickImporter/` は削除しても再生成されます。削除した場合、WebView2 profile も消えるため BOOTH への再ログインが必要になります。

### 削除/リセット機能

設定画面から以下を削除できるようにします。

- キャッシュ
- ダウンロード済みファイル
- 展開済みファイル
- WebView ログイン情報/profile
- database.json

## Import 仕様

### .unitypackage

初期設定では Unity 標準の確認画面を出さずに自動インポートします。

```csharp
AssetDatabase.ImportPackage(path, false);
```

設定で確認画面を有効化した場合のみ、Unity 標準の import チェック画面を表示します。

```csharp
AssetDatabase.ImportPackage(path, true);
```

### .zip

初期 MVP から対応対象にします。

処理方針:

1. ZIP を一時展開する
2. 中に `.unitypackage` があれば、それを優先して import する
3. `.unitypackage` が無い場合は ZIP 構造を判定する
4. 安全と判断できる一般素材 ZIP のみ `Assets/BoothImports/{商品名 or zip名}/` に展開する
5. Unity プロジェクト構造に見える ZIP は自動展開せず、確認またはスキップする

### .unitypackage が無い ZIP の判定案

危険寄りとして自動展開しない候補:

```text
Assets/
Packages/
ProjectSettings/
```

一般素材として展開候補:

```text
Textures/
Materials/
Prefabs/
README.txt
*.fbx
*.png
*.psd
*.blend
*.mat
*.prefab
```

展開先候補:

```text
Assets/BoothImports/{商品名 or zip名}/
```

## 規約確認について

import 前の規約表示は行いません。

理由:

- 購入時点でユーザーが確認済みとみなす
- ZIP に規約が同梱されていないことがある
- 規約ファイルの自動判定が難しい

## 一括処理/キュー

- 完全一括自動 import ではなく、キュー方式にする
- ダウンロード/展開/import を 1 件ずつ順次処理する
- Unity import は並列実行しない
- 失敗時はキューを止めるか、対象だけ失敗扱いにして続行するかを設定可能にする

## MVP 範囲

MVP では以下を実装対象にします。

1. VCC/VPM package として導入できる最小構成
2. Unity Editor Window
3. WebView2 helper による BOOTH ログイン
4. BOOTH ライブラリ 1 ページ分の手動同期（未実装。モックUIの雛形のみ実装済み）
5. Unity EditorWindow の自作UIによる商品/ダウンロードファイルの一覧表示（モック/雛形は実装済み）
6. ダウンロード
7. `.unitypackage` import
8. ZIP 内 `.unitypackage` 検出/import
9. import キュー管理
10. キャッシュ/ログイン情報削除機能

## 非 MVP / 後回し

- ライブラリ全ページ自動巡回
- 完全一括自動 import
- Chrome Cookie 流用
- macOS/Linux 対応
- BOOTH 商品ページへの UI 注入
- 規約ファイルの自動検出/表示
- 差分更新の高度化
- 複数プロジェクト間での共有キャッシュ

## データモデル案

### Product

```csharp
class BoothProduct
{
    string productId;
    string title;
    string creatorName;
    string productUrl;
    string thumbnailUrl;
    DateTime syncedAt;
    List<BoothDownloadFile> files;
}
```

### DownloadFile

```csharp
class BoothDownloadFile
{
    string fileId;
    string productId;
    string displayName;
    string fileName;
    string downloadUrl;
    long? sizeBytes;
    string localPath;
    string sha256;
    DownloadState downloadState;
    ImportState importState;
}
```

### ImportHistory

```csharp
class ImportHistoryEntry
{
    string fileId;
    string localPath;
    DateTime importedAt;
    string unityVersion;
    string result;
    string message;
}
```

### QueueItem

```csharp
class ImportQueueItem
{
    string id;
    string fileId;
    QueueAction action;
    QueueState state;
    string message;
    DateTime createdAt;
    DateTime? startedAt;
    DateTime? finishedAt;
}
```

## 予定ディレクトリ構成

VPM package として公開しやすい構造を前提にします。

```text
VRCQuickImporter/
  package.json
  README.md
  CHANGELOG.md
  LICENSE.md
  ROADMAP.md
  Editor/
    VRCQuickImporter.Editor.asmdef
    Windows/
    UI/
    Booth/
    Download/
    Import/
    Storage/
    Queue/
  Samples~ /
  Documentation~ /
```

実際に Unity の Package Manager/VCC で扱いやすい配置にするため、後続フェーズで VPM package 仕様を確認して確定します。

## Phase 計画

### Phase 0: 調査・仕様確定

- WebView 実装選定
  - 第一候補: Windows WebView2
  - 代替: CEF 系、既存 Unity WebView package、外部ブラウザ fallback
- BOOTH ライブラリ取得方法の確認
  - DOM 解析
  - 通信/API 相当の解析
  - ダウンロード URL 取得方法
- VPM package 仕様確認
- ZIP 判定ルールの詳細化

完了条件:

- WebView 方式が決まっている
- ライブラリ 1 ページ同期の取得方法が決まっている
- VPM package の最小構成が決まっている

### Phase 1: Package 骨格

- `package.json` 作成
- asmdef 作成
- EditorWindow 作成
- 基本メニュー追加
- 設定保存の土台作成
- `Library/VRCQuickImporter/` 初期化

完了条件:

- VCC/VPM package として Unity プロジェクトに追加できる
- Unity Editor メニューから VRCQuickImporter window を開ける

### Phase 2: Storage / Database

- database.json 読み書き
- Product/File/History/Queue モデル実装
- cache/downloads/extracted/profile path 管理
- 削除/リセット API 実装

完了条件:

- 商品/ファイル/履歴/キュー状態を永続化できる
- 設定画面から各種データを削除できる

### Phase 3: WebView Login

- WebView 表示
- BOOTH ログインページ表示
- profile 永続化
- ログイン状態確認
- WebView 表示/非表示切り替え

完了条件:

- Unity Editor 内で BOOTH にログインできる
- Unity 再起動後もログイン状態が維持される

### Phase 4: Library Sync

- ライブラリページ取得
- 1 ページ分の商品情報を解析
- 商品一覧 UI 表示
- 手動同期ボタン
- キャッシュ更新

完了条件:

- BOOTH ライブラリ 1 ページ分を Unity UI に表示できる

### Phase 5: Download

- ダウンロード候補ファイル一覧表示
- ファイルダウンロード
- 進捗表示
- 再ダウンロード/既存ファイル検出
- 失敗時の retry

完了条件:

- UI から BOOTH ファイルを `downloads/` に保存できる

### Phase 6: Import

- `.unitypackage` import
- 設定による確認画面 ON/OFF
- ZIP 展開
- ZIP 内 `.unitypackage` 検出/import
- `.unitypackage` なし ZIP の安全判定
- `Assets/BoothImports/` への展開

完了条件:

- ダウンロード済み `.unitypackage` と `.zip` を import できる

### Phase 7: Queue

- ダウンロード/import キュー
- 1 件ずつ順次処理
- 停止/再開/スキップ
- 失敗表示
- import 履歴記録

完了条件:

- 複数ファイルをキューに積んで順次処理できる

### Phase 8: Public Release Preparation

- README 整備
- LICENSE 整備
- CHANGELOG 整備
- package metadata 確定
- VPM repository 公開手順整理
- サンプル画像/GIF 作成
- 既知の制限事項明記

完了条件:

- VCC package として公開可能な状態になっている

## リスク

### BOOTH 側仕様変更

DOM/API が変わると同期やダウンロードが壊れる可能性があります。

対策:

- 解析ロジックを局所化する
- エラー時に WebView を開いて手動確認しやすくする
- キャッシュを保持する

### WebView2 配布/互換性

ユーザー環境に WebView2 Runtime が無い可能性があります。

対策:

- Runtime 有無の検出
- エラーメッセージと導入案内
- 代替方式の検討

### Unity import の副作用

`.unitypackage` import は既存 Assets を上書きする可能性があります。

対策:

- import 前の確認画面を設定で有効化可能にする
- import 履歴を残す
- キュー上で対象ファイルを明示する

### ZIP 自動展開の危険性

Unity プロジェクト丸ごと ZIP や特殊構造の ZIP を誤って `Assets/` に展開すると危険です。

対策:

- 危険構造検出
- 不明な ZIP は確認を出す
- 展開先を `Assets/BoothImports/` に限定する

## 未決事項

- 正式な package id
- GitHub/VPM repository 名
- WebView2 実装方法
- BOOTH ライブラリ取得の具体的 URL/DOM/API
- database.json の schema versioning
- `.unitypackage` なし ZIP の確認 UI
- ログ出力方式
- エラー報告/診断情報の出し方
