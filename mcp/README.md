# ローカルMCPの導入と操作

PLO Syntax Labの条件生成・CSV比較・履歴・結果出力を、MCP対応AIから操作できます。既存画面やブラウザを起動する必要はありません。処理はWindows上で実行します。AIクライアントにはツールの結果が返るため、そのクライアントのデータ取扱設定も適用されます。

## 導入

必要環境：64ビットWindows、.NET Framework 4.8、Windows PowerShell 5.1、Node.js 20以上。MCP経由の生成ではEdge/Chromeは不要です。GUIのデフォルト生成には引き続きEdge/Chromeを使います。

リポジトリ直下で実行します。

```powershell
npm.cmd ci --ignore-scripts
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
Copy-Item .\mcp\config.example.json .\mcp.local.json
New-Item -ItemType Directory -Path .\data -Force
```

`mcp.local.json`の`inputRoots`に集計CSVのフォルダーを指定します。初期例はリポジトリ内の`data`です。既存のCSVフォルダーの絶対パスでも構いません。相対パスは**設定ファイルのあるフォルダー**から解決します。

```json
{
  "inputRoots": ["data"],
  "outputRoot": "mcp-output",
  "allowHistoryDelete": false,
  "maxCsvBytes": 268435456,
  "timeoutSeconds": 300
}
```

- `inputRoots`：読み込めるCSVフォルダー。存在するフォルダーを指定します。
- `outputRoot`：グラフ・レポート等の保存先。なければ作成します。
- `historyRoot`：任意。省略すると既存GUIと同じ`%LOCALAPPDATA%\FlopCommandApp\History`。別の履歴を使う場合に指定します。
- `allowHistoryDelete`：初期値false。AIからの個別削除を使う場合にtrueへ変更してサーバーを再起動します。
- `maxCsvBytes`：1CSVの上限。初期値256MiB。
- `timeoutSeconds`：生成・集計1処理の制限。初期値300秒。

`mcp.local.json`、実データ、出力ファイルはGit管理対象外です。

## AIクライアントへの接続

ローカルstdio MCPに対応するクライアントで、次のプログラムをMCPサーバーとして登録します。

```text
プログラム：node（必要に応じてnode.exeの絶対パス）
引数：<リポジトリの絶対パス>/mcp/server.mjs --config <設定ファイルの絶対パス>/mcp.local.json
```

[client.example.json](client.example.json)は`mcpServers`形式の設定例です。`C:/path/to/plo-syntax-lab`を実際のパスへ置き換えてください。設定場所・形式・承認方法はクライアントによって異なります。既存設定を丸ごと上書きせず、サーバー項目を追加します。

このリポジトリはクライアント設定を自動変更しません。公式SDKのstdioクライアントとの接続をテストしています。個々の製品へのインストール、Web/モバイルからの接続は未検証です。Webだけで動くクライアントが、このローカルプロセスをそのまま利用できるわけではありません。

接続後、AIへ「PLO Syntax Labの状態とCSV一覧を取得して」と依頼します。`get_status`と`list_datasets`が成功すれば、基本接続を確認できます。

## AIへの依頼例

> JsTd7cでCheckとBetのCSVを使い、Tset allとMset allを比較してください。全ハンドも含め、グラフとMarkdownレポートを保存してください。各比率の意味も説明してください。

AIの操作手順：

1. `list_datasets`でファイルを探し、`register_dataset`でラベルを付ける。
2. `list_conditions`へボードを渡す。初回はジョブIDが返るため`get_job`で完了を確認し、再び`list_conditions`を呼ぶ。
3. Tset/Msetの`condition_id`と各CSVの`dataset_id`を`start_comparison`へ渡す。
4. `get_job`がcompletedになったら、その`result_id`で`get_result`を呼ぶ。
5. `export_result(format="svg")`でグラフを保存する。
6. 取得した数値から考察を書き、`save_report`へ渡す。表・syntax・再現情報はサーバーが付ける。

個別条件は`get_filter_schema`で役キーを調べ、`generate_syntax`へ役・ドロー・ポケット・ブロッカーを渡します。戻るジョブの完了結果に`condition_id`が含まれます。

## ツール一覧

