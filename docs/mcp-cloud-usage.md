# PLO Syntax Lab：クラウドMCP

同じPLO4エンジンをHTTPSのMCPから呼び出します。Windows exeやNode.jsを利用者PCへインストールする必要はありません。11ツールの引数は[API仕様](mcp-api-spec.md)・[JSON Schema](mcp-tools.schema.json)と共通です。

## 接続とCSVの受け渡し

Sitesの非公開プラグインとして提供します。ChatGPTのPlugins → Personal → Created by youで「PLO Syntax Lab MCP」を選んで接続します。会話に接続カードが表示されている場合はConnectから接続できます。SitesがサインインとOAuthを担当します。APIキーをAIへ入力する必要はありません。

公開済みの非公開サービス：

- [CSV登録画面](https://plo-syntax-lab-mcp.m14socom8011.chatgpt.site)
- MCP endpoint / OAuth resource：`https://plo-syntax-lab-mcp.m14socom8011.chatgpt.site/mcp`
- プラグイン名：PLO Syntax Lab MCP（Sitesが作成する同名の接続を使用）

初回公開は2026-10-08。実際のAIホストでのツール呼び出しはプラグイン接続後に確認します。

1. CSV登録画面を開き、ボードを入力する。
2. 同じボードのUTF-8 CSVを2〜3個選んで「登録する」を押す。
3. AIに「登録済みCSVを一覧にし、Ts9h3dのナッツガットとナッツオープンエンドを生成して比較して」と依頼する。
4. AIはlist_datasets → generate_ranges → start_compare → get_job → export_resultを呼ぶ。
5. 成果物はMCP resources/readまたは認証付きdownloadUrlから取得できる。URLを開く利用者も同じアカウントである必要がある。

小さなCSVはplo_register_datasetのsource.kind=inlineでも渡せます。画面で受け渡したuploadIdはsource.kind=uploadで一度だけ登録できます。AIホストの会話添付ファイルが自動でuploadIdになるとは仮定しません。PCの絶対パス・他人のCSV・共有先での無認証ダウンロードには対応しません。

## 保存と制限

利用者ごとの条件・CSV・比較ジョブ・成果物をD1/R2に保存します。再接続やWorkerインスタンスの変更をまたいで取得でき、IDの有効期限は発行から1時間です。期限で参照を拒否し、後続の認証済みアクセスで物理データを少しずつ削除します。恒久的な履歴保管機能ではありません。

| 対象 | クラウドの上限 |
| --- | --- |
| CSV単体 | 2MiB・50,000行 |
| inline CSV | 256KiB |
| 比較するCSV | 2〜3個、合計6MiB |
| 比較条件 | 20件 |
| 1回のカスタム生成 | 10条件 |
| 一覧・デフォルト・結果のページ | 10件 |
| 同時比較 | 利用者ごと1件、同一isolateのエンジン計算1件 |
| 計算時間 | 25秒。保存ジョブの監視期限30秒 |
| CSV・比較結果・成果物の保持 | それぞれ8MiB |
| 条件・CSV・upload・job・成果物の数 | 512・32・8・16・32 |
| MCPツール応答・resources/read | 256KiB・8MiB（JSONエスケープ後） |

全185デフォルト条件はoffset/limitで取得します。重いボードや複合条件は時間・応答サイズ制限でエラーになる場合があります。syntaxを黙って省略・短縮せずOUTPUT_TOO_LARGEを返します。plo_capabilitiesで実際の制限と検証済み版を確認してください。

weight合計は小数28桁の整数計算、比率は小数8桁の切り捨てで文字列として返します。合計0ではnull。同じrequestKey・同じ入力の比較再送は同じjobIdを返し、異なる入力への再利用はREQUEST_KEY_CONFLICTです。ジョブは最初の終端更新が確定し、成功後のcancelで結果を消しません。

## 開発・検証

```sh
npm ci --prefix cloud
npm run build --prefix cloud
npm test --prefix cloud
```

専用lockfileに公式MCP SDK 1.32.1とWorker用依存を固定しています。公式SDKのStreamable HTTPクライアントを実際のworkerdへ接続し、全11ツール、D1/R2保存、複数利用者、再接続、アップロード、50,000行境界、2/3CSV、小数精度、キャンセル競合、期限・quota、CSV/JSONダウンロードを検証します。ローカルstdioは引き続きWindows/Linuxで検証します。

検証済みprotocolは2025-11-25です。実際のAIホストの接続・表示・会話添付ファイルの受け渡しは、そのホストで別途確認します。MCP 2026版、MonkerSolver実機での構文受理、対応ブラウザでのWebMCP実行は未検証です。ブラウザのWebMCPは対応時だけCSV一覧とuploadId登録の2ツールを追加し、通常の画面・HTTPS MCPは非対応ブラウザでも利用できます。

## Sitesへの公開・更新

GitHubを開発の正本とし、Sitesの別ソースリポジトリへ対応ソースを保存して公開します。GitHubへpushしただけではクラウドの更新は行いません。Windows/ローカルMCPの配布はGitHub Actionsで自動ビルドされます。

Sites用checkoutのルートpackage.jsonでbuildをnode cloud/build.mjsへ設定し、typeはcommonjsまたは未指定とする（従来のsrc/*.jsをCommonJSとして扱うため）。.openai/hosting.jsonに登録済みproject_id・d1=DB・r2=FILES・capabilities=[mcp]を保存します。cloud/source-info.jsonには対応GitHubコミットを記録します。buildはdist/server/index.jsとhosting metadata、ルートdrizzle/のmigrationを生成し、Sitesの公式ソース保存・packaging手順で同じcommitを公開します。GitHub側だけのbuildではdist/cloud/server/index.jsを生成します。

D1 schemaはcloud/db/schema.ts、生成migrationはcloud/drizzle/です。変更時はnpm run db:generate --prefix cloudでDrizzle migrationを追加します。既に公開したmigrationは書き換えません。DBとR2のruntime bindingsはSitesが提供します。Secretsや認証トークンをソース・hosting manifest・ブラウザに書き込みません。

認証済みIDヘッダーはSites dispatch境界を通る時だけ信頼できます。このWorkerを認証のない直結URLへ公開してはいけません。サービス用バイパストークンから利用者本人を推測しません。保存障害はSTORAGE_UNAVAILABLEとして本文を通常ログへ出さず再試行を案内します。
