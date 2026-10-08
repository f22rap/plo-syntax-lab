# PLO Syntax Lab：MCP / AI利用API仕様案 v1

作成日：2026-10-08。状態：設計案。API version：1.0.0。
対象ソース：36b3b47513a57fe3a2bc14140593af86cea10f12。
この文書とツール定義JSONを追加する。MCPサーバー・プラグインはまだ実装していない。

## 1. 目的と利用例

生成AIがUIを操作せずに、ボード解析、条件syntax生成、ハンド照合、2〜3CSVのweight比較、結果取得まで実行できるようにする。モデルはユーザーの自然言語をツール引数へ変換し、役やドローの判定と計算はアプリのエンジンが行う。

例：
- 「As9h3dのナッツBDFDとセカンドナッツBDFDを生成して」
- 「T93のナッツガットとナッツオープンエンドを比較条件にして」
- 「この4枚ハンドが条件に入るか、その理由を調べて」
- 「Check・Bet・Raiseの3CSVを指定条件で比較し、CSVとして出力して」
- 「複数ボードの同じ条件を順に生成し、結果を一覧にして」

「自由に利用」は、接続時に許可されたデータを使い、定義済みのツールをAIが選択・組み合わせられることを指す。任意のコード・PowerShellを実行するAPIやPC全体の操作APIにはしない。アプリ独自の都度確認は読み取り・計算に追加しない。ホスト側のツール承認設定は尊重する。

## 2. 構成と実装範囲

共通サービスをNode.js上に設け、同じドメインAPIを2つの接続方式で公開する。

| 層 | 計画する責務 |
| --- | --- |
| 共通サービス | 入力検証、版管理、エンジン起動、条件・データセット・ジョブの管理 |
| stdio MCP | 対応するローカルAIクライアントが子プロセスとして起動 |
| HTTPS MCP | 到達可能なサーバーの /mcp にStreamable HTTPで接続 |
| プラグイン | MCPへの接続設定、ツールの説明、CSV取り込み、必要に応じた結果表示 |
| 既存Windows / HTML / Linux UI | 従来の利用方法を保持。計算の実装を共通サービスと共有できる構成へ段階的に移行 |

プラグインはMCPの上に載せるクライアント用パッケージとして扱い、PLO計算を別実装しない。クライアントごとの配布形式・登録方式は導入時に確定する。どの生成AIでも同じ接続方式やファイル受け渡しが使えるとは扱わない。

ローカル版はWindows・Linux上でNode.js 24を使用する計画。既存の製品exe単体をMCPサーバーとして呼び出す方式にはしない。MCP用依存は専用package.jsonとlockfileで管理し、従来のnpm依存なしのビルド手順を保つ。Node不要のMCP配布は、別途ランタイム同梱パッケージで提供する段階とする。

クラウド版が利用できるのはアップロード・登録済みのCSVのみ。利用者PCのパスはクラウド側から直接読み取れない。taffy3などの環境でも、stdio起動またはHTTPS接続の可否を確認して接続方式を選ぶ。

## 3. 再利用するソースと必要な変更

| 既存ソース | 再利用する機能 | MCP実装で加えるもの |
| --- | --- | --- |
| src/lab/engine.js | prepare / summary / query / syntax / compile | worker実行、入力schema、版情報、ハンドごとの説明 |
| src/defaults.js / default-labels.json | 185件のデフォルト条件 | ページング、安定したcell、条件ID |
| src/linux/compare.cjs | CSV解析、重複・weight検証、BigInt集計、結果CSV | 検証処理の分離、IDからの入力解決、精度を保った構造化結果 |
| src/linux/worker.cjs | Workerによる比較・デフォルト生成 | 解析・カスタム条件・ジョブ・中止への対応 |
| src/linux/server.cjs | ローカルUI用HTTP | 既存のloopback・Origin制限を保持。MCPの通信処理は別アダプターで実装 |

新規ファイルの候補は src/api/service.cjs、src/api/contracts.cjs、src/api/worker.cjs、src/mcp/stdio.cjs、src/mcp/http.cjs。詳細な配置は実装時に調整できる。既存UIのHTTPエンドポイントを外部公開するだけでMCP化したと扱わない。

