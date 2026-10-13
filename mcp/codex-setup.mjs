import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {root,loadConfig} from './runtime.mjs';

export const serverName='plo-syntax-lab-windows';
export const starterPrompt=`PLO Syntax LabのMCPを使い、登録済みAhiボードのベット戦略をボードの特徴ごとに調査してください。
まずresearch_list_workspaceで私の条件セットとカタログを確認してください。候補が複数あって特定できない場合だけ尋ねてください。
ユーザー設定のsyntaxを変更せず、双方のアクション前レンジによるEQと全アクション頻度を比較してください。
スート構成とペアの有無で分類し、ボード別・特徴別の表、色分けグラフ、対象数、欠損理由を示してください。
条件定義やCSV登録の追加・変更は勝手に行わず、未登録ボードの戦略は推測しないでください。
MCPの検証→実行→結果取得を通して、CSV・JSON・SVG・Markdownを保存し、数値の根拠とresult_idを提示してください。
`;

export function codexFragment(node,server,config,cwd){
 const q=x=>JSON.stringify(x);
 return `[mcp_servers.${serverName}]\ncommand = ${q(node)}\nargs = [${q(server)}, "--config", ${q(config)}]\ncwd = ${q(cwd)}\nstartup_timeout_sec = 60\ntool_timeout_sec = 3600\n`;
}
export async function prepare({directory=root,inputRoots=[],node=process.execPath}={}){
 const configFile=path.join(directory,'mcp.local.json');let created=false;
 try{await fs.access(configFile);if(inputRoots.length)throw Error('mcp.local.json already exists. Edit that file explicitly to change CSV roots; setup will not replace it.');}
 catch(e){if(e.code!=='ENOENT')throw e;if(!inputRoots.length)throw Error('Choose at least one CSV folder.');
  const roots=await Promise.all(inputRoots.map(async p=>{const full=await fs.realpath(p);if(!(await fs.stat(full)).isDirectory())throw Error('CSV root is not a folder.');return full;}));
  const config={inputRoots:[...new Set(roots)],outputRoot:path.join(directory,'research-output'),allowHistoryDelete:false,maxCsvBytes:268435456,timeoutSeconds:300};
  await fs.writeFile(configFile,JSON.stringify(config,null,2)+'\n',{flag:'wx'});created=true;
 }
 const config=await loadConfig(configFile),server=path.join(directory,'mcp/server.mjs');
 const fragment=codexFragment(node,server,configFile,directory);
 // Generated setup helpers contain local paths, never copy them into a release.
 await fs.writeFile(path.join(directory,'codex-mcp.toml'),fragment);
 await fs.writeFile(path.join(directory,'research-prompt.txt'),starterPrompt);
 return {server_name:serverName,config_file:configFile,created,command:node,args:[server,'--config',configFile],fragment_file:path.join(directory,'codex-mcp.toml'),prompt_file:path.join(directory,'research-prompt.txt'),input_roots:config.inputRoots,history_root:config.historyRoot,output_root:config.outputRoot};
}
if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
 try{if(process.platform!=='win32')throw Error('Run this setup on the Windows PC that holds your PLO CSV files.');if(Number(process.versions.node.split('.')[0])<20)throw Error('Node.js 20+ is required.');const roots=process.argv.slice(2);console.log(JSON.stringify(await prepare({inputRoots:roots}),null,2));}catch(e){console.error(e.message);process.exitCode=1;}
}
