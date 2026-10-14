# PLO Syntax Lab：AI開発引継ぎ

更新日：2026-10-08。対象：v11の公開ソース。
確認した実装の基準コミット：`22a60e23585fa47ab8be92eea161f5ca92ffc264`。

追記：ローカルWindows連携MCP 0.2.0を復元・配布。以下のv11説明に加え、[MCPの導入・設計・検証](mcp/README.md)を読むこと。`src/Core.cs`へ`Choice`と`Commands`を切り出し、GUIと`mcp/NativeHost.cs`で同じ生成コマンド・集計・履歴処理を使う。`build.ps1`は`dist/PloMcp.Native.exe`も生成する。履歴schema 1には任意の`Provenance`を追加した。MCP関連のnpm依存は`package-lock.json`で管理する。GUI単体にはNode.jsやnpmの実行時依存を追加していない。

## 1. 次の開発者が最初に行うこと

1. この文書、[README](README.md)、[操作説明](docs/usage.txt)、[条件の定義](docs/default-conditions.txt)を読む。
2. `git status`と現在のブランチ・最新コミットを確認する。ユーザーの未コミット変更は保持する。
3. 下記の手順でビルドし、変更する領域に対応するテストを実行する。
4. 次に依頼された機能を実装する。この文書の改善候補だけを根拠に、機能や方式を変更しない。

リポジトリ：https://github.com/f22rap/plo-syntax-lab

引継ぎ時点の公開ブランチ・デフォルトブランチは`codex/initial-import`。`main`が存在すると仮定しない。現時点では、既知の未完了の実装依頼はない。今後のユーザーの指示を優先し、この文書は現状と過去の合意の記録として扱う。

## 2. アプリの目的と確定した要件

PLO4のボードを入力し、条件に対応するMonker形式のsyntaxを生成する。そのsyntaxに当てはまるハンドについて、2〜3つのCSVのweight合計と比率を比較するWindowsアプリ。

当初はExcelの`filter自動生成xyz_r`シートを利用し、平均frequencyの計算を検討していた。現在は**Excel・集計CSVに依存しない生成方式と、weight合計・比率の比較**へ変更済み。古い依頼に戻して実装しない。

- ボードは3〜5枚。完成役の判定には手札からちょうど2枚、ボードからちょうど3枚を使う。
- ラジオボタンは「デフォルト」「syntax生成」の2つ。起動時はデフォルト。
- デフォルトは従来の結果CSVから引き継いだ185件の条件名を内蔵する。集計用CSVから条件名を抽出する機能ではない。
- 各条件のsyntaxは現在のボードと生成エンジンから生成する。古いsyntaxのランク文字だけを置換する方式ではない。
- syntax生成は「syntax生成を開く」→役・条件を選択→「比較条件に追加」という操作を指す。
- CSVは2つ必須、3つ目は任意。CSVごとのラベルを結果表・グラフ・出力CSVに使う。
- 「全ハンド（syntaxなし）を集計」は初期状態でオン。条件の選択がなくても、この行だけで比較できる。
- PowerShellコマンドの表示・コピー・保存・実行・中止、結果CSVの保存を維持する。
- 正常終了した結果を自動保存し、履歴タブで閲覧・検索・再出力・個別削除できる。
- 空の`:()`、`:!()`は省く。ただし、対象外・該当0件の条件を全ハンドへ変換してはならない。
- 高速化では計算結果と既存機能を保持する。

## 3. 開発・実行環境

実行環境は64ビットWindows 10/11、.NET Framework 4.8、Windows PowerShell 5.1、EdgeまたはChrome。生成画面はHTML/CSS/JavaScript、比較画面はC# WinForms。Webサーバーへのデプロイを前提としたアプリではない。

GUIのビルドにはNode.js 20以上が必要。引継ぎ前の確認ではNode.js 24.12.0を使用した。GUI単体にはnpm依存パッケージ、Excel、Visual Studio、.NETの新しいSDKは不要。追加したMCPサーバーは実行時にもNode.jsとnpmの公式SDKを使用する。C#はWindowsの.NET Framework付属`csc.exe`でコンパイルするため、新しいC#構文の導入時は互換性に注意する。

```powershell
git clone https://github.com/f22rap/plo-syntax-lab.git
Set-Location plo-syntax-lab
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\dist\FlopCommandApp.exe
```

`build.ps1`は`src/make-offline.cjs`でHTMLを組み立て、次のリソースをexeに埋め込む。

| 元ファイル・生成物 | 埋め込み名 |
| --- | --- |
| `src/compare-runtime.ps1` | `compare-runtime.ps1` |
| `src/SyntaxMatcher.cs` | `SyntaxMatcher.cs` |
| `dist/PLO-Syntax-Lab.html` | `Lab.html` |
| `dist/defaults.html` | `Defaults.html` |

配布するアプリは`dist/FlopCommandApp.exe`。exe単体で動作し、利用者にNode.jsは不要。`dist/PLO-Syntax-Lab.html`は生成画面単独用。生成されたHTMLを直接修正せず、`src/`を修正して再ビルドする。

