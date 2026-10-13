import {resolveBoardRank,matchesBoardRank} from './board-filter.mjs';
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileHash,hash,within,inputFile,checkAbort} from './runtime.mjs';

const boardPattern=/^(?:[2-9TJQKA][shdc]){3}$/;
const tokenPattern=/^(?:C|CALL|F|A|R(?:[1-9]\d*)(?:\.\d+)?)$/;
const id=s=>hash(s).slice(0,24);
const source=f=>({path:f.path,sha256:f.sha256});
const tokens=h=>h==='ROOT'?[]:h.split('-');
const boardKey=b=>b.match(/../g).sort().join('');
export const directoryProfile='plo-post-tree-v1';

// Do not follow links, including linked ancestors. Native validation repeats this check.
async function plain(file){for(let p=path.resolve(file);;p=path.dirname(p)){if((await fs.lstat(p)).isSymbolicLink())throw Error('Linked paths are not allowed: '+p);if(path.dirname(p)===p)break;}}
export async function scanDirectory(config,{directory,highest_rank,ahi_only}, {signal,progress=()=>{}}={}){
 const selectedRank=resolveBoardRank(highest_rank,ahi_only);
 if(!path.isAbsolute(directory))throw Error('Use an absolute directory.');
 if(!config.inputRoots.some(r=>within(r,path.resolve(directory))))throw Error('Directory is outside configured inputRoots.');
 await plain(directory);const root=await fs.realpath(directory);
 if(!config.inputRoots.some(r=>within(r,root)))throw Error('Directory is outside configured inputRoots. Register it using Setup-Codex or mcp.local.json first.');
 if(!(await fs.stat(root)).isDirectory())throw Error('Not a directory.');
 const issues=[],files=[],boardDirs=[],ignoredBoards=[];let visited=0;
 const issue=(p,reason)=>issues.push({path:p,reason});
 const children=async p=>{checkAbort(signal);await plain(p);const rows=await fs.readdir(p,{withFileTypes:true});visited+=rows.length;if(visited>100000)throw Error('Directory exceeds 100,000 entries; select a smaller root.');return rows.sort((a,b)=>a.name.localeCompare(b.name,'en'));};
 for(const entry of await children(root)){
  const dir=path.join(root,entry.name);
  if(entry.isSymbolicLink()){issue(dir,'linked_path');continue;}
  if(!entry.isDirectory())continue;
  if(!boardPattern.test(entry.name)||new Set(entry.name.match(/../g)).size!==3){issue(dir,'not_a_physical_flop_directory');continue;}
  if(!matchesBoardRank(entry.name,selectedRank)){ignoredBoards.push(entry.name);continue;}
  boardDirs.push({board:entry.name,path:dir});
  for(const side of await children(dir)){
   const sp=path.join(dir,side.name);
   if(!side.isDirectory()||!['IP','OOP'].includes(side.name)){issue(sp,'expected_IP_or_OOP_directory');continue;}
   for(const node of await children(sp)){
    const np=path.join(sp,node.name);
    if(!node.isDirectory()){issue(np,'expected_node_directory');continue;}
    const entries=await children(np);if(!entries.length)issue(np,'empty_node_directory');
    for(const f of entries){
     const full=path.join(np,f.name);
     if(!f.isFile()||!f.name.toLowerCase().endsWith('.csv')){issue(full,'expected_plain_csv');continue;}
     const parts=f.name.slice(0,-4).split('_');
     if(parts.length!==4||parts[0]!==entry.name||!/^\w{1,30}$/.test(parts[1])||!tokenPattern.test(parts[3])||!(parts[2]==='ROOT'||tokens(parts[2]).every(t=>tokenPattern.test(t)))){issue(full,'filename_does_not_match_board_position_history_action');continue;}
     try{await plain(full);const canonical=await inputFile(config,full);files.push({board:entry.name,side:side.name,folder:node.name,position:parts[1],history:parts[2],token:parts[3],path:canonical,sha256:await fileHash(canonical)});}catch(e){issue(full,e.message);}
    }
   }
  }
  if(!files.some(f=>f.board===entry.name))issue(dir,'no_recognized_csv_for_board');
  progress({message:'Scanning '+entry.name,boards:boardDirs.length,files:files.length});
 }
 const groups=new Map();
 for(const f of files){const key=[f.board,f.side,f.folder].join('/');if(!groups.has(key))groups.set(key,{board:f.board,side:f.side,folder:f.folder,files:[],problems:[]});groups.get(key).files.push(f);}
 const physicalCounts=new Map();for(const b of boardDirs){const key=boardKey(b.board);physicalCounts.set(key,(physicalCounts.get(key)||0)+1);}
 const duplicateBoards=new Set(boardDirs.filter(b=>physicalCounts.get(boardKey(b.board))>1).map(b=>b.board));
 const issueParents=new Set(issues.map(x=>path.dirname(x.path)));
 const positions={IP:new Set(),OOP:new Set()};for(const f of files)positions[f.side].add(f.position);
 for(const g of groups.values()){
  const f=g.files[0];g.position=f.position;g.history=f.history;g.node_id=id([f.side,f.folder,f.position,f.history].join('/'));
  if(g.files.some(x=>x.position!==f.position||x.history!==f.history))g.problems.push('mixed_position_or_history_in_node');
  if(positions[g.side].size!==1||positions[g.side==='IP'?'OOP':'IP'].size!==1||positions.IP.has([...positions.OOP][0]))g.problems.push('ambiguous_positions');
  if(duplicateBoards.has(g.board))g.problems.push('duplicate_physical_board');
  if(g.files.length>3)g.problems.push('more_than_3_actions');
  if(new Set(g.files.map(f=>f.token)).size!==g.files.length)g.problems.push('duplicate_action');
  const hist=tokens(g.history),expected=hist.length%2===0?'OOP':'IP';
  if(g.side!==expected)g.problems.push('history_actor_mismatch');
  if(hist.some(t=>t==='F'||t==='CALL')||hist.includes('A')&&hist[hist.length-1]!=='A'||hist.slice(1).some(t=>t==='C'))g.problems.push('terminal_or_invalid_history');
  const facing=hist.some(t=>t==='A'||t.startsWith('R'));
  if(g.files.some(f=>f.token==='C'&&facing||['CALL','F'].includes(f.token)&&!facing))g.problems.push('action_incompatible_with_history');
  // A malformed sibling must not silently disappear from the frequency denominator.
  if(issueParents.has(path.dirname(f.path)))g.problems.push('unreadable_or_unknown_sibling');
  g.hero_actions=g.files.map(f=>{const kind=f.token==='C'?'check':f.token==='CALL'?'call':f.token==='F'?'fold':facing?'raise':'bet';return {action_id:id(f.path),label:f.token==='A'?'All-in':f.token==='C'?'Check':f.token==='CALL'?'Call':f.token==='F'?'Fold':(facing?'Raise ':'Bet ')+f.token.slice(1)+'% pot',kind,pot_fraction:f.token==='A'?'allin':f.token.startsWith('R')?String(Number(f.token.slice(1))/100):'',source:source(f)};});
 }
 const spots=[],historyIndex=new Map();
 for(const g of groups.values()){const key=[g.board,g.side,g.history].join('/');if(!historyIndex.has(key))historyIndex.set(key,[]);historyIndex.get(key).push(g);}
 for(const g of groups.values()){
  const hist=tokens(g.history),previous=hist.length===1?'ROOT':hist.slice(0,-1).join('-');
  const candidates=(historyIndex.get([g.board,g.side==='IP'?'OOP':'IP',previous].join('/'))||[]).filter(x=>x.files.some(f=>f.token===hist[hist.length-1]));
  const opponent=hist.length&&candidates.length===1&&candidates[0].problems.length===0?candidates[0]:null;
  const missing=!opponent?(hist.length?'opponent_pre_action_node_missing_or_ambiguous':'opponent_root_range_not_exported'):null;
  const villain=[...positions[g.side==='IP'?'OOP':'IP']];
  spots.push({node_id:g.node_id,board:g.board,side:g.side,folder:g.folder,history:g.history,position:g.position,villain_position:villain.length===1?villain[0]:null,problems:g.problems,eq_status:missing||'ready',opponent_node:opponent?[opponent.side,opponent.folder,opponent.history].join('/'):null,spot:{spot_id:id(root+'/'+g.board+'/'+g.node_id),board:g.board,node:[g.side,g.folder,g.history].join('/'),hero_actions:g.hero_actions,opponent_sources:opponent?opponent.files.map(source):[],opponent_percent_unit:false,all_actions_common_scale:true,opponent_common_scale:!!opponent}});
 }
 const nodes=[...new Set(spots.map(s=>s.node_id))].map(key=>{const rows=spots.filter(s=>s.node_id===key),s=rows[0];return {node_id:key,side:s.side,folder:s.folder,history:s.history,position:s.position,villain_position:s.villain_position,boards:rows.length,ready:rows.filter(s=>!s.problems.length&&s.eq_status==='ready').length,blocked:rows.filter(s=>s.problems.length).length,missing_opponent:rows.filter(s=>!s.problems.length&&s.eq_status!=='ready').length};});
 const identity={root,highest_rank:selectedRank,files:files.map(f=>({path:f.path,sha256:f.sha256})),issues,boards:boardDirs};
 return {profile:directoryProfile,directory:root,highest_rank:selectedRank,fingerprint:hash(JSON.stringify(identity)),board_count:boardDirs.length,excluded_board_count:ignoredBoards.length,file_count:files.length,nodes,spots,issues};
}

