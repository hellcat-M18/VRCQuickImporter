# Changelog

## 0.2.0

### Changed
- 配布方式をVPMパッケージから`.unitypackage`（Assets直下配置）に変更
- 導入方法: `Assets/VRCQuickImporter/` に配置、Tools メニューから起動

### Removed
- VPMパッケージマニフェスト（package.json）
- VCC/ALCOMリポジトリ登録
- GitHub Actionsリリースワークフロー（手動Export Packageに移行）

## 0.1.1

### Changed
- リポジトリ構成をVPM標準形（パッケージルート＝リポジトリルート）に移行
- README全面整備（導入方法・使い方・技術詳細・免責事項）

### Added
- WebView2 Runtime未検出時にMicrosoft公式Bootstrapperをダウンロード・サイレントインストールする機能

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
