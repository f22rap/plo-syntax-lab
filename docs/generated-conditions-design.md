# 生成条件の保存仕様（第1段階・設計案）

状態：第1段階で合意した設計。2026-10-14。
対象：Node.js版MCP＋通常Windowsアプリ。
第2段階でNode.js用の条件モデル・生成処理・確認用CLIを追加した。使い方と実装範囲は
[生成プレビュー](generated-conditions-preview.md)を参照。第3段階でチェック式選択と保存形式2への移行を追加した。
[選択・保存の手順](generated-conditions-selection.md)を参照。第4段階のNode.js版MCP接続は実装済み。[調査手順と検証範囲](generated-conditions-research.md)を参照。

## 1. 到達点と現行コードで確認したこと

ユーザーが生成条件を一度保存し、調査時に対象ボードごとのsyntaxへ展開する。
2026-10-14追加要件：登録の主操作は「Syntax Labのデフォルト条件から選択」とする。
現在の185条件を同じ名称・順序で表示し、チェックした条件を一括で保存できるようにする。
ユーザーが役・ドロー・JSONを一から組み立てなくても、この選択だけでボード別生成へ進める。
生成は既存の判定エンジンが行い、AIは保存された条件セットと対象を指定する。
生成してもユーザーの条件セットを書き換えず、同じ条件IDでボード間比較する。

現行コード：
- ResearchConditionはliteral / per_boardだけを持つ。ResearchValidation.Resolveは文字列を選択する。
- src/lab/app.jsの選択JSONにはfilterがあるが、src/LabSelection.csのLabRangeには対応項目がなく、アプリ取り込みで保持されない。
- エンジンはfdThird、bdsd等を扱う。mcp/server-core.mjsの入力スキーマはfdThirdとbdsdを公開していない。
- role=set:14等は物理ランクに解決済み。これを別ボードの「トップセット」として使い回せない。
- defaults.jsは表示ラベルを解析する。新しい保存形式ではラベルを識別子として使わない。
- workspace schema_version=1以外は現行Readerが拒否する。新形式での誤読防止に利用できる。
- 結果Readerは保存された条件と各行syntaxの一致を検証しており、生成結果のスナップショットへ対応させる必要がある。

## 2. 条件と生成結果を分離する

| kind | 正本として保存する内容 | 計算時 |
| --- | --- | --- |
| literal | syntax | 入力したまま使う |
| per_board | by_board | 物理ボードをキーに選ぶ |
| generated | generator | 保存した生成条件を対象ボードで解釈する |

condition_id、label、enabledは既存と共通。条件IDはボード別に増やさない。
labelは表示専用。ラベルを変えても条件の意味は変化しない。
generatedではsyntax=""、by_board={}に固定し、生成文字列をユーザー設定内へ書き戻さない。
既存2方式にはgeneratorを混在させない。適用方式の変更はユーザーが明示した編集のみ。
一つのセットに3方式の条件を混在させられるが、一つの条件に暗黙の優先順位やフォールバックを持たせない。

generator：
- recipe_version=1：生成条件のデータ形式。
- ruleset=plo4-flop-v1：PLO4・フロップ、役・ドローと相対指定の意味の版。
- source=filter：構造化された生成条件を指定。
- source=preset：preset_idとpreset_revisionで登録済みプリセットを指定。
- additional_syntax：生成集合にANDする固定syntax。追加指定なしは"*"を明示する。
  ここに記入したAやssは固定値であり、ボードに合わせて変換しない。

関連JSON：generated-condition.schema.json（条件単体）、generated-condition.examples.json（配列）。
JSON Schemaは形・列挙値・基本的な矛盾のみ検証する。PLOの意味やsyntaxの正しさは実装時の意味検証が必要。
新規生成条件の名前は既存C#モデルに合わせ最大1,000文字。MCP側の既存200文字制限も保存形式と整合させる。
per_boardのキーは既存カードパーサーで意味検証し、同じ物理ボードの重複を拒否する。
旧v1ファイルの読込には旧版の入力許容範囲を維持し、本Schemaの新規入力上限を遡及適用しない。

