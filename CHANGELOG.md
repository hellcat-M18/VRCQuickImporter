# Changelog

## 0.2.3

### Added
- 起動時バージョンチェック: GitHub Releasesの最新版と比較し、更新があれば画面上部に通知バナーを表示
- バナークリックでBOOTH商品ページをブラウザで開く

### Fixed
- ダウンロード完了後にhelperプロセスが終了しないレアバグを修正（Unity側10秒安全弁 + Helper側SafeClose）

## 0.2.2

### Changed
- 計画用ディレクトリを AGENTS/ → .agents/ にリネーム（Unity Export Package で自動除外されるように）
- ヘルパーバイナリ配置を Editor/Helpers~/ → Editor/Helpers/ に変更（Export Package に同梱）
- .gitignore を Assetモデル用に整理（VPM時代の不要ルール削除）

### Fixed
- ダウンロード後にhelperプロセスが終了しないレアバグの安全弁を追加（Unity側: 10秒待って強制Kill、Helper側: 二重Close防止）
- 未追跡の .meta ファイルを追加（Unity Package で必要）

## 0.2.1

### Added
- インポート履歴記録（商品ごとに展開先パスを保存、`import-history.json`）
- カードダブルクリックでProjectに展開先を表示（複数パス時はオーバーレイ選択UI）
- DL前商品検証（候補ライブラリページ±1を自動取得し、商品データを最新化）
- 完全リフレッシュ時の専用進行バー（ウィンドウ内にページ進捗・商品数を表示）
- パーサー異常検出（商品リンクあり抽出0件時はDB上書きせずエラー停止）
- ParserVersionフィールド（将来の抽出形式検証用）

### Changed
- 抽出スクリプトをCSSクラス非依存のセマンティック方式に書き換え（BOOTHデザイン変更耐性向上）
- ファイル名抽出をDLボタンとの兄弟関係ベースに変更（表示クラス不使用）
- バリエーション商品を内部で個別スロット保持・UI表示時に統合（ページ順の正確性向上）
- DL検証の2〜3ページ目は5秒インターバルをスキップ（高速化）
- 完全リフレッシュ開始時に商品カードをクリア（進捗の視認性向上）

### Fixed
- BOOTHのHTMLリニューアルにより抽出が全滅する問題を修正
- ファイル名に「その他のDL方法」「BOOTH Library ManagerでDL」等が混入するバグを修正
- インポートコールバックがAssembly Reloadで切れる問題を修正（[InitializeOnLoad]追加）
- .NET Framework 4.8ヘルパーの依存DLL欠落を修正（System.ComponentModel.Primitives.dll）
- 既存フォルダへの展開時に `(1)` サフィックスが付く問題を修正（上書き展開に変更）
- WebView2ヘルパー多重起動ガード追加

## 0.2.0

### Added
- 軽量WebView2同期モード（Image/Media/Fontブロックで負荷軽減）
- 同期キャンセルボタン（進行中の同期を中断・キャッシュ保護）
- JSONキャッシュ（database.json）による商品データ永続化と差分同期
- 初回利用時同意ポップアップ（BOOTHアクセスの同意確認）
- 全件再取得（詳細設定内のメンテナンス機能）
- 商品名検索（ローカル専用、BOOTHアクセスなし）
- インポート完了トースト通知（Windowsネイティブ通知）
- 複数unitypackage選択UI（チェックボックス付き専用ウィンドウ）
- WebView2 Runtime自動インストール（Microsoft公式Bootstrapper方式）
- Noto Sans JPバンドル（OFL 1.1）
- 長時間ダウンロード時のzip解凍完了通知

### Changed
- 配布方式をVPMパッケージから`.unitypackage`（Assets直下配置）に変更
- 導入方法: `Assets/VRCQuickImporter/` に配置、Tools メニューから起動
- メニュー: `Tools → VRCQuickImporter → 開く` → `Tools → VRCQuickImporter`（直接クリックで起動）
- BOOTHライブラリページアクセス間隔を5秒以上に変更
- 通知タイミングをインポート完了時・zip解凍時に調整
- BOOTHアクセスを常にユーザー操作主導に変更（自動同期なし）

### Removed
- プロトタイプ表記（ウィンドウタイトル・メニュー・ドキュメント）
- System.Windows.Forms依存（トースト通知をP/Invokeに変更）
- VPMパッケージマニフェスト（package.json）
- VCC/ALCOMリポジトリ登録
- GitHub Actionsリリースワークフロー（手動Export Packageに移行）

## 0.1.1

### Changed
- リポジトリ構成をVPM標準形（パッケージルート＝リポジトリルート）に移行
- README全面整備（導入方法・使い方・技術詳細・免責事項）

### Removed
- ROADMAP.mdと.agents/ディレクトリをリポジトリ追跡から除外（.gitignore）

## 0.1.0

### Added

- Initial VPM package manifest.
- EditorWindow entry point.
- WebView2 helper exe for BOOTH login and sync processing.
- Policy: library list/download/import UI is implemented as native Unity EditorWindow UI, not as an embedded BOOTH web page.
- BOOTH-style library card grid UI.
- Initial BOOTH library sync test that writes `Library/VRCQuickImporter/database.json` from the logged-in WebView2 helper.
- Project-local data/profile directories under `Library/VRCQuickImporter`.
