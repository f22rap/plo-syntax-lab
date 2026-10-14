# デフォルト条件からボード別syntaxを生成するプレビュー

第2段階の実装です。Syntax Labのデフォルト条件をIDで選び、同じ条件を複数のフロップに適用できます。Node.js 20以上で動作し、このプレビューに追加パッケージのインストールは不要です。

第3段階でアプリ画面の選択・保存を追加しました。[選択・保存の手順](generated-conditions-selection.md)を参照してください。自動生成条件のMCP・アプリの調査実行への接続は次段階です。プレビューはsyntaxと合法ハンド数を確認する機能で、CSV集計・EQ計算は実行しません。

## 条件を選ぶ

リポジトリのフォルダーでターミナルを開きます。

```powershell
node mcp/preview-generated-conditions.mjs --list
node mcp/preview-generated-conditions.mjs --list --query Tset
```

一覧の名称・順番は、既存のデフォルト185条件と一致します。`preset_id`が保存する識別子です。対応済み159条件は選択可能です。従来から定義がない26条件（`pp all`とSD blocker系）は、`supported: false`と理由を返し、新規選択を拒否します。現在のボードで件数0という理由で条件を選択不可にはしません。

| 表示名 | preset_id |
| --- | --- |
| Tset all | set.top.all |
| Mset all | set.middle.all |
| Thit all | pair.top.all |
| AA all | aa.all |
| AA SD BDSD | aa.sd.bdsd |
| TM2P SD nuts oe | two_pair.top_middle.sd.nut_open |

`AA all`は既存デフォルトの完成役・ポケット条件を引き継ぎます。単にAAを含むハンドの条件とは異なります。表示名を変更しても意味は変わらず、保存したIDと版で判定します。

## 選択を保存して、別ボードでも使う

```powershell
node mcp/preview-generated-conditions.mjs --presets set.top.all,set.middle.all,aa.sd.bdsd --boards AsKd7s,Kh9h3c --name "ボード比較" --output preview.json
```

`preview.json`に、選んだ条件セットとボード別syntax・合法ハンド数・判定状態が保存されます。元条件にはボード依存の生成済みsyntaxを埋め込みません。

次回はそのファイルを指定できます。選択・条件ID・表示名は保持され、対象ボードについて生成し直します。

```powershell
node mcp/preview-generated-conditions.mjs --set preview.json --boards JsTd7c,AsAd7c --output next-preview.json
```

`--set`には、プレビュー全体、その中の`condition_set`だけを保存したJSON、またはアプリが保存した設定JSONを渡せます。複数セットを含む設定では `--list-sets` で一覧を確認し、`--set-id`でセットを選択します。設計資料の`generated-condition.examples.json`は条件の配列なので、直接渡さず条件セットに含めます。出力先の親フォルダーは先に作成してください。既存ファイルへの上書きは拒否します。

## 結果の読み方

| 項目 | 内容 |
| --- | --- |
| condition_set | 再利用する元条件・ID・表示名 |
| rows[].board | ランク降順、同ランクはs/h/d/c順の物理フロップ |
| rows[].syntax | このボードで生成・照合した式。ready以外は空文字 |
| rows[].legal_hand_count | ボードを除く49枚から作る4枚ハンドの該当数。CSVレンジで絞る前の数 |
| rows[].resolved_preset / resolved_filter | 実際に使ったデフォルト定義または解決後フィルター |
| rows[].recipe_hash | 表示名に依存しない条件のハッシュ |
| versions | 生成コードと定義ファイルのハッシュ |
| input_fingerprint / snapshot_hash | 入力・生成結果の識別値 |

| status | 意味 |
| --- | --- |
| ready | syntaxを生成し、合法ハンド集合との照合を通過 |
| empty_condition | 定義は有効だが該当ハンド0件。例：ペアボードのMset |
| unsupported_recipe | 未対応の版・プリセット、または未定義の条件 |
| missing_definition | 既存のボード別手入力条件に対象ボードの定義がない |
| generation_failed | 生成中の例外・集合の不一致など |

不明な設定項目や不正なsyntaxは、生成開始前の入力エラーとして拒否します。未設定・未対応・失敗は合法ハンド数を`null`にし、0件として扱いません。全体の`status`が`blocked`なら、問題のある行を確認してください。

フロップ3枚のみ、1回200ボードまで、使用する条件との積で10,000行までです。1ボード内の選択条件はまとめて処理し、ボード解析は1回だけ実行します。プロセス内キャッシュは最大16MiB・2,048件で、同じボード・条件・コードの検証済み結果だけを再利用します。CLIを終了するとキャッシュは消えます。

Ctrl+Cで中断した場合、計算中のボードは破棄して、完了したボードの結果を`cancelled`として保存します。中断された未完了ボードを0件の結果にはしません。

## 実装と検証

- `src/default-presets.json`：185条件の安定ID・対応状況。
- `src/research-recipes.cjs`：保存形式の検証、相対ランク、意味のハッシュ。
- `src/research-generation.cjs`：既存生成処理を使う解決器、固定syntaxとのAND照合。
- `mcp/generated-conditions.mjs`：ボード単位のワーカー、中断、キャッシュ、プレビュー。
- `mcp/preview-generated-conditions.mjs`：上記の確認用CLI。

```powershell
npm run test:generated
```

既存生成との比較、相対ランクと固定ランクの区別、第三FD、BDSDの通常SD除外、追加syntax、空集合とエラーの区別、キャッシュ、中断、出力保護を検証します。Windows EXEとC#抽出器の接続検証は、画面・調査実行に接続する後続段階で行います。
