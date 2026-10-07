const assert=require('node:assert/strict'),P=require('../src/lab/engine.js');
const cards=s=>s.match(/../g).map(c=>'23456789TJQKA'.indexOf(c[0])*4+'shdc'.indexOf(c[1]));
// Independent oracle: form exactly two hole cards plus three board cards,
// compare their five ranks, and never score suits or other poker categories.
function straightHigh(hand,board){let high=0;for(const pair of P.combos(hand,2))for(const triple of P.combos(board,3)){
 const ranks=[...new Set([...pair,...triple].map(P.rank))].sort((a,b)=>a-b);
 if(ranks.length===5){if(ranks[4]-ranks[0]===4)high=Math.max(high,ranks[4]);else if(ranks.join(',')==='2,3,4,5,14')high=Math.max(high,5);}
 }return high;}
function oracle(hand,board){if(board.length===5||straightHigh(hand,board))return {outs:0,nut:0};let outs=0,nut=0;
 const remaining=Array.from({length:52},(_,i)=>i).filter(c=>!hand.includes(c)&&!board.includes(c));
 for(const out of remaining){const next=[...board,out],hero=straightHigh(hand,next);if(!hero)continue;outs++;let better=false;
  for(const pair of P.combos(remaining.filter(c=>c!==out),2))if(straightHigh(pair,next)>hero){better=true;break;}
  if(!better)nut++;
 }return {outs,nut};}
function handIndex(e,hand){const h=hand.slice().sort((a,b)=>a-b),packed=h[0]|h[1]<<6|h[2]<<12|h[3]<<18;const i=e.hands.indexOf(packed);assert.ok(i>=0);return i;}
const fixtures=[
 ['As Kd 7s',[['AhAdQsJs',4,4,'nutGut'],['AhAdTsJh',4,4,'nutGut'],['AhAdKc7h',0,0,null]]],
 ['As Kd 7c',[['AhAdQsJs',4,4,'nutGut']]],
 ['As Ks 7s',[['AhAdQsJs',4,4,'nutGut']]],
 ['Ts 8d 2c',[['Js7hAcAd',4,0,'nonGut'],['QsJhAcAd',4,4,'nutGut'],['Js7h9s9d',16,11,'wrap'],['QsJh9s9d',12,12,'wrap'],['9s7hAcAd',8,4,'nonOpen']]],
 ['Ts 8s 2s',[['Js7hAcAd',4,0,'nonGut'],['QsJhAcAd',4,4,'nutGut']]],
 ['Ts 8s 2d 3d',[['Js7hAcAd',4,0,'nonGut'],['QsJhAcAd',4,4,'nutGut']]],
 ['Ts 8d 2c 2h',[['QsJhAcAd',4,4,'nutGut']]]
];
(async()=>{let checks=0;for(const [text,hands] of fixtures){const e=new P.Engine();await e.prepare(P.parseBoard(text));
 for(const [holding,outs,nut,flag] of hands){const hand=cards(holding),i=handIndex(e,hand),truth=oracle(hand,e.board);assert.deepEqual(truth,{outs,nut},text+' '+holding);assert.equal(e.outs[i],truth.outs);assert.equal(e.nutOuts[i],truth.nut);if(flag)assert.ok(e.flags[i]&P.F[flag]);checks++;}
 for(let i=0;i<e.hands.length;i++){if(e.flags[i]&P.F.nonGut)assert.equal(e.nutOuts[i],0,'non-nut gutshot must have zero nut outs');if(e.flags[i]&P.F.nutGut)assert.equal(e.nutOuts[i],e.outs[i]);}
 for(const sd of ['nutGut','nonGut','nutOpen','nonOpen']){const result=e.syntax({sd});if(result.count){assert.equal(result.format,'compact');assert.doesNotMatch(result.syntax,/(?:[2-9TJQKA][shdc]){4}/i);}checks++;}
 if(text.startsWith('As K')){assert.equal(e.query({sd:'nonGut'}).count,0);assert.equal(e.syntax({role:'set:14',sd:'nonGut'}).syntax,'');assert.equal(e.query({role:'set:14',sd:'nutGut'}).count,text==='As Ks 7s'?135:144);checks++;}
 console.log(JSON.stringify({board:text,checks,nonGut:e.query({sd:'nonGut'}).count}));
 }console.log(JSON.stringify({status:'passed',checks}));})().catch(e=>{console.error(e);process.exitCode=1;});
