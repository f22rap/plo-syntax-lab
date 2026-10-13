# AIから複数ボードを調査する（第4ステップ）

Windows MCP 0.3.0は従来15ツールに調査用12ツールを追加する。既存15ツールの名前・意味を維持する。GUIの集計タブと同じResearchStore・ResearchRun・EQエンジンを使い、別の計算式は実装しない。共通11ツール・クラウド版は今回変更しない。

接続先にはWindows上のCodexを選定。[セットアップ](codex-setup.md)の`Setup-Codex.cmd`で初回設定・MCP診断・CLI登録を行える。

## 起動と設定

配布ZIPを展開し、`dist/FlopCommandApp.exe`で条件セットとボード別CSVを登録する。GUI単体にNode.jsは不要。MCPを利用するPCにはNode.js 20以上と.NET Framework 4.8が必要。SDK依存とネイティブEXEはZIPに同梱する。

1. `mcp/config.example.json`をコピーして`mcp.local.json`を作る。
2. inputRootsを実際のCSVフォルダーに変更し、outputRootに結果の保存先を指定する。historyRootを省略するとGUIと同じ`%LOCALAPPDATA%\FlopCommandApp\History`を使う。GUIで独自の履歴先を使う場合はMCPにも同じ場所を設定する。
3. stdio対応AIホストのMCP設定に次を登録する。以下の`C:/PLO`は例であり、実際の展開先に置き換える。

```json
{
  "mcpServers": {
    "plo-syntax-lab-windows": {
      "command": "node",
      "args": ["C:/PLO/mcp/server.mjs", "--config", "C:/PLO/mcp.local.json"]
    }
  }
}
```

4. ホストを再接続し、`get_status`と`research_list_workspace`を呼ぶ。27ツールが見え、GUIで保存した条件セット・カタログが返ることを確認する。
5. AIに「Ahiボードのベット戦略を、ボードの特徴ごとに調査して。登録した○○条件セットを使って」と依頼する。[AI向け手順](../mcp/research-workflow.md)も参照。

AIホスト固有の設定画面・インストール操作は環境によって異なる。WindowsのローカルstdioをWeb版ChatGPTへ直接接続するURLは、このパッケージでは提供しない。クラウド実行には別のEQ実行基盤が必要。特定のAI製品への接続完了をSDK試験だけで判断しない。

## 追加ツール

| ツール | 用途 |
| --- | --- |
| research_list_workspace | 条件セット・カタログ・版の一覧 |
| research_list_spots | Ahi／ノードで対象を検索し、CSV参照を確認 |
| research_get_condition_set | ユーザーsyntaxそのものを取得 |
| research_save_condition_set | 版を照合して条件セットを新規保存／更新 |
| research_save_catalog | 許可フォルダー内のCSVを本文検証して登録 |
| research_validate | 版・CSVハッシュ・syntax・規模を検証するジョブ |
| research_start | 検証済み計画を非同期実行 |
| research_get_job | 進捗・状態・result_idを取得 |
| research_cancel_job | 協調的に中止、確定済みボードを保存 |
| research_get_result | metadata／board／categoryを取得 |
| research_list_history | 再起動後も保存済み調査を検索 |
| research_export | GUIと同じCSV・JSON・SVG・Markdownを新規保存 |

既存のget_artifactと`plo://artifacts/{id}`で出力を読む。旧比較履歴と調査履歴のIDを混用しない。調査結果IDは32桁の16進数。設定IDはユーザーが管理する識別子、plan/job/artifact IDはセッションのUUID。

## 固定された入力と再実行

- validateはジョブを返す。research_get_jobの完了結果がplan_id・入力fingerprint・ボード別syntax・計算規模を持つ。
- startはカタログ・条件版と正確なスナップショットを再確認し、登録されたCSVハッシュを確認してから実行する。検証後の編集は別の計画として再検証する。
- 計画は1時間、ジョブ・request_keyはMCPプロセスの間だけ保持する。同じキー・同じ入力は同じジョブを返し、異なる入力へのキー再使用を拒否する。再起動後は新規検証が必要。
- 研究ジョブは同時に1件、セッションに最大200ジョブ・200計画。計算上限は200ボード・10,000条件行・合計1億試行。レジストリが満杯なら保存結果を確認して再起動する。
- cancelledの結果は途中の1ボードを含まず、完了したボードだけを含む。検証中・キュー中・強制終了時には結果がないことがある。タイムアウトでもまず協調的中止を要求し、10秒応答しなければプロセスを終了する。
- 設定はworkspace revisionと対象revisionを照合し、GUIとMCPの競合を拒否する。保存は対象セット／カタログだけを置き換え、他の登録を保持する。GUI側では「再読込」で変更を表示する。

## 数値・来歴・アクセス範囲

計算と集計の詳細は[一括調査](research-run.md)。metadataには局面、条件セット版、実際のsyntax、CSVパス・SHA-256、分類・計算版、シード・試行数、状態別件数を含む。boardには個別EQ・頻度・欠損理由、categoryには対象／有効数・最小最大・paired対象を含む。

入力CSVはローカル設定のinputRoots内だけ。Node側の実パス確認に加え、ネイティブ側もルート・サイズ・シンボリックリンク／ジャンクションを検証する。AIの引数から許可ルートや実行コマンドは変更できない。任意パスへの出力は提供せず、outputRoot内の新しいUUIDファイルに保存する。調査履歴の削除ツールは提供しない。

## 検証と制約

実装は既存C# 5／.NET Framework 4.8互換。`build.ps1 -WithTests`はGUI・旧MCP・新しい`PloResearch.Native.exe`と研究ブリッジテストをビルドする。

```powershell
.\build.ps1 -WithTests
.\dist\PloResearch.Tests.exe
npm ci --ignore-scripts
npm run test:research
npm run test:mcp
npm run package:windows-mcp
```

Linuxでは同じC#ソースを.NET 8へコンパイルし、公式SDKの実stdio通信で27ツールの発見、条件・CSV登録、計画、EQ／頻度・集計・出力、入力変更・許可範囲・競合・再起動・中止を試験した。17件のC#ブリッジ検証ではGUI単一ボード計算との数値・シード一致、確定済みボードの保持も確認。既存20,968件の計算検証も維持する。Windows EXEはクロスコンパイルで確認する。

Windows実機での新しいEXE起動、およびユーザーが選ぶAIホストからの自然文調査は、この環境では未確認。配布物はその接続確認に使うプレビュー。ローカルプレビューには基準コミットとファイル別SHA-256を記録し、未プッシュの内容をそのコミットに含まれるものとして扱わない。
