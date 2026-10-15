// Read-only CSV diagnostics. No EV interpretation, subtraction or policy loss.
import fs from 'node:fs/promises';
import pathModule from 'node:path';
import {inputFile,hash,fileHash} from './runtime.mjs';
const fields=['weight','frequency','percent','ev','ev_diff'];
const ranks='23456789TJQKA',suits='cdhs';
function cards(text,n){
 const parts=text.match(/[2-9TJQKA][cdhs]/g);
 if(!parts||parts.length!==n||parts.join('')!==text||new Set(parts).size!==n)throw Error('Invalid physical cards: '+text);
 return parts.sort((a,b)=>ranks.indexOf(a[0])-ranks.indexOf(b[0])||suits.indexOf(a[1])-suits.indexOf(b[1]));
}
function decode(bytes){
 if(bytes[0]===255&&bytes[1]===254)return {encoding:'utf-16le',text:new TextDecoder('utf-16le',{fatal:true}).decode(bytes)};
 if(bytes[0]===254&&bytes[1]===255)return {encoding:'utf-16be',text:new TextDecoder('utf-16be',{fatal:true}).decode(bytes)};
 try{return {encoding:'utf-8',text:new TextDecoder('utf-8',{fatal:true}).decode(bytes)};}
 catch{return {encoding:'shift_jis',text:new TextDecoder('shift_jis',{fatal:true}).decode(bytes)};}
}
function records(text){
 const rows=[];let row=[],cell='',quoted=false,closed=false,line=1,start=1;
 for(let i=0;i<text.length;i++){
  const ch=text[i];
  if(quoted){if(ch==='"'){if(text[i+1]==='"'){cell+='"';i++;}else{quoted=false;closed=true;}}else{cell+=ch;if(ch==='\n')line++;}continue;}
  if(ch==='"'){if(cell||closed)throw Error('Unexpected quote at line '+line);quoted=true;continue;}
  if(ch===','||ch==='\n'||ch==='\r'){
   row.push(cell);cell='';closed=false;
   if(ch!==','){if(ch==='\r'&&text[i+1]==='\n')i++;if(row.some(x=>x!==''))rows.push({line:start,cells:row});row=[];line++;start=line;}
  }else{if(closed)throw Error('Unexpected text after quote at line '+line);cell+=ch;}
 }
 if(quoted)throw Error('Unclosed CSV quote');
 if(cell||row.length||closed){row.push(cell);rows.push({line:start,cells:row});}
 return rows;
}
function number(raw){
 if(raw==null||raw.trim()==='')return null;
 const s=raw.trim();if(!/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/.test(s)||!Number.isFinite(Number(s)))return NaN;
 return Number(s);
}
export async function readCsv(config,{path,board,expected_sha256}){
 const safe=await inputFile(config,path);
 if((await fs.stat(safe)).size>32*1024*1024)throw Error('Inspection limit is 32 MiB per file.');
 const bytes=await fs.readFile(safe);
 if(bytes.length>Math.min(config.maxCsvBytes,32*1024*1024))throw Error('Inspection limit is 32 MiB per file.');
 const sha256=hash(bytes);if(expected_sha256&&sha256!==expected_sha256)throw Error('CSV changed since prior inspection.');
 const decoded=decode(bytes),all=records(decoded.text.replace(/^\uFEFF/,''));
 if(!all.length)throw Error('Empty CSV');
 const columns=all.shift().cells.map(x=>x.trim().toLowerCase());
 if(new Set(columns).size!==columns.length||columns.some(x=>!x))throw Error('Duplicate or empty CSV header');
 if(!columns.includes('hand')&&!columns.includes('combo'))throw Error('Missing hand/combo column');
 if(columns.includes('hand')&&columns.includes('combo'))throw Error('Ambiguous hand and combo columns');
 const handIndex=columns.indexOf(columns.includes('hand')?'hand':'combo');
 if(!columns.includes('weight'))throw Error('Missing weight column');
 const bc=cards(board,3);
 const named=pathModule.basename(safe).match(/^((?:[2-9TJQKA][cdhs]){3})_([^_]+)_(.+)_([^_]+)\.csv$/i);
 if(named&&cards(named[1],3).join('')!==bc.join(''))throw Error('Filename board differs from requested board');
 const node=named?{player:named[2],history:named[3],action:named[4]}:null;
 const stats=Object.fromEntries(fields.map(f=>[f,{present:columns.includes(f),numeric:0,missing:0,invalid:0,min:null,max:null}]));
 const rows=[],byHand=new Map(),issues=[];let issue_count=0;
 const issue=(line,code)=>{issue_count++;if(issues.length<50)issues.push({line,code});};
 for(const r of all){
  if(r.cells.length!==columns.length){issue(r.line,'column_count');continue;}
  let hand;try{const cs=cards(r.cells[handIndex].trim(),4);hand=cs.join('');if(cs.some(c=>bc.includes(c)))issue(r.line,'board_collision');}catch{issue(r.line,'invalid_hand');continue;}
  const row={line:r.line,hand,raw_hand:r.cells[handIndex],values:{}};
  for(const f of fields){const index=columns.indexOf(f),raw=index<0?null:r.cells[index],value=number(raw),s=stats[f];row.values[f]=raw;
   if(value===null)s.missing++;else if(Number.isNaN(value)){s.invalid++;issue(r.line,'invalid_'+f);}else{s.numeric++;s.min=s.min===null?value:Math.min(s.min,value);s.max=s.max===null?value:Math.max(s.max,value);}
  }
  const w=number(row.values.weight);if(w===null||Number.isNaN(w)||w<0||w>1)issue(r.line,'weight_out_of_range');
  if(byHand.has(hand))issue(r.line,'duplicate_physical_hand');else byHand.set(hand,row);
  rows.push(row);
 }
 if(await inputFile(config,path)!==safe||await fileHash(safe)!==sha256)throw Error('CSV changed during inspection.');
 return {rows,byHand,summary:{path:safe,sha256,bytes:bytes.length,encoding:decoded.encoding,board:bc.join(''),filename_node:node,columns,row_count:all.length,physical_hand_count:byHand.size,zero_weight_rows:rows.filter(r=>number(r.values.weight)===0).length,columns_summary:stats,issue_count,issues,issues_truncated:issue_count>issues.length,ev_semantics:'unverified',ev_unit:'unknown',ev_calculation_performed:false}};
}
export async function inspectCsv(config,a){
 const {rows,summary}=await readCsv(config,a);const offset=a.offset??0,limit=a.limit??20;
 return {...summary,offset,samples:rows.slice(offset,offset+limit),sample_total:rows.length,next_offset:offset+limit<rows.length?offset+limit:null,notes:['Numeric ranges are approximate JS numbers; raw cell strings preserve source precision.','Zero-weight rows are retained. EV columns are not interpreted.']};
}
export async function compareCsv(config,a){
 const left=await readCsv(config,{path:a.left_path,board:a.board,expected_sha256:a.left_sha256});
 const right=await readCsv(config,{path:a.right_path,board:a.board,expected_sha256:a.right_sha256});
 if(left.summary.path===right.summary.path)throw Error('Select two distinct action CSVs');
 const ln=left.summary.filename_node,rn=right.summary.filename_node;
 if(ln&&rn&&(ln.player!==rn.player||ln.history!==rn.history||ln.action===rn.action))throw Error('Action CSVs must belong to the same player and decision node with distinct actions');
 if(left.summary.issue_count||right.summary.issue_count)throw Error('CSV validation issues; inspect each file before matching.');
 // Freeze both sources across the entire join, including paginated follow-ups.
 for(const data of [left,right])if(await inputFile(config,data.summary.path)!==data.summary.path||await fileHash(data.summary.path)!==data.summary.sha256)throw Error('CSV changed during matching.');
 const hands=[...new Set([...left.byHand.keys(),...right.byHand.keys()])].sort(),rows=[];
 const coverage={union_hands:hands.length,matched_hands:0,left_only:0,right_only:0,both_numeric_ev:0,zero_combined_weight:0};
 for(const hand of hands){const l=left.byHand.get(hand),r=right.byHand.get(hand);let match_status=l&&r?'matched':l?'left_only':'right_only';coverage[match_status==='matched'?'matched_hands':match_status]++;
  const both=!!(l&&r&&number(l.values.ev)!==null&&number(r.values.ev)!==null&&Number.isFinite(number(l.values.ev))&&Number.isFinite(number(r.values.ev)));if(both)coverage.both_numeric_ev++;
  if((l?number(l.values.weight):0)+(r?number(r.values.weight):0)===0)coverage.zero_combined_weight++;
  if(!a.view||a.view==='all'||a.view===match_status)rows.push({hand,match_status,both_numeric_ev:both,left:l?.values??null,right:r?.values??null});
 }
 const offset=a.offset??0,limit=a.limit??20;
 return {left:left.summary,right:right.summary,coverage,ev_semantics:'unverified',ev_unit:'unknown',ev_calculation_performed:false,total:rows.length,offset,items:rows.slice(offset,offset+limit),next_offset:offset+limit<rows.length?offset+limit:null,notes:['Pair only actions from the same player, board and decision node; filenames are not proof of EV comparability.','Missing action EV remains null, including absent zero-frequency actions. No EV difference or loss is calculated.','For stable pagination, pass both returned SHA-256 hashes on subsequent calls.']};
}
