# PLO Syntax Lab

PLO4のボードからMonker形式のsyntaxを生成し、2〜3つのCSVの該当ハンドをweight合計・比率で比較するWindowsアプリです。v11のソース一式を収録しています。

開発を引き継ぐ場合は[AI開発引継ぎ](AI_HANDOFF.md)を参照してください。構成・確定仕様・テスト・未確認事項をまとめています。

AIから利用するMCPには、[Windows/Linux共通版](docs/mcp-usage.md)とHTTPSの[クラウド版](docs/mcp-cloud-usage.md)があります。接続するAIクライアントと実行環境に合わせて選択してください。

## 機能

- **デフォルト**：従来の185件の条件名から選択。syntaxは現在のボードに合わせて生成します。
- **syntax生成**：「syntax生成を開く」→役・条件を選択→「比較条件に追加」。
- フロップ・ターン・リバー（ボード3〜5枚）、オマハの手札2枚＋ボード3枚で判定。
- CSVごとのラベル、weight合計・比率の表とグラフ、結果CSVの保存。
- 「全ハンド（syntaxなし）」の集計チェックボックス。
- PowerShellコマンドの表示・コピー・保存・実行・中止。
- 結果の自動保存、履歴タブでの閲覧・検索・個別削除。

Excelや元の条件一覧CSVは実行時に不要です。185件の条件名のみを内蔵し、集計用CSVとは独立してsyntaxを生成します。

## Linux版

Node.js 20以上と最新のChrome / Chromium / Firefoxがあれば、Linuxでもボードの条件生成・CSV比較を利用できます。npmパッケージ、PowerShell、.NETは不要です。リポジトリ直下で実行してください。

```bash
./start-linux.sh --open
```

`--open`は`xdg-open`でブラウザを開く指定です。ブラウザの自動起動が不要なら`./start-linux.sh`を実行し、端末に表示されるURLを同じPCのブラウザで開きます。終了はCtrl+C。Node.jsプロセスが必要なので、HTMLだけを開く場合と異なりCSV比較・履歴も利用できます。

Linux版では、185件のデフォルト条件を生成して選択するか、syntax生成画面で「比較条件に追加」を押して条件を取り込みます。条件JSONの読み込みにも対応しています。2〜3つのCSVのラベルを指定してweight合計・比率・件数を表示し、結果CSVを保存できます。計算の中止、結果の自動保存、履歴の検索・閲覧・結果CSVの再保存・個別削除にも対応しています。ボード変更時は条件を再生成してください。

CSVはブラウザから同じPCのNode.jsプロセスへ送られ、外部サーバーへ送信されません。サーバーは`127.0.0.1`のランダムポートとランダムトークン付きURLのみで待ち受けます。固定ポートが必要な場合は`PLO_PORT=8080 ./start-linux.sh`を使えます。入力CSVの内容は履歴には保存せず、ファイル名・ラベル・条件・集計結果を保存します。履歴の場所は`${XDG_DATA_HOME:-$HOME/.local/share}/plo-syntax-lab/history`です。Windows版の履歴形式とは独立しています。

Linux版の検証:

```bash
./test-linux.sh
```

6つの生成エンジン・ドロー回帰テストに加え、CSV検証・小数加算・2/3CSV比較・HTTP制限・生成条件の受信・履歴の保存/再取得/削除をテストします。テスト履歴は専用の一時フォルダーを使用します。

weightは小数28桁までを整数に変換して正確に加算し、比率は小数8桁で切り捨てて返します。合計0の比率は未定義です。CSVの合計サイズは画面で30MiB、HTTPリクエスト全体では32MiBまで。同名のCSVは重複として拒否します。Windows版のPowerShellコマンド生成・実行はLinux版には含まれず、Node.jsで集計します。Windowsのexeの生成には以下のWindows環境が引き続き必要です。

## Windows版のダウンロード・更新

Windows版はGitHub Actionsで自動ビルドします。利用するPCでのビルドやNode.jsのインストールは不要です。

