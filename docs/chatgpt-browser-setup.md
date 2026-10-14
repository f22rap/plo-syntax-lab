# ブラウザ版ChatGPTからPLO Syntax Labを使う

対象：Windows MCP 0.4.2の30ツール版。CSV読込とEQ計算はCSVを保存したWindows PCで実行し、ChatGPTがMCPを通じて操作する。既存の共通11ツール版・クラウド版とは別の接続である。

2026-10-14確認：コミット00a173aのWindows CIでソース版・配布版のMCPテストとZIP生成が成功した。以下は公式Secure MCP Tunnel仕様と本アプリのコードに基づく導入手順であり、利用者のChatGPT・Windows PC・トンネルを通した実接続は未検証。

## 1. アプリとCSVの設定

[検証済みZIP](https://github.com/f22rap/plo-syntax-lab/actions/runs/38495439561/artifacts/11864020250)を取得する。GitHubログインが必要。artifact内のPLO-Syntax-Lab-Windows-MCP.zipも展開し、たとえば`F:\PLO-Syntax-Lab`に全ファイルを置く。Node.js 20以上、.NET Framework 4.8を用意する。配布ZIPにはNode.jsとtunnel-clientは含まれない。

PowerShellで実行する。以下はこの展開先専用の例なので、別の場所に置いた場合は全コマンドのパスを合わせる。

```powershell
Set-Location 'F:\PLO-Syntax-Lab'
node --version
node .\mcp\codex-setup.mjs 'F:\PLOrange\post\6max\lowstakes_5%4bb\100bb\SRP\COvsBTN'
node .\mcp\check-connection.mjs .\mcp.local.json
```

このNodeコマンドは設定ファイルと補助ファイルを作るだけで、Codexへの登録は行わない。Codexのインストールは不要。

`mcp.local.json`が既にある場合は、上の設定作成コマンドにCSVパスを渡さず、`node .\mcp\codex-setup.mjs`で再利用する。入力先を変更するときは同ファイルの`inputRoots`を編集する。既存設定は自動上書きしない。

診断の`mcp_connection: passed`、`tool_count: 30`、`input_roots`の指定フォルダーを確認する。これはローカル接続の確認であり、`ai_host_connection: not_tested`はこの段階では正常。

`dist\FlopCommandApp.exe`を開き、集計タブの「調査条件セット」で条件名とsyntaxを保存する。GUIとMCPは同じWindowsユーザーで起動する。GUI独自の履歴先を使う場合は`mcp.local.json`の`historyRoot`も一致させる。

## 2. トンネルの準備

[OpenAI公式Secure MCP Tunnel手順](https://developers.openai.com/api/docs/guides/secure-mcp-tunnels)のPlatform tunnel settingsから、利用する組織とChatGPTワークスペースに関連付けたトンネルを準備する。作成にはTunnels Read + Manage、実行・選択にはRead + Use、ChatGPTにはカスタムMCPの利用権限が必要。

同ページで案内される公式配布元から、実行環境に対応するtunnel-clientを取得する。Windows実行ファイルを`F:\PLO-Syntax-Lab\tunnel-client.exe`に置いた場合の例を以下に示す。本手順ではそのバイナリ自体のWindows動作は未検証なので、最初にhelpが動くことを確認する。利用可能な配布形式が異なる場合は公式手順を優先する。

```powershell
.\tunnel-client.exe help quickstart
```

トンネルの実行用APIキーは、PC上で次の入力欄に入力する。チャットや設定ファイルへキーそのものを貼り付けない。

```powershell
$ploTunnelKey = Read-Host 'トンネル実行用APIキー' -AsSecureString
$ploTunnelKeyPtr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($ploTunnelKey)
try {
    $env:CONTROL_PLANE_API_KEY = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ploTunnelKeyPtr)
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ploTunnelKeyPtr)
    $ploTunnelKey.Dispose()
    Remove-Variable ploTunnelKey, ploTunnelKeyPtr
}
```

同じPowerShell内で実行する。`tunnel_...`を自分のトンネルIDに置き換える。次のinitは新規プロファイル用で、既存の同名プロファイルがある場合は設定を確認して再利用する。

```powershell
.\tunnel-client.exe init --sample sample_mcp_stdio_local --profile plo-syntax-lab --tunnel-id tunnel_... --mcp-command "node F:/PLO-Syntax-Lab/mcp/server.mjs --config F:/PLO-Syntax-Lab/mcp.local.json"
.\tunnel-client.exe doctor --profile plo-syntax-lab --explain
.\tunnel-client.exe run --profile plo-syntax-lab
```

この例は空白のない展開先を使用する。起動したPowerShellとPCを動かしたまま次へ進む。終了時はCtrl+Cで止め、`Remove-Item Env:CONTROL_PLANE_API_KEY`でこのセッションのキーを除去する。

## 3. ChatGPTへ登録

[ChatGPT Plugins](https://chatgpt.com/plugins)の「＋」→「Add custom MCP server」でConnectionにTunnelを選び、同じトンネルIDを指定する。表示される認証・確認手順に従って登録し、作成したプラグインを会話で選ぶ。現行のアプリMCPには独自OAuthは実装していない。トンネルの実行用APIキーとアプリの認証設定は別である。

トンネルが表示されない場合は、ChatGPTワークスペースへの関連付けとRead + Use権限を確認する。

## 4. ChatGPTから接続を確認

最初は読取のみを依頼する。

```text
PLO Syntax Labのget_statusとresearch_list_workspaceを呼んでください。
入力フォルダー、アプリ版、保存済みの条件セット名を表示してください。
まだCSV登録・計算・設定変更はしないでください。
```

ツールが実際に呼ばれ、指定したFドライブのフォルダーと保存した条件セットが返ることを確認する。プラグイン登録だけでは接続完了としない。

次に調査を依頼する。以下の「自分の条件セット名」はGUIで保存した名前へ置き換える。

```text
F:\PLOrange\post\6max\lowstakes_5%4bb\100bb\SRP\COvsBTN
にあるKhiボードのIP/vs_checkを調査してください。
「自分の条件セット名」のsyntaxを変更せず使ってください。
Rはポット比、Aはオールインです。全アクションのweightは共通基準です。
まず最高ランクKでスキャンし、対象ボード・CSV対応・相手レンジの不足を示して承認を待ってください。
承認後にCSV登録を行い、さらに計算前に承認を得てください。
双方のアクション前レンジによるEQと、全アクション頻度を色分けしたグラフを出してください。
スート構成・ペアの有無で比較し、result_id、対象数、欠損理由と出力ファイルを示してください。
```

Ahi・Qhiなどを調査するときは依頼の最高ランクを変更する。KhiにはAを含むボードを入れない。条件セットをAIが勝手に作り替えない。取込とレンジ対応の詳細は[ディレクトリ取込](directory-import.md)を参照。

CSVはPCから読み、ツール要求・結果はトンネル経由でChatGPTへ渡る。ローカルの結果ファイルのパスが返っても、ブラウザのダウンロードリンクとは限らない。必要に応じてAIが`get_artifact`で結果本文を取得する。

## 接続を確認したと判断する条件

- ローカル診断で30ツールを確認。
- トンネルのdoctorが成功し、runが稼働。
- ブラウザの会話からget_status・research_list_workspaceが成功。
- 承認した小規模調査でresult_id、EQ、全アクション頻度、出力が返る。

最後の二つは利用者環境での確認が必要。大量CSVではスキャン・計算をジョブとして開始し、research_get_jobで進捗を取得する。単一呼出が期限を超える場合は対象を小さくして原因を確認する。
