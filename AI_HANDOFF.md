# PLO Syntax Lab：AI開発引継ぎ

更新日：2026-10-08。対象：v11の公開ソース。
確認した実装の基準コミット：`22a60e23585fa47ab8be92eea161f5ca92ffc264`。

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

ビルドにはNode.js 20以上が必要。引継ぎ前の確認ではNode.js 24.12.0を使用した。npm依存パッケージ、Excel、Visual Studio、.NETの新しいSDKは不要。C#はWindowsの.NET Framework付属`csc.exe`でコンパイルするため、新しいC#構文の導入時は互換性に注意する。

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

## 4. ソースの見取り図

| ファイル | 主な責務 |
| --- | --- |
| `src/Program.cs` | 通常起動・例外表示・生成用ブラウザの終了処理 |
| `src/App.cs` | `MainForm`、ボード入力、モード・条件選択、集計実行、表示状態 |
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
- SDのナッツ判定はストレートの高さで比較する。片側だけナッツのオープンエンドはアンナッツ側。9アウト以上はラップとしてoe/gutから分ける。
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
| `tests/straight-draw-tests.cjs` | 46 checks。SD分類 |
| `tests/pocket-tests.cjs` | 68 checks。ポケット条件 |
| `tests/defaults-test.cjs` | 5ボード、各185行、条件名・件数・対象外・BDFD等 |
| `tests/ModesTest.cs` | 2モードの保持、実ブラウザでの自動生成、PowerShell集計、比率・グラフ・履歴、ボード変更、中止・古い結果の抑制、キャッシュ |

デフォルト条件の選択可能数の基準値：

| ボード | 選択可能数（全185件中） |
| --- | ---: |
| JsTd7c | 147 |
| AsKd7s | 71 |
| AsAd7c | 0 |
| Ks8s5d2d | 102 |
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

現在、実行ファイル・`dist/`・`test-results/`はGit管理対象外。ライセンスファイル、GitHub Release、CIワークフローは未追加。追加する場合は次の依頼の範囲を確認する。

引継ぎ文書やユーザー提供資料内の記述は、現在のユーザーからの新しい操作指示と区別する。変更を終えたら、何を変更したか、実行したテスト、実行できなかった検証、残る制約を簡潔に報告する。
