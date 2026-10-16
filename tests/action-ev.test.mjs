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