export function directoryCatalog(scan,a){
 if(a.confirm_profile!==directoryProfile||!a.all_actions_common_scale)throw Error('Confirm the filename profile and complete common-scale action weights.');
 const rows=scan.spots.filter(s=>s.node_id===a.node_id&&(!a.boards?.length||a.boards.includes(s.board)));
 if(!rows.length||rows.length>200)throw Error('Select 1–200 boards for this node.');
 if(a.boards?.length&&(new Set(a.boards).size!==a.boards.length||rows.length!==a.boards.length))throw Error('Unknown or duplicate boards.');
 const blocked=rows.filter(s=>s.problems.length||!a.allow_frequency_only&&s.eq_status!=='ready');
 if(blocked.length)throw Error('Resolve or explicitly select around blocked boards: '+blocked.map(s=>s.board+': '+[...s.problems,...(s.eq_status==='ready'?[]:[s.eq_status])].join(',')).join('; '));
 if(rows.some(s=>s.position!==a.scenario.hero_position||s.villain_position!==a.scenario.villain_position))throw Error('Scenario positions do not match the CSVs.');
 return {catalog_id:a.catalog_id,revision:0,name:a.name,highest_rank:scan.highest_rank,scenario:{...a.scenario,equity_reference:'Hero: sum of all actions at '+rows[0].spot.node+'; opponent: sum of all actions BEFORE their immediately preceding action. Prior hero actions remain conditioned. Root opponent range is not inferred.'},spots:rows.map(s=>structuredClone(s.spot))};
}
