# Changelog

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
- ROADMAP.mdとAGENTS/ディレクトリをリポジトリ追跡から除外（.gitignore）

## 0.1.0

### Added

- Initial VPM package manifest.
- EditorWindow entry point.
- WebView2 helper exe for BOOTH login and sync processing.
- Policy: library list/download/import UI is implemented as native Unity EditorWindow UI, not as an embedded BOOTH web page.
- BOOTH-style library card grid UI.
- Initial BOOTH library sync test that writes `Library/VRCQuickImporter/database.json` from the logged-in WebView2 helper.
- Project-local data/profile directories under `Library/VRCQuickImporter`.
