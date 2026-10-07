# PLO Syntax Lab

PLO4のボードからMonker形式のsyntaxを生成し、2〜3つのCSVの該当ハンドをweight合計・比率で比較するWindowsアプリです。v11のソース一式を収録しています。

## 機能

- **デフォルト**：従来の185件の条件名から選択。syntaxは現在のボードに合わせて生成します。
- **syntax生成**：「syntax生成を開く」→役・条件を選択→「比較条件に追加」。
- フロップ・ターン・リバー（ボード3〜5枚）、オマハの手札2枚＋ボード3枚で判定。
- CSVごとのラベル、weight合計・比率の表とグラフ、結果CSVの保存。
- 「全ハンド（syntaxなし）」の集計チェックボックス。
- PowerShellコマンドの表示・コピー・保存・実行・中止。
- 結果の自動保存、履歴タブでの閲覧・検索・個別削除。

Excelや元の条件一覧CSVは実行時に不要です。185件の条件名のみを内蔵し、集計用CSVとは独立してsyntaxを生成します。

## 動作環境とビルド

Windows 10/11（64ビット）、.NET Framework 4.8、Windows PowerShell 5.1、Microsoft EdgeまたはGoogle Chromeを使用します。**ビルド時のみNode.js 20以上**が必要です。外部のnpmパッケージは不要です。

リポジトリを取得した後、リポジトリ直下で実行します。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\dist\FlopCommandApp.exe
```

`dist/FlopCommandApp.exe`は単体で利用できます。`dist/PLO-Syntax-Lab.html`は生成画面のみを単独で使うファイルです。生成・集計はPC内で行います。比較アプリとの連携にはランダムなトークン付きのローカル接続を使います。

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

## 対応範囲

最強役を使うため、例えばJsTd7cのトップセットは`JJ!(98)`、ミドルセットは`TT!((98,JJ))`になります。BDSD、旧一覧のSDブロッカー、未定義の`pp all`は対象外として表示します。生成式は合法ハンドに照合しますが、MonkerSolver実機での構文受理・抽出一致・文字数制限は未確認です。

元のExcel、集計用CSV、個人の履歴、認証情報、実行ファイルはこのリポジトリには含めていません。
