import {Server} from '@modelcontextprotocol/sdk/server/index.js';
import {WebStandardStreamableHTTPServerTransport} from '@modelcontextprotocol/sdk/server/webStandardStreamableHttp.js';
import {ListToolsRequestSchema,CallToolRequestSchema,ListResourcesRequestSchema,ReadResourceRequestSchema,McpError,ErrorCode} from '@modelcontextprotocol/sdk/types.js';
import contracts from '../src/mcp/contracts.cjs';
import {CloudService,limits,sha256} from './service.mjs';
import {page} from './page.mjs';
const {catalog,ApiError}=contracts;
const sourceCommit=typeof PLO_SOURCE_COMMIT==='undefined'?null:PLO_SOURCE_COMMIT;
const encoder=new TextEncoder(),noCache={'Cache-Control':'private, no-store','X-Content-Type-Options':'nosniff','Referrer-Policy':'same-origin'};
function json(data,status=200){return new Response(JSON.stringify(data),{status,headers:{...noCache,'Content-Type':'application/json; charset=utf-8'}});}
export async function boundedBody(request,max){
 const length=Number(request.headers.get('content-length'));if(Number.isFinite(length)&&length>max)throw new ApiError('LIMIT_EXCEEDED','入力サイズの上限を超えています。');
 const reader=request.body?.getReader();if(!reader)return new Uint8Array();const chunks=[];let count=0;
 try{while(true){const {done,value}=await reader.read();if(done)break;count+=value.byteLength;if(count>max){await reader.cancel();throw new ApiError('LIMIT_EXCEEDED','入力サイズの上限を超えています。');}chunks.push(value);}}finally{reader.releaseLock();}
 const body=new Uint8Array(count);let offset=0;for(const chunk of chunks){body.set(chunk,offset);offset+=chunk.length;}return body;
}
function utf8(bytes){try{return new TextDecoder('utf-8',{fatal:true}).decode(bytes);}catch{throw new ApiError('CSV_VALIDATION_ERROR','UTF-8のデータが必要です。');}}
function requireIdentity(service){if(!service.store)throw new ApiError('PERMISSION_DENIED','サインインが必要です。');}
async function mcp(request,service){
 if(request.method!=='POST')return new Response(null,{status:405,headers:{Allow:'POST',...noCache}});
 const body=await boundedBody(request,4*1024*1024);let message;try{message=JSON.parse(utf8(body));}catch{return json({jsonrpc:'2.0',id:null,error:{code:-32700,message:'Parse error'}},400);}
 const publicMethod=['initialize','notifications/initialized','ping','tools/list'].includes(message?.method)||message?.method==='tools/call'&&['plo_capabilities','plo_definitions'].includes(message.params?.name);
 if(!publicMethod&&!service.store)return json({error:{code:'PERMISSION_DENIED',message:'サインインが必要です。'}},401);
 const server=new Server({name:'plo-syntax-lab-cloud',version:'1.0.0'},{capabilities:{tools:{},resources:{}},instructions:'PLO4の解析・生成・CSV比較。最初にplo_capabilities/plo_definitionsを確認する。クラウドCSVは2MiB・50,000行、生成10条件、比較20条件、ページ10件。PCのパスは読めない。アップロード済みIDまたはinline CSVを登録する。比較はstart_compare→get_job→export_result。Monker実機で検証済みとは説明しない。'});
 server.setRequestHandler(ListToolsRequestSchema,async()=>({tools:catalog.tools}));
 server.setRequestHandler(CallToolRequestSchema,async(r,extra)=>{const result=await service.call(r.params.name,r.params.arguments||{},{signal:extra.signal});return {content:[{type:'text',text:JSON.stringify(result)}],structuredContent:result,isError:!result.ok};});
 server.setRequestHandler(ListResourcesRequestSchema,async()=>{requireIdentity(service);return {resources:(await service.store.list('file')).map(f=>({uri:'plo-result://'+f.fileId,name:f.fileName,mimeType:f.mimeType}))};});
 server.setRequestHandler(ReadResourceRequestSchema,async r=>{requireIdentity(service);try{return await service.readResource(r.params.uri);}catch{throw new McpError(ErrorCode.InvalidParams,'Result resource not found or expired');}});
 const transport=new WebStandardStreamableHTTPServerTransport({sessionIdGenerator:undefined,enableJsonResponse:true});
 try{await server.connect(transport);const response=await transport.handleRequest(new Request(request,{body}));for(const [key,value]of Object.entries(noCache))response.headers.set(key,value);return response;}finally{await server.close();}
}
export default {
 async fetch(request,env,ctx){
  try{
   const url=new URL(request.url),origin=request.headers.get('origin');
   if(origin&&origin!==url.origin)return json({error:{code:'PERMISSION_DENIED',message:'許可されていないOriginです。'}},403);
   const identity=request.headers.get('oai-authenticated-user-id');
   // These headers are trusted only behind Sites dispatch, which authenticates and supplies them.
   const owner=identity?await sha256(identity):null;
   const service=new CloudService(env,owner,{origin:url.origin,sourceCommit,defer:promise=>ctx.waitUntil(promise)});
   if(owner)ctx.waitUntil(service.store.cleanup().catch(()=>console.error('PLO cloud: expiry cleanup deferred')));
   if(url.pathname==='/mcp')return await mcp(request,service);
   if(url.pathname==='/'&&request.method==='GET'){
    if(!owner)return new Response('<!doctype html><html lang="ja"><meta charset="utf-8"><title>PLO Syntax Lab</title><p><a href="/signin-with-chatgpt?return_to=%2F" target="_top">ChatGPTでサインイン</a></p></html>',{headers:{...noCache,'Content-Type':'text/html; charset=utf-8'}});
    const nonce=crypto.randomUUID();return new Response(page(nonce),{headers:{...noCache,'Content-Type':'text/html; charset=utf-8','Content-Security-Policy':`default-src 'none'; script-src 'nonce-${nonce}'; style-src 'unsafe-inline'; connect-src 'self'; img-src data:; base-uri 'none'; form-action 'self'; frame-ancestors 'self' https://chatgpt.com`}});
   }
   requireIdentity(service);
   if(url.pathname==='/uploads'&&request.method==='POST'){
    if(!request.headers.get('content-type')?.toLowerCase().startsWith('text/csv'))return json({error:{code:'INVALID_ARGUMENT',message:'Content-Type: text/csvが必要です。'}},415);
    let name;try{name=decodeURIComponent(request.headers.get('x-plo-file-name')||'');}catch{throw new ApiError('INVALID_ARGUMENT','ファイル名が不正です。');}
    return json(await service.upload(name,utf8(await boundedBody(request,limits.csvBytesPerFile))),201);
   }
   if(url.pathname==='/api/datasets'&&request.method==='GET')return json(await service.call('plo_list_datasets',{...(url.searchParams.has('offset')?{offset:Number(url.searchParams.get('offset'))}:{}),...(url.searchParams.has('board')?{board:url.searchParams.get('board')}:{})}));
   if(url.pathname==='/api/register'&&request.method==='POST'){let data;try{data=JSON.parse(utf8(await boundedBody(request,4*1024*1024)));}catch(error){if(error instanceof ApiError)throw error;throw new ApiError('INVALID_ARGUMENT','JSONが不正です。');}return json(await service.call('plo_register_dataset',data));}
   const file=/^\/files\/([a-f0-9-]{36})$/.exec(url.pathname);
   if(file&&request.method==='GET'){const record=await service.store.get(file[1],'file'),object=await service.store.blob(record.blobKey);return new Response(object.body,{headers:{...noCache,'Content-Type':record.mimeType+'; charset=utf-8','Content-Disposition':'attachment; filename="'+record.fileName+'"','ETag':'"'+record.sha256+'"'}});}
   return json({error:{code:'NOT_FOUND',message:'見つかりません。'}},404);
  }catch(error){const known=error instanceof ApiError;return json({error:{code:known?error.code:'STORAGE_UNAVAILABLE',message:known?error.message:'一時的に処理できません。少し待って再実行してください。'}},known?(error.code==='PERMISSION_DENIED'?401:error.code==='ID_NOT_FOUND'?404:error.code==='LIMIT_EXCEEDED'?413:400):503);}
 }
};