### Linux版の追加

LinuxではNode.js 20以上で`./start-linux.sh --open`を実行し、同じPCのブラウザから利用する。npm依存は不要。`src/linux/server.cjs`がloopback・ランダムトークン・Origin検証付きHTTPを提供し、`worker.cjs`で生成と比較を実行する。`compare.cjs`は既存JS matcherを使い、小数28桁の整数加算でCSVのweightを集計する。Linux UIは`src/linux/index.html`・`app.js`・`style.css`。既存の生成画面は`lab/`で配信し、比較条件をPOSTで受信する。

185件の条件、カスタム条件、2〜3CSV、CSV出力、履歴検索/閲覧/削除、計算の中止を提供する。WindowsのPowerShellコマンド生成機能とWindows履歴形式は移植対象に含めない。Linux履歴はXDG_DATA_HOME配下（未設定なら`~/.local/share`）の`plo-syntax-lab/history`に保存する。詳細な制限・操作はREADMEのLinux版節を参照。

`./test-linux.sh`は6つのエンジン・ドロー回帰テストと`tests/linux-tests.cjs`を実行する。追加テストではCSV入力、正確なweight合計、比率、HTTPの制限、条件受信、デフォルト生成、独立した一時履歴を検証する。クラウドではNode/API統合とDOMによる画面ロジックを確認した。実ブラウザでの表示・操作は、この環境のChromium sandboxの制約により未確認。Windowsネイティブテストの実行条件は変更していない。

## 4. ソースの見取り図

| ファイル | 主な責務 |
| --- | --- |
| `src/Program.cs` | 通常起動・例外表示・生成用ブラウザの終了処理 |
| `src/App.cs` | `MainForm`、ボード入力、モード・条件選択、集計実行、表示状態 |
| `src/Core.cs` | GUIとMCPで共有する`Choice`・`Commands`、ボード正規化とCSVヘッダー検証 |
| `src/default-labels.json` | 185件の`cell`・`label`。条件の安定した識別子と順番 |
| `src/defaults.js` | 旧条件名を新エンジンの役・追加条件に対応付ける |
| `src/lab/engine.js` | 合法ハンド列挙、役・ドロー判定、symbolic syntax生成・照合 |
| `src/lab/app.js` | 生成画面の操作、条件追加、JSON/TXT出力 |
| `src/lab/index.html`、`style.css`、`worker.js` | 生成画面と計算用Worker |
| `src/DefaultRunner.cs` | Edge/Chromeの専用一時プロファイル・非表示計算・中止 |
| `src/LabBridge.cs` | loopback HTTPによるHTML配信と条件受信 |
| `src/LabSelection.cs` | 条件JSONの読み込み・検証・ネイティブの条件一覧への変換 |
| `src/CompareCommands.cs` | 単独実行可能なPowerShellコマンドの組み立て |
| `src/SyntaxMatcher.cs` | C#の構文解析と4枚の手札への照合。PowerShellにも埋め込む |
| `src/compare-runtime.ps1` | CSV検証、条件ごとのweight集計、比率、CSV出力 |
| `src/Execution.cs` | PowerShell実行・中止・結果デコード |
| `src/RatioChart.cs` | CSVラベルを使う比率グラフ |
| `src/History.cs`、`HistoryPanel.cs` | 履歴の保存・読込・検索・表示・個別削除 |
| `tests/` | NodeテストとWindows連携テスト。製品の起動コードとは分離 |

## 5. 生成から集計までの流れ

### デフォルト

`MainForm.QueueDefaults` → `DefaultRunner` → ローカルの`defaults`ページ → `engine.js`と`defaults.js` → 条件JSONを`LabBridge`へPOST → `ReceiveSelection` → 条件一覧。

ボード入力には350msの遅延処理がある。生成は専用の非表示ブラウザで行う。生成結果は起動中のメモリに最大6ボード分を保持し、新しい結果を追加するときに既存が6件ならキャッシュを全消去する。LRU方式ではない。再生成ボタンはキャッシュを使わずに生成する。

生成要求の`job`とボードを確認し、古い要求の完了で最新の表示を上書きしない。ボード変更・モード変更・終了に伴うキャンセルを維持する。`QueueDefaults`と`ExecuteAsync`のWindowsForms同期コンテキスト設定は、非同期処理後のUI更新にも関係する。

### syntax生成

`OpenGenerator` → 通常のブラウザでローカル生成画面 → 「比較条件に追加」 → `LabBridge` → `AcceptSelection`。条件JSONのファイル読み込みも可能。取り込み時にはsyntax生成モードへ切り替わる。

同じボードでは2モードの条件一覧・チェック状態を別々に保持する。選択中のモードだけを集計する。モード変更時は古い集計表示を消し、保存済み履歴は残す。ボード変更時は古い生成条件をクリアする。起動をまたぐ選択状態の復元は未実装。

### PowerShell集計

`CompareCommands.Build`がボード・条件・CSVパス・ラベルとC# matcherを含むスクリプトを生成する。CSV未選択でもコマンドは作成できるが、実行には2〜3ファイルが必要。

