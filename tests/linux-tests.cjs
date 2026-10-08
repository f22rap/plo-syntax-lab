'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),os=require('node:os'),path=require('node:path');
const {compare,csv,decimal,format,resultCsv}=require('../src/linux/compare.cjs');
const {createApp}=require('../src/linux/server.cjs');
const file=(name,text,label=name)=>({name,text,label});
const input={board:'JsTd7c',all:true,ranges:[{cell:'L49',label:'トップセット',syntax:'JJ!(98)'}],files:[file('a.csv','hand,weight\nJhJdAsKc,0.1\nThTsAhKh,0.2\n','A'),file('b.csv','combo,weight\r\nJhJdAsKc,0.3\r\n','B')]};
(async()=>{
 const rows=compare(input);assert.equal(rows.length,2);assert.equal(rows[0].total_weight,'0.6');assert.equal(rows[0].csv1_weight_sum,'0.3');assert.equal(rows[0].csv1_percent,50);assert.equal(rows[1].csv1_matched_hands,1);assert.equal(rows[1].csv1_percent,25);assert.equal(rows[1].csv2_percent,75);
 assert.equal(format(decimal('1e-28')),'0.0000000000000000000000000001');assert.throws(()=>decimal('NaN'));assert.throws(()=>decimal('1.01'));assert.throws(()=>decimal('-0.1'));assert.throws(()=>decimal('1e-29'));
 assert.deepEqual(csv('a,b\r\n"x,y","z""q"\r\n'),[['a','b'],['x,y','z"q']]);assert.throws(()=>csv('a\n"bad'));assert.throws(()=>csv('"a"x,b'));
 const invalid=['hand,weight\nJhJhAsKc,0.1','hand,weight\nJsJdAsKc,0.1','hand,weight\nJhJdAsKc,0.1\nKcAsJdJh,0.2','hand,frequency\nJhJdAsKc,0.1','hand,weight\nJhJdAsKc,2','hand,weight'];
 for(const text of invalid)assert.throws(()=>compare({...input,files:[file('a.csv',text),input.files[1]]}));
 assert.throws(()=>compare({...input,files:[input.files[0],input.files[0]]}));assert.throws(()=>compare({...input,ranges:[{label:'empty',syntax:''}]}));assert.throws(()=>compare({...input,files:[file('range_AsKd7s.csv',input.files[0].text),input.files[1]]}));
 const zero=compare({...input,files:input.files.map(f=>({...f,text:f.text.replace(/0\.[123]/g,'0')}))});assert.equal(zero[0].csv1_percent,null);assert.equal(zero[0].status,'ZeroTotal');assert.match(resultCsv(zero),/ZeroTotal/);
 assert.equal(compare({...input,files:[...input.files,file('c.csv','hand,weight\nJhJdAsKc,0.6')]} )[0].csv_count,3);
 const dir=await fs.mkdtemp(path.join(os.tmpdir(),'plo-linux-test-')),app=createApp({historyDir:dir});
 try{
  const url=await app.start(),origin=new URL(url).origin;
  const post=(route,data,options={})=>fetch(new URL(route,url),{method:'POST',headers:{'Content-Type':'application/json',Origin:origin},body:JSON.stringify(data),...options});
  assert.equal((await fetch(new URL('health',url))).status,200);assert.equal((await fetch(origin+'/')).status,404);
  assert.equal(await new Promise((resolve,reject)=>{require('node:http').get(url,{headers:{Host:'evil.test'}},res=>{res.resume();resolve(res.statusCode);}).on('error',reject);}),404);
  assert.equal((await post('compare',input,{headers:{'Content-Type':'application/json',Origin:'http://evil.test'}})).status,400);
  assert.equal((await fetch(new URL('app.js',url))).status,200);assert.equal((await fetch(new URL('lab/worker.js',url))).status,200);
  const response=await post('compare',input);assert.equal(response.status,200);const saved=await response.json();assert.equal(saved.record.rows[1].csv1_percent,25);assert.match(saved.csv,/トップセット/);
  const list=await(await fetch(new URL('history',url))).json();assert.equal(list.history.length,1);assert.equal(list.history[0].id,saved.record.id);
  assert.equal((await fetch(new URL('history/'+saved.record.id,url))).status,200);
  const selection={app:'PLO Syntax Lab',game:'PLO4',board:['Js','Td','7c'],ranges:[{label:'トップセット',count:3103,syntax:'JJ!(98)'}]};
  assert.equal((await post('lab/selection',selection)).status,200);assert.equal((await(await fetch(new URL('selection',url))).json()).selection.ranges[0].count,3103);
  assert.equal((await post('selection',{...selection,ranges:[{label:'empty',count:0,syntax:''}]})).status,400);
  const generated=await(await post('defaults',{board:'JsTd7c'})).json();assert.equal(generated.ranges.length,185);assert.equal(generated.ranges.find(r=>r.cell==='L49').syntax,'JJ!(98)');
  const removed=await fetch(new URL('history/'+saved.record.id,url),{method:'DELETE',headers:{'Content-Type':'application/json',Origin:origin},body:'{}'});assert.equal(removed.status,200);assert.equal((await(await fetch(new URL('history',url))).json()).history.length,0);
  assert.equal((await fetch(new URL('history/not-an-id',url))).status,404);
  console.log('Linux comparison, decimal arithmetic, CSV validation, HTTP origin/token restrictions, generator transfer, 185 defaults, history export/delete: passed');
 }finally{await app.close();await fs.rm(dir,{recursive:true,force:true});}
})().catch(e=>{console.error(e);process.exitCode=1;});
