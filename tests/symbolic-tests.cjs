const assert=require('node:assert/strict'),P=require('../src/lab/engine.js');
const fixtures=['Ts 8d 2c','Ts 8s 2d','9s 8s 2s','Js Ts 4d 3d','As Ad 7c','As Ah Ad','5s 4d 2c','9s 8d 7c 2h'];
const shapes=['sd','wrap','nutGut','nutOpen','nonGut','nonOpen','none'];
(async()=>{let checks=0,maxLength=0;for(const board of fixtures){const e=new P.Engine();await e.prepare(P.parseBoard(board));const filters=shapes.map(sd=>({role:'all',sd}));if(e.board.length===3)filters.push({role:'all',clean:'all'});else filters.push({role:'all',clean:'regular'});
 const role=e.roles.find(r=>r.count!==0)?.key||'all';for(const sd of shapes)filters.push({role,sd,blockers:[{rank:e.ranks[0],mode:'no'}]});
 for(const filter of filters){const out=e.syntax(filter);if(out.count){assert.equal(out.format,'compact');assert.doesNotMatch(out.syntax,/(?:[2-9TJQKA][shdc]){4}/i);const match=P.compile(out.syntax);for(let i=7;i<e.hands.length;i+=631)assert.equal(match(P.unpack(e.hands[i])),e.matches(i,filter),board+JSON.stringify(filter));maxLength=Math.max(maxLength,out.syntax.length);}else assert.equal(out.syntax,'');checks++;}
 console.log(JSON.stringify({board,checks,lastRole:role,maxLength}));}console.log(JSON.stringify({status:'passed',checks,maxLength}));})().catch(e=>{console.error(e);process.exitCode=1});