実行時はCSVを検証し、4枚ハンドを52ビットのマスクに変換する。各条件のmatcherで照合し、decimalでweightを集計する。`ALL`行は専用処理で全件合計を使用し、syntaxは空文字。

```text
各CSVの比率 = そのCSVで条件に一致するweight合計
             / 選択した全CSVでその条件に一致するweight合計 * 100
```

条件ごとの合計が0なら比率はnull。画面は「—」、CSVは空欄。0%とみなさない。CSV間の同じハンドはそれぞれ数える。平均frequencyやハンド件数の比率に置き換えない。

## 6. 入力・連携データの契約

### CSV

- UTF-8のCSV。`hand`または`combo`列と`weight`列が必要。
- 手札は`JhJdAsKc`のような8文字。ランクは大文字、スートは小文字の`shdc`。
- weightは0〜1の数値。`frequency`で代用しない。
- ファイル内の重複ハンド、手札内の同一カード、ボードとの重複、空データ、不正weightを拒否する。
- 手札の並び順が違っても、同じ4枚なら重複として扱う。
- 同じCSVファイルを複数欄に指定しない。ファイル名に認識可能なボードがあれば入力ボードとの一致を検証する。フロップの順番は許容し、ターン・リバーは順番を区別する。
- 出力で入力CSVや実行中スクリプトを上書きしない。

### 条件JSON

以下はJsTd7cのトップセットを追加する例。`count`はCSV内の件数ではなく、ボードに対する合法ハンド集合の件数。

```json
{
  "app": "PLO Syntax Lab",
  "game": "PLO4",
  "board": ["Js", "Td", "7c"],
  "ranges": [
    {"label": "トップセット", "syntax": "JJ!(98)", "count": 3103}
  ]
}
```

デフォルト用には`mode: "defaults"`、`job`と各行の`cell`・`problem`を加える。185件のユニークなcellが必要。0件の条件はsyntaxを空にする。個別生成では0件の条件を比較一覧に追加しない。

`LabSelection.Read`は形式・件数の非負・構文を検証するが、外部JSONの`count`を全合法ハンドから再計算する処理ではない。CSV集計の件数は実際の照合で計算する。

正規化ボードのC#プロパティ名は`NormalizedBoard`。JSONの`board`と紛らわしい名前にすると、`JavaScriptSerializer`の名前解決で問題が起こるため注意する。

## 7. 役・条件の意味を保つ

詳細は[デフォルト条件の定義](docs/default-conditions.txt)が基準。条件名が旧版と同じでも、旧Excelと件数が一致することは保証しない。

- 完成役は最強役で分類する。ストレートを作る手札をセットにも含めない。
- JsTd7cのTset all（cell `L49`）は`JJ!(98)`、3103コンボ。Mset all（`L61`）は`TT!((98,JJ))`、3094コンボ。選択可能なら、この2つを初期選択する。
- T/M/Bはボードの重複を除いたランクの最高・第2位・最低。ランクが2種類以下ならM条件は対象外。
- BDFDはフロップ限定。0/1/2bdfdは特定スートの固定指定ではなく、該当するスート数を数える。
- ポケットは同ランク2枚以上。`pp under`には22も含む。
- 2026-10-08の追加指示でSDの4種類は手札2枚組の条件に変更済み。T93のnutGutは(KQ,KJ)、nutOpenはQJ、nonGutは(Q8,J7,86,76)、nonOpenは(J8,87)。残り2枚のドローやラップと重複する。組み合わせの全完成ランクで最高ストレートを作れる場合がナッツで、片側だけナッツのoeはアンナッツ側。残り2枚のブロッカーで昇格させない。全SD・ラップ・表示する実アウト数は従来どおり4枚ハンド全体で判定する。
- BDSD・旧SDブロッカー条件・未定義の`pp all`は対象外。名前だけを残し、理由を表示する。
- ペアボードでは旧セット等の多くが該当しない。フルハウスやトリップス等は個別生成側を使う。
- 空集合を`*`や全ハンド条件に置き換えない。具体的な4枚ハンドの羅列に切り替えず、ランク・スート・除外条件のsymbolic syntaxを生成する。

JavaScriptの生成・照合とC# matcherは別実装である。構文を拡張する場合は片方だけ変更しない。C# matcherは同じ実カードを複数のトークンに重複割当しないことが重要。syntaxをPowerShellの式として直接評価しない。

## 8. 履歴とローカル連携

履歴は`%LOCALAPPDATA%\FlopCommandApp\History`に1実行1JSONで保存する。`Schema = 1`。主要フィールドは`Id`、`SavedUtc`、`Board`、`WorkbookPath`、`Labels`、`Paths`、`Conditions`、`Command`、`ResultJson`、`CsvBase64`。

`WorkbookPath`は旧版の履歴互換性のため残っており、新規保存では空文字。Excel依存を戻すためのフィールドではない。履歴は結果のスナップショットであり、元CSVから再計算せずに表示する。保存は一時ファイルへの書き込み・flush・rename。履歴IDからのパス生成には形式検証がある。