## 4. MCPプロトコルと接続

2026-10-08時点で公式latestが指す2026-07-28版を第一候補とし、2025-11-25版のクライアント互換も設計対象とする。公式SDKの対応状況を実装開始時に確認して依存版を固定し、実際に通した版だけをcapabilitiesで「対応済み」と返す。

ドメインのAPI versionとMCPのprotocol versionは別物。2026版のリクエストごとのメタデータ・HTTPヘッダーと、2025版の初期化・接続処理を混在させない。wire形式と版別処理はSDK／アダプターが担当し、共通サービスはツール名と引数だけを受け取る。

- stdio：stdoutにはMCPメッセージだけを出し、ログはstderrへ。
- HTTPS：/mcp、TLS、Origin検証、認証を提供。ローカルHTTPを追加する場合はloopbackへ限定する。
- tools/listとtools/callを必須とし、inputSchema・outputSchema・structuredContentを提供。
- toolsの返却順を固定し、意味と入出力を変える場合はAPI versionを更新。
- structuredContentは互換性のため常にobjectとする。textにも同じJSONを返す。
- この文書のJSON例はドメイン引数とstructuredContent。完全なMCP wireメッセージの例ではない。

比較ジョブは本アプリのjobIdを使う。MCP Tasks拡張がなくても実行できる設計とし、Tasks対応は後から追加できる。

## 5. ツール一覧

詳細な入出力schemaは [mcp-tools.schema.json](mcp-tools.schema.json) に収録する。全ての入力objectは追加キーを拒否する。JSON Schemaのdefaultはサービス側で明示的に補完する。

| ツール | 主な入力 | 返す内容 |
| --- | --- | --- |
| plo_capabilities | なし | API・エンジン版、対応通信方式、機能、制限、権限 |
| plo_definitions | topics | 役・FD・BDFD・SD・syntax・CSV計算の定義と例 |
| plo_analyze_board | board | 正規化ボード、street、合法ハンド数、役のkey・名称・件数 |
| plo_generate_ranges | board、mode、requestsまたはページ指定 | syntax、合法ハンド件数、条件ID、対象外理由 |
| plo_match_hands | board、hands、conditionIdまたはsyntax | 一致判定、最強役、2枚組SD、実アウト数 |
| plo_register_dataset | board、name、label、source | 検証済みdatasetId、行数、weight合計、有効期限 |
| plo_list_datasets | board任意、offset、limit | 呼び出し元が登録したCSVのメタデータ |
| plo_start_compare | board、datasetIds、conditionIds、includeAll、requestKey | jobId、state、有効期限 |
| plo_get_job | jobId、offset、limit | ジョブ状態・進捗・失敗情報、成功結果のページ |
| plo_cancel_job | jobId | cancelRequested、現在のstate |
| plo_export_result | jobId、format | ファイルID、取得URLまたはresource URI、checksum |

データセット登録・ジョブ開始・中止は変更操作として記述する。生成・照合・一覧取得は読み取り／計算として記述する。結果exportはアプリ管理の一時成果物を作るが、任意のPCパスへ書き込まない。

### 5.1 共通の返却形式

成功時は ok=true、error=null、dataは必ずobject。失敗時は ok=false、data=null、errorにcode・message・retryable・detailsを返す。warningsは常にarray。

共通フィールド：
- apiVersion：1.0.0
- engine.sourceCommit：実装に組み込まれた40桁commit SHA
- engine.classificationVersion：2026-10-08-pair-draws-v1
- engine.game：PLO4

入力JSON/schemaの不正、未定義ツールなどの通信エラーは版に応じたMCPエラーへ変換する。計算・CSV検証などのツール内エラーはisError=trueと上記エラーobjectで返す。出力は宣言したschemaに照合する。

### 5.2 ボード・条件

boardは3〜5枚の文字列。parseBoardを使い、T/10・スート記号・空白を正規化し、重複・不正カードを拒否する。normalizedBoardは元のstreet順を保つ。boardKeyは最初の3枚のみ数値card ID昇順で並べ、ターン・リバーは入力順のまま連結する。同じフロップの並び違いを同一視し、ターン・リバーの入れ替えは同一視しない。

