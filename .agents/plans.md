## Phase 4: インポート履歴とダブルクリックProject表示

### Step 4A: 履歴Store＋Pipeline記録 [x]
- BoothImportHistoryStore 新規作成（Editor/Import/）
- VRCQuickImporterPaths.ImportHistoryPath 追加
- import-history.json モデル定義
- BoothImportPipeline に履歴記録追加
  - DeployToAssets: 展開先パス
  - onImportPackageItemsCompleted: unitypackageパス

### Step 4B: ProductCardダブルクリック＋オーバーレイUI [x]
- ProductCardにダブルクリック検出（ボタンからのバブル除外）
- オーバーレイポップアップ（カード子要素、絶対配置）
- 外クリック/Escapeで閉じる
- 単一パス→即Ping、複数→ポップアップ選択

## Phase 5: DLクリック時オンデマンド商品検証

### Step 5A: DB基盤（PageSizeHint + in-place update + delete）
- BoothLibraryStore に PageSizeHint 保存
- UpsertProductInPlace メソッド（並び順維持）
- RemoveProduct メソッド
- 通常同期終了時に PageSizeHint を計算

### Step 5B: DL前検証フロー [x]
- BoothImportPipeline: StartImport 前に検証
  - HEAD productUrl → 404 → 削除
  - helperで候補ページ±1取得 → 比較 → 分岐
  - 見つからなければ誘導ダイアログ

### Step 5C: UI統合
- ダイアログ文言
- カード再構築（削除時・更新時）
