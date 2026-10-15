const test=require('node:test'),assert=require('node:assert/strict');
const P=require('../src/lab/engine.js'),generate=require('../src/defaults.js'),catalog=require('../src/default-labels.json');
test('Ahit kicker promotion and four exclusive FD tiers match all legal hands',async()=>{
 for(const board of ['As7s2d','AdKs7s','AsKdQc','As4d4c','Ks7d2c']){
  const e=new P.Engine();await e.prepare(board);const rows=await generate(P,e,catalog.filter(c=>c.label.startsWith('Ahit ')));
  const boardRanks=e.board.map(P.rank),paired=boardRanks.find(r=>boardRanks.filter(x=>x===r).length===2),available=Array.from({length:13},(_,i)=>14-i).filter(r=>r!==14&&!boardRanks.includes(r));
  const drawSuit=[0,1,2,3].find(s=>e.board.filter(c=>P.suit(c)===s).length===2);
  const suitRanks=drawSuit===undefined?[]:Array.from({length:13},(_,i)=>14-i).filter(r=>!e.board.some(c=>P.suit(c)===drawSuit&&P.rank(c)===r));
  const sets=rows.map(r=>r.syntax?e.expressionSet(r.syntax):null),counts=rows.map(()=>0);
  for(let i=0;i<e.hands.length;i++){
   const h=P.unpack(e.hands[i]),rs=h.map(P.rank),base=boardRanks.includes(14)&&e.matches(i,{role:paired&&paired!==14?'two:14:'+paired:'pair:14'})&&rs.includes(14);
   const high=Math.max(...rs.filter(r=>r!==14)),suited=h.filter(c=>P.suit(c)===drawSuit),tier=suitRanks.indexOf(Math.max(...suited.map(P.rank)));
   const want=[base&&high===available[0],base&&high===available[1],base&&high!==available[0]&&high!==available[1],...Array.from({length:4},(_,k)=>base&&suited.length>=2&&(k===3?tier>=3:tier===k))];
   for(let j=0;j<7;j++){const actual=!!(sets[j]&&(sets[j][i>>>5]&(1<<(i&31))));assert.equal(actual,!!want[j],board+' '+rows[j].label+' '+h.map(P.card).join(''));if(actual)counts[j]++;}
  }
  rows.forEach((r,i)=>assert.equal(r.count,counts[i]));console.log(JSON.stringify({board,counts}));
 }
});
