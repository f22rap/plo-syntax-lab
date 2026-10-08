'use strict';
const http = require('node:http'), fs = require('node:fs/promises'), path = require('node:path'), os = require('node:os');
const {randomBytes, randomUUID} = require('node:crypto'), {Worker} = require('node:worker_threads');
const P = require('../lab/engine.js'), {validateSelection,resultCsv} = require('./compare.cjs');
function createApp(options = {}) {
 const token = randomBytes(16).toString('hex'), prefix = `/${token}/`;
 const historyDir = options.historyDir || path.join(process.env.XDG_DATA_HOME || path.join(os.homedir(),'.local','share'),'plo-syntax-lab','history');
 const workers = new Set(); let selection = null, origin;
 const staticFiles = {'':['index.html','text/html'], 'app.js':['app.js','text/javascript'], 'style.css':['style.css','text/css'], 'lab/':['../lab/index.html','text/html'], 'lab/engine.js':['../lab/engine.js','text/javascript'], 'lab/app.js':['../lab/app.js','text/javascript'], 'lab/style.css':['../lab/style.css','text/css'], 'lab/worker.js':['../lab/worker.js','text/javascript']};
 const send = (res,status,data) => {res.writeHead(status,{'Content-Type':'application/json; charset=utf-8'}); res.end(JSON.stringify(data));};
 async function body(req) { if (!/^application\/json(?:;|$)/i.test(req.headers['content-type'] || '') || req.headers.origin !== origin) throw Error('リクエストの送信元・形式が不正です。'); let size=0, chunks=[]; for await (const chunk of req) { size+=chunk.length; if(size>32*1024*1024) throw Error('入力は合計32MiBまでです。'); chunks.push(chunk); } return JSON.parse(Buffer.concat(chunks).toString('utf8')); }
 function run(action,input,res) { return new Promise((resolve,reject)=>{ if(workers.size>=2) return reject(Error('別の計算が実行中です。少し待ってください。')); const w=new Worker(path.join(__dirname,'worker.cjs'),{workerData:{action,input},resourceLimits:{maxOldGenerationSizeMb:512}}); workers.add(w); let done=false;
  const finish=(error,result)=>{if(done)return;done=true;clearTimeout(timeout);res.off('close',cancel);workers.delete(w);void w.terminate();error?reject(error):resolve(result);};
  const cancel=()=>finish(Error('計算を中止しました。')); const timeout=setTimeout(()=>finish(Error('計算が5分を超えたため中止しました。')),300000);
  res.on('close',cancel);w.once('message',data=>finish(data.error?Error(data.error):null,data.result));w.once('error',e=>finish(e));w.once('exit',code=>{if(!done)finish(Error(`計算プロセスが終了しました (${code})`));});
 }); }
 const validId=id=>/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/.test(id);
 const server = http.createServer(async(req,res)=>{
  res.setHeader('Cache-Control','no-store');res.setHeader('X-Content-Type-Options','nosniff');res.setHeader('Referrer-Policy','no-referrer');
  res.setHeader('Content-Security-Policy',"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; worker-src 'self' blob:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'");
  try {
   if(req.headers.host!==new URL(origin).host || !req.url.startsWith(prefix)) return send(res,404,{error:'Not found'});
   const route=new URL(req.url,origin).pathname.slice(prefix.length);
   if(req.method==='GET' && staticFiles[route]) { const [file,type]=staticFiles[route];res.setHeader('Content-Type',type+'; charset=utf-8');return res.end(await fs.readFile(path.join(__dirname,file))); }
   if(req.method==='GET' && route==='health') return send(res,200,{status:'ok',platform:process.platform});
   if(req.method==='GET' && route==='selection') return send(res,200,{selection});
   if(req.method==='POST' && (route==='selection'||route==='lab/selection')) {const data=await body(req), board=P.parseBoard(data.board?.join('')).map(P.card).join('');const ranges=validateSelection(data,board);selection={...data,ranges,id:randomUUID()};return send(res,200,{message:'比較画面に条件を追加しました。'});}
   if(req.method==='POST' && route==='defaults') {const input=await body(req);P.parseBoard(input.board);return send(res,200,{ranges:await run('defaults',input,res)});}
   if(req.method==='POST' && route==='compare') {const input=await body(req),rows=await run('compare',input,res);if(res.destroyed)return;const record={id:randomUUID(),savedUtc:new Date().toISOString(),board:input.board,ranges:input.ranges,files:input.files.map(f=>({name:f.name,label:f.label})),rows};await fs.mkdir(historyDir,{recursive:true,mode:0o700});await fs.writeFile(path.join(historyDir,record.id+'.json'),JSON.stringify(record),{flag:'wx',mode:0o600});return send(res,200,{record,csv:resultCsv(rows)});}
   if(req.method==='GET' && route==='history') {await fs.mkdir(historyDir,{recursive:true,mode:0o700});const list=[];for(const file of await fs.readdir(historyDir)){if(!validId(file.slice(0,-5))||!file.endsWith('.json'))continue;const r=JSON.parse(await fs.readFile(path.join(historyDir,file),'utf8'));list.push({id:r.id,savedUtc:r.savedUtc,board:r.board,files:r.files});}list.sort((a,b)=>b.savedUtc.localeCompare(a.savedUtc));return send(res,200,{history:list});}
   if(route.startsWith('history/')) {const id=route.slice(8);if(!validId(id))return send(res,404,{error:'Not found'});const file=path.join(historyDir,id+'.json');if(req.method==='GET'){const record=JSON.parse(await fs.readFile(file,'utf8'));return send(res,200,{record,csv:resultCsv(record.rows)});}if(req.method==='DELETE'){await body(req);await fs.unlink(file);return send(res,200,{status:'deleted'});}}
   send(res,404,{error:'Not found'});
  } catch(e) { if(!res.destroyed)send(res,e.code==='ENOENT'?404:400,{error:e.message}); }
 });
 return {server,historyDir,async start(port=0){await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(port,'127.0.0.1',resolve);});origin=`http://127.0.0.1:${server.address().port}`;return origin+prefix;},async close(){for(const w of workers)await w.terminate();server.closeAllConnections();await new Promise(resolve=>server.close(resolve));}};
}
if(require.main===module){const app=createApp();app.start(Number(process.env.PLO_PORT || 0)).then(url=>{console.log(`PLO Syntax Lab Linux: ${url}\n履歴: ${app.historyDir}\n終了: Ctrl+C`);if(process.argv.includes('--open')){const child=require('node:child_process').spawn('xdg-open',[url],{stdio:'ignore'});child.on('error',()=>console.error('ブラウザを自動起動できません。表示されたURLを開いてください。'));}for(const s of ['SIGINT','SIGTERM'])process.once(s,()=>app.close().then(()=>process.exit(0)));}).catch(e=>{console.error(e.message);process.exitCode=1;});}
module.exports={createApp};
