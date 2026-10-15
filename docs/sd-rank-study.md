# ランク別SDブロッカー調査：pp込みの固定方針

`sd-rank-pp-inclusive-v1` を条件生成・結果選択・順位・レポートの共通定義として使用する。
対象はボードのトップランクのワンペア、**自分の通常SDなし、ppを含む**。
BDFD・BDSDは制限しない。ランク2～Aの保有あり／なし、26条件を生成する。
ボードはAhiに固定せず、保存されたgenerated recipeがボードごとに解決される。

## MCP（Node.js接続）

1. `research_build_sd_rank_set` に新規 `set_id` と名前を指定する。保存前の条件セットが返る。
2. `research_save_condition_set` で保存。通常どおり `research_validate` → `research_start` で計算する。
3. `research_sd_rank_report` に保存された `result_id` を渡す。必要なら `spot_id` を指定する。
4. `research_get_job` で完了を確認。pp込みのランク別頻度、差、ハンド数、OESD/ガット/その他SDの候補組、定義・注意が返る。

新しいツールは更新後のNode.jsサーバーを再起動すると利用できる。既に起動中の旧サーバーやEXEを自動更新するものではない。

## CLI / 再生成

リポジトリのルートで実行する。

```powershell
node mcp/sd-rank-study.cjs conditions my_sd_rank_set > conditions.json
node mcp/sd-rank-study.cjs report rows.json > report.json
node mcp/sd-rank-study.cjs report rows.json --html > report.html
```

入力は `research_get_result(view: "board")` の**全ページ**のitemsを連結したJSON配列。
単一ページの応答も受け付けるが、next_offsetが残っていれば拒否する。
HTMLは同じ集計関数を使用し、ブラウザで印刷/PDF保存できるSDランク分析用レポート。
以前の4ページPDF全体のGUI自動生成機能とは別。

## 結果の取り違え防止

- `rank_<rank>_all_yes/no` のみ採用。`_nopp_` へのフォールバックはしない。
- IDだけを信用せず、保存syntaxを同じ物理ボードの全合法4枚ハンド集合上で照合する。
  pp除外や別フィルターが混入していればエラーにして再計算を要求する。
- 完了していない結果、重複行、同一ボードの別ノード混在、不正頻度を拒否する。
- 空条件・範囲内該当なしは比較から省略し、0%に変換しない。
- 各ランクのΔ＝保有あり頻度－なし頻度。上位はΔ順。文面の数値も同じ集計値から生成する。
- OESD/ガット候補はボードから列挙。相手CSVの保有重みは使わない。
  ppやキッカーなどは固定せず、純粋な因果効果とは解釈しない。

旧調査セットや保存済みの結果は勝手に書き換えない。旧セットに明示的なpp除外がある場合は、
この生成処理で新しいセットを作成する。通常のユーザー指定フィルターとしてのpp条件は維持する。

検証：`npm run test:sd-rank`
