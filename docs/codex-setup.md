# CodexでAhiボードを調査する

接続先は、CSVとEXEを置くWindows PC上のCodexを使用する。ローカルstdio MCPを起動し、アプリと同じ研究設定・計算コードを使う。

## 最初の一回

1. ZIPを固定のフォルダーへすべて展開する。展開後にフォルダーを移動した場合はMCP設定のパスも更新する。
2. `dist/FlopCommandApp.exe`で、自分のsyntax条件セットを保存する。ボード別CSVはAIがディレクトリから一括登録できるため、手動登録は不要。既存カタログも使える。
3. Node.js 20以上をインストール済みのWindowsで、`Setup-Codex.cmd`を開く。
4. 初回はCSVを保存している親フォルダーを選ぶ。サブフォルダーも対象。MCPの31ツールと研究設定の読込を自動確認する。
5. Codex CLIが見つかれば、`plo-syntax-lab-windows`を登録する。同名の登録がある場合は保持し、更新する内容を表示する。CLIがなければ、次の設定画面から登録する。
6. Codex側のMCP接続を再起動し、`/mcp`などの接続一覧で確認する。`research-prompt.txt`の内容を送信する。

セットアップはAPIキーやログイン情報を要求しない。Codex自体のインストール・ログインは利用するCodex環境で済ませる。Windowsの実行・組織のポリシーでブロックされた場合、その管理設定は変更せず、表示されたエラーに従う。

## Codex CLIがない場合

対応デスクトップアプリの「Settings → MCP servers → Add server」でSTDIOを選ぶ。セットアップが作る`codex-mcp.toml`の`command`と`args`を入力する。既存の`config.toml`を丸ごと置き換えない。

CLIを使う場合の登録形式は以下。実際のパスはセットアップが指定する。

```powershell
codex mcp add plo-syntax-lab-windows -- "C:\Program Files\nodejs\node.exe" "C:\PLO\mcp\server.mjs" --config "C:\PLO\mcp.local.json"
codex mcp get plo-syntax-lab-windows --json
```

CSV登録などに時間がかかる場合は、このMCPの設定に`startup_timeout_sec = 60`と`tool_timeout_sec = 3600`を指定する。生成するTOMLにも含めている。CLIのaddだけではこの二つを保存しないため、必要に応じて同じMCPの設定へ反映する。研究計算そのものは短い開始要求と進捗取得に分かれる。

## フォルダーが複数ある場合

初回はPowerShellで次を実行できる。

```powershell
.\Setup-Codex.ps1 -InputRoots "D:\PLO\Hero","D:\PLO\Opponent"
```

`mcp.local.json`が既にあればセットアップは内容を保持する。CSVフォルダーの追加・変更は、そのファイルのinputRootsを明示的に編集する。GUI独自の履歴先を使う場合はhistoryRootも一致させる。

接続だけの再確認：

```powershell
.\Setup-Codex.ps1 -CheckOnly
```

ディレクトリの取込規則とAIへの依頼例は[ディレクトリからの調査](directory-import.md)を参照。

## 接続後の依頼

「PLO Syntax Labで、登録済みAhiボードのベット戦略をボードの特徴ごとに調査して。私の条件セットを使い、EQと全アクション頻度の表・グラフ、対象数、欠損理由、結果ファイルを出して。」

複数の条件セット・カタログから一意に特定できなければAIが選択を尋ねる。syntaxや未入力の局面を勝手に作らない。詳細は[AI向け調査手順](../mcp/research-workflow.md)。

## 判定の区別

- `mcp-diagnostics.json`のmcp_connection=passedは、SDKによるローカルMCP接続・ツール発見・設定読込の成功。
- `codex mcp get`の成功はCodexへの登録確認。
- Codexから実際にresearchツールを呼び、result_idと数値が返ることで、AIホストからの調査実行を確認する。前二つだけでこの確認を代用しない。

このパッケージでは生成設定の保持・Windowsパスのエスケープ・接続診断・MCP通信を試験済み。Windows上でのセットアップ操作と、利用者のCodexからの実調査は未確認。こちらの実行環境から利用者PCへ直接登録する機能はないため、上記の一回のセットアップが必要。

生成するmcp.local.json、codex-mcp.toml、mcp-diagnostics.json、research-prompt.txtとresearch-outputはローカル用。Gitや配布ZIPへ含めない。

## 参照

2026-10-14に[OpenAI公式MCP設定ドキュメント](https://learn.chatgpt.com/docs/extend/mcp?surface=cli)を確認。ローカルSTDIO、デスクトップ設定、CLI add/get、MCP instructions、起動・ツールのタイムアウトを参照した。CLIの引数はcodex-cli 0.162.1のhelpでも確認した。Codexの承認設定やサンドボックス設定は変更しない。
