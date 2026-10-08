import fs from 'node:fs/promises';
import {createReadStream} from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
import {spawn} from 'node:child_process';
import {Worker} from 'node:worker_threads';

export const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
export const hash=text=>createHash('sha256').update(text).digest('hex');
export async function fileHash(file){const h=createHash('sha256');for await(const chunk of createReadStream(file))h.update(chunk);return h.digest('hex');}
export const within=(base,file)=>{const r=path.relative(base,file);return r===''||(!r.startsWith('..'+path.sep)&&r!=='..'&&!path.isAbsolute(r));};
export function checkAbort(signal){if(signal?.aborted)throw Error('Cancelled.');}
export async function loadConfig(file){
 const full=path.resolve(file),base=path.dirname(full),q=JSON.parse((await fs.readFile(full,'utf8')).replace(/^\uFEFF/,''));
 for(const key of Object.keys(q))if(!['inputRoots','outputRoot','historyRoot','allowHistoryDelete','maxCsvBytes','timeoutSeconds'].includes(key))throw Error('Unknown config key: '+key);
 if(!Array.isArray(q.inputRoots)||!q.inputRoots.length||q.inputRoots.some(x=>typeof x!=='string'))throw Error('inputRoots must contain directories.');
 if(typeof q.outputRoot!=='string')throw Error('outputRoot is required.');
 const inputRoots=await Promise.all(q.inputRoots.map(async x=>{const p=await fs.realpath(path.resolve(base,x));if(!(await fs.stat(p)).isDirectory())throw Error('Input root is not a directory.');return p;}));
 const output=path.resolve(base,q.outputRoot);await fs.mkdir(output,{recursive:true});const outputRoot=await fs.realpath(output);
 const history=typeof q.historyRoot==='string'?path.resolve(base,q.historyRoot):path.join(process.env.LOCALAPPDATA||base,'FlopCommandApp','History');
 await fs.mkdir(history,{recursive:true});const historyRoot=await fs.realpath(history);
 if(outputRoot===historyRoot)throw Error('Output and history directories must differ.');
 const maxCsvBytes=q.maxCsvBytes??268435456,timeoutSeconds=q.timeoutSeconds??300;
 if(!Number.isInteger(maxCsvBytes)||maxCsvBytes<1||maxCsvBytes>1073741824)throw Error('Invalid maxCsvBytes.');
 if(!Number.isInteger(timeoutSeconds)||timeoutSeconds<10||timeoutSeconds>3600)throw Error('Invalid timeoutSeconds.');
 if(q.allowHistoryDelete!==undefined&&typeof q.allowHistoryDelete!=='boolean')throw Error('Invalid allowHistoryDelete.');
 return {inputRoots,outputRoot,historyRoot,maxCsvBytes,timeoutSeconds,allowHistoryDelete:q.allowHistoryDelete===true};
}
export async function inputFile(config,file){
 if(!path.isAbsolute(file))throw Error('Use an absolute CSV path returned by list_datasets.');
 const resolved=await fs.realpath(file);
 if(!config.inputRoots.some(r=>within(r,resolved)))throw Error('CSV is outside configured inputRoots.');
 if(path.extname(resolved).toLowerCase()!=='.csv')throw Error('Only CSV files can be registered.');
 const stat=await fs.stat(resolved);if(!stat.isFile()||stat.size>config.maxCsvBytes)throw Error('CSV is not a file or exceeds the configured size limit.');
 return resolved;
}
async function stop(child){
 if(child.exitCode!==null||!child.pid)return;
 if(process.platform==='win32')await new Promise(resolve=>{
  const killer=spawn(path.join(process.env.SystemRoot||'C:\\Windows','System32','taskkill.exe'),['/PID',String(child.pid),'/T','/F'],{windowsHide:true,stdio:'ignore'});
  killer.on('error',resolve);killer.on('close',resolve);
 });else child.kill('SIGTERM');
}
export function native(request,{signal,timeoutSeconds=300}={}){
 checkAbort(signal);
 return new Promise((resolve,reject)=>{
  const child=spawn(path.join(root,'dist','PloMcp.Native.exe'),[],{windowsHide:true,stdio:['pipe','pipe','pipe']});
  let stdout='',stderr='',failed=false,timedOut=false;
  const abort=()=>{void stop(child);};
  signal?.addEventListener('abort',abort,{once:true});
  const timer=setTimeout(()=>{timedOut=true;abort();},timeoutSeconds*1000);
  const finish=()=>{clearTimeout(timer);signal?.removeEventListener('abort',abort);};
  child.on('error',e=>{failed=true;finish();reject(e);});
  child.stdout.setEncoding('utf8');child.stderr.setEncoding('utf8');
  child.stdout.on('data',x=>{stdout+=x;if(stdout.length>64*1024*1024){failed=true;abort();}});
  child.stderr.on('data',x=>{stderr=(stderr+x).slice(-10000);});
  child.stdin.on('error',()=>{});
  child.on('close',code=>{
   finish();if(signal?.aborted)return reject(Error('Cancelled.'));
   if(timedOut)return reject(Error('Native comparison timed out.'));
   if(failed||code!==0)return reject(Error(stderr||'Native host failed. Run build.ps1 first.'));
   try{resolve(JSON.parse(stdout.replace(/^\uFEFF/,'')));}catch{reject(Error('Invalid native result.'));}
  });
  child.stdin.end(JSON.stringify(request));
 });
}
export function engine(data,{signal,timeoutSeconds=300,progress=()=>{}}={}){
 checkAbort(signal);
 return new Promise((resolve,reject)=>{
  const worker=new Worker(new URL('./engine-worker.cjs',import.meta.url),{workerData:data});
  let done=false;
  const finish=(error,result)=>{if(done)return;done=true;clearTimeout(timer);signal?.removeEventListener('abort',abort);void worker.terminate();error?reject(error):resolve(result);};
  const abort=()=>finish(Error('Cancelled.'));
  const timer=setTimeout(()=>finish(Error('Syntax generation timed out.')),timeoutSeconds*1000);
  signal?.addEventListener('abort',abort,{once:true});
  worker.on('error',e=>finish(e));worker.on('exit',code=>{if(!done)finish(Error('Engine exited before returning a result: '+code));});
  worker.on('message',m=>{if(m.progress)progress(m.progress);else if(m.error)finish(Error(m.error));else finish(null,m);});
 });
}
