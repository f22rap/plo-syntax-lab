import fs from 'node:fs/promises';
import path from 'node:path';
import {analyzeActionCsvs} from './action-ev.mjs';
try {
 let text='';for await(const chunk of process.stdin){text+=chunk;if(text.length>24*1024*1024)throw Error('Request too large');}
 const a=JSON.parse(text);
 const roots=[...new Set(await Promise.all(a.actions.map(async x=>path.dirname(await fs.realpath(x.path)))))];
 const result=await analyzeActionCsvs({inputRoots:roots,maxCsvBytes:32*1024*1024},{...a,limit:0});
 process.stdout.write(JSON.stringify(result));
}catch(e){process.stderr.write(e.message);process.exitCode=1;}
