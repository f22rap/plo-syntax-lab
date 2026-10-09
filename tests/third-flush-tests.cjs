'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const P = require('../src/lab/engine.js');
let checked = 0;
async function verify(board) {
  const e = new P.Engine();
  await e.prepare(board);
  const drawn = ['fdNut', 'fdSecond', 'fdThird', 'fdLow'];
  const roles = ['flush:0', 'flush:1', 'flush:third', 'flush:2'];
  const tops = Array.from({length:4}, (_,s) => Array.from({length:52},(_,c)=>c).filter(c=>P.suit(c)===s&&!e.board.includes(c)).sort((a,b)=>P.rank(b)-P.rank(a)));
  const suitCounts = tops.map((_,s)=>e.board.filter(c=>P.suit(c)===s).length);
  const fdResults = drawn.map(fd=>e.syntax({fd}));
  const fdSyntax = fdResults.map(r=>P.compile(r.syntax||'!*'));
  const roleResults = roles.map(role=>e.syntax({role}));
  const roleSyntax = roleResults.map(r=>P.compile(r.syntax||'!*'));
  const counts = [0,0,0,0];
  for(let i=0;i<e.hands.length;i++) {
    const h=P.unpack(e.hands[i]);
    const tiers=tops.map(top=>top.findIndex(c=>h.includes(c)));
    const held=suitCounts.map((_,s)=>h.filter(c=>P.suit(c)===s).length);
    const made=e.roles[e.handRoles[i]].cat===5;
    for(let tier=0;tier<4;tier++) {
      const fd=e.board.length<5&&held.some((n,s)=>n>=2&&suitCounts[s]===2&&(tier===3?tiers[s]>=3:tiers[s]===tier));
      assert.equal(e.matches(i,{fd:drawn[tier]}),fd,board+' FD classifier');
      assert.equal(fdSyntax[tier](h),fd,board+' FD syntax');
      const flush=made&&held.some((n,s)=>n>=2&&suitCounts[s]>=3&&(tier===3?tiers[s]>=3:tiers[s]===tier));
      assert.equal(e.matches(i,{role:roles[tier]}),flush,board+' flush classifier');
      assert.equal(roleSyntax[tier](h),flush,board+' flush syntax');
      if(flush)counts[tier]++;
      checked+=4;
    }
  }
  assert.equal(counts.reduce((a,b)=>a+b,0),e.query({role:'cat:5'}).count,'flush categories partition all ordinary flushes');
  if(board==='As9s3d') {
    assert.equal(fdResults[2].syntax,'Jss:!(ks,qs)');
    assert.equal(fdResults[3].syntax,'ss:!(ks,qs,js)');
    assert.equal(fdResults.reduce((n,r)=>n+r.count,0),e.query({fd:'fd'}).count);
    e.syntax({fd:'fdThird',role:'set:14'});
    e.syntax({fd:'fdThird',sd:'nonGut',blockers:[{rank:13,mode:'no'}]});
    e.syntax({fd:'fdThird',bdfd:'bdLow'});
  }
  if(board==='As9s3d2d') {
    const h=P.parseBoard('Js4sTd5d').sort((a,b)=>a-b),packed=h[0]|h[1]<<6|h[2]<<12|h[3]<<18;
    const i=e.hands.indexOf(packed);
    assert.ok(e.matches(i,{fd:'fdThird'})&&e.matches(i,{fd:'fdLow'})&&e.matches(i,{fd:'dualFd'}),'different suits may have different FD tiers');
  }
  console.log('Verified '+board, JSON.stringify({flushCounts:counts,thirdFD:fdResults[2].syntax}));
}
(async()=>{
  for(const b of ['As9s3d','KsQs3d','As9s3d2d','As9s3s','KsQs3s2h','9s8s7s2d3c','AsKs9s4s2d']) await verify(b);
  require('node:child_process').execFileSync(process.execPath,[require('node:path').join(__dirname,'../src/make-offline.cjs')]);
  const html=fs.readFileSync(require('node:path').join(__dirname,'../dist/PLO-Syntax-Lab.html'),'utf8');
  assert.ok(html.includes('<option value="fdThird">サードナッツFD</option>'));
  assert.ok(html.includes("'fd','fdNut','fdSecond','fdThird','fdLow'"));
  const context={setTimeout};vm.createContext(context);
  vm.runInContext(html.match(/<script id="engine-inline">([\s\S]*?)<\/script>/)[1],context);
  const e=new context.PLO.Engine();await e.prepare('As9s3d');
  assert.equal(e.syntax({fd:'fdThird'}).syntax,'Jss:!(ks,qs)');
  assert.equal(e.syntax({fd:'fdLow'}).syntax,'ss:!(ks,qs,js)');
  console.log(JSON.stringify({status:'passed',checked,standalone:'passed'}));
})().catch(e=>{console.error(e);process.exitCode=1;});