成功したアプリ内実行だけを自動保存する。失敗・中止・単独実行した.ps1は自動保存しない。履歴の個別削除は確認後にそのJSONだけを削除する。元CSVや既にエクスポートした結果CSVは消さない。

`LabBridge`は`127.0.0.1`の動的ポートとランダムトークンのURLで動く。Host・Origin・Content-Type・長さを検証し、POSTは条件データだけを受け取る。任意のファイル操作やコマンド実行をHTTP経由で公開する機能はない。変更時もloopback限定とこれらの検証を維持する。

`DefaultRunner`は専用の一時ブラウザプロファイルを使う。中止・終了では自身が起動したプロセスを終了する。ユーザーが普段使うブラウザのプロファイルや全ブラウザプロセスを操作しない。

## 9. テストと確認済みの範囲

```powershell
# 生成エンジンとデフォルト条件
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1

# 上記に加え、WinForms・実ブラウザ・PowerShell・履歴の連携
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -Native
```

引継ぎ前に、基準コミットの公開用ソースからビルドし、以下を確認した。

| テスト | 結果・対象 |
| --- | --- |
| `tests/tests.cjs` | 125 checks。基本役・合法ハンド数・syntax等 |
| `tests/symbolic-tests.cjs` | 120 checks。複数ボードのsymbolic syntax照合 |
| `tests/straight-draw-tests.cjs` | 46 checks。実アウト数とSD基本ケース |
| `tests/draw-regression-tests.cjs` | FD/BDFDのナッツ・セカンドナッツ、独立した2枚組SD oracle、全合法ハンドとの照合、単独HTML内の実エンジン |
| `tests/pocket-tests.cjs` | 68 checks。ポケット条件 |
| `tests/defaults-test.cjs` | 5ボード、各185行、条件名・件数・対象外・BDFD等 |
| `tests/ModesTest.cs` | 2モードの保持、実ブラウザでの自動生成、PowerShell集計、比率・グラフ・履歴、ボード変更、中止・古い結果の抑制、キャッシュ |

デフォルト条件の選択可能数（2026-10-08の2枚組SDへの変更後、defaults-testで確認）：

| ボード | 選択可能数（全185件中） |
| --- | ---: |
| JsTd7c | 149 |
| AsKd7s | 71 |
| AsAd7c | 0 |
| Ks8s5d2d | 103 |
| AsKd7s5h9c | 31 |

合法な4枚ハンドの総数はフロップ211876、ターン194580、リバー178365。

ネイティブテストは合成CSVと`test-results/native/history-<GUID>`を使い、普段の履歴に書き込まない。成功結果は`test-results/native/modes-test-passed.txt`、失敗時は`test-error.txt`。過去の失敗ファイルが残る可能性があるので、実行の終了コードと更新日時で判断する。テスト用exeは`dist/FlopCommandApp.Tests.exe`であり、製品exeに旧開発時のテスト用引数を渡してもテストは実行されない。

制限付きの実行環境ではEdgeの子プロセスが起動できず、生成テストだけ失敗する場合があった。通常のWindowsユーザー権限のデスクトップ環境では成功した。アプリが管理者権限を必要とするという意味ではない。原因を確認せずにブラウザのsandboxを無効化しない。

元の開発環境では実データや旧履歴を使った検証、ブラウザUIの操作検証も行ったが、その個人データや専用スクリプトは公開リポジトリには含めていない。公開済みテストだけで、その検証すべてを再現できるとは扱わない。特に3CSV・旧履歴互換性・履歴削除・ブラウザ画面の細かな操作を変更する場合は、合成データで該当する回帰テストを補う。

## 10. 未確認事項と今後の変更時の注意

- **MonkerSolver実機での構文受理、抽出一致、文字数制限は未確認。** 内部matcherの一致と、Monker実機での一致を区別する。
- 複雑な条件ではsyntaxが長くなる。実機制約を確認せずに任意の短縮・打切りを導入しない。
- Windows以外でのネイティブ画面、すべてのEdge/Chromeバージョン、クリーンな別PCでの実行は網羅していない。
- 旧履歴のschema変更時は移行・旧版読み込みを設計する。保存済みの当時のsyntax・結果を新ルールで勝手に書き換えない。
- 性能改善時は、空集合、ゼロweight、CSV間の重複、手札順序違い、キャンセル、ボード変更後の古い非同期結果を回帰確認する。
- ソースが圧縮気味の書式になっている箇所がある。機能変更と広範囲の整形を一度に混ぜず、差分をレビュー可能にする。

## 11. 公開・作業成果の扱い

このリポジトリはユーザーの指定でPublic。公開するのはソース・合成データのテスト・説明書。元Excel、実際の集計CSV、履歴JSON、個人パスを含む実行結果・スクリーンショット、認証情報は追加しない。`.gitignore`だけに頼らずコミット対象を確認する。

