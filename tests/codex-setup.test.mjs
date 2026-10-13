import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {prepare,codexFragment,starterPrompt} from '../mcp/codex-setup.mjs';

test('Codex setup creates a canonical local config and preserves it on reuse',async()=>{
 const dir=await fs.mkdtemp(path.join(os.tmpdir(),'plo-codex-setup-'));
 try{
  const csv=path.join(dir,'CSV 日本語 with spaces');await fs.mkdir(csv);
  const prepared=await prepare({directory:dir,inputRoots:[csv,csv]});assert.equal(prepared.created,true);assert.equal(prepared.input_roots.length,1);assert.equal(prepared.input_roots[0],await fs.realpath(csv));
  const original=await fs.readFile(prepared.config_file,'utf8');assert.equal(JSON.parse(original).allowHistoryDelete,false);
  const again=await prepare({directory:dir});assert.equal(again.created,false);assert.equal(await fs.readFile(prepared.config_file,'utf8'),original);
  await assert.rejects(()=>prepare({directory:dir,inputRoots:[csv]}),/already exists/);assert.equal(await fs.readFile(prepared.config_file,'utf8'),original);
  assert.equal(await fs.readFile(prepared.prompt_file,'utf8'),starterPrompt);assert.match(await fs.readFile(prepared.fragment_file,'utf8'),/tool_timeout_sec = 3600/);
 }finally{await fs.rm(dir,{recursive:true,force:true});}
});
test('Codex setup rejects missing folders before creating a config',async()=>{
 const dir=await fs.mkdtemp(path.join(os.tmpdir(),'plo-codex-invalid-'));
 try{await assert.rejects(()=>prepare({directory:dir,inputRoots:[path.join(dir,'missing')]}));await assert.rejects(()=>fs.access(path.join(dir,'mcp.local.json')));await assert.rejects(()=>prepare({directory:dir}),/Choose/);}finally{await fs.rm(dir,{recursive:true,force:true});}
});
test('Codex TOML escapes Windows paths without shell interpolation',()=>{
 const node='C:\\Program Files\\nodejs\\node.exe',server='C:\\PLO 日本語\\mcp\\server.mjs',config='C:\\PLO 日本語\\mcp.local.json';const fragment=codexFragment(node,server,config,'C:\\PLO 日本語');
 assert.equal(JSON.parse(fragment.split('\n').find(s=>s.startsWith('command = ')).slice(10)),node);
 assert.deepEqual(JSON.parse(fragment.split('\n').find(s=>s.startsWith('args = ')).slice(7)),[server,'--config',config]);assert.ok(!fragment.includes('approval_policy'));assert.ok(!fragment.includes('sandbox_mode'));
});
