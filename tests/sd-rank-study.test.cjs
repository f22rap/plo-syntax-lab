'use strict';
const test=require('node:test'),assert=require('node:assert/strict');
const S=require('../src/sd-rank-study.cjs'),P=require('../src/lab/engine.js'),G=require('../src/research-generation.cjs');
const {execFileSync}=require('node:child_process');
let e,rows;
test.before(async()=>{
 e=new P.Engine();await e.prepare('AsTd7c');
 const generated=await G.generatePrepared(e,S.createSet());
 rows=generated.rows.map(r=>({Board:'AsTd7c',SpotId:'one',ConditionId:r.condition_id,Syntax:r.syntax,Status:r.status==='ready'?'completed':'empty_condition',SelectedHands:r.legal_hand_count,Actions:r.status==='ready'?[{Key:'total_bet',Frequency:r.condition_id.endsWith('_yes')?60:40}]:[]}));
});
test('new CLI process and saved recipe roundtrip keep pp included without nopp variants',()=>{
 const saved=JSON.parse(execFileSync(process.execPath,['mcp/sd-rank-study.cjs','conditions','test_inclusive'],{encoding:'utf8'}));
 assert.equal(saved.conditions.length,26);
 for(const c of saved.conditions){assert.ok(!c.condition_id.includes('nopp'));assert.equal(c.generator.filter.pocket.type,'all');assert.equal(c.generator.filter.sd,'none');assert.equal(c.generator.additional_syntax,'*');}
 assert.deepEqual(JSON.parse(JSON.stringify(saved)),S.createSet({set_id:'test_inclusive'}));
});
test('AT7 includes A88x, excludes own SD and two-pair, and allows BDFD',()=>{
 const s=rows.find(r=>r.ConditionId==='rank_8_all_yes').Syntax,match=P.compile(s);
 assert.equal(match(P.parseHand('Ah8s8d2c',e.board)),true);
 assert.equal(match(P.parseHand('Ah8h8d2c',e.board)),true);
 assert.equal(match(P.parseHand('Ah8s9d2c',e.board)),false);
 assert.equal(match(P.parseHand('AhTs8d2c',e.board)),false);
 const no=P.compile(rows.find(r=>r.ConditionId==='rank_8_all_no').Syntax);
 assert.equal(no(P.parseHand('Ah8s8d2c',e.board)),false);
});
test('report ranks only blocker target ranks and computes paired delta; ignores nopp distractors',async()=>{
 const report=await S.summarize([...rows,{Board:'AsTd7c',SpotId:'one',ConditionId:'rank_8_nopp_yes',Status:'completed',Actions:[{Key:'total_bet',Frequency:99}]}]);
 assert.equal(report.policy,S.POLICY);assert.equal(report.pocket_pairs,'included');
 const r=report.boards[0].ranks.find(r=>r.rank==='8');assert.equal(r.delta,20);assert.deepEqual(r.targets.oesd,['98']);
 assert.ok(!report.boards[0].ranks.some(r=>r.rank==='2'));
 assert.match(S.renderHtml(report),/pp含む/);assert.match(report.boards[0].summary,/20.0/);
});
test('report rejects disguised pp exclusion and missing pp-inclusive rows',async()=>{
 const wrong=structuredClone(rows);wrong.find(r=>r.ConditionId==='rank_8_all_yes').Syntax+=':!(22,33,44,55,66,77,88,99,TT,JJ,QQ,KK,AA)';
 await assert.rejects(S.summarize(wrong),/Definition mismatch/);
 await assert.rejects(S.summarize(rows.filter(r=>r.ConditionId!=='rank_8_all_yes')),/no nopp fallback/);
});
test('no matching hands is omitted, not zero; incomplete and duplicate results fail closed',async()=>{
 const missing=structuredClone(rows);missing.find(r=>r.ConditionId==='rank_8_all_yes').Status='no_matching_hands';
 const r=await S.summarize(missing);assert.ok(r.boards[0].omitted.some(x=>x.rank==='8'));assert.ok(!r.boards[0].ranks.some(x=>x.rank==='8'));
 const bad=structuredClone(rows);bad.find(r=>r.ConditionId==='rank_8_all_yes').Status='failed';await assert.rejects(S.summarize(bad),/Incomplete/);
 await assert.rejects(S.summarize([...rows,rows.find(r=>r.ConditionId==='rank_8_all_yes')]),/duplicate/);
});
test('MCP builder is read-only and report service reads every page through the shared policy',async()=>{
 const {ResearchService}=await import('../mcp/research-service.mjs');
 const {registerResearchTools}=await import('../mcp/research-tools.mjs');
 const service=new ResearchService({timeoutSeconds:120},{},{native:()=>{throw Error('Unexpected workspace write');}});
 const registered=new Map();registerResearchTools((name,desc,schema,fn)=>registered.set(name,fn),service);
 const draft=await registered.get('research_build_sd_rank_set')({set_id:'fresh'});
 assert.equal(draft.policy,S.POLICY);assert.equal(draft.condition_set.conditions.length,26);
 const offsets=[];service.result=async a=>{offsets.push(a.offset);return a.offset===0?{items:rows.slice(0,10),next_offset:10}:{items:rows.slice(10),next_offset:null};};
 const queued=await registered.get('research_sd_rank_report')({result_id:'1'.repeat(32)});
 let job;
 do {await new Promise(r=>setTimeout(r,10));job=service.getJob(queued.job_id);}while(['queued','running'].includes(job.state));
 assert.equal(job.state,'completed',job.error);assert.deepEqual(offsets,[0,10]);assert.equal(job.result.policy,S.POLICY);assert.equal(job.result.boards[0].ranks.find(r=>r.rank==='8').delta,20);
 await service.close();
});
