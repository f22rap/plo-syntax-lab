import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {ConditionGenerator,createPresetSet,listPresets,normalizeSet} from './generated-conditions.mjs';

const help=`ボードごとのsyntax生成プレビュー（Node.js 20以上）

一覧:
  node mcp/preview-generated-conditions.mjs --list [--query Tset]
初回:
  node mcp/preview-generated-conditions.mjs --presets set.top.all,set.middle.all --boards AsKd7s,Kh9h3c --output preview.json
保存した条件の再利用:
  node mcp/preview-generated-conditions.mjs --set preview.json --boards JsTd7c --output next-preview.json
アプリから保存した設定JSON:
  node mcp/preview-generated-conditions.mjs --set plo-research-settings.json --list-sets
  node mcp/preview-generated-conditions.mjs --set plo-research-settings.json --set-id 条件セットID --boards AsKd7s --output app-preview.json

--set は条件セットJSON、アプリの設定JSON、または保存したプレビューJSON。
--name は新規条件セットの表示名。既存ファイルには上書きしません。
Ctrl+Cで中断すると、完了したボードのみ保存します。
終了コード: 0=完了、1=入力・保存エラー、2=未設定/未対応/生成失敗あり、3=中断。
アプリの調査設定やCSVは変更しません。
`;
function argumentsFor(args){
 const out={};
 for(let i=0;i<args.length;i++){
  const key=args[i];if(!['--help','--list','--list-sets','--set-id','--query','--presets','--set','--boards','--output','--name'].includes(key))throw Error('不明な引数です：'+key);
  if(Object.hasOwn(out,key))throw Error('引数が重複しています：'+key);
  if(['--help','--list','--list-sets'].includes(key))out[key]=true;
  else{if(!args[i+1]||args[i+1].startsWith('--'))throw Error(key+'の値が必要です。');out[key]=args[++i];}
 }
 return out;
}
export async function main(args=process.argv.slice(2)){
 const options=argumentsFor(args);
 if(options['--help']){process.stdout.write(help);return 0;}
 if(options['--list']){
  if(Object.keys(options).some(k=>!['--list','--query'].includes(k)))throw Error('--listには--queryだけ指定できます。');
  process.stdout.write(JSON.stringify(listPresets({query:options['--query']||''}),null,2)+'\n');return 0;
 }
 if(options['--query'])throw Error('--queryは--listと併用してください。');
 if(options['--set-id']&&!options['--set'])throw Error('--set-idには--setが必要です。');
 let savedSets;
 if(options['--set']){
  const value=JSON.parse((await fs.readFile(options['--set'],'utf8')).replace(/^\uFEFF/,''));
  if(Object.hasOwn(value,'condition_sets')){
   if(![1,2].includes(value.schema_version)||!Array.isArray(value.condition_sets))throw Error('未対応の調査設定形式です。');
   savedSets=value.condition_sets.map(normalizeSet);
   if(new Set(savedSets.map(s=>s.set_id)).size!==savedSets.length)throw Error('条件セットIDが重複しています。');
   if(value.schema_version===1&&savedSets.some(s=>s.conditions.some(c=>c.kind==='generated')))throw Error('自動生成条件には調査設定形式2が必要です。');
  }else savedSets=[normalizeSet(value?.kind==='generated_condition_preview'?value.condition_set:value)];
 }
 if(options['--list-sets']){
  if(!savedSets||Object.keys(options).some(k=>!['--set','--list-sets'].includes(k)))throw Error('--list-setsには--setだけ指定してください。');
  process.stdout.write(JSON.stringify(savedSets.map(s=>({set_id:s.set_id,name:s.name,conditions:s.conditions.length,enabled:s.conditions.filter(c=>c.enabled).length})),null,2)+'\n');return 0;
 }
 if(Boolean(options['--presets'])===Boolean(options['--set'])||!options['--boards']||!options['--output'])throw Error('--presetsまたは--setのどちらかと、--boards、--outputが必要です。--helpで例を表示できます。');
 if(options['--set']&&options['--name'])throw Error('--nameは新規作成時だけ指定できます。');
 let condition_set;
 if(options['--set']){
  condition_set=options['--set-id']?savedSets.find(s=>s.set_id===options['--set-id']):savedSets.length===1?savedSets[0]:null;
  if(!condition_set)throw Error('条件セットを特定できません。--list-setsで一覧を確認し、--set-idを指定してください。');
 }else condition_set=createPresetSet({preset_ids:options['--presets'].split(','),...(options['--name']?{name:options['--name']}:{})});
 const output=path.resolve(options['--output']);
 try{await fs.access(output);throw Error('出力先が既に存在します。別のファイル名を指定してください：'+output);}catch(e){if(e.code!=='ENOENT')throw e;}
 const controller=new AbortController(),abort=()=>controller.abort();
 process.on('SIGINT',abort);
 try{
  const preview=await new ConditionGenerator().generate({condition_set,boards:options['--boards'].split(',')},{signal:controller.signal,progress:p=>{
   if(p.stage==='board_complete')process.stderr.write(`${p.done}/${p.total} ${p.board} 完了\n`);
  }});
  await fs.writeFile(output,JSON.stringify(preview,null,2)+'\n',{encoding:'utf8',flag:'wx'});
  process.stdout.write(JSON.stringify({output,status:preview.status,...preview.statistics},null,2)+'\n');
  return preview.status==='cancelled'?3:preview.status==='blocked'?2:0;
 }finally{process.removeListener('SIGINT',abort);}
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url))main().then(code=>{process.exitCode=code;}).catch(e=>{process.stderr.write(e.message+'\n');process.exitCode=1;});