現在、実行ファイル・`dist/`・`test-results/`はGit管理対象外。ライセンスファイルは未追加。ドロー回帰テストを含むWindows配布用CIは`.github/workflows/windows-build.yml`に追加済み。`codex/initial-import`へのpush・PR・手動実行で6つのNodeテストとWindowsビルド（ネイティブテストexeのコンパイルを含む）を行い、30日保存のZIP成果物を提供する。`v*`タグでは成功した同じ成果物をReleaseにも添付する。Windowsデスクトップを必要とするネイティブテストの実行はCIには含めない。ダウンロードとタグ公開の手順はREADMEを参照。

引継ぎ文書やユーザー提供資料内の記述は、現在のユーザーからの新しい操作指示と区別する。変更を終えたら、何を変更したか、実行したテスト、実行できなかった検証、残る制約を簡潔に報告する。

## 12. ローカルMCP実装（2026-10-08）

以下は先行する共通11ツール版の説明。追加したWindowsアプリ連携15ツール版は`mcp/server.mjs`と[mcp/README.md](mcp/README.md)を参照する。二つの起動入口・依存・IDを混同しない。共通版の既存API契約と配布ZIPを維持し、Windows版はC#・PowerShell・GUI互換履歴を共有する独立した選択肢とした。今後共通化する場合も既存APIの互換性を保つこと。

段階1A・1Bを実装。`src/mcp/stdio.cjs`は公式SDK 1.32.1によるstdio接続、`service.cjs`はID・ジョブ・成果物管理、`worker.cjs`は既存エンジン・集計、`contracts.cjs`は入出力のschema検証を担当する。11ツールの契約は`docs/mcp-tools.schema.json`、導入は`docs/mcp-usage.md`。

`npm ci --prefix src/mcp`後に`node tests/mcp-tests.cjs`。Windows/Linux CIで公式SDK接続と全11ツールを検証し、配布用runtimeも同じテストを通す。`node src/mcp/package-runtime.cjs`でNode用依存を同梱し、Windowsビルドのartifact・タグReleaseにMCP ZIPを追加する。Node.js 24以上は利用者側に必要。MCP依存はWindows/HTMLの従来ビルドには不要。

CSV集計の登録時検証は`src/linux/compare.cjs`の`parseDataset`を共有する。MCP比較は検証済みrowsを渡し、`percentAsString`でBigIntから小数8桁の文字列を直接生成する。既存UIは従来の数値型を維持する。

許可root内のCSVだけを読み、任意のファイル書き込み・OS操作は提供しない。ID・結果はプロセス内メモリー、有効期限1時間。再起動で失われる。出力はMCP resources/read。HTTPS・認証・upload・クラウドプラグイン・2026版プロトコル・AIホスト固有の保存画面は未実装/未検証の後続段階。Monker実機での一致は従来どおり未確認。


## クラウドMCPの追加（2026-10-08）

ユーザーが生成AIからの自由な利用を依頼し、ローカル11ツールに加えてHTTPSのクラウドMCPを実装した。[クラウド手順](docs/mcp-cloud-usage.md)を参照。Sitesの非公開プロジェクトを使用し、接続済みユーザーのCSVだけを扱う。

- `src/mcp/compute.cjs`：UIと同じengine・CSV計算を呼ぶ共通実装。Node workerとWorkerで再利用する。
- `src/mcp/domain.cjs`：条件定義・boardInfo・classificationVersion。`contracts.cjs`とJSON Schemaも共有。
- `cloud/worker.mjs`：公式SDKによるstateless POST /mcp、Sites dispatchの認証ID、Origin・入力サイズの検証。
- `cloud/service.mjs`：11ツール、25秒の制限、ジョブのCAS更新、CSV2MiB/50,000行、精度28桁、結果出力。
- `cloud/store.mjs`、`db/schema.ts`、`drizzle/`：D1メタデータとR2本文。owner区分、1時間の期限、原子的quota・一度だけのupload消費。
- `cloud/page.mjs`：CSV登録とID一覧。WebMCPの任意拡張はfeature detectionし、実ブラウザでは未検証。
- `cloud/tests/cloud-tests.mjs`：公式Streamable HTTPクライアント、workerd、D1/R2を使う統合テスト。

クラウド用依存はcloud/package.jsonとlockfile。Windows/HTML/Linuxの従来ビルドには不要。既存CIはMCPのWindows/Linuxテストにクラウドテストを追加し、全て成功した後にexe・MCP ZIPをビルドする。Sites公開は別の管理ソースリポジトリから行う。GitHub変更だけでは自動再デプロイされない。Sites用checkoutのcloud/source-info.jsonに対応するGitHubコミットを記録し、版と出力の来歴を一致させる。

適用済みD1 migrationは変更しない。Drizzleで追加migrationを生成する。稼働中WorkerはSites dispatchのみに公開し、認証IDヘッダーを信用できない直結の経路へそのまま公開しない。実AIホストの接続完了を、公式SDKのテスト成功だけで主張しない。


### 15ツール版の復元（2026-10-09）

ユーザーが「15ツール版を実装せよ」と明示したため、取り消されていたWindowsアプリ連携版を復元した。15ツールはWindows専用のstdio。`mcp/server.mjs`、`mcp/README.md`、`tests/mcp.test.mjs`を参照。共通11ツール・公開済みクラウド11ツールの契約を変更しない。