## 3. 相対指定と固定指定

RankRef：
- {"type":"fixed","rank":"A"}：常にA。ランクは2〜9/T/J/Q/K/A。
- {"type":"board","position":"top"}：ボードの最大ランク。
- position=bottom：最小ランク。
- position=middle：3種類の異なるランクがあるフロップの中央。ペア・トリップスボードでは未成立。

ランク順位はAを最高として扱う。Aのロー使用はストレート判定内でのみ扱い、トップ順位には使わない。
top/bottomは同一ランクになることがある。二つの異なるランクが必要な役で同じ値になった場合は該当なし。
middle未成立は「指定なし」に変換しない。「middleを持たない」指定でも条件全体を該当なしとし、プレビューに理由を出す。

| 条件 | AsKs7d | Kh9h3c |
| --- | --- | --- |
| トップセット | Aのセットを対象に生成 | Kのセットを対象に生成 |
| 固定ポケットAA | AAのまま | AAのまま |
| ナッツFD | スペードの残存カードから判定 | ハートの残存カードから判定 |
| 固定追加syntax ss | スペード2枚以上 | スペード2枚以上のまま |

表は解釈の例であり、生成済みsyntaxや件数・EQの計算結果ではない。
ナッツFDは「A＋同スート」と決め打ちせず、ボード上のカードを除いた既存エンジンの順位判定を使う。

役の指定はtypeを持つ構造とする：
- all / category（one_pair、flush等）。
- pair / set / trips / quads＋RankRef。
- two_pair＋2つのRankRef、full_house＋trips/pairのRankRef。
- straight / straight_flush＋highのRankRef。
- flush＋tier=nut / second / third / other。
選択役は現在のエンジンと同じ「手札4枚から作れる最良の完成役」の分類。
単に特定ランクを持つ条件はblockersやpocketを使い、set/one_pairとは区別する。
flush:third等の旧内部キーは解決結果にだけ使い、保存する意味の識別子は上記のtierとする。

フロップで該当しない有効な役の指定はempty_conditionとする。
不明なroleやtierは保存形式の入力エラーとして拒否する。構造は有効でも版・プリセットが未対応ならunsupported_recipeとし、「全て」に戻さない。

## 4. 個別生成・プリセットの保存範囲

source=filterは既存生成画面の全軸を保持する：
role / fd / sd / bdfd / bdsd / pocket / clean / blockers。
各軸をANDで組み合わせる。allは不問、noneはそのドローを持たない条件。

FD：
all, none, fd, fdNut, fdSecond, fdThird, fdLow, fdTriple, dualFd。
fdThirdはサードナッツ、fdLowはそれ以下。
fdTripleは手札の同スート3枚以上であり、サードナッツと混同しない。
dualFdはフロップでは通常成立しないが、既知の条件としてempty_conditionを返す。

SD：
all, none, sd, wrap, nutGut, nutOpen, nonGut, nonOpen。

BDFD：
all, none, bdfd, bdNut, bdSecond, bdLow, bdTriple。
現行エンジンにないbdThirdはこの版では受理しない。

BDSD：
all, none, bdsd, bdsd4, bdsd8, bdsd9, bdsdOther。
正のBDSD条件は、フロップでストレート完成・通常SDのある4枚ハンドを除外する。
4/8は固定2枚組ごとの有効リバーアウツで判定する。
9+は3枚以上の組を必要とする経路で判定し、各ストレート完成には必ず手札2枚だけを使う。
全4枚のブロッカー・上位ストレート除外を既存エンジンで扱う。異なる2枚組のアウツを足して8にしない。
この判定契約はrulesetに固定し、意味を変える更新ではrulesetを更新する。
正のBDSD条件と正の通常SD条件を同時指定した入力は保存時エラー（sd=all / noneは可）。

clean：
""は追加制限なし。
regularは通常FD/SDなし。fd/sdはallに固定し、BDFD/BDSDの指定は併用可能。
allはFD/SD/BDFD/BDSDなし。4ドロー軸はallに固定する。
無効化されたUI項目に古い値を残さず、保存時に正規化した状態をプレビューする。

