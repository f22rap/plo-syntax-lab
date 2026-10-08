#!/usr/bin/env node
'use strict';
const {Server}=require('@modelcontextprotocol/sdk/server/index.js');
const {StdioServerTransport}=require('@modelcontextprotocol/sdk/server/stdio.js');
const {ListToolsRequestSchema,CallToolRequestSchema,ListResourcesRequestSchema,ReadResourceRequestSchema,McpError,ErrorCode}=require('@modelcontextprotocol/sdk/types.js');
const {Service}=require('./service.cjs');
async function main(){
 const args=process.argv.slice(2),allowReadRoots=[];
 for(let i=0;i<args.length;i++){
  if(args[i]==='--allow-read-root'&&args[i+1])allowReadRoots.push(args[++i]);
  else if(args[i]==='--help'){process.stderr.write('Usage: node stdio.cjs [--allow-read-root <folder>]\nCSV access is disabled unless a root is specified. Inline CSV and syntax generation work without a root.\n');return;}
  else throw Error('Unknown argument. Use --help.');
 }
 const service=new Service({allowReadRoots});
 const server=new Server({name:'plo-syntax-lab',version:'1.0.0'},{capabilities:{tools:{},resources:{}},instructions:'PLO4を解析する。まずplo_capabilities/plo_definitionsで定義を確認する。SDの2枚組分類は重複する。CSV・条件・ジョブIDは後続の呼び出しに引き継ぐ。CSV比較はplo_start_compare後にplo_get_jobで取得する。Monker実機の検証済みとは説明しない。'});
 server.setRequestHandler(ListToolsRequestSchema,async()=>({tools:service.tools()}));
 server.setRequestHandler(CallToolRequestSchema,async(request,extra)=>{
  const result=await service.call(request.params.name,request.params.arguments||{},{signal:extra.signal});
  return {content:[{type:'text',text:JSON.stringify(result)}],structuredContent:result,isError:!result.ok};
 });
 server.setRequestHandler(ListResourcesRequestSchema,async()=>{service.cleanup();return {resources:[...service.files.values()].map(f=>({uri:'plo-result://'+f.fileId,name:f.fileName,mimeType:f.mimeType}))};});
 server.setRequestHandler(ReadResourceRequestSchema,async(request)=>{try{return service.readResource(request.params.uri);}catch{throw new McpError(ErrorCode.InvalidParams,'Result resource not found or expired');}});
 server.onerror=()=>process.stderr.write('PLO MCP: protocol error\n');
 let stopping=false;
 const stop=async()=>{if(stopping)return;stopping=true;await service.close();await server.close();};
 server.onclose=()=>{void service.close();};
 process.once('SIGINT',()=>void stop());process.once('SIGTERM',()=>void stop());
 await server.connect(new StdioServerTransport());
}
if(require.main===module)main().catch(()=>{process.stderr.write('PLO MCP could not start. Run npm ci in src/mcp and check arguments.\n');process.exitCode=1;});
module.exports={main};