`npm run package:windows-mcp`は事前にビルドした`dist/PloMcp.Native.exe`とNode用依存・対応ソースを`dist/PLO-Syntax-Lab-Windows-MCP`へコピーする。CIはソース版・同梱版の実stdio接続、全15機能、8ドロー条件、同一フロップ順序、2/3CSV、小数精度、永続履歴、削除制約、全出力形式を検証し、専用ZIPとSHA-256をartifact／タグReleaseへ添付する。通常利用はZIP展開・設定だけでよく、Node.jsを別途用意する。

ユーザーのCSV・GUI履歴は移行・削除しない。設定のallowHistoryDeleteは初期false。削除ツールはユーザーの明示的な削除依頼がある場合だけ使う。テストは合成CSVと独立した一時履歴を使用する。クラウド11ツールにWindows用exe・PowerShellを持ち込まない。

## EQ機能の追加（2026-10-12時点の記録）

Windows EXEの「EQ」タブに、双方のアクション前レンジから選択syntaxごとのエクイティを計算する機能を追加した。`src/EquityEngine.cs`、`EquityInput.cs`、`EquityJob.cs`、`EquityPlot.cs`、`EquityPanel.cs`、`EquityScatterChart.cs`、`EquityArchive.cs`、`EquityHistoryForm.cs`を参照。

- EQは合法な対戦組の自分weight×相手weightで正規化。現在の局面のベット／チェックで相手レンジや自分のレンジを絞らない。
- 自分のレンジは均等・手入力・共通基準の全アクションCSV合計から選択。相手レンジはテキスト／CSV入力。
- サイズ別ベット頻度は各ハンドのアクション比率を自分のアクション前レンジで平均。CSV情報のないハンドを勝手に除外・再正規化しない。
- CSV／JSON／SVGの出力と、既存比較履歴から分離した `History/EQ` の自動保存・閲覧・削除。履歴は保存時の結果であり、元CSVを再読込しない。
- `test.ps1`は新しい`test-equity.ps1`も実行。C# 5／.NET Framework 4.8を維持し、追加参照はSystem.Runtime.Serializationのみ。
- Linuxの.NET 8で計20,722チェック通過。製品と画面テストの.NET Framework参照でのクロスコンパイル成功。Windows実機での起動・EQ画面操作は未確認。過去のWindows検証を今回のEQ機能の確認として扱わない。
- 詳細：[入力](docs/equity-ui.md)、[計算](docs/equity-engine.md)、[散布図](docs/equity-plot.md)、[保存・検証](docs/equity-save.md)。ステップ2〜4文書の未実装記述は各段階時点の記録。

## EQ集計画面・複数相手CSVへの更新（2026-10-13）

上記の独立したEQタブとレンジ選択方式は更新済み。現行仕様は[集計画面へのEQ統合](docs/equity-integrated-ui.md)を参照。

- 「集計＋EQ計算」でCSV比較とEQ計算を続けて実行し、集計ページのグラフ・表に両方を表示する。共通の中止ボタンを使用する。
- 自分のアクション前レンジは、集計に入力した全CSVの物理ハンド別weight合計に固定する。ファイルごとの正規化はしない。
- 相手レンジはテキスト、または複数CSVの一括読込に対応。7列CSVではweight列を優先。同一物理ハンドをファイル間で加算し、全体の比率を維持する。ファイル内の重複は従来の最大weight方式を維持する。
- 読込済みファイル数・統合ハンド数・全ファイルのフルパスを表示する。不正ファイルや中止時は直前のレンジとパス一覧を保持する。全入力パスを履歴に保存し、結果出力による入力ファイルの上書きを防ぐ。
- EQ×アクション頻度グラフには入力した全アクションを表示し、CSV1は青・CSV2は緑・CSV3は橙。ホバーで条件名・syntax・アクション・EQ・頻度を表示する。旧BetActionsは互換メタデータとして保持し、グラフの選別には使わない。
- 合法な対戦組の照合とsyntax抽出を高速化。合成データ・同一シードの比較で結果一致を確認。Linuxでの4条件ジョブは中央値4.871秒から1.987秒。Windows速度は未測定。[測定手順](docs/equity-performance.md)とbenchmark-equity.ps1を参照。
- 計算・CSV入力・頻度・保存の20,850チェックがLinux .NET 8で通過。製品EXEとWindows操作テストEXEはC# 5／.NET Framework 4.8参照でコンパイル済み。Windows実機での起動、ホバー、パス欄の表示と操作テスト実行は未確認。
- ウィンドウタイトルの版識別は2026.10.13-multi-range。配布はソースから既存のWindows buildを使用し、ローカル確認用ZIPや個人CSVはGitに追加しない。


## ユーザーsyntax・複数ボード登録（2026-10-14、第2ステップ）

