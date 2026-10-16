import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {evaluateHand,analyzeActionCsvs,EV_DEFINITION} from '../mcp/action-ev.mjs';
const row=(ev,diff,weight=1)=>({ev:ev==null?null:String(ev),ev_diff:diff==null?null:String(diff),weight:String(weight)});
test('two actions: recover missing alternative and loss, preserve zero weight',()=>{
 const x=evaluateHand([row(2.5,.3),null]);assert.equal(x.actions[1].ev_source,'derived_from_other_ev_diff');assert.equal(x.actions[1].ev_bb,2.2);assert.ok(Math.abs(x.actions[1].loss_bb-.3)<1e-12);assert.equal(x.actions[0].loss_bb,0);
 const y=evaluateHand([row(2,-.5,0),row(2.5,.5)]);assert.equal(y.actions[0].ev_source,'csv');assert.equal(y.actions[0].loss_bb,.5);assert.equal(y.pre_action_weight,1);
});
test('three actions: compare against best OTHER, not global runner-up for every row',()=>{
 const x=evaluateHand([row(2,-.5),row(2.5,.3),row(2.2,-.3)]);assert.equal(x.status,'complete');assert.deepEqual(x.actions.map(a=>a.loss_bb),[.5,0,.3]);
 const bad=evaluateHand([row(2,-.2),row(2.5,.3),row(2.2,-.3)]);assert.equal(bad.status,'inconsistent');assert.ok(bad.actions.every(x=>x.loss_bb===null));
});
test('three actions: missing alternatives stay unknown but known action loss is valid',()=>{
 const x=evaluateHand([row(2,-.5),null,null]);assert.equal(x.status,'partial');assert.equal(x.actions[0].loss_bb,.5);assert.equal(x.actions[1].ev_bb,null);assert.equal(x.actions[1].loss_bb,null);
 const y=evaluateHand([row(2,-.5),row(3,null),null]);assert.equal(y.status,'inconsistent');
 const z=evaluateHand([row(2,-.5),row(2.2,-.8),null]);assert.equal(z.status,'inconsistent');
});
test('rounding residual, missing diff and no-evidence cases',()=>{
 assert.equal(evaluateHand([row(1.1,-.08),row(1.17,.07)]).status,'complete');
 assert.equal(evaluateHand([row(1.1,-.08),row(1.17,.07)],0).status,'inconsistent');
 const x=evaluateHand([row(2,null),row(2.5,null),row(2.2,null)]);assert.equal(x.status,'complete');assert.equal(x.actions[0].loss_bb,.5);
 assert.equal(evaluateHand([row(null,null),null,null]).status,'partial');
});
test('file-level aggregation uses pre-action weight, publishes missing coverage and rejects bad menus',async t=>{
 const dir=await fs.mkdtemp(path.join(os.tmpdir(),'plo-action-ev-'));t.after(()=>fs.rm(dir,{recursive:true,force:true}));const config={inputRoots:[await fs.realpath(dir)],maxCsvBytes:1e6};
 const names=['C','R50','R100'],contents=[['AhKsQcJd,0.25,2,-.5','AcKhQdJc,1,1,.1'],['JdQcKsAh,0.5,2.5,.3'],['AhKsQcJd,0.25,2.2,-.3']];
 const actions=[];for(let i=0;i<3;i++){const file=path.join(dir,'AsTd7c_BTN_C_'+names[i]+'.csv');await fs.writeFile(file,'hand,weight,ev,ev_diff\n'+contents[i].join('\n')+'\n');actions.push({action_id:names[i],path:file});}
 const args={board:'AsTd7c',actions,ev_definition:EV_DEFINITION,complete_action_menu:true,common_weight_scale:true};const r=await analyzeActionCsvs(config,args);
 assert.equal(r.totals.pre_action_weight,2);assert.equal(r.actions[0].coverage_percent,100);assert.equal(r.actions[0].mean_loss_bb_on_covered_range,.25);assert.equal(r.actions[1].coverage_percent,50);assert.equal(r.actions[1].mean_loss_bb_on_covered_range,0);assert.equal(r.totals.partial_hands,1);
 await assert.rejects(()=>analyzeActionCsvs(config,{...args,complete_action_menu:false}),/Explicit/);
 await assert.rejects(()=>analyzeActionCsvs(config,{...args,actions:[actions[0],actions[0]]}),/unique/);
 await assert.rejects(()=>analyzeActionCsvs(config,{...args,actions:[...actions.slice(0,2),{...actions[2],path:actions[1].path}]}),/Duplicate/);
 await assert.rejects(()=>analyzeActionCsvs(config,{...args,actions:actions.map(a=>({...a,expected_sha256:'0'.repeat(64)}))}),/changed/);
});

