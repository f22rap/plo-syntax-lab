'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),{spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'../..');
async function main(){
 const destination=path.join(root,'dist/PLO-Syntax-Lab-MCP');
 const git=spawnSync('git',['rev-parse','HEAD'],{cwd:root,encoding:'utf8',timeout:1000,windowsHide:true});
 const sourceCommit=process.env.GITHUB_SHA||(git.stdout||'').trim();
 if(!/^[a-f0-9]{40}$/.test(sourceCommit))throw Error('A valid source commit is required.');
 // Dependencies and their license files travel with the runtime. Node.js remains a prerequisite.
 await fs.access(path.join(__dirname,'node_modules/@modelcontextprotocol/sdk/package.json'));
 await fs.rm(destination,{recursive:true,force:true});
 const files=['src/lab/engine.js','src/defaults.js','src/default-labels.json','src/linux/compare.cjs',
  'src/mcp/contracts.cjs','src/mcp/compute.cjs','src/mcp/domain.cjs','src/mcp/service.cjs','src/mcp/worker.cjs','src/mcp/stdio.cjs',
  'src/mcp/package.json','src/mcp/package-lock.json','tests/mcp-tests.cjs',
  'docs/mcp-tools.schema.json','docs/mcp-api-spec.md','docs/mcp-usage.md','docs/mcp-config.example.json'];
 for(const file of files){const target=path.join(destination,file);await fs.mkdir(path.dirname(target),{recursive:true});await fs.copyFile(path.join(root,file),target);}
 await fs.cp(path.join(__dirname,'node_modules'),path.join(destination,'src/mcp/node_modules'),{recursive:true,filter:file=>path.basename(file)!=='.bin'});
 await fs.copyFile(path.join(root,'docs/mcp-usage.md'),path.join(destination,'README.md'));
 await fs.writeFile(path.join(destination,'src/mcp/build-info.json'),JSON.stringify({sourceCommit},null,2)+'\n');
 console.log(JSON.stringify({package:destination,sourceCommit,dependenciesIncluded:true,nodeRequired:'24+'}));
}
if(require.main===module)main().catch(error=>{console.error(error.message);process.exitCode=1;});
module.exports={main};