ユーザー承認済みの第2ステップとしてResearchData.cs、ResearchPanel.cs、ResearchEditors.csを追加。集計タブ内の条件セット編集・ボードCSV登録を実装した。手入力syntaxはそのまま保持し、literal/per_boardをサポート。未設定ボードは適用を中止し、全ハンドや別ボード定義に置き換えない。生成条件の取込はボード別定義として保存する。

ResearchStoreは履歴ルートのResearch/workspace.jsonへ、版検証・ロック・一時ファイル置換で保存。JSON読込は既存データを置き換えずコピーとして追加する。カタログは共通局面条件1つと複数スポットを持ち、別局面は別カタログ。PLO4・HU・フロップ、2〜3アクション、相手複数CSV。本文検証・全パス・SHA-256・統合ハンド数を登録する。相手省略は頻度調査用。詳細はdocs/research-setup.md。設計段階のJSONと、実装のschema_version=1の読込用JSONを混同しない。

今回の範囲は設定の編集・保存・登録、および現在ボードへのsyntax適用まで。登録カタログの一括計算、ボード特徴による横断調査、MCP公開、クラウド更新は未実装。次のステップはユーザーの承認後に進める。既存の15/11ツール版MCPは変更していない。

既存20,850チェックに研究設定54チェックを追加して20,904チェック通過。Windows製品と操作テストをクロスコンパイル。実際のWindows画面操作は未実施。tests/ResearchUiTests.csはWindowsデスクトップ用であり、コンパイル成功を実行成功とは扱わない。

## ボード横断の一括調査（2026-10-14、第3ステップ）

ResearchRun.cs、ResearchFiles.cs、ResearchPanel.Run.cs、ResearchResultsView.csを追加。既存集計タブから登録カタログを一括計算し、ボード別・特徴別の表と全アクション散布図を表示する。Ahi抽出、スート／ペア／ランク構成の分類、ノードごとの分離、CSV/JSON/SVG/Markdown出力、Research/Runsへの自動保存・復元を実装。細部はdocs/research-run.md。

数値計算は既存EquityInput/EquityRange/EquityFrequencyPlan/EquityEngineを使う。単一ボードEQと同じシード派生・入力で一致する。特徴別は登録された有効ボードの等重み平均、図はEQと頻度が両方ある共通対象。欠損を0にせず、存在しないベットサイズを補わない。総betはraiseを含まない。進行中は入力を固定し、中止では確定したボードだけを保存する。入力ファイルの登録ハッシュと照合する。

64件の新規チェックを含む20,968チェック成功。製品／Windows操作テストはクロスコンパイル。SVG出力はレンダリング確認済み。Windows実画面操作は未実施。MCP公開とAIホスト接続は未着手の第4ステップで、ユーザーの次の承認後に進める。

## 研究MCP接続（2026-10-14、第4ステップ実装）

Windows版mcp/server.mjsは従来15＋研究12の27ツール。server-core.mjsはスキーマ登録を分離。ResearchHost.csを別のPloResearch.Native.exeへビルドし、GUIと同じResearchStore/ResearchRun/ResearchFilesを利用する。research-runtime.mjsはJSON行通信と協調的キャンセル、research-service.mjsは計画・ジョブ・キーの冪等性を担当。既存11ツール／クラウドは変更しない。ユーザーsyntaxはliteral/per_boardのまま、generatedは未実装。

検証はtests/research/BridgeTests.cs（GUI計算との一致・途中結果保持等17チェック）、tests/research-mcp.test.mjs（公式SDK実stdioの一連の調査）。Linuxで同じC#コードを.NET 8で動かすハーネスはtests/research-harness.mjsであり、製品のWindows制約を迂回する設定は提供しない。新EXEはWindows参照でコンパイル。Windows実機と選択AIホストの自然文操作は未確認で、ユーザー環境での接続確認が残る。詳細はdocs/research-mcp.md。

### Codex接続先の選定とセットアップ

ユーザーの「任せる」により、WindowsローカルCodexを接続先に選定。Setup-Codex.cmd/ps1、mcp/codex-setup.mjs、check-connection.mjsを追加。既存mcp.local.jsonと同名Codex MCP登録は保持し、公式SDKの読込確認後にCLI登録する。CLIなしはTOMLと設定画面での手順を提供。診断のMCP成功とAIホスト実行を区別する。docs/codex-setup.md参照。ユーザーPCへの登録・実Windows操作は引き続き未確認。

## ディレクトリ取込（0.4.0）

ユーザーのCOvsBTN/board/IP・OOP/node/board_position_history_action.csv形式をplo-post-tree-v1として実装。Rはポット比、Aはall-in、全アクションweightは共通基準とユーザー確認済み。research-directory.mjsがハッシュ付きスキャン・局面別プレビュー・直前相手ノードの全CSV対応付けを行い、3つのMCPツールを追加（合計30）。登録は新規カタログのみ、最大200ボード・既存のC#本文検証とatomic saveを通す。OOP ROOTの相手レンジは未出力として扱い、IP/vs_checkを無条件流用しない。ResearchActionのbet/raiseはpot_fraction=allinを許可、1アクション局面にも対応。詳細はdocs/directory-import.md。実ユーザーFドライブのデータとWindows上のAIホスト操作は未確認。