filterは既存エンジンのrole、fd、sd、bdfd、clean、pocket、blockersに対応する。
- role：all、cat:0〜cat:8、またはanalyze_boardが返したkey。任意の未定義roleは拒否。
- pocket：2〜14またはnull。nullは指定なし。エンジン呼び出し前にnullを省く。
- blockers：rankとyes/no/one/two。矛盾する同ランク指定は拒否する。
- cleanと個別ドロー条件に矛盾がある場合はINVALID_FILTER。黙って片方を無視しない。
- ターンのBDFDやリバーのドロー等は正常なUNSUPPORTED行とし、理由を返す。
- 空集合はEMPTY、count=0、syntax=""、conditionId=null。全ハンドへ変換しない。

mode=customではrequestsを1〜100件指定する。各requestにlabelとfilterを付ける。mode=defaultsでは185件をoffset/limitで取得する。全185cellを安定した順で返し、対象外行も含める。カスタムmodeではoffset/limitを受け取らない。

includeSyntax=falseの場合はsyntax=nullで件数と条件IDだけを返す。条件IDがない空集合はsyntax=""を維持する。examplesLimitは0〜8、初期値0。examplesは自分のCSVを使わず、ボードに対する合法ハンドから取得する。

条件IDはボード・filterまたはdefault cell・syntax・classificationVersionを記録し、呼び出し元に関連付ける。AIの与えたcountを信用せず、syntax生成結果から実件数を取得する。条件IDの別ボード利用や別版との混在は拒否する。

### 5.3 照合と説明

handsは1〜100件。各handは正規化した8文字のPLO4ハンド。手札内重複とボード衝突を拒否する。conditionIdとsyntaxは同時指定しない。

syntax直接指定では既存compileで解析し、hand照合に使う。任意コードとして評価しない。役とpairDrawsをエンジンから返し、モデルが分類を推測する必要をなくす。outs/nutOutsは4枚ハンド全体の実アウト数で、pairDrawsの分類と同じ数値とは限らない。完成ストレートを持つハンドのpairDrawsはドロー一覧として返さない。

## 6. 確定したPLOの意味

1. 完成役は手札ちょうど2枚＋ボードちょうど3枚による最強役。
2. FD/BDFDの最高・第2位カードは、ボードカードを除いた対象スートから選ぶ。
3. SDのnutGut/nonGut/nutOpen/nonOpenは手札2枚組を持つ条件。残り2枚のドローやラップと重複する。
4. 組み合わせの全完成ランクで最高ストレートを作れる場合がナッツ。片側だけナッツのoeはアンナッツ側。
5. 残り2枚のブロッカーでその2枚組の分類を昇格させない。フラッシュ・フルハウスの可能性はSDの高さ分類に含めない。
6. 全SD・wrap・表示アウト数は4枚ハンド全体。完成ストレートのリドローは対象外。
7. BDFDはフロップ限定、リバーにドローはない。
8. 役・ポケット・ブロッカーとの複合条件を維持する。デフォルトのセットのSD gut/oeはナッツとアンナッツ両方を含む既存仕様を維持。

回帰基準：

| ボード | filter | syntax |
| --- | --- | --- |
| As9h3d | bdfd=bdNut | (Kss,Ahh,Add) |
| As9h3d | bdfd=bdSecond | (Qss:!ks,Khh:!ah,Kdd:!ad) |
| As9s3d | fd=fdNut | Kss |
| As9s3d | fd=fdSecond | Qss:!ks |
| Ts9h3d | sd=nutGut | (KQ,KJ) |
| Ts9h3d | sd=nutOpen | QJ |
| Ts9h3d | sd=nonGut | (Q8,J7,86,76) |
| Ts9h3d | sd=nonOpen | (J8,87) |

generated syntaxは全合法ハンドで内部照合する。Monker実機の受理・抽出一致を確認済みとは返さない。

## 7. CSV登録・比較・export

### 7.1 入力元

| source.kind | 指定方法 | 利用範囲 |
| --- | --- | --- |
| inline | csvText | 小さい検証CSV。UTF-8で256KiBまで |
| local_file | path | stdio等のローカル運用。起動時の許可フォルダー配下のみ |
| upload | uploadId | HTTPS運用。ユーザーが同一サービスへアップロード済みのID |

