const assert=require('node:assert/strict'),P=require('../src/lab/engine.js'),G=require('../src/research-generation.cjs'),R=require('../src/research-recipes.cjs');
const c=(id,groups)=>({condition_id:id,label:id,enabled:true,kind:'generated',syntax:'',by_board:{},generator:{recipe_version:1,ruleset:'plo4-flop-v1',source:'combination',groups:groups.map(g=>g.map(s=>s+'@1')),additional_syntax:'*'}});
(async()=>{
 const set={set_id:'combination_test',revision:0,name:'test',conditions:[
 c('and',[['pair.top.all'],['axis.fd.nut'],['axis.pp.3'],['axis.bdfd.pair']]),
 c('or',[['pair.top.all'],['axis.pp.3','axis.pp.4'],['axis.bdfd.pair','axis.bdfd.triple']]),
 c('impossible',[['pair.top.all'],['axis.fd.all'],['axis.bdfd.triple']]),
 c('base',[['pair.top.all']]),
 c('two',[['axis.bdfd.two'],['axis.bdfd.pair']]) ]};
 R.normalizeSet(set);
 for(const board of ['As7s2d','As7d2c','As4d4c']){
 const e=new P.Engine();await e.prepare(board);
 for(const name of ['bdPair','bdTriple'])e.syntax({bdfd:name});
 for(let i=0;i<e.hands.length;i++){
 const h=P.unpack(e.hands[i]),counts=[0,1,2,3].filter(s=>e.bs[s]===1).map(s=>h.filter(c=>P.suit(c)===s).length);
 assert.equal(e.matches(i,{bdfd:'bdPair'}),counts.includes(2));assert.equal(e.matches(i,{bdfd:'bdTriple'}),counts.some(n=>n>=3));
 assert.equal(!!(e.flags[i]&P.F.bdfd),e.matches(i,{bdfd:'bdPair'})||e.matches(i,{bdfd:'bdTriple'}));
 }
 const result=await G.generatePrepared(e,set);assert(!result.rows.some(r=>['generation_failed','unsupported_recipe'].includes(r.status)),JSON.stringify(result.rows));
 assert.equal(result.rows.find(r=>r.condition_id==='impossible').legal_hand_count,0);
 const row=result.rows.find(r=>r.condition_id==='or'),test=row.syntax?P.compile(row.syntax):()=>false;
 const role=board==='As4d4c'?'two:14:4':'pair:14';
 for(let i=0;i<e.hands.length;i++){
 const h=P.unpack(e.hands[i]),pp=[3,4].some(r=>h.filter(c=>P.rank(c)===r).length>=2);
 assert.equal(test(h),e.matches(i,{role})&&pp&&!!(e.flags[i]&P.F.bdfd));
 }
 if(board==='As7s2d')assert(result.rows.find(r=>r.condition_id==='and').legal_hand_count>0);
 console.log(board,result.rows.map(r=>[r.condition_id,r.status,r.legal_hand_count]));
 }
 const changed=structuredClone(set);changed.conditions[0].generator.groups[0][0]='pair.top.all@999';const result=await G.generateBoard('As7s2d',changed);assert.equal(result.rows[0].status,'unsupported_recipe');
 console.log('Combination, BDFD size, board-relative role, stale revision: passed');
})().catch(e=>{console.error(e);process.exitCode=1});
