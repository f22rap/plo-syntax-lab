import {build} from 'esbuild';
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';
const folder=path.dirname(fileURLToPath(import.meta.url)),root=path.resolve(folder,'..');
const git=spawnSync('git',['rev-parse','HEAD'],{cwd:root,encoding:'utf8',timeout:1000});
let provenance;try{provenance=JSON.parse(await fs.readFile(path.join(folder,'source-info.json'),'utf8'));}catch{}
const source=provenance?.commit||process.env.GITHUB_SHA||(git.stdout||'').trim();
const metadata=path.join(root,'.openai/hosting.json');
let hosting={d1:'DB',r2:'FILES',capabilities:['mcp']};
try{hosting=JSON.parse(await fs.readFile(metadata,'utf8'));}catch{}
const output=path.join(root,hosting.project_id?'dist':'dist/cloud');
await fs.mkdir(path.join(output,'server'),{recursive:true});
await build({entryPoints:[path.join(folder,'worker.mjs')],outfile:path.join(output,'server/index.js'),
 bundle:true,format:'esm',platform:'browser',target:'es2022',minify:true,
 nodePaths:[path.join(folder,'node_modules'),path.join(root,'node_modules')],
 define:{PLO_SOURCE_COMMIT:/^[a-f0-9]{40}$/.test(source)?JSON.stringify(source):'null'}});
await fs.mkdir(path.join(output,'.openai'),{recursive:true});
await fs.writeFile(path.join(output,'.openai/hosting.json'),JSON.stringify(hosting,null,2)+'\n');
if(hosting.project_id)await fs.cp(path.join(folder,'drizzle'),path.join(root,'drizzle'),{recursive:true});
else await fs.cp(path.join(folder,'drizzle'),path.join(output,'drizzle'),{recursive:true});
console.log(JSON.stringify({worker:path.join(output,'server/index.js'),sourceCommit:source||null}));