local_fileはrealpathを解決して起動時の許可root内か確認し、シンボリックリンク・相対パスの脱出を拒否する。クラウドではこのkindを受け付けない。

大きなCSVは、認証済みのPOST /uploadsへtext/csvの本文とファイル名メタデータを送ってuploadIdを取得する。プラグイン側のアップロードUI・ファイル受け渡し機能が担当する。APIレスポンスはuploadId、sizeBytes、expiresAt。取り込み完了後にplo_register_datasetへ渡す。uploadIdは呼び出し元専用で1回の取り込み後に消費する。MCPホストごとのアップロード対応は接続テストで確認し、非対応ホストではローカルファイルまたはinlineを使う。

ツールへ任意URLを渡してサーバーがダウンロードする方法は初期版には含めない。nameはパス区切りのないCSVファイル名、labelは表示名。

### 7.2 検証・比較

CSVは既存と同じUTF-8、handまたはcombo列、0〜1のweight列。frequencyでは代用しない。重複ハンド、手札内重複、ボードとの衝突、空データ、不正weight、ファイル名に認識できるボードがある場合の不一致を拒否する。

登録時に全行検証し、rowCount・totalWeight・sizeBytes・boardKeyを返す。CSVの内容を一覧ツールで返さない。同名CSVは比較時に重複として拒否し、別CSVに同じハンドがある場合はそれぞれ数える。

比較はdatasetIds=2〜3件、conditionIds=0〜500件、includeAll=trueを初期値とする。全ハンドだけの比較も可能。条件IDなし＋includeAll=falseはINVALID_SELECTION。空集合の条件IDは作らない。条件・CSVのboardKeyとclassificationVersionを実行前に検証する。

weight合計は小数28桁のBigInt集計を維持し、JSONでは10進文字列で返す。percentは「そのCSVの条件該当weight / 選択した全CSVの条件該当weight合計 ×100」、小数8桁切り捨ての文字列。合計0はpercent=null、status=ZeroTotal。平均frequencyや件数比率へ変更しない。

compare.cjsの現在のflat rowsをドメインのrows/sourcesへ変換する。percentを一度浮動小数へ丸めてから復元する方式ではなく、BigInt計算値から文字列を生成する箇所を共通化する。既存画面の表示型はUIアダプターで維持する。

### 7.3 ジョブと結果

stateはqueued → running → succeeded / failed / cancelled。失敗理由はplo_get_job.data.failure。ジョブ取得自体が成功した場合は、ジョブstate=failedでも共通envelopeのok=trueとする。

requestKeyは必須。同じ呼び出し元・同じkey・同じ正規化入力は同じjobIdを返す。異なる入力で同じkeyを再使用するとREQUEST_KEY_CONFLICT。再送で比較を二重起動しない。

cancelは自分のジョブだけを対象とし、実行workerを終了する。終了済みジョブはstateを維持しcancelRequested=false。中止済みへの再要求はcancelRequested=true。中止と正常終了の競合では先に確定した終端状態を維持する。

結果ページにはboardKey・totalRows・offset・nextOffset・rows・warningsを返す。rowsの各sourcesにはdatasetId・label・sourceRows・matchedHands・weightSum・percentを返す。全ハンド行はconditionId=null、cell=ALL、syntax=""。

履歴はMCP初期版では自動保存しない。UIの既存履歴と混在させない。永続保存は後続の専用機能とし、元CSV・既存履歴の削除や上書きを初期ツールに含めない。

exportはCSVまたはJSON。CSVはUTF-8 BOM・CRLF、既存の列意味を維持し、labelを正しく引用する。元ファイル・実行ファイルへの上書きは行わない。呼び出し元専用のfileId、SHA-256、sizeBytes、mimeType、有効期限を返す。HTTPSは短期の取得URL、stdioはresources/read対応のresourceUriを返す。最低どちらか一方を必須とする。ファイル内容をMCPのresources/readまたはGET /files/{fileId}で取得できるようにする。ホストがresourceを利用者向けファイルへ変換できるかは別途テストする。

## 8. 初期制限とデータ管理

以下は実装予定の既定値。既存UIの制限を勝手に変えるものではない。変更する場合はcapabilitiesで公開する。

