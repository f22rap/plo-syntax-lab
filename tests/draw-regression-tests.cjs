'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const {execFileSync}=require('node:child_process'),P=require('../src/lab/engine.js');
const R='23456789TJQKA',S='shdc',shapes=['nutGut','nutOpen','nonGut','nonOpen'];
const cards=text=>P.parseBoard(text);
let assertions=0;
function equal(actual,expected,message){assert.equal(actual,expected,message);assertions++;}
function choose3(board){const out=[];for(let a=0;a<board.length-2;a++)for(let b=a+1;b<board.length-1;b++)for(let c=b+1;c<board.length;c++)out.push([board[a],board[b],board[c]]);return out;}
// Independent rank oracle: score exactly two hole ranks and three board ranks.
function straightHigh(pair,board){let high=0;for(const triple of choose3(board)){const ranks=[...new Set([...pair,...triple])].sort((a,b)=>a-b);if(ranks.length!==5)continue;if(ranks[4]-ranks[0]===4)high=Math.max(high,ranks[4]);else if(ranks.join(',')==='2,3,4,5,14')high=Math.max(high,5);}return high;}
function rankPairs(deck){const pairs=new Map();for(let i=0;i<deck.length-1;i++)for(let j=i+1;j<deck.length;j++){const pair=[P.rank(deck[i]),P.rank(deck[j])].sort((a,b)=>a-b);pairs.set(pair.join(','),pair);}return [...pairs.values()];}
function pairOracle(engine){
 const board=engine.board.map(P.rank),available=engine.available,cache=new Map(),nutHigh=new Map();
 for(const out of available){const r=P.rank(out);if(nutHigh.has(r))continue;const next=[...board,r];nutHigh.set(r,Math.max(0,...rankPairs(available.filter(c=>c!==out)).map(pair=>straightHigh(pair,next))));}
 return hole=>{
  const pair=hole.map(P.rank).sort((a,b)=>a-b),key=pair.join(',');if(cache.has(key))return cache.get(key);
  let shape=null;
  if(board.length<5&&!straightHigh(pair,board)){
   const outs=available.filter(c=>!hole.includes(c)&&straightHigh(pair,[...board,P.rank(c)])>0),ranks=[...new Set(outs.map(P.rank))];
   const nuts=outs.length>0&&outs.every(c=>straightHigh(pair,[...board,P.rank(c)])===nutHigh.get(P.rank(c)));
   // Two distinct completion ranks must be the two ends of a four-rank run.
   const open=ranks.length===2&&Math.abs(ranks[0]-ranks[1])===5 || ranks.length===2&&ranks.includes(14)&&ranks.includes(6);
   if(open)shape=nuts?'nutOpen':'nonOpen';else if(ranks.length===1)shape=nuts?'nutGut':'nonGut';
  }
  cache.set(key,shape);return shape;
 };
}
function handIndex(engine,text){const hand=cards(text).sort((a,b)=>a-b),packed=hand[0]|hand[1]<<6|hand[2]<<12|hand[3]<<18;const i=engine.hands.indexOf(packed);assert.ok(i>=0,text);return i;}
async function verifyStraightDraws(board,expected){
 const e=new P.Engine();await e.prepare(board);const oracle=pairOracle(e),patterns=Object.fromEntries(shapes.map(sd=>[sd,new Set()]));
 for(const p of e.pairList){const sd=oracle(p.p);for(const name of shapes)equal(!!(p.drawFlags&P.F[name]),sd===name,board+' pair '+p.p.map(P.card));if(sd)patterns[sd].add(p.p.map(P.rank).sort((a,b)=>b-a).map(r=>R[r-2]).join(''));}
 const made=P.compile(e.pairExpression(p=>p.sh>0)||'!*');
 for(const sd of shapes){
  const result=e.syntax({sd}),want=P.compile(patterns[sd].size?'('+[...patterns[sd]].join(',')+')':'!*'),actual=P.compile(result.syntax||'!*');
  for(let i=0;i<e.hands.length;i++){const h=P.unpack(e.hands[i]),yes=want(h)&&!made(h);equal(e.matches(i,{sd}),yes,board+' '+sd+' classification');equal(actual(h),yes,board+' '+sd+' syntax');}
  if(expected)equal(result.syntax,expected[sd],board+' short syntax');
 }
 if(board==='Ts9h3d'){
  const mixed=handIndex(e,'KsQhJs8c');equal(e.matches(mixed,{sd:'nutGut'}),true,'KQ/KJ remain selected with another draw');equal(e.matches(mixed,{sd:'nutOpen'}),true,'QJ remains selected with another draw');equal(e.matches(mixed,{sd:'wrap'}),true,'mixed hand also belongs to wrap');
  const lower=handIndex(e,'Js7hJdJc');equal(e.matches(lower,{sd:'nutGut'}),false,'extra J blockers do not promote J7 into nut gut');equal(e.matches(lower,{sd:'nonGut'}),true,'J7 remains non-nut');
  for(const filter of [{role:'set:10',sd:'nutGut'},{sd:'nutGut',fd:'none',bdfd:'bdNut'},{sd:'nonOpen',blockers:[{rank:14,mode:'no'}]}])e.syntax(filter);
 }
 console.log('Pair SD classification and syntax verified: '+board);
}
async function verifyFlushDraws(board){
 const e=new P.Engine();await e.prepare(board);const bc=[0,0,0,0];for(const c of e.board)bc[P.suit(c)]++;
 const available=Array.from({length:52},(_,i)=>i).filter(c=>!e.board.includes(c));
 const tops=S.split('').map((_,s)=>available.filter(c=>P.suit(c)===s).sort((a,b)=>P.rank(b)-P.rank(a)));
 for(const [key,required,nut,second] of [['fd',2,'fdNut','fdSecond'],['bdfd',1,'bdNut','bdSecond']])for(const [value,tier] of [[nut,0],[second,1]]){
  const result=e.syntax({[key]:value}),match=P.compile(result.syntax||'!*');
  for(let i=0;i<e.hands.length;i++){
   const hand=P.unpack(e.hands[i]),yes=(key!=='bdfd'||e.board.length===3)&&e.board.length<5&&tops.some((top,s)=>bc[s]===required&&hand.filter(c=>P.suit(c)===s).length>=2&&hand.includes(top[tier])&&(tier===0||!hand.includes(top[0])));
   equal(e.matches(i,{[key]:value}),yes,board+' '+value+' classification');equal(match(hand),yes,board+' '+value+' syntax');
  }
  for(const c of e.board)assert.ok(!result.syntax.includes(P.card(c))&&!result.syntax.includes(P.card(c).toLowerCase()),'no board card in '+result.syntax);
 }
 if(board==='As9h3d'){equal(e.syntax({bdfd:'bdNut'}).syntax,'(Kss,Ahh,Add)');equal(e.syntax({bdfd:'bdSecond'}).syntax,'(Qss:!ks,Khh:!ah,Kdd:!ad)');}
 if(board==='As9s3d'){equal(e.syntax({fd:'fdNut'}).syntax,'Kss');equal(e.syntax({fd:'fdSecond'}).syntax,'Qss:!ks');}
 console.log('Nut/second-nut FD and BDFD verified: '+board);
}
(async()=>{
 for(const board of ['As9h3d','As9s3d','AsKs3d','KsQs3d','As9s3d2d','AsAd3c'])await verifyFlushDraws(board);
 await verifyStraightDraws('Ts9h3d',{nutGut:'(KQ,KJ)',nutOpen:'QJ',nonGut:'(Q8,J7,86,76)',nonOpen:'(J8,87)'});
 for(const board of ['AsKd7s','5s4d2c','JsTs4d3d','9s8d7c2h'])await verifyStraightDraws(board);
 // Exercise the actual embedded engine, not just the source module.
 execFileSync(process.execPath,[path.join(__dirname,'../src/make-offline.cjs')]);
 const html=fs.readFileSync(path.join(__dirname,'../dist/PLO-Syntax-Lab.html'),'utf8'),source=html.match(/<script id="engine-inline">([\s\S]*?)<\/script>/)[1];
 const context={setTimeout};vm.createContext(context);vm.runInContext(source,context);
 const engine=new context.PLO.Engine();await engine.prepare(['As','9h','3d']);equal(engine.syntax({bdfd:'bdNut'}).syntax,'(Kss,Ahh,Add)','standalone HTML BDFD');
 await engine.prepare('Ts9h3d');equal(engine.syntax({sd:'nutGut'}).syntax,'(KQ,KJ)','standalone HTML gutshot');
 await assert.rejects(()=>engine.prepare([48,48,4]));await assert.rejects(()=>engine.prepare([48,4,52]));
 console.log(JSON.stringify({assertions,status:'passed',standaloneHTML:'passed'}));
})().catch(e=>{console.error(e);process.exitCode=1;});
