'use strict';
const P=require('../lab/engine.js'),{ApiError}=require('./contracts.cjs');
const VERSION='2026-10-10-pair-backdoors-v2';
const definitions={
 roles:'PLO4は手札2枚＋ボード3枚の最強役。role.keyはplo_analyze_boardから取得する。',
 fd:'ボードに対象スート2枚、手札に2枚以上。ナッツ・セカンド・サードはボードを除いた上位3枚。それ以下は4番目以下。',
 bdfd:'フロップ限定。ボードに対象スート1枚、手札に2枚以上。As9h3dのナッツ=(Kss,Ahh,Add)、セカンド=(Qss:!ks,Khh:!ah,Kdd:!ad)。',
 sd:'nutGut/nonGut/nutOpen/nonOpenは2枚組条件で重複する。T93は順に(KQ,KJ)、(Q8,J7,86,76)、QJ、(J8,87)。残り2枚のブロッカーで分類を昇格させない。全SD・ラップ・実アウト数は4枚ハンド全体。完成ストレートのリドローは除く。',
 bdsd:'フロップ限定。ターンとリバーの両方を使う2手札＋3ボードのストレート経路。ターン完成と相手上位のターン・リバーを除外、同率は可。実カードと4枚手札のブロッカーを反映。フロップ完成・通常SDがあるハンド全体は対象外。bdsd4/8は固定した2枚の組ごとに4/8アウツ。bdsd9は3枚以上の組で9枚以上。bdsdOtherは2枚組の1〜3/5〜7アウツ。分類は成立ターン・組ごとに重複可。残り手札はブロッカーだけに使い、弱い2枚組を別の組で救済しない。clean:allはBDSDも除外。',
 syntax:'ランク・スート・除外・和集合・積集合で表す。生成式は全合法ハンドで内部照合する。Monker実機での受理・抽出一致は未確認。空集合は空文字で全ハンドではない。',
 csv:'hand/comboとweight列。重複・衝突・不正weightは拒否。weightは0〜1、小数28桁。各CSV該当weight÷選択CSV該当weight合計×100。比率は8桁切り捨て、合計0はnull。',
 limits:'CSV単体30MiB、1比較合計30MiB、inline256KiB、条件500件、1ページ最大100件。IDの有効期限は1時間。'
};
function boardInfo(text){let cs;try{cs=P.parseBoard(text);}catch(e){throw new ApiError('INVALID_BOARD',e.message);}return {normalizedBoard:cs.map(P.card),boardKey:cs.slice(0,3).sort((a,b)=>a-b).concat(cs.slice(3)).map(P.card).join('')};}
module.exports={VERSION,definitions,boardInfo};

