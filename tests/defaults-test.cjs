const P=require('../src/lab/engine.js'),generate=require('../src/defaults.js'),catalog=require('../src/default-labels.json'),assert=require('assert/strict'),fs=require('fs'),path=require('path');
const out=path.resolve(__dirname,'../test-results/defaults');fs.mkdirSync(out,{recursive:true});
(async()=>{const report=[];for(const board of ['JsTd7c','AsKd7s','AsAd7c','Ks8s5d2d','AsKd7s5h9c']){
 const e=new P.Engine();await e.prepare(P.parseBoard(board));const start=Date.now(),rows=await generate(P,e,catalog);
 assert.equal(rows.length,185);assert.deepEqual(rows.map(r=>r.label),catalog.map(r=>r.label));assert.equal(new Set(rows.map(r=>r.cell)).size,185);
 const by=id=>rows.find(r=>r.cell===id);const top=e.ranks[0],mid=e.ranks.length>=3?e.ranks[1]:null;
 assert.equal(by('L49').count,e.query({role:'set:'+top}).count);if(mid)assert.equal(by('L61').count,e.query({role:'set:'+mid}).count);
 if(e.board.length===3)assert.equal(by('L49').count,['L50','L51','L52'].reduce((sum,key)=>sum+by(key).count,0));
 else for(const r of rows.filter(r=>/bdfd/.test(r.label)))assert.match(r.problem,/フロップ/);
 for(const r of rows.filter(r=>/BDSD|blocker/i.test(r.label)))assert.ok(!r.syntax&&r.problem);
 if(board==='JsTd7c'){assert.equal(by('L49').syntax,'JJ!(98)');assert.equal(by('L61').syntax,'TT!((98,JJ))');assert.ok(rows.filter(r=>r.count>0).length>100);}
 if(board==='AsKd7s'){assert.equal(by('L49').count,3151);for(const r of rows.filter(r=>/SD gut$/.test(r.label)&&!r.label.startsWith('Tset')&&!r.label.startsWith('Mset')&&!r.label.startsWith('Bset')))assert.equal(r.count,0);}
 if(board==='AsAd7c')assert.ok(by('L61').problem.includes('ミドル'));
 if(e.board.length===5)for(const r of rows.filter(r=>/ SD /.test(r.label)))assert.ok(!r.syntax);
 for(const r of rows)assert.equal(!!r.syntax,r.count>0);
 report.push({board,available:rows.filter(r=>r.count>0).length,seconds:(Date.now()-start)/1000});
 fs.writeFileSync(path.join(out,'defaults-'+board+'.json'),JSON.stringify({app:'PLO Syntax Lab',game:'PLO4',mode:'defaults',board:e.board.map(P.card),ranges:rows}));console.log(report[report.length-1]);
 }fs.writeFileSync(path.join(out,'defaults-test-passed.json'),JSON.stringify(report,null,2));})().catch(e=>{console.error(e);process.exitCode=1;});