| 項目 | 初期値 |
| --- | --- |
| CSV / upload単体 | 30MiB |
| 1比較のCSV合計 | 30MiB |
| inline CSV | UTF-8で256KiB |
| 条件syntax | 50,000文字 |
| 1生成のカスタム条件 | 100件 |
| 1比較の条件 | 500件＋任意の全ハンド行 |
| 1照合のハンド | 100件 |
| 1ページ | 既定25件、最大100件 |
| 1workerのメモリー | 512MiB |
| 同時ジョブ | ownerごと2件。サーバー全体は運用設定で制限 |
| ジョブの実行時間 | 300秒。待ち行列は最大30秒 |
| 同期ツール呼び出し | 30秒 |
| MCPツール引数のJSON | 4MiB |
| 1ツール結果のstructuredContent＋text | 合計256KiB。structuredContent単体128KiBを目安 |
| exportファイル | 64MiB |
| 条件・CSV・ジョブ・成果物 | 発行から1時間。処理中参照は終了まで保持 |
| engine cache | workerごと最大6ボード。版・boardKeyを含むkey |

出力上限を超えたらOUTPUT_TOO_LARGEを返し、ページサイズ・requestsの削減またはincludeSyntax=falseを案内する。syntaxを黙って短縮しない。全合法ハンドの列挙は返さず、件数・最大8例・syntaxを返す。

ownerごとのCSV保持量は初期100MiB、ジョブ・条件登録数にも運用上限を設ける。認証済みでも無制限にworkerを作らない。長時間操作はworker内で行い、通信・中止を止めない。

ローカル版のownerはMCPサーバープロセスの作業領域。クラウド版は認証主体ID。conditionId / datasetId / jobId / fileIdは接続をまたいで持ち運ぶ不透明なIDとし、全操作でownerを検証する。HTTP接続そのものに暗黙の選択中ボードやCSVを置かない。複数インスタンスの場合はowner付きの共有ストレージとジョブ管理を使う。期限切れ・他ownerのIDはID_NOT_FOUNDとして扱う。

## 9. 認証と権限

ローカルstdioは許可rootと機能設定を起動時に固定する。HTTPS版はMCPの対象版に準拠した認可方式を使用する。クライアント互換を検証して登録手順を文書化する。鍵・認証情報をLLMに入力させない。

アプリの権限案：
- plo:analyze：定義・解析・生成・照合
- plo:datasets：明示的に指定したCSVの取り込み・一覧
- plo:compare：比較・ジョブ取得・中止・export

owner検証はscopeとは別に行う。アップロードと成果物URLも同じowner制約を適用する。CSV本文、手札一覧、ローカル絶対パス、トークンを通常ログへ出さず、tool名・時刻・件数・所要時間・結果状態を記録する。出力labelなどのユーザー文字列はデータとして扱う。

## 10. エラーコード

| code | 意味・対応 |
| --- | --- |
| INVALID_BOARD / INVALID_HAND | 枚数・カード・重複・衝突を修正 |
| INVALID_FILTER / INVALID_SYNTAX | 未定義条件・矛盾・構文を修正 |
| INVALID_SELECTION | 条件または全ハンド行を選択 |
| BOARD_MISMATCH / ENGINE_VERSION_MISMATCH | 同じボード・エンジン版で登録・生成し直す |
| CSV_VALIDATION_ERROR | ファイル名、行番号、原因をdetailsで確認 |
| EMPTY_CONDITION | 比較用の条件IDを生成できない空集合 |
| ID_NOT_FOUND | 期限切れまたは呼び出し元が参照できないID |
| PERMISSION_DENIED | 許可root・scope・通信方式を確認 |
| LIMIT_EXCEEDED / OUTPUT_TOO_LARGE | 入力・件数・ページサイズを減らす |
| SERVER_BUSY | 空きworkerを待つ。retryable=true |
| JOB_TIMEOUT / JOB_CANCELLED | ジョブの終端理由 |
| REQUEST_KEY_CONFLICT | 新しいrequestKeyを使う |
| INTERNAL_ERROR | 内部例外を秘匿し、サーバーログで調査 |

