'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),{spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'..');
async function main(){
 const destination=path.join(root,'dist/PLO-Syntax-Lab-Windows-MCP');
 const git=spawnSync('git',['rev-parse','HEAD'],{cwd:root,encoding:'utf8',timeout:1000,windowsHide:true});
 const sourceCommit=process.env.GITHUB_SHA||(git.stdout||'').trim();
 if(!/^[a-f0-9]{40}$/.test(sourceCommit))throw Error('A valid source commit is required.');
 // Copy the compiled host and matching source/SDK; callers never rebuild the native executable.
 await fs.access(path.join(root,'dist/PloMcp.Native.exe'));
 await fs.access(path.join(root,'node_modules/@modelcontextprotocol/server/package.json'));
 const files=['package.json','package-lock.json','mcp/server.mjs','mcp/service.mjs','mcp/runtime.mjs',
  'mcp/engine-worker.cjs','mcp/artifacts.mjs','mcp/NativeHost.cs','mcp/README.md',
  'mcp/config.example.json','mcp/client.example.json','tests/mcp.test.mjs',
  'src/lab/engine.js','src/defaults.js','src/default-labels.json','src/SyntaxMatcher.cs',
  'src/compare-runtime.ps1','src/CompareCommands.cs','src/Core.cs','src/Execution.cs','src/History.cs','src/LabSelection.cs','dist/PloMcp.Native.exe'];
 await fs.rm(destination,{recursive:true,force:true});
 for(const file of files){const target=path.join(destination,file);await fs.mkdir(path.dirname(target),{recursive:true});await fs.copyFile(path.join(root,file),target);}
 await fs.cp(path.join(root,'node_modules'),path.join(destination,'node_modules'),{recursive:true,filter:file=>path.basename(file)!=='.bin'});
 await fs.copyFile(path.join(root,'mcp/README.md'),path.join(destination,'README.md'));
 await fs.writeFile(path.join(destination,'mcp/build-info.json'),JSON.stringify({sourceCommit},null,2)+'\n');
 console.log(JSON.stringify({package:destination,sourceCommit,version:require('../package.json').version,tools:15,dependenciesIncluded:true,nativeHostIncluded:true,nodeRequired:'20+'}));
}
if(require.main===module)main().catch(error=>{console.error(error.message);process.exitCode=1;});
module.exports={main};