- **開発中の最新版**：[Windows build](https://github.com/f22rap/plo-syntax-lab/actions/workflows/windows-build.yml)で、`codex/initial-import`の最新の成功した実行を開き、下部のArtifactsから`PLO-Syntax-Lab-Windows`をダウンロードします。GitHubへのログインが必要です。成果物の保存期間は30日です。
- **タグ付き配布版**：[Releases](https://github.com/f22rap/plo-syntax-lab/releases)のAssetsから`PLO-Syntax-Lab-Windows.zip`をダウンロードします。最初のバージョンタグを公開するまで配布版はありません。

ActionsからダウンロードしたZIPを展開すると、アプリのZIPと`SHA256SUMS.txt`が入っています。アプリの`PLO-Syntax-Lab-Windows.zip`も展開して、`FlopCommandApp.exe`を起動してください。Releaseから取得した場合はアプリのZIPを一度展開するだけです。`PLO-Syntax-Lab.html`は生成画面単独用です。

更新時はアプリを終了し、新しいZIPの内容で前の配布ファイルを置き換えます。個人の履歴はアプリのフォルダーとは別の場所に保存されるため保持されます。アプリ内の自動更新機能はありません。実行環境はWindows 10/11（64ビット）、.NET Framework 4.8、Windows PowerShell 5.1、EdgeまたはChromeです。

### 開発者向け：自動ビルドとRelease

`.github/workflows/windows-build.yml`は`codex/initial-import`へのpush・PRと手動実行で動きます。6つのNodeテストを実行し、`build.ps1 -WithTests`で製品exeとネイティブテストexeをコンパイルします。デスクトップと実ブラウザを必要とする`test.ps1 -Native`はCIでは実行しません。Windowsの画面・ブラウザ連携は別途確認してください。配布物には製品exe、単独生成HTML、使い方、ビルド元コミットを記載したREADMEだけを含め、テストexeや個人データは含めません。

配布版を公開する場合は、このワークフローを含むコミットに新しいバージョンタグを付けてpushします。例（未使用のバージョン名を使ってください）：

```bash
git tag v1.0.0
git push origin v1.0.0
```

`v*`タグのpushではビルド・テストの成功後にReleaseを作成し、同じZIPとSHA-256チェックサムを添付します。通常ビルドは読み取り権限のみ、Releaseジョブだけが`contents: write`を使います。個人アクセストークンの登録は不要です。同じタグの実行を再試行した場合は添付ファイルを更新します。

## Windows版の動作環境とビルド

Windows 10/11（64ビット）、.NET Framework 4.8、Windows PowerShell 5.1、Microsoft EdgeまたはGoogle Chromeを使用します。**ビルド時のみNode.js 20以上**が必要です。外部のnpmパッケージは不要です。

リポジトリを取得した後、リポジトリ直下で実行します。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\dist\FlopCommandApp.exe
```

`dist/FlopCommandApp.exe`は単体で利用できます。`dist/PLO-Syntax-Lab.html`は生成画面のみを単独で使うファイルです。生成・集計はPC内で行います。比較アプリとの連携にはランダムなトークン付きのローカル接続を使います。

## AIから利用するローカルMCP

stdio MCPで、ボード解析・syntax生成・ハンド照合・CSV比較・結果出力の11ツールを公開しています。既存UIと同じ生成エンジン・集計を使います。

GitHub Actionsの`PLO-Syntax-Lab-MCP` artifactには依存同梱ZIPを用意します。Node.js 24以上を用意し、対応するAIクライアントに`src/mcp/stdio.cjs`の絶対パスを登録してください。ソースから起動する場合は`npm ci --prefix src/mcp`で依存をインストールします。MCP用の依存は既存Windows/HTMLビルドとは別です。

設定例・CSV読み込み・11ツールの使い方は[MCP利用手順](docs/mcp-usage.md)、入出力は[API仕様](docs/mcp-api-spec.md)に記載しています。Windows/Linuxの公式SDK接続テストをCIで実行し、`v*`タグのReleaseにはMCP ZIPも添付します。HTTPSのクラウドMCPも実装しています。以下を参照してください。

## AIから利用するクラウドMCP

HTTPSのStreamable HTTPで同じ11ツールを利用できます。Sitesの非公開プラグインを接続し、CSV登録画面から入力を渡します。PCにNode.jsやexeを用意する必要はありません。条件・CSV・ジョブ・成果物は利用者ごとに隔離し、通信をまたいで1時間保持します。

[クラウドMCPの利用・開発手順](docs/mcp-cloud-usage.md)。クラウドの上限はCSV単体2MiB・50,000行、比較合計6MiB、比較20条件、生成10条件、1ページ10件です。現在の接続先は同文書で案内します。実際のAIホストからの接続確認はプラグイン接続後に行います。

## CSVの形式・計算

各CSVには`hand`または`combo`列と、0〜1の`weight`列が必要です。例：

```csv
hand,weight
JhJdAsKc,0.2
ThTsAhKh,0.3
```

各CSVの比率 = そのCSVの条件該当weight合計 ÷ 選択した全CSVの条件該当weight合計 × 100。

合計が0の場合は比率を未定義として表示します。`frequency`をweightの代わりには使いません。同じハンドが別々のCSVにあれば各CSVのweightとして数えます。CSV内の重複ハンド、不正なweight、ボードカードとの衝突はエラーになります。

詳しくは[使い方](docs/usage.txt)と[デフォルト条件の定義](docs/default-conditions.txt)を参照してください。

## 履歴

成功した実行結果は`%LOCALAPPDATA%\FlopCommandApp\History`に保存します。履歴には入力CSVのパス、ラベル、条件、コマンド、結果を含みます。元CSVがなくても結果を閲覧できます。履歴の個別削除は元CSVには影響しません。

## テスト

```powershell
# 役・syntax・SD・ポケット・185件のデフォルト条件
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1

# 上記に加え、Windows画面・ブラウザ連携・PowerShell集計・履歴
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -Native
```

ネイティブテストはWindowsのデスクトップ環境とEdgeまたはChromeを必要とします。テストでは合成CSVと`test-results/`内の独立した履歴を使います。普段の履歴には書き込みません。

## 構成

- `src/`：Windowsアプリ、PowerShell集計、syntax照合、デフォルト条件。
- `src/lab/`：生成画面とJavaScriptの生成エンジン。
- `tests/`：エンジン・生成条件・Windows連携のテスト。
- `docs/`：操作説明と条件の定義。
- `dist/`：ビルド出力（Git管理対象外）。

## ドロー条件の定義

ナッツ／アンナッツのガットショット・オープンエンドは、手札2枚の組み合わせを持つ条件です。例えばT93のナッツガットショットは`(KQ,KJ)`です。残りの手札に別のドローがあっても除外せず、ラップや他の2枚組分類との重複を許容します。ナッツ判定はその2枚組で全ての完成ランクに対して最高ストレートを作れるかで行い、残り2枚のブロッカーによる昇格は行いません。完成ストレートのリドローは対象外です。「全てのSD」「ラップ」と画面のアウト数は4枚ハンド全体を基準にします。

FD/BDFDの最高・第2位のスートカードはボードを除いたデッキから選びます。As9h3dのナッツBDFDは`(Kss,Ahh,Add)`、セカンドナッツは`(Qss:!ks,Khh:!ah,Kdd:!ad)`です。

## 対応範囲

最強役を使うため、例えばJsTd7cのトップセットは`JJ!(98)`、ミドルセットは`TT!((98,JJ))`になります。BDSD、旧一覧のSDブロッカー、未定義の`pp all`は対象外として表示します。生成式は合法ハンドに照合しますが、MonkerSolver実機での構文受理・抽出一致・文字数制限は未確認です。

元のExcel、集計用CSV、個人の履歴、認証情報、実行ファイルはこのリポジトリには含めていません。
