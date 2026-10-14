import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
import {ConditionGenerator,createPresetSet,researchSnapshot,verifyGenerationVersion} from '../mcp/generated-conditions.mjs';

test('Snapshot rejects failed generation, cancellation, incomplete rows and changed definitions',async()=>{
 const condition_set=createPresetSet({preset_ids:['set.top.all']}),preview=await new ConditionGenerator().generate({condition_set,boards:['AsKd7c']});
 assert.ok(researchSnapshot(preview,condition_set).sha256);
 for(const change of [p=>p.status='cancelled',p=>p.rows=[],p=>p.rows[0].status='generation_failed',p=>p.rows[0].recipe_hash='f'.repeat(64),p=>p.rows[0].label='changed']){
  const bad=structuredClone(preview);change(bad);assert.throws(()=>researchSnapshot(bad,condition_set,{allowMissing:true}));
 }
 const changed=structuredClone(condition_set);changed.conditions[0].generator.additional_syntax='KK';assert.throws(()=>researchSnapshot(preview,changed));
 assert.throws(()=>verifyGenerationVersion('0'.repeat(64)),/変更/);
});

test('Source edits invalidate even a cached generation in an isolated runtime copy',async()=>{
 const tmp=await fs.mkdtemp(path.join(os.tmpdir(),'plo-generation-version-'));
 try{
  const files=['src/lab/engine.js','src/defaults.js','src/default-labels.json','src/default-presets.json','src/research-recipes.cjs','src/research-generation.cjs','mcp/generated-condition-worker.cjs','mcp/generated-conditions.mjs'];
  for(const file of files){await fs.mkdir(path.dirname(path.join(tmp,file)),{recursive:true});await fs.copyFile(new URL('../'+file,import.meta.url),path.join(tmp,file));}
  const isolated=await import(pathToFileURL(path.join(tmp,'mcp/generated-conditions.mjs')));isolated.verifyGenerationVersion();
  await fs.appendFile(path.join(tmp,'src/research-generation.cjs'),'\n// changed after startup\n');
  assert.throws(()=>isolated.verifyGenerationVersion(),/起動後に変更/);
  await assert.rejects(()=>new isolated.ConditionGenerator().generate({condition_set:createPresetSet({preset_ids:['set.top.all']}),boards:['AsKd7c']}),/起動後に変更/);
 }finally{await fs.rm(tmp,{recursive:true,force:true});}
});