| ツール | 内容 |
| --- | --- |
| `get_status` | バージョン、許可フォルダー、履歴先、指標の説明 |
| `list_datasets` | 許可フォルダー内のCSV検索。ページ分割あり |
| `register_dataset` | CSVとラベルを登録。ヘッダー確認・SHA-256取得 |
| `list_conditions` | デフォルト185件の取得・検索。選択不可の理由も返す |
| `get_filter_schema` | ボード固有の役キーとブロッカー対象ランク |
| `generate_syntax` | 個別条件の生成・全合法ハンド照合 |
| `start_comparison` | 2〜3CSVの比較。成功時に履歴保存 |
| `get_job` / `cancel_job` | 進捗・結果の確認、中止 |
| `get_result` | 保存済み結果・再現情報。PowerShellコマンドも取得可能 |
| `list_history` / `delete_history` | 履歴検索・許可された個別削除 |
| `export_result` | CSV、SVG、JSON、Markdown、PowerShell出力 |
| `save_report` | AIの考察を付けたMarkdownレポート |
| `get_artifact` | 出力ファイルの内容を取得。2MiBまで |

出力ファイルは`plo://artifacts/<ID>`のMCP resourceからも取得できます。SVGをクライアント内で表示できるかはクライアント依存です。対応しない場合も、保存したSVGをブラウザ等で開けます。

## 数値・状態の意味

- weight合計と比率は**decimal文字列**で返します。サーバーでJavaScriptの浮動小数点へ変換して再集計しません。
- 比率の分母は、選択したCSVのその条件に一致するweight合計。戦略全体の行動頻度ではありません。
- ゼロ合計はpercentがnull。0%へ置き換えません。
- 条件は重複し得るため、条件別の比率を合算しません。
- `include_all`は初期値true。条件を選ばず全ハンドだけの比較も可能です。
- 未対応条件・0件の条件は比較へ追加できません。生成条件と比較ボードの不一致も拒否します。
- CSV登録ではヘッダーのみ確認し、行の完全な検証は比較時に既存PowerShell処理が行います。
- 登録時と比較前後のSHA-256を確認し、変更を検出したら結果を保存しません。分析中は元CSVを編集しないでください。

ジョブは`queued → running → committing → completed`、または`failed`・`cancelled`へ進みます。`committing`は履歴の最終保存中なので中止できません。生成にはcommitting段階はありません。同時に重い処理を走らせず、1件ずつキューで実行します。

ジョブ・CSV登録・条件ID・artifact IDはサーバー起動中だけ有効。再起動後は再登録・再生成してください。保存済みresult IDは再起動後も有効です。出力ファイルも残ります。

## 保存と互換性

GUIと同じHistoryStoreで結果を保存します。履歴schema 1に任意の`Provenance`フィールドを追加し、入力SHA-256・条件・各処理のバージョンを記録します。旧履歴にはこの情報がない場合があります。

MCPで保存した結果は、同じ履歴フォルダーを使うGUIの履歴タブから確認できます。元CSVがなくても表・グラフ・コマンド・再出力を利用できます。

履歴削除は`allowHistoryDelete=true`に加え、`confirm`に削除対象と同じresult IDが必要です。AIはユーザーが削除を依頼した場合だけ使用してください。削除しても元CSVや既に出力したファイルは残ります。

## 開発と検証

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -WithTests
npm.cmd run test:mcp
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -Native
```

MCPテストは公式クライアントで実際にサーバーを起動し、条件生成・集計・レポート・resource取得・再起動・履歴削除を検証します。CSVは合成データ、履歴は`test-results/`内の隔離先です。

構成：

- `server.mjs`：公式SDK、ツールスキーマ、stdio、resource。
- `service.mjs`：ジョブ、条件・CSV登録、比較、履歴、出力。
- `engine-worker.cjs`：既存JSエンジンをWorkerで実行。メインの応答を止めない。
- `runtime.mjs`：設定・パス検証、プロセス、タイムアウト・中止。
- `NativeHost.cs`：GUIと共通のC#・PowerShell・HistoryStoreを使うコンソール入口。
- `artifacts.mjs`：SVGとMarkdownの生成。グラフは表示時のみ数値へ変換し、正確な値はツールチップと表に残す。

MCPコードは既存JSを直接利用するNode.jsのES Modulesです。TypeScriptのビルド工程は追加していません。MCP公式SDKのバージョンはlockfileで固定します。任意のPowerShell実行ツールは公開せず、検証した条件からアプリ自身が組み立てたコマンドを実行します。

MonkerSolver実機との一致は従来どおり未確認です。MCP接続や内部照合の成功を、実機対応の確認と混同しないでください。
