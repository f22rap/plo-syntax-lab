import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {Client} from '@modelcontextprotocol/client';
import {StdioClientTransport} from '@modelcontextprotocol/client/stdio';
import {root} from './runtime.mjs';
import {researchToolNames} from './research-tools.mjs';

export async function checkConnection({command=process.execPath,args,env=process.env}={}){
 const client=new Client({name:'plo-codex-connection-check',version:'0.3.0'}),transport=new StdioClientTransport({command,args,env,stderr:'pipe'});
 let stderr='';transport.stderr.on('data',b=>{stderr=(stderr+b).slice(-8000);});
 const timer=setTimeout(()=>{void transport.close();},60000);
 try{
  await client.connect(transport);const tools=await client.listTools();const names=tools.tools.map(t=>t.name);
  for(const name of researchToolNames)if(!names.includes(name))throw Error('Missing research tool: '+name);
  async function call(name){const r=await client.callTool({name,arguments:{}});if(r.isError)throw Error(r.content.map(c=>c.text||'').join('\n'));return r.structuredContent||JSON.parse(r.content[0].text);}
  const status=await call('get_status'),workspace=await call('research_list_workspace');
  if(status.research?.tools!==researchToolNames.length)throw Error('The connected server does not support this research workflow.');
  return {checked_utc:new Date().toISOString(),mcp_connection:'passed',ai_host_connection:'not_tested',version:status.version,tool_count:names.length,research_tool_count:researchToolNames.length,workspace_revision:workspace.workspace_revision,condition_sets:workspace.condition_sets.total,catalogs:workspace.catalogs.total,history_root:status.history_root,input_roots:status.input_roots,output_root:status.output_root,note:workspace.catalogs.total&&workspace.condition_sets.total?'Ready to select saved data in Codex.':'Connection works. Register a condition set and board CSV catalog in the EXE first.'};
 }catch(e){throw Error(e.message+(stderr?'\nServer: '+stderr:''));}finally{clearTimeout(timer);await client.close();}
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
 try{if(process.platform!=='win32')throw Error('Run the connection check on Windows.');const config=path.resolve(process.argv[2]||path.join(root,'mcp.local.json'));const r=await checkConnection({args:[path.join(root,'mcp/server.mjs'),'--config',config]});await fs.writeFile(path.join(root,'mcp-diagnostics.json'),JSON.stringify(r,null,2)+'\n');console.log(JSON.stringify(r,null,2));}catch(e){console.error(e.message);process.exitCode=1;}
}
