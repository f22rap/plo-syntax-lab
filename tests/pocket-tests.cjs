const assert=require('node:assert/strict'),P=require('../src/lab/engine.js');
const cards=s=>s.match(/../g).map(c=>'23456789TJQKA'.indexOf(c[0])*4+'shdc'.indexOf(c[1]));
function has(e,s,f){const h=cards(s).sort((a,b)=>a-b),i=e.hands.indexOf(h[0]|h[1]<<6|h[2]<<12|h[3]<<18);assert.ok(i>=0);return e.matches(i,f);}
(async()=>{let checks=0;for(const board of ['As Kd 7s','As Kd 7s 5h','As Kd 7s 5h 9c']){const e=new P.Engine();await e.prepare(P.parseBoard(board));
 for(let r=2;r<=14;r++){const f={role:'pair:14',pocket:String(r)},out=e.syntax(f);let count=0;
  for(let i=0;i<e.hands.length;i++){const h=P.unpack(e.hands[i]),expected=e.roles[e.handRoles[i]].key==='pair:14'&&h.filter(c=>P.rank(c)===r).length>=2;assert.equal(e.matches(i,f),expected);count+=Number(expected);}
  assert.equal(out.count,count);if(count){assert.equal(out.format,'compact');assert.doesNotMatch(out.syntax,/(?:[2-9TJQKA][shdc]){4}/i);}checks++;
 }
 if(e.board.length===3){for(const r of [2,3])assert.equal(e.query({role:'pair:14',pocket:r}).count,660);
  assert.equal(e.query({pocket:2}).count,6121);assert.equal(e.query({role:'set:14',pocket:2}).count,18);
  assert.ok(has(e,'Ah2s2dQc',{role:'pair:14',pocket:2}));assert.ok(has(e,'Ah2s2d2c',{role:'pair:14',pocket:2}));
  assert.ok(!has(e,'Ah2s2dKc',{role:'pair:14',pocket:2}));assert.ok(!has(e,'AhAd2s2d',{role:'pair:14',pocket:2}));
  assert.equal(e.query({role:'pair:14',pocket:2,blockers:[{rank:14,mode:'no'}]}).count,0);checks+=9;
 }
 const filters=[{role:'pair:14',pocket:2,clean:'all'},{role:'set:14',pocket:2},{role:'two:14:13',pocket:2},{role:'pair:14',pocket:3,blockers:[{rank:14,mode:'one'}]}];
 if(e.board.length<5)filters.push({role:'pair:14',pocket:2,fd:'fd'},{role:'pair:14',pocket:2,sd:'none'},{pocket:2,sd:'nonGut'},{pocket:3,sd:'nutOpen'});
 for(const f of filters){const out=e.syntax(f);if(out.count)assert.equal(out.format,'compact');checks++;}
 console.log(JSON.stringify({board,checks,topPair22:e.query({role:'pair:14',pocket:2}).count,syntax:e.syntax({role:'pair:14',pocket:2}).syntax}));
 }console.log(JSON.stringify({status:'passed',checks}));})().catch(e=>{console.error(e);process.exitCode=1;});
