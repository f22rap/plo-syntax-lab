#!/usr/bin/env node
'use strict';
const fs=require('node:fs');
const S=require('../src/sd-rank-study.cjs');
async function main(){
 const [mode,input,...rest]=process.argv.slice(2);
 if(mode==='conditions'){
  if(rest.length)throw Error('Usage: node mcp/sd-rank-study.cjs conditions [set_id]');
  console.log(JSON.stringify(S.createSet(input?{set_id:input}:{}),null,2));return;
 }
 if(mode!=='report'||!input||rest.some(x=>x!=='--html')||rest.length>1)throw Error('Usage: node mcp/sd-rank-study.cjs report rows.json [--html]');
 const data=JSON.parse(fs.readFileSync(input,'utf8'));
 if(!Array.isArray(data)&&data.next_offset!=null)throw Error('Paginated input is incomplete; concatenate all board-view items first');
 const report=await S.summarize(Array.isArray(data)?data:data.items);
 console.log(rest.includes('--html')?S.renderHtml(report):JSON.stringify(report,null,2));
}
main().catch(e=>{console.error(e.message);process.exitCode=1;});
