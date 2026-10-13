import {spawn} from 'node:child_process';
import path from 'node:path';
import {root,checkAbort} from './runtime.mjs';

// The optional launch object is constructor-injected by the integration tests.
// It is never accepted from MCP input or the user's runtime configuration.
export function researchNative(request,{signal,timeoutSeconds=300,progress=()=>{}}={},launch={command:path.join(root,'dist','PloResearch.Native.exe'),args:[]}){
 checkAbort(signal);
 return new Promise((resolve,reject)=>{
  const child=spawn(launch.command,launch.args,{windowsHide:true,stdio:['pipe','pipe','pipe']});
  let buffer='',stderr='',value,error,received=false,killTimer,timedOut=false,settled=false;
  const cancel=()=>{child.stdin.write('cancel\n',()=>{});killTimer??=setTimeout(()=>child.kill(),10000);};
  const timer=setTimeout(()=>{timedOut=true;cancel();},timeoutSeconds*1000);
  const finish=err=>{if(settled)return;settled=true;clearTimeout(timer);clearTimeout(killTimer);signal?.removeEventListener('abort',cancel);err?reject(err):resolve(value);};
  signal?.addEventListener('abort',cancel,{once:true});
  child.on('error',finish);child.stdin.on('error',()=>{});
  child.stdout.setEncoding('utf8');child.stderr.setEncoding('utf8');
  child.stderr.on('data',s=>{stderr=(stderr+s).slice(-10000);});
  child.stdout.on('data',s=>{
   buffer+=s;if(buffer.length>64*1024*1024){error='Research response too large.';child.kill();return;}
   let end;while((end=buffer.indexOf('\n'))>=0){const line=buffer.slice(0,end);buffer=buffer.slice(end+1);try{const frame=JSON.parse(line);if(frame.type==='progress')progress(frame.value);else if(frame.type==='result'){value=frame.value;received=true;}else if(frame.type==='error')error=frame.value;else throw Error('Unknown frame.');}catch{error='Invalid research host response.';child.kill();}}
  });
  child.on('close',code=>{
   if(received&&code===0)return finish(); // a cooperative cancellation returns a saved partial result
   finish(Error(error||(timedOut?'Research timed out.':signal?.aborted?'Cancelled.':stderr||'Research host failed. Run build.ps1.')));
  });
  child.stdin.write(JSON.stringify(request)+'\n');if(signal?.aborted)cancel();
 });
}
