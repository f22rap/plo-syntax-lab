import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {fileURLToPath} from 'node:url';
import {Client} from '@modelcontextprotocol/client';
import {StdioClientTransport} from '@modelcontextprotocol/client/stdio';
import {researchToolNames} from '../mcp/research-tools.mjs';

// Node is used by the development client only. The actual server child has an
// empty PATH and is a standalone EXE on Windows (same source + .NET 8 on Linux).
test('Native EXE: official SDK transport, no Node on child PATH, config scope and preview capabilities', {timeout:30000}, async()=>{
 const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
 const base=await fs.mkdtemp(path.join(os.tmpdir(),'plo-native-sdk-'));
 const input=path.join(base,'CSV 日本語 with spaces'),output=path.join(base,'output'),history=path.join(base,'history'),empty=path.join(base,'empty-path');
 for(const dir of [input,output,history,empty])await fs.mkdir(dir);
 const config=path.join(base,'native.json');
 await fs.writeFile(config,JSON.stringify({inputRoots:[input],outputRoot:output,historyRoot:history}));
 const windows=process.platform==='win32';
 const command=windows?(process.env.PLO_NATIVE_TEST_EXE||path.join(root,'dist/PloMcp.Connect.exe')):process.env.PLO_NATIVE_TEST_DOTNET;
 const target=process.env.PLO_NATIVE_TEST_HOST;
 if(!command||(!windows&&!target))throw Error('Set PLO_NATIVE_TEST_DOTNET and PLO_NATIVE_TEST_HOST for the Linux source harness.');
 const env={...process.env,PATH:empty};
 for(const key of Object.keys(env))if(key.toUpperCase()==='PATH'&&key!=='PATH')delete env[key];
 let client,transport;let stderr='';
 async function connect(){
  client=new Client({name:'plo-native-sdk-tests',version:'1'});
  transport=new StdioClientTransport({command,args:[...(!windows?[target]:[]),'--stdio','--config',config],env,cwd:empty,stderr:'pipe'});
  transport.stderr.on('data',b=>{stderr+=b.toString();});await client.connect(transport);
 }
 try{
  await connect();await client.ping();
  assert.equal(client.getServerVersion().name,'plo-syntax-lab-native');
  const listed=await client.listTools();assert.deepEqual(listed.tools.map(x=>x.name).sort(),['get_status','get_artifact',...researchToolNames.filter(name=>!['research_list_default_presets','research_build_sd_rank_set','research_sd_rank_report','research_inspect_csv','research_match_action_csvs','research_analyze_action_evs'].includes(name))].sort());
  assert.equal(listed.tools[0].annotations.readOnlyHint,true);
  const r=await client.callTool({name:'get_status',arguments:{}});assert.ok(!r.isError);
  const s=r.structuredContent;assert.equal(s.requires_node,false);assert.equal(s.research_ready,true);assert.equal(s.tool_count,17);assert.equal(s.planned_tool_count,17);
  assert.equal(s.chatgpt_connection,'not_tested');assert.deepEqual(s.input_roots,[await fs.realpath(input)]);
  assert.deepEqual(JSON.parse(r.content[0].text),s);
  const argsError=await client.callTool({name:'get_status',arguments:{inputRoots:['/']}});assert.equal(argsError.isError,true);
  await assert.rejects(()=>client.callTool({name:'not_a_tool',arguments:{}}),/Unknown tool/);
  assert.equal((await client.callTool({name:'research_start',arguments:{}})).isError,true);
  const before=await fs.readFile(config,'utf8');await fs.appendFile(config,' ');
  assert.equal((await client.callTool({name:'get_status',arguments:{}})).isError,true);
  await client.close();await fs.writeFile(config,before);await connect();
  assert.ok(!(await client.callTool({name:'get_status',arguments:{}})).isError);
  assert.equal(stderr,'');
  // The configuration cannot substitute an arbitrary shell command or caller path.
  await client.close();
  const external=path.join(base,'external');await fs.mkdir(external);
  const linked=path.join(base,'linked');
  try{
   await fs.symlink(external,linked,windows?'junction':'dir');
   await fs.writeFile(config,JSON.stringify({inputRoots:[linked],outputRoot:output,historyRoot:history}));
   await assert.rejects(connect);
  }catch(e){if(!['EPERM','EACCES'].includes(e.code))throw e;}
 }finally{await client?.close();await fs.rm(base,{recursive:true,force:true});}
});