pocketはallまたはrank＋RankRefで、指定ランクを2枚以上持つ条件。
blockersはRankRef＋mode=yes/no/one/two（1枚以上/0枚/ちょうど1枚/2枚以上）。
同じRankRefの重複指定は拒否。固定・相対指定が同じ物理ランクに解決された場合もANDの意味を維持する。
その結果矛盾する場合はempty_condition。片方を削除したり、別のランクへ変換したりしない。
固定ランクの指定はボードにないランクでも解釈可能。旧MCPの「ブロッカーはボードランクのみ」の制限と区別する。

source=presetを通常の登録経路とし、既存のデフォルト一覧の組合せ意味（OR・追加除外等）を保持する。
例：Tset allはset.top.all、AA SD BDSDはaa.sd.bdsd＋preset_revision=1。
これらの安定IDをsrc/default-presets.jsonに登録し、表示条件はsrc/default-labels.jsonに実在する項目に対応させる。
全プリセットを安定IDのレジストリへ登録し、表示ラベル・表のセル位置から独立させる。
既存のcell（例L49/L165）はレジストリ内の対応情報として保持し、条件の意味の識別子にはしない。
デフォルトのAAは単なる固定syntax AAとは異なり、既存の完成役・ポケット条件も維持する。
「Tset SD BDSD」は現在の185条件には含まれないため、デフォルト一覧へ存在するように表示しない。
必要な追加条件は個別生成として登録し、デフォルト一覧を暗黙に増減しない。
未定義の旧プリセットは未対応として明示し、空集合や別条件へ代用しない。
初版では任意の再帰的AND/OR/NOT編集は追加しない。既存の組合せと固定additional_syntaxのANDを対象にする。

## 5. 選択内容の取り込みと操作

### 5.1 デフォルト条件から選ぶ（主操作）

操作：集計 → 調査条件セット → 新規／編集 → 「デフォルト条件から追加」。
1. 現行185条件を同じ名称・順序で表示する。Tset/Mset/Bset、2ペア、AA/KK、hit等のまとまりと検索を用意する。
2. チェックボックスで複数選択し、「表示中をすべて選択」「選択解除」ができる。
   ボード未入力でも選択・保存できる。既存画面のボード別ハンド件数で選択可否を決めない。
3. 定義が有効で、今のボードだけ件数0の項目も選択できる。定義自体が未対応の項目は理由付きで選択不可。
   例：ペアボードでのMsetはそのボードでは該当なし。未定義のSDブロッカー等とは区別する。
4. 選択した各プリセットをsource=presetの生成条件として一括追加し、セット名を付けて保存する。
5. 次の調査からはこのセットを選ぶだけで、対象ボードごとにsyntaxを生成する。

一覧には「選択」「デフォルト条件名」「簡単な説明」「定義の対応状況」を表示し、
プレビュー時だけ「現在ボードでの件数・生成状態」を追加する。
件数が0という理由で選択済み条件を削除したり、次のボードで選択をリセットしたりしない。
保存画面の選択は調査条件セットの選択として管理し、従来の単一ボード集計の選択とは分ける。
同一preset_id・revision・追加条件を再追加した場合は重複追加せず、既存の項目を示す。
セット内の表示名は変更できるが、元のデフォルト名とpreset_idを残し、意味を変更しない。
保存するのはpreset_idと版であり、現在ボードで生成されたsyntaxのコピーではない。

初期対応の確認例：Tset all / Mset all / Bset all / TM2P SD nuts oe / AA SD BDSD。
全185項目を元一覧と1対1で照合し、既存で定義された項目の抽出集合を維持する。
未対応の項目は対応状況に明示するため、「185件すべて計算可能」とは表示しない。

### 5.2 個別生成画面から保存する（追加の操作）

生成画面から、新形式のrecipeと参考用preview_boardを渡す。
app.jsの解決済みfilterやsyntaxだけから、「A固定」か「トップ相対」かを推測しない。
保存前にランク指定の固定/相対をユーザーへ表示する。生成画面の「トップセット」はtopとして、
ポケット選択のAAはfixed Aとして扱う。ブロッカー等は保存画面で固定/相対を明示選択する。

