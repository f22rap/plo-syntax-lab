# ディレクトリからAIが指定ボードを登録・調査する

対応する形式は `親ディレクトリ/ボード/IPまたはOOP/局面/ボード_ポジション_履歴_アクション.csv`。親ディレクトリの例：

```text
F:\PLOrange\post\6max\lowstakes_5%4bb\100bb\SRP\COvsBTN
```

最初の一回はSetup-Codex.cmdでこの親ディレクトリを選択する。既存設定がある場合はmcp.local.jsonのinputRootsに追加してMCPを再起動する。許可範囲をAIが勝手に広げる機能はない。登録後は、各CSVをGUIで指定する必要はない。ユーザーsyntaxは既存の条件セットから選択する。

## ファイル名の取込規則

プロファイル名は `plo-post-tree-v1`。ユーザーが確認した次の規則に対応する。

| 記号 | 意味 |
| --- | --- |
| ROOT | フロップの最初のOOPの判断前 |
| C | チェック |
| CALL | コール |
| F | フォールド |
| R50 / R75 / R100 | ポット比50 / 75 / 100%。ベット／レイズは履歴で判別 |
| A | オールイン。ポット比は推測せずallinとして別集計 |

例：AsKd7c_BTN_C_R50.csvはCOチェック後のBTNの50%ベット。履歴はROOT以外をハイフンで区切り、アクターはOOPから交互。CSVは従来のhand,frequency,percent,weight,ev,ev_diff,variations形式。weightは0～1で、同じ判断局面の全アクションが共通基準で出力されていることが必要。各ファイルを別々に正規化したデータは対象外。1～3アクションに対応し、唯一のCALLのみの局面も登録する。

ボードは重複のない物理カード3枚として検証し、ユーザー指定の最高ランクで絞り込む。Ts9d2cとTs9d2dはAhi調査から除外する。同じ物理ボードの並べ替えを二重集計しない。1つの局面内で不明なCSVや不正なファイルが見つかった場合は、その局面全体を停止し、分母から黙って取り除かない。

## 相手レンジとEQの時点

自分の判断局面の全アクションweightを合算し、自分のアクション前レンジを復元する。相手は**直前の相手アクションの前にある判断局面の全アクションCSV**を合算する。選ばれた相手アクションのCSVだけを使わない。

| 自分の局面 | 相手レンジの復元元 |
| --- | --- |
| IP/vs_check：C | OOP/donk：ROOTの全CSV |
| IP/vs_donk：R75 | OOP/donk：ROOTの全CSV |
| OOP/vs_bet：C-R50 | IP/vs_check：Cの全CSV |
| IP/vs_XR：C-R50-R75 | OOP/vs_bet：C-R50の全CSV |
| OOP/vs_XRraise：C-R50-R75-R100 | IP/vs_XR：C-R50-R75の全CSV |

これより以前の自分の行動によるレンジの絞り込みは残る。全局面をフロップROOTまで巻き戻す計算ではない。元データの各判断局面がどのような条件で出力されたかは、ユーザー側で確認する。

OOP/donk：ROOTには、直前のIP判断局面が存在しない。IP/vs_checkを無条件にROOTのIPレンジと見なさない。必要な相手レンジを別途明示してresearch_save_catalogで登録するか、頻度のみを明示的に許可する。欠損EQは0にしない。

## AIの操作

1. get_statusで許可フォルダー、research_list_workspaceでユーザー条件セットを確認する。
2. research_scan_directoryにdirectoryとユーザー指定のhighest_rankを渡し、research_get_jobで完了を待つ。
3. research_get_directory_scanのnodes/spots/issuesを全ページ確認する。局面ごとのCSV、相手の復元元、欠損・不正を確認する。
4. 依頼に合うnode_idを選ぶ。例：BTNのチェック後ベット戦略ならIP/vs_check。異なる局面は混ぜない。COの戦略かBTNの戦略か特定できない場合は質問する。
5. 確認済みの規則・共通weightをconfirm_profileとall_actions_common_scaleで指定し、research_import_directoryで新規カタログへ登録する。ポジション、100bb、SRPなど明示された情報をscenarioに設定し、不明なレーキ詳細・ソルバー設定・過去のサイズは創作しない。solution_contextには元フォルダー名など確認できる文字列と不明点を記録する。
6. 一回1～200ボード。200を超えるときはプレビューを全件読み、boardsを指定して分割登録・調査する。まとめる際は各ボードを一度だけ数え、バッチ平均を単純平均しない。
7. research_get_jobで登録完了を確認し、保存されたcatalog_id/revisionとユーザーのset_id/revisionでresearch_validate→research_start→結果取得・出力へ進む。

取込は新規作成のみ。既存カタログを同じIDで上書きしない。プレビュー後のファイル変更・追加・削除、workspace競合、CSV本文の不正は登録を中断する。一括登録は局面単位で全件成功か全件不保存。対象を絞って実行する場合は、除外ボードと理由を報告する。スキャンIDは最大8件・1時間・サーバー起動中のみ保持される。ディレクトリの許可設定と登録済みカタログは再起動後も残る。

## 依頼例

「登録したCOvsBTNディレクトリからAhiボードを探し、BTNのチェック後のベット戦略を特徴別に調査して。CSV登録も行って。条件セットは○○を使って。Rはポット比、Aはオールイン、全アクションweightは共通基準です。相手レンジの復元元、除外理由と対象数も示して。」

元ファイルをコピー・分割する必要はなく、既存CSVの絶対パスとハッシュを登録する。元CSVを変更しない。実際のFドライブへのアクセスと利用者のCodexからの実行は、この開発環境では未確認。

## 最高ランクの指定（0.4.1）

Ahi固定ではない。ユーザーの指示を次の値へ変換する。

| 指示 | highest_rank | 条件 |
| --- | --- | --- |
| Ahi | A | 最高ランクA |
| Khi | K | Aを含まず最高ランクK。Kのペア・トリップスも含む |
| Qhi | Q | A・Kを含まず最高ランクQ |
| Jhi〜2hi | J, T, 9…2 | 指定ランクが最高 |
| 全ボード・指定なし | all | ランク制限なし |

スキャンの省略値はall。取込時にhighest_rankをカタログへ保存する。research_list_spotsのhighest_rankとresearch_validateのsettings.HighestRankは、省略時にカタログの条件を継承する。条件のない旧カタログはall。明示的に別の条件を指定した場合は、既存カタログ内だけを再選択するため、取り込まれていないボードが増えるわけではない。対象を広げる場合は元ディレクトリを再探索する。

GUIの「登録条件に従う」が初期値。全ボードまたはAhi〜2hiへ明示的に切り替えられる。計算結果JSONとMarkdownには実際に使用した条件を記録する。

旧ahi_only / AhiOnlyは互換用に保持する。trueはA、falseはall。新旧条件を矛盾して指定した場合はエラーにし、一方を黙って優先しない。以前の保存結果のAhiOnlyはそのまま解釈する。

Khiの依頼例：「COvsBTNディレクトリのKhiボードについて、BTNのチェック後ベット戦略を調査して。条件セット○○を使用し、CSV登録から実行して。」