## 最高ランク条件（0.4.1）

Ahi固定を撤廃。scan/list highest_rankと計算settings.HighestRankはall/A/K/Q/J/T/9…2。scan省略=all、登録catalog.highest_rankをlist/計算省略時に継承し、GUIは登録条件が初期選択。ResearchBoardFilterとmcp/board-filter.mjsで最高ランクを判定。旧AhiOnly/ahi_onlyはtrue=A、false=allとして互換維持し新旧矛盾は拒否。結果JSON/Markdownに実効条件を記録。スキャン件数はexcluded_board_countへ汎用化。

## Windowsのディレクトリ許可判定（0.4.2）

61cfca3のWindows CIはEXEビルド・計算21,160件・bridge 17件を通過後、directoryテストでinputRoots範囲外エラー。fixtureがloadConfigの正規化を省略していたため本番と同じloadConfig経由へ修正。scanDirectoryも入力表記のlexical事前判定を廃し、plainでリンクを拒否した後にrealpathで解決し、canonical inputRootsとの範囲検査を実施する。短縮名と通常名の同一場所を受け入れ、範囲外・リンク拒否は維持。元表記とcanonical表記、非リンク別名の模擬回帰テストを追加。Windows再実行は次のpush後に確認。

### 0.4.2 Windows CIの結果と配布テストの準備修正

d1adfd9のCI（38494688812）でWindowsの短縮名RUNNER~1と通常名runneradmin、日本語・空白を含むパスの同一判定を確認。directory 8件、setup 3件、bridge 17件、研究と従来MCPの実stdio統合テスト、計算21,160件、EXEビルドは成功。次の配布テストでPLO_MCP_TEST_ROOTの未作成によるmkdtemp ENOENTが発生し、ZIP公開は未達。tests/research-mcp.test.mjsを既存mcp.test.mjsと同様にrecursive mkdir後mkdtempへ修正。未作成の入れ子フォルダーを指定した配布版SDK統合テストはLinux/.NET 8で成功。修正後の再push・Windows配布テストは承認待ち。

その後00a173aでWindows配布テスト・ZIP生成が成功。ブラウザ接続手順を含む117b860もCI 38504566136で成功し、Windows-MCP artifact 11867242351を公開済み。トンネル・利用者のChatGPTからの実接続は未検証。

## Node.js不要の接続EXE（配布・Windows検証の段階）

社内のNode.jsダウンロード制限を受け、ユーザーがC#版の開発を希望。各ステップで承認を得る方針を維持。接続EXE・設定画面の第1段階と、研究機能移植の第2段階が承認済み。mcp/nativeのC#5/.NET Framework 4.8でPloMcp.Connect.exeを実装し、独立したbuild-native-mcp.ps1でNode.jsなしにビルド可能。GUIで複数CSVフォルダー・結果/履歴先を保存し、自己子プロセスとのMCP診断を実行する。mcp.local.jsonと同じキーを使用し、既存設定読込は無変更、保存は競合検知とバックアップ付き。短縮名を実パスへ統一、リンク拒否、JSONサイズ/深度制限を持つ。

0.2.0-preview.1は研究15ツール＋get_status/get_artifactの17件を実装。DirectoryImport.csが既存ディレクトリ規約を解釈し、NativeResearch.csがResearchBridgeをプロセス内で呼ぶ。計算本体・履歴形式・SVG等は既存GUIと共有。C#側でもJSON Schemaを検証し、定義は既存research-tools.mjsから生成した埋込JSONとCIの差分検査で維持する。既存syntax自動生成等の追加13ツールは移植対象外。Node.jsの隠れた同梱や起動で代替しない。

Windows EXEをクロスコンパイル済み。同一ソースのLinux/.NET 8で72項目のC#検証と公式SDKの3つの実stdio統合シナリオが成功。研究フローはtests/research-mcp.test.mjsをPLO_NATIVE_MCP=1で共用し、EXE側のPATHからNode.jsを除外する。追加のnative-mcp-directory.test.mjsで10ノードの対応一致、不正な分母・重複ボード・範囲・スキーマを検証。単一ジョブ、版/ハッシュ固定、1時間の計画、冪等キー、協調的中止、EOFでの部分履歴保存、リソース読込に対応。

ユーザーは2026-10-14に配布ZIP作成・push・Windows検証を承認。package-native-mcp.ps1はNode.js不要版PLO-Syntax-Lab-NoNode.zipを生成し、接続EXE・通常GUI・HTML・導入手順・SHA-256付きbuild-info.jsonをまとめる。CIはNode.jsなしのビルド、Windows設定画面の起動/パス表示/保存/終了、ZIPを展開した実EXEでの公式SDKと研究フローを検証してからartifactを公開する。既存30ツール版の配布を変更・廃止しない。Windows上の検証結果は対象コミットのCIを確認すること。利用者のChatGPTと公式トンネルを通した実接続は未確認。詳細はdocs/native-mcp-preview.mdとdocs/native-mcp-install.txt。