既存の「今の選択条件を保存」はボード別syntax保存として保持。
別操作「生成条件として保存」を追加する。
旧JSONに解決済みfilterが残っていても、それだけで自動移行しない。
recipeがない旧選択データは従来方式で利用し、生成条件にする場合は選択を確認し直す。

生成元に該当ハンドが0でも、意味として有効なrecipeは保存できる。
プレビュー用ボードは必須条件ではなく、計算対象やキャッシュキーにも混入させない。
ユーザーの条件名は全ボード共通、実際に解決されたランク・スートは別欄に表示する。

## 6. 展開結果・プレビュー・状態

調査時は「ボード×条件」ごとのGeneratedResolutionを別に記録する。
必須内容：
- canonical_board（スートを含む実カード3枚、カード順だけ正規化）
- condition_id、condition_set_id / revision、recipe_hash
- engine_hash、resolver_version、ruleset、preset_registry_hash（プリセットの場合）
- status、reason_code、reason、resolved_filterまたはresolved_preset
- syntax（ready時は全文）、legal_hand_count（入力レンジで絞る前の件数）
- 生成時間、使用した固定additional_syntax、検証状態
既存literal/per_boardの解決結果も同じプレビューへ載せる。

| 状態 | 意味 | 計算・集計 |
| --- | --- | --- |
| ready | ボードに対して非空のsyntaxを生成・検証済み | CSV抽出へ進む |
| empty_condition | ボード上に条件が成立しない、又は条件の交差が空 | 件数0。EQ/アクション頻度はnull |
| unsupported_recipe | 版・プリセット・指定が未対応 | 標準で計画を停止 |
| generation_failed | 判定とsyntax不一致・例外等 | 標準で計画を停止 |
| missing_definition | 手入力per_boardの定義不足 | 既存の扱いを維持 |
| cancelled | 生成処理を中止 | 未完了分を計算しない |

empty_conditionの理由例：no_flush_draw_suit / relative_rank_unavailable / role_not_present /
selectors_collide / contradictory_constraints / empty_intersection。
一般的な空集合を特定理由に推測せず、断定できない場合はno_legal_handsとする。
空集合にはsyntax=""とlegal_hand_count=0を記録し、空文字をSyntaxMatcherへ渡さない。
既存の手入力syntaxには空文字を認めない。
no_matching_handsは「条件は存在するがCSVの自分レンジには該当しない」計算時状態で、empty_conditionと分ける。
入力不正は保存時拒否。実行能力不足はプレビューでunsupported_recipe。
「未設定を許可する」を有効にしても、生成エラーの黙認には流用しない。
有効な該当なしは調査を継続できるが、EQ/頻度平均から除外し、理由・対象数・有効数を出す。
部分的なエラーを除外して続ける場合は、対象を明示して改めて承認する。

## 7. 保存場所・互換性

正本：従来どおり History/Research/workspace.json。
- 新版Readerはschema_version 1/2を受理する。
- generatedのない既存設定はversion 1のまま読み書きする。
- generatedを初めて保存する時点で、元ファイルを別名バックアップしversion 2へ変更する。
- v2に昇格後は自動でv1へ降格しない。読込だけでファイルを書き換えない。
- 既存のcondition_id、set_id、catalog、CSV参照、literal/per_boardの内容を保持する。
- 保存は現在のrevision照合・排他・原子的置換を維持し、migrationも同じロック内で行う。
- 未対応バージョン・未知プロパティ・未知列挙値を黙って落として再保存しない。
- 旧Readerはv2を拒否する。旧版との同時利用では「バックアップ又は更新が必要」と案内する。
  新版Node.js不要版が生成を未実装の場合も、能力不足を表示して生成条件の計算を止める。

JSON例は条件単体のレビュー資料であり、現行の「設定JSON読込」に投入しない。
互換性試験では旧Readerが新形式を拒否し、元ファイルを変更しないことを確認する。

## 8. 調査結果・キャッシュ・承認