test('syntax groups retain overlaps, use pre-action frequencies and identical common cohorts',async()=>{
 const {conditionAccumulator}=await import('../mcp/action-ev.mjs');
 const c=conditionAccumulator([{condition_id:'all',label:'all',syntax:'*'},{condition_id:'ak',label:'AK',syntax:'A:K'},{condition_id:'none',label:'22',syntax:'22'}],'AsTd7c',['C','R50','R100']);
 c.add('AhKsQcJd',evaluateHand([row(2,-.5,.25),row(2.5,.3,.5),row(2.2,-.3,.25)]));
 c.add('AcKhQdJc',evaluateHand([row(1,.1,3),null,null]));
 const [all,ak,empty]=c.finish();
 assert.equal(all.hands,2);assert.equal(ak.hands,2);assert.equal(all.common_coverage_percent,25);
 assert.equal(all.actions[0].frequency_percent,81.25);
 assert.equal(all.actions[0].mean_loss_bb_on_covered_range,.125);
 assert.equal(all.actions[0].mean_loss_bb_on_common_range,.5);
 assert.equal(all.actions[1].coverage_percent,25);assert.equal(all.actions[1].mean_loss_bb_on_common_range,0);
 assert.equal(empty.actions[0].frequency_percent,null);assert.equal(empty.actions[0].mean_loss_bb_on_common_range,null);
 assert.throws(()=>conditionAccumulator([{condition_id:'a',syntax:'A('}],'AsTd7c',['C','B']));
 assert.throws(()=>conditionAccumulator([{condition_id:'a',syntax:'A'},{condition_id:'a',syntax:'K'}],'AsTd7c',['C','B']));
});

test('saved condition EV jobs resolve literals and guard revisions and selectors',async t=>{
 const {ResearchService}=await import('../mcp/research-service.mjs');
 const dir=await fs.mkdtemp(path.join(os.tmpdir(),'plo-saved-ev-'));t.after(()=>fs.rm(dir,{recursive:true,force:true}));
 const config={inputRoots:[await fs.realpath(dir)],maxCsvBytes:1e6,timeoutSeconds:30};
 const actions=[];
 for(const [id,ev,diff] of [['C',2,-.5],['R50',2.5,.5]]){const p=path.join(dir,'AsTd7c_BTN_C_'+id+'.csv');await fs.writeFile(p,`hand,weight,ev,ev_diff\nAhKsQcJd,0.5,${ev},${diff}\n`);actions.push({action_id:id,path:p});}
 const service=new ResearchService(config,{});
 service.workspace=async()=>({revision:1,condition_sets:[{set_id:'saved',revision:1,name:'Saved',conditions:[{condition_id:'ak',label:'AK',enabled:true,kind:'literal',syntax:'A:K',by_board:{}}]}]});
 const args={board:'AsTd7c',actions,condition_set:{set_id:'saved',revision:1},ev_definition:EV_DEFINITION,complete_action_menu:true,common_weight_scale:true};
 const job=service.analyzeActionEvs(args);await service.queue;const done=service.getJob(job.job_id);assert.equal(done.state,'completed',done.error);assert.equal(done.result.conditions[0].hands,1);assert.equal(done.result.conditions[0].actions[0].mean_loss_bb_on_common_range,.5);assert.equal(done.result.generation.rows[0].syntax,'A:K');assert.equal(done.result.persisted,false);
 assert.throws(()=>service.analyzeActionEvs({...args,conditions:[{condition_id:'x',label:'X',syntax:'A'}]}));
 const stale=service.analyzeActionEvs({...args,condition_set:{set_id:'saved',revision:2}});await service.queue;assert.equal(service.getJob(stale.job_id).state,'failed');
 const unknown=service.analyzeActionEvs({...args,condition_set:{set_id:'saved',revision:1,condition_ids:['unknown']}});await service.queue;assert.equal(service.getJob(unknown.job_id).state,'failed');
});

test('GUI CLI uses shared calculation and returns condition labels without hand pages',async t=>{
 const {spawnSync}=await import('node:child_process');const dir=await fs.mkdtemp(path.join(os.tmpdir(),'plo-gui-ev-'));t.after(()=>fs.rm(dir,{recursive:true,force:true}));
 const actions=[];for(const [id,ev,diff] of [['C',2,-.5],['R50',2.5,.5]]){const p=path.join(dir,'AsTd7c_BTN_C_'+id+'.csv');await fs.writeFile(p,`hand,weight,ev,ev_diff\nAhKsQcJd,0.5,${ev},${diff}\n`);actions.push({action_id:id,path:p});}
 const input={board:'AsTd7c',actions,conditions:[{condition_id:'a',label:'トップペア',syntax:'A'}],ev_definition:EV_DEFINITION,complete_action_menu:true,common_weight_scale:true};
 const run=spawnSync(process.execPath,['mcp/action-ev-cli.mjs'],{input:JSON.stringify(input),encoding:'utf8'});assert.equal(run.status,0,run.stderr);const result=JSON.parse(run.stdout);assert.equal(result.conditions[0].label,'トップペア');assert.equal(result.conditions[0].actions[0].mean_loss_bb_on_common_range,.5);assert.equal(result.items.length,0);
});
