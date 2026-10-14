const test=require('node:test');
const assert=require('node:assert/strict');
const P=require('../src/lab/engine.js');
const defaults=require('../src/defaults.js');
const catalog=require('../src/default-labels.json');
const G=require('../src/research-generation.cjs');
const presets=require('../src/default-presets.json');

test('Thit on a low-paired flop hits the singleton top rank, excluding stronger hands',async()=>{
 for(const board of ['As4d4c','As7d7c','AsJdJc','AsAd3c','AsKd7c']){
  const e=new P.Engine();await e.prepare(board);
  const rows=await defaults(P,e,catalog.filter(x=>x.label.startsWith('Thit ')));
  const all=rows.find(x=>x.label==='Thit all');
  const paired=e.ranks.find(r=>e.bc[r]===2),top=e.ranks[0];
  let expectedCount=0;const bits=all.syntax?e.expressionSet(all.syntax):null;
  for(let i=0;i<e.hands.length;i++){
   const h=P.unpack(e.hands[i]),rs=h.map(P.rank);
   // Independent rank oracle for these rainbow flops: A44 requires one A
   // and no 4. AAx cannot be top-pair Ahit. AK7 retains only one-pair A.
   const expected=paired!=null?paired!==top&&rs.filter(r=>r===top).length===1&&!rs.includes(paired):
    rs.filter(r=>r===top).length===1&&!rs.includes(13)&&!rs.includes(7);
   assert.equal(!!(bits&&(bits[i>>>5]&(1<<(i&31)))),expected,board+' '+h.map(P.card).join(''));
   if(expected)expectedCount++;
  }
  assert.equal(all.count,expectedCount);if(paired!==top)assert.ok(all.count>0);
  const chosen=presets.filter(p=>p.preset_id.startsWith('pair.top.')&&p.supported);
  const set={set_id:'paired_top',revision:1,name:'paired top',conditions:chosen.map((p,i)=>({condition_id:'c'+i,label:p.label,enabled:true,kind:'generated',syntax:'',by_board:{},generator:{recipe_version:1,ruleset:'plo4-flop-v1',source:'preset',preset_id:p.preset_id,preset_revision:1,additional_syntax:'*'}}))};
  const generated=(await G.generatePrepared(e,set)).rows;
  for(const row of generated){const direct=rows.find(x=>x.label===row.label);assert.equal(row.verified,true);assert.equal(row.legal_hand_count,direct.count);assert.equal(row.syntax,direct.syntax);}
  console.log(JSON.stringify({board,count:all.count,syntax:all.syntax,conditions:generated.length}));
 }
});

test('rank-split pp preserves BDFD constraint and board-specific exclusions',async()=>{
 const e=new P.Engine();await e.prepare('AsTd6c');
 const conditions='23456789TJQKA'.split('').map((r,i)=>({condition_id:'pp'+i,label:'Thit pp '+r+r,enabled:true,kind:'generated',syntax:'',by_board:{},generator:{recipe_version:1,ruleset:'plo4-flop-v1',source:'preset',preset_id:'pair.top.pocket.under',preset_revision:1,additional_syntax:'ss:'+r+r}}));
 const rows=(await G.generatePrepared(e,{set_id:'pp',revision:1,name:'pp ranks',conditions})).rows;
 for(let j=0;j<rows.length;j++){
  const row=rows[j],rank=j+2;assert.equal(row.verified,true);
  if([6,10,14].includes(rank)){assert.equal(row.status,'empty_condition');continue;}
  assert.equal(row.status,'ready');const bits=e.expressionSet(row.syntax);let count=0;
  for(let i=0;i<e.hands.length;i++){
   const h=P.unpack(e.hands[i]),rs=h.map(P.rank);
   const expected=rs.filter(r=>r===14).length===1&&!rs.includes(6)&&!rs.includes(10)&&rs.filter(r=>r===rank).length>=2&&h.filter(c=>P.suit(c)===0).length>=2;
   assert.equal(!!(bits[i>>>5]&(1<<(i&31))),expected,row.label);if(expected)count++;
  }
  assert.equal(row.legal_hand_count,count);
 }
});