結果ファイルは生成条件を含む新形式ではschema=2とし、元の条件セットと使用した展開結果を両方保存する。
過去のschema=1の結果は読めるようにする。結果を開くときは再生成や再計算を行わない。
保存syntax・展開状態・ハッシュ・件数の整合性を検証して表示する。
旧ResearchFiles.Validateの「元条件のResolveに行syntaxが一致する」検証は、
generatedについて保存スナップショットとの一致検証へ変更する。検証自体は省略しない。

生成キャッシュのキー：
物理ボード＋正規化recipe（意味のある項目のみ）＋ruleset＋engine_hash＋resolver_version＋必要ならpreset_registry_hash。
label、日時、UIのプレビューボードは含めない。fixedランク・スートやadditional_syntaxは必ず含める。
オブジェクトのキー順・デフォルト値・blockersの順序を決定的に正規化する。
スート同型ボードの統合は初版で行わない。CSVのカード・ブロッカーとの取り違えを避ける。
正常に検証されたready / empty_conditionのみ再利用し、失敗や中止を完成キャッシュにしない。

保存設定には巨大なby_board展開を追加しない。キャッシュはResearch/Generated/配下等の再生成可能な領域、
承認済みの完全スナップショットは結果・計画の領域に分離する。
一括処理ではボードを一度prepareし、そのボードの全条件へ使う。キャッシュにはサイズ上限を設ける。

プレビューから実行までに、設定revision、カタログrevision、対象ボード/ノード、CSVハッシュ、
生成条件・エンジン・解決器・展開結果・計算設定を計画fingerprintに固定する。
変化があれば旧計画で計算せず再プレビューする。AIが毎ボード別に条件を書き換える必要はない。
調査許可は保存済み条件を使う許可として扱い、条件そのものの変更・保存許可と区別する。

## 9. 実装時の検証項目

1. 同じトップセットrecipeをAhi/Khiに適用し、解決役がset:14/set:13となる。固定AAは変わらない。
2. 同じNFD条件でスート・ボード上のA/K等が変わっても、正しい残存カード順位へ解決される。
3. fdThirdとfdTriple、その他のFDの集合を取り違えない。
4. ペアボードのmiddle、モノトーンの通常FD、フロップのdualFdは明示的な空集合になる。
5. BDSD4/8の2枚組、9+の3枚以上、通常SD除外、全4枚ブロッカー、AK7の過去事例を回帰確認する。
6. 生成集合とsyntax評価集合を全合法4枚ハンドで照合し、実際のC#抽出器との代表ケース一致も確認する。
   既存エンジンとの一致だけでポーカーの正しさを保証したとしない。手計算可能な既知ケースも用いる。
7. 旧固定・ボード別条件、条件ID、表示名、旧結果の読込を維持する。
8. 空集合、CSV内0件、未設定、未対応、生成失敗を分離し、nullを0%に変換しない。
9. 保存後再起動、旧版拒否、原子的移行失敗、競合編集、未知フィールドの拒否を確認する。
10. プレビュー後の変更、キャッシュ再利用と失効、キャンセル・再開で未検証データを使わない。
11. 同一ボードの再解析回数を測り、複数条件でもprepareを1回にする。
12. Windows GUIとNode MCPで同じrecipeの展開が一致し、EQ/全アクション頻度/出力へ到達する。
13. デフォルト185項目の名称・順序・cell対応に欠落や重複がないことを確認する。
14. 複数選択→保存→再起動→別ボードへの適用で選択と条件IDを維持し、件数0と未対応を区別する。
15. 実装されたデフォルト条件について、現行generateDefaultsとの集合一致と異なるボードでの自動生成を確認する。

## 10. 承認単位

第1段階の成果は仕様書・条件JSON Schema・JSON例。
第2段階ではモデルと解決器・生成処理を実装し、少数ボードのプレビューを提示する。
アプリのworkspace移行・画面からの保存（第3段階）、Node.js版MCP調査計画への接続（第4段階）は実装済み。
その後GUI、MCP一括調査、Windows検証・配布の順で各段階の承認を得る。
pushは実装の検証結果を提示した後、ユーザーが指示した段階で行う。
