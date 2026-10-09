'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
const P=require('../src/lab/engine.js');
const ranks='23456789TJQKA',suits='shdc',rank=c=>Math.floor(c/4)+2,card=c=>ranks[Math.floor(c/4)]+suits[c%4];
const deck=Array.from({length:52},(_,i)=>i);
function choose(xs,n){const out=[];function go(i,a){if(a.length===n){out.push(a);return;}for(let j=i;j<xs.length;j++)go(j+1,[...a,xs[j]]);}go(0,[]);return out;}
function high5(cs){const r=[...new Set(cs.map(rank))].sort((a,b)=>a-b);if(r.length!==5)return 0;if(r[4]-r[0]===4)return r[4];return r.join(',')==='2,3,4,5,14'?5:0;}
// Independent physical-card oracle: pick exactly 2 actual hole cards and 3
// actual board cards. No engine evaluator, rank masks, or BDSD helpers.
function high(h,b,required=[]){let best=0;for(const p of choose(h,2))for(const t of choose(b,3))if(required.every(c=>t.includes(c)))best=Math.max(best,high5([...p,...t]));return best;}
function oracle(board,h){
 const remaining=deck.filter(c=>!board.includes(c)&&!h.includes(c)),opCache=new Map(),ownCache=new Map();
 function own(held,b,required=[]){const k=held.map(rank).join(',')+'|'+b.map(rank).join(',')+'|'+required.map(rank).join(',');if(!ownCache.has(k))ownCache.set(k,high(held,b,required));return ownCache.get(k);}
 function opponent(b){const k=b.map(rank).slice().sort((a,b)=>a-b).join(',');if(opCache.has(k))return opCache.get(k);const pool=remaining.filter(c=>!b.includes(c));let best=0;const pairs=new Set();for(const p of choose(pool,2)){const key=p.map(rank).sort((a,b)=>a-b).join(',');if(pairs.has(key))continue;pairs.add(key);best=Math.max(best,high(p,b));}opCache.set(k,best);return best;}
 const made=own(h,board),normal=remaining.filter(t=>own(h,[...board,t]));
 if(made||normal.length)return {primary:made?'madeStraight':'sd',excludedAtFlop:true,normal:made?[]:normal,turns:[]};
 const groups=[];for(const size of [2,3,4]){const seen=new Set();for(const cs of choose(h,size)){const rs=cs.map(rank).sort((a,b)=>a-b),key=rs.join(',');if(new Set(rs).size!==size||seen.has(key))continue;seen.add(key);groups.push({cs,rs,key});}}
 const turns=[],categories=new Set();
 for(const turn of remaining){
  const b=[...board,turn],rivers=remaining.filter(c=>c!==turn),opp=opponent(b),evaluated=[],accepted=[];
  for(const g of groups){
   const max=Math.max(...rivers.map(r=>own(g.cs,[...b,r]))),outs=[],excluded=[];
   if(opp<=max)for(const river of rivers){const full=[...b,river];if(!own(g.cs,full,[turn,river]))continue;const score=own(g.cs,full),other=opponent(full);(other>score?excluded:outs).push(river);}
   let category=null;
   if(g.cs.length===2&&outs.length)category=outs.length===4?'bdsd4':outs.length===8?'bdsd8':'bdsdOther';
   if(g.cs.length>=3&&outs.length>=9&&!evaluated.some(s=>s.rs.every(r=>g.rs.includes(r))&&s.outs.length===outs.length&&s.outs.every(c=>outs.includes(c))))category='bdsd9';
   const row={...g,outs,excluded,category};evaluated.push(row);if(category){accepted.push(row);categories.add(category);}
  }
  turns.push({turn,groups:accepted,outs:[...new Set(accepted.flatMap(g=>g.outs))]});
 }
 return {turns,excludedAtFlop:false,normal:[],primary:['bdsd9','bdsd8','bdsd4','bdsdOther'].find(k=>categories.has(k))||'none'};
}
function validateWitness(w,h,b){assert.ok(w);assert.equal(w.hand.length,2);assert.equal(w.board.length,3);const cs=x=>x.map(c=>ranks.indexOf(c[0])*4+suits.indexOf(c[1]));const pair=cs(w.hand),triple=cs(w.board);assert.ok(pair.every(c=>h.includes(c)));assert.ok(triple.every(c=>b.includes(c)));assert.equal(new Set([...pair,...triple]).size,5);assert.equal(high5([...pair,...triple]),w.high);}
let comparisons=0;
async function verify(boardText,handText){
 const b=P.parseBoard(boardText),h=P.parseHand(handText,b),r=new P.BackdoorStraight(b).details(handText),o=oracle(b,h);
 assert.equal(r.primary,o.primary);assert.equal(r.provisional,h.length!==4);assert.equal(r.excludedAtFlop,o.excludedAtFlop);
 if(o.excludedAtFlop){assert.deepEqual(r.turns,[]);assert.deepEqual(r.normalTurns.map(t=>t.card).sort(),o.normal.map(card).sort());console.log('Excluded at flop',boardText,handText,r.primary);return r;}
 assert.equal(r.turns.reduce((n,t)=>n+t.turnCount,0),52-b.length-h.length);
 const byRank=new Map(r.turns.map(t=>[t.rank,t]));
 for(const expected of o.turns){
  const t=byRank.get(ranks[rank(expected.turn)-2]);assert.equal(t.outCount,expected.outs.length);assert.equal(t.groups.length,expected.groups.length);
  for(const g of expected.groups){const actual=t.groups.find(x=>x.ranks.join('')===g.rs.map(r=>ranks[r-2]).join(''));assert.ok(actual,handText+' '+card(expected.turn)+' '+g.key);assert.equal(actual.category,g.category);assert.equal(actual.outCount,g.outs.length);assert.deepEqual(actual.valid.flatMap(x=>x.cards).sort(),g.outs.map(card).sort());assert.deepEqual(actual.excluded.flatMap(x=>x.cards).sort(),g.excluded.map(card).sort());}
  comparisons++;
 }
 for(const t of r.turns){
  const turn=deck.find(c=>card(c)===t.representativeTurn),tb=[...b,turn];
  if(t.opponent)validateWitness(t.opponent,deck.filter(c=>!h.includes(c)&&!tb.includes(c)),tb);
  for(const g of [...t.groups,...t.excludedGroups]){
   const held=g.hands[0].map(c=>deck.find(x=>card(x)===c));assert.equal(g.size,held.length);
   if(g.category==='bdsd4'||g.category==='bdsd8')assert.equal(g.size,2);
   if(g.category==='bdsd9')assert.ok(g.size>=3&&g.outCount>=9);
   for(const x of [...g.valid,...g.excluded]){const river=deck.find(c=>card(c)===x.cards[0]),rb=[...tb,river];validateWitness(x.self,held,rb);validateWitness(x.backdoor,held,rb);assert.ok(x.backdoor.board.includes(card(turn))&&x.backdoor.board.includes(card(river)));if(x.opponentHigh)validateWitness(x.opponent,deck.filter(c=>!h.includes(c)&&!rb.includes(c)),rb);assert.equal(new Set(x.cards).size,x.count);}
  }
 }
 console.log('Combination oracle passed',boardText,handText,r.primary);return r;
}
(async()=>{
 const a=await verify('Ts8h2d','5s6h');assert.equal(a.primary,'bdsd4');for(const rank of ['3','4'])assert.equal(a.turns.find(t=>t.rank===rank).outCount,4);for(const rank of ['7','9'])assert.equal(a.turns.find(t=>t.rank===rank).status,'opponentOnTurn');assert.ok(a.turns.find(t=>t.rank==='4').groups.some(g=>g.excluded.some(x=>x.rank==='7')));
 const b=await verify('AsTh3d','AhQs');assert.equal(b.primary,'bdsd4');assert.deepEqual(b.turns.filter(t=>t.status==='valid').map(t=>t.rank),['J','K']);
 for(const [board,hand] of [['Ts8h2d','5s6hKcKd'],['AsTh3d','AhQsAcQd'],['AsKd7s','2s3s4s5s'],['AsKd7s','2s2h5s6s'],['AsKd7s','2s2h3s4s'],['AsKd7s','QhJh2c3c'],['As2d3c','4h5hKcKd'],['Ts8h2d','5s6h4c4d'],['AsAh2d','3s4h5c6d'],['Ts8h2d','9s9h9d9c']])await verify(board,hand);
 const union=await verify('AsKd7s','2s4h6c9d');const five=union.turns.find(t=>t.rank==='5');assert.equal(five.outCount,8);assert.ok(five.groups.every(g=>g.category==='bdsd4'&&g.outCount===4));assert.ok(!union.turns.some(t=>t.groups.some(g=>g.category==='bdsd8')));
 const wrap=await verify('AsKd7s','2s3s4s5s');assert.ok(wrap.turns.some(t=>t.groups.some(g=>g.size===3&&g.category==='bdsd9'&&g.outCount===9)));
 assert.throws(()=>new P.BackdoorStraight(P.parseBoard('AsKd7s2d')),/フロップ/);
 const check=new P.BackdoorStraight(P.parseBoard('AsKd7s'));for(const h of ['As2h3c4d','2s2s3h4h','2s3h4c','2x3h4c5d'])assert.throws(()=>check.details(h));
 let engine;for(const board of ['Ts8h2d','AsKd7s','AsAh2d']){
  engine=new P.Engine();await engine.prepare(board);
  if(board==='AsKd7s'){
   const aa=engine.syntax({role:'set:14',bdsd:'bdsd8'});
   assert.equal(aa.syntax,'AA:(54,65,86)');assert.equal(aa.count,144);assert.equal(aa.verified,true);
   assert.equal(engine.syntax({bdsd:'bdsd8'}).syntax,'(54:!(2,6,JT,QT,QJ),65:!(3,8,JT,QT,QJ),86:!(4,9,JT,QT,QJ))');
   for(const [kind,count] of [['bdsd8',27996],['bdsd4',126826],['bdsd9',16768]])assert.equal(engine.query({bdsd:kind}).count,count);
  }
  for(const bdsd of ['bdsd','bdsd9','bdsd8','bdsd4','bdsdOther','none']){
   const result=engine.syntax({bdsd}),match=P.compile(result.syntax||'!*');
   for(let i=0;i<engine.hands.length;i++)assert.equal(match(P.unpack(engine.hands[i])),engine.matches(i,{bdsd}));
   console.log('Range verified',board,bdsd,result.count,result.syntax.length);
  }
  for(const filter of [{bdsd:'bdsd4',sd:'none',fd:'none'},{bdsd:'bdsd8',pocket:2},{bdsd:'bdsd9',sd:'nutGut'},{clean:'all'},{bdsd:'bdsd4',role:'set:'+engine.ranks[0]}])engine.syntax(filter);
  assert.equal(engine.query({bdsd:'bdsd',sd:'sd'}).count,0);
  for(let i=0;i<engine.hands.length;i++)if(engine.flags[i]&P.F.bdsd)assert.equal(engine.flags[i]&P.F.sd,0);
  const r=engine.query({clean:'all'});assert.equal(r.counts.bdsd,0);
 }
 for(const board of ['AsKd7s2h','AsKd7s2h9c']){await engine.prepare(board);assert.equal(engine.query({bdsd:'bdsd'}).count,0);assert.throws(()=>engine.bdsdDetails('2s3h4c5d'),/フロップ/);}
 require('node:child_process').execFileSync(process.execPath,[path.join(__dirname,'../src/make-offline.cjs')]);
 const html=fs.readFileSync(path.join(__dirname,'../dist/PLO-Syntax-Lab.html'),'utf8'),ctx={setTimeout};vm.createContext(ctx);vm.runInContext(html.match(/<script id="engine-inline">([\s\S]*?)<\/script>/)[1],ctx);
 const report=new ctx.PLO.BackdoorStraight(ctx.PLO.parseBoard('Ts8h2d')).details('5s6hKcKd');assert.equal(report.primary,'bdsd4');assert.equal(report.remainingCards,45);
 assert.ok(html.includes('<select id="bdsd">')&&html.includes('bdsdDetails')&&html.includes('id="save-bdsd"'));
 console.log(JSON.stringify({status:'passed',physicalTurns:comparisons,standalone:true}));
})().catch(e=>{console.error(e);process.exitCode=1;});