schemaにはmessageだけでなくcodeを必須とし、モデルが文字列の部分一致で分岐する必要をなくす。EMPTY/UNSUPPORTEDの生成行はエラーではない。

## 11. 呼び出し例

### syntax生成

plo_generate_rangesの引数：

~~~json
{
  "board": "Ts9h3d",
  "mode": "custom",
  "requests": [
    {"label": "ナッツガット", "filter": {"role": "all", "sd": "nutGut"}},
    {"label": "ナッツオープンエンド", "filter": {"role": "all", "sd": "nutOpen"}}
  ]
}
~~~

結果のrows[0].syntaxは(KQ,KJ)、rows[1].syntaxはQJ。各条件のcountとconditionIdは実計算から返す。

### CSV登録と比較

plo_register_datasetの引数（合成データ）：

~~~json
{
  "board": "Ts9h3d",
  "name": "check.csv",
  "label": "Check",
  "source": {
    "kind": "inline",
    "csvText": "hand,weight\nKsQhAsAd,0.2\n"
  }
}
~~~

同様にbet.csvを登録し、返されたIDを使う。以下のdatasetId・conditionIdはプレースホルダー：

~~~json
{
  "board": "Ts9h3d",
  "datasetIds": ["dataset_check", "dataset_bet"],
  "conditionIds": ["condition_nut_gut"],
  "includeAll": true,
  "requestKey": "compare-example-001"
}
~~~

plo_get_jobへjobIdを渡し、succeeded後にplo_export_resultでCSVを取得する。CSV未指定のsyntax生成にデータセット登録は不要。

## 12. 実装順序と受入条件

| 段階 | 作業 | 完了条件 |
| --- | --- | --- |
| 1A | 共通サービス＋stdio、最初の5ツール | MCPクライアントから解析・生成・照合が動く |
| 1B | データセット・比較ジョブ・export | 2/3CSV比較・小数精度・中止・ファイル取得が動く |
| 2 | HTTPS、認証、アップロード、プラグイン | 実際の対象クラウドAIから一連の操作が動く |
| 3 | 結果表・グラフ、保存済み履歴、Tasks等 | 必要な拡張対応を明示し、初期APIの互換を保持 |

受入テスト：
1. 上記8つのドローsyntaxを仕様通り生成し、空集合・対象外・複合条件も検証する。
2. 既存の生成・SD・FD/BDFD・ポケット・185条件・Linux集計テストを全て保持する。
3. 同じボード・filterのUIとMCPでsyntax・件数が一致する。
4. 全合法ハンドによる内部照合を維持し、手札2枚組SDの重複を保持する。
5. CSVの精度28桁、2/3CSV、合計0、並び違い重複、ボード衝突、同名ファイルを検証する。
6. 全inputSchema / outputSchemaの正常・異常例を検証し、全ツールが定義済みoutputに従う。
7. stdioのstdoutがMCPメッセージ以外で汚れない。通知・応答の通信テストを行う。
8. 対応する各MCP版を公式クライアント／Inspector等と実際のAIホストで接続テストする。
9. 別ownerのID、期限切れ、同時ボード、再送、キャンセル競合、worker異常終了を検証する。
10. root外のパス、リンク脱出、巨大入力、過大出力、Origin不正を拒否する。
11. 非同期比較のジョブ状態と結果ページ、CSV/JSONダウンロードを検証する。
12. プラグイン実装ではユーザーがCSVを渡し、AIが生成から結果取得まで行う実演を完了する。

最初の実装着手点は1A。ドメイン契約を固定してから1Bへ進める。HTTPS公開やプラグイン登録は、この仕様書追加だけでは行わない。

## 13. 参照

- [対象リポジトリ](https://github.com/f22rap/plo-syntax-lab)
- [条件の定義](default-conditions.txt)
- [MCP 2026-07-28](https://modelcontextprotocol.io/specification/2026-07-28)
- [MCP Tools](https://modelcontextprotocol.io/specification/2026-07-28/server/tools)
- [MCP stdio](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/stdio)
- [MCP Streamable HTTP](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/streamable-http)
- [MCP Authorization](https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization)
- [2025-11-25互換仕様](https://modelcontextprotocol.io/specification/2025-11-25)
