# PLO Syntax Lab：ローカルMCPの使い方

この文書は`src/mcp/stdio.cjs`のWindows/Linux共通11ツール版を説明します。Windowsアプリと同じ永続履歴、PowerShell出力、SVGグラフ・Markdown保存が必要な場合は[Windowsアプリ連携15ツール版](../mcp/README.md)を使用してください。両版のIDは相互利用できません。

Node.js 24以上と、ローカルstdio MCPを起動できるAIクライアントを使用します。Windows・Linux向けです。既存のWindows exe・HTML画面とは独立して起動できます。

## 起動と接続

GitHub Actionsの **PLO-Syntax-Lab-MCP** artifactからZIPを取得し、展開します。npm依存はZIPに同梱されています。Node.jsは別途必要です。ZIPを差し替える更新ではnpmの再インストールは不要です。

リポジトリのソースから使う場合のみ、リポジトリ直下で依存をインストールします：

```sh
npm ci --prefix src/mcp
```

AIクライアントのMCP設定に、`node`と`src/mcp/stdio.cjs`の**絶対パス**を登録します。設定の形式はクライアントごとに確認してください。`mcpServers`形式の例：

```json
{
  "mcpServers": {
    "plo-syntax-lab": {
      "command": "node",
      "args": ["C:/tools/PLO-Syntax-Lab-MCP/src/mcp/stdio.cjs"]
    }
  }
}
```

Linuxでは`/home/user/tools/PLO-Syntax-Lab-MCP/src/mcp/stdio.cjs`などへ置き換えます。クライアントから`node`が見つからなければ、`command`にもNode実行ファイルの絶対パスを指定します。作業ディレクトリの指定は不要です。[設定テンプレート](mcp-config.example.json)も利用できます。

CSVファイルを読み込ませる場合は、起動時に許可フォルダーを追加します。複数指定できます：

```json
"args": [
  "C:/tools/PLO-Syntax-Lab-MCP/src/mcp/stdio.cjs",
  "--allow-read-root", "C:/poker/csv"
]
```

許可フォルダーなしでもsyntax生成・照合・inline CSVによる比較は利用できます。ローカルファイルはUTF-8で指定し、リンク先も許可フォルダー内である必要があります。ツールからPCの任意の場所へファイルを書き込む機能はありません。

## AIへの依頼と処理の流れ

例えば「Ts9h3dのナッツガットショットとナッツオープンエンドを生成して」と依頼すると、`plo_generate_ranges`が`(KQ,KJ)`と`QJ`、件数、条件IDを返します。

CSV比較は次の順番で実行します：

1. `plo_capabilities`と`plo_definitions`で対応範囲・定義を確認する。
2. `plo_generate_ranges`で条件を生成し、返された`conditionId`を保持する。
3. `plo_register_dataset`で同じボードのCSVを2〜3個登録し、`datasetId`を保持する。
4. `plo_start_compare`に各IDと、比較ごとに一意な`requestKey`を渡す。同じ入力の再送は同じジョブを返す。
5. `plo_get_job`で`state=succeeded`になるまで取得する。失敗時は`failure`を確認する。
6. `plo_export_result`に`format=csv`または`json`を渡す。
7. 返された`resourceUri`をMCPの`resources/read`で取得する。保存方法はAIクライアント側の機能を使う。

CSV登録の例：

```json
{
  "board": "Ts9h3d", "name": "check.csv", "label": "Check",
  "source": {"kind": "inline", "csvText": "hand,weight\nKsQhAsAd,0.2\n"}
}
```

ローカルファイルでは`source`を`{"kind":"local_file","path":"C:/poker/csv/check.csv"}`へ変更します。

## 提供する11ツール

| ツール | 用途 |
| --- | --- |
| plo_capabilities | 対応機能・版・制限 |
| plo_definitions | 役・ドロー・CSVの定義 |
| plo_analyze_board | ボード解析、合法ハンド件数、役一覧 |
| plo_generate_ranges | カスタム条件・185デフォルト条件の生成 |
| plo_match_hands | syntaxまたは条件IDに対する4枚ハンド照合 |
| plo_register_dataset | CSVの検証・登録 |
| plo_list_datasets | 登録済みCSV一覧 |
| plo_start_compare | 2〜3CSVの比較開始 |
| plo_get_job | 状態・結果のページ取得 |
| plo_cancel_job | 待機中・実行中の比較中止 |
| plo_export_result | CSV/JSONの一時成果物作成 |

入出力の詳細は[API仕様](mcp-api-spec.md)と[JSON Schema](mcp-tools.schema.json)を参照してください。エラーは`isError=true`、`structuredContent.ok=false`と機械判定できる`error.code`で返します。

weightは小数28桁の整数計算を使い、合計と比率をJSONで文字列として返します。比率は小数8桁で切り捨て、合計0では`null`です。CSV単体・比較合計は30MiB、inlineは256KiB、結果取得はページ単位です。同時計算は2件、比較の待機上限は8件です。比較結果の保持は合計64MiB、成果物の保持も合計64MiB、1リソースの通信はJSONエスケープ後に8MiBまでです。計算中に`SERVER_BUSY`が返った場合は少し待って再実行します。

条件・CSV・ジョブ・成果物はプロセスごとに隔離され、有効期限は発行から1時間です。再起動すると失われます。ボードの異なるIDを混ぜると`BOARD_MISMATCH`になります。成果物はメモリー内に保持され、既存UIの履歴には保存されません。

## 対応状況と検証

公式MCP SDK 1.32.1を固定し、protocol `2025-11-25`で公式SDKクライアントによる接続・全ツール・成果物取得をテストしています。実際のAIホスト固有の設定や、成果物のユーザー向け保存画面は別途確認が必要です。HTTP/HTTPS、認証、クラウドアップロード、クラウド向けプラグイン、MCP 2026版は後続段階です。クラウドAIからローカルstdioに直接接続できるとは限りません。

ドロー分類とsyntaxは既存UIと同じエンジンを使用します。MonkerSolver実機での構文受理・抽出一致は未確認です。返却される`engine.sourceCommit`は配布ビルドのコミット、ソース起動ではGit HEADです。ビルド情報もGitもない場合は`null`と警告を返します。ソースの未コミット変更はHEADには含まれません。

開発時のテスト：

```sh
npm ci --prefix src/mcp
node tests/mcp-tests.cjs
node src/mcp/package-runtime.cjs
node dist/PLO-Syntax-Lab-MCP/tests/mcp-tests.cjs
```

MCPテストは合成CSVを使います。Windows/LinuxのCIは公式SDKによる子プロセス接続、生成・照合、比較精度、ID隔離、有効期限、中止、読込範囲、結果リソースを確認します。Windows CIは依存同梱のZIPを生成し、タグ`v*`のReleaseにも添付します。
