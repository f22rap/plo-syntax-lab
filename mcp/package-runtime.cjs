'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),{spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'..');
async function main(){
 const destination=process.env.PLO_MCP_PACKAGE_DIR?path.resolve(process.env.PLO_MCP_PACKAGE_DIR):path.join(root,'dist/PLO-Syntax-Lab-Windows-MCP');
 const git=spawnSync('git',['rev-parse','HEAD'],{cwd:root,encoding:'utf8',timeout:1000,windowsHide:true});
 const sourceCommit=process.env.GITHUB_SHA||(git.stdout||'').trim();
 if(!/^[a-f0-9]{40}$/.test(sourceCommit))throw Error('A valid source commit is required.');
 // Copy the compiled host and matching source/SDK; callers never rebuild the native executable.
 await fs.access(path.join(root,'dist/PloMcp.Native.exe'));
 await fs.access(path.join(root,'node_modules/@modelcontextprotocol/server/package.json'));
 const files=['Start-App.cmd','Check-MCP.cmd','docs/windows-generated-preview.txt','mcp/board-filter.mjs','mcp/research-directory.mjs','tests/research-directory.test.mjs','tests/directory-fixture.mjs','docs/directory-import.md','Setup-Codex.cmd','Setup-Codex.ps1','mcp/codex-setup.mjs','mcp/check-connection.mjs','docs/codex-setup.md','docs/chatgpt-browser-setup.md','tests/codex-setup.test.mjs','package.json','package-lock.json','mcp/server.mjs','mcp/service.mjs','mcp/runtime.mjs',
  'mcp/server-core.mjs','mcp/research-tools.mjs','mcp/recipe-schema.mjs','mcp/research-service.mjs','mcp/research-runtime.mjs','mcp/ResearchHost.cs','mcp/research-workflow.md','docs/research-mcp.md','docs/research-setup.md','docs/research-run.md','docs/research-settings-example.json','tests/research-mcp.test.mjs','tests/research-harness.mjs','tests/research/BridgeTests.cs',
  'src/ResearchData.cs','src/ResearchRecipes.cs','src/ResearchGeneration.cs','src/ResearchRun.cs','src/ResearchFiles.cs','src/EquityEngine.cs','src/EquityInput.cs','src/EquityJob.cs','src/EquityPlot.cs','src/EquityArchive.cs','dist/PloResearch.Native.exe','dist/FlopCommandApp.exe','dist/PLO-Syntax-Lab.html',
  'mcp/engine-worker.cjs','mcp/artifacts.mjs','mcp/NativeHost.cs','mcp/README.md',
  'mcp/config.example.json','mcp/client.example.json','tests/mcp.test.mjs',
  'src/lab/engine.js','src/defaults.js','src/default-labels.json','src/SyntaxMatcher.cs',
  'src/default-presets.json','src/research-recipes.cjs','src/research-generation.cjs',
  'mcp/generated-conditions.mjs','mcp/generated-condition-worker.cjs','mcp/preview-generated-conditions.mjs',
  'tests/generated-conditions.test.mjs','tests/generated-plan-guards.test.mjs','tests/generated-research.test.mjs','docs/generated-conditions-research.md','docs/generated-conditions-preview.md','docs/generated-conditions-selection.md',
  'docs/generated-conditions-design.md','docs/generated-condition.schema.json','docs/generated-condition.examples.json',
  'src/compare-runtime.ps1','src/CompareCommands.cs','src/Core.cs','src/Execution.cs','src/History.cs','src/LabSelection.cs','dist/PloMcp.Native.exe'];
 // Check every input before creating a package. Never replace an earlier build.
 for(const file of files)await fs.access(path.join(root,file));
 await fs.mkdir(destination,{recursive:false});
 for(const file of files){const target=path.join(destination,file);await fs.mkdir(path.dirname(target),{recursive:true});await fs.copyFile(path.join(root,file),target);}
 await fs.cp(path.join(root,'node_modules'),path.join(destination,'node_modules'),{recursive:true,filter:file=>path.basename(file)!=='.bin'});
 await fs.copyFile(path.join(root,'mcp/README.md'),path.join(destination,'README.md'));
 await fs.copyFile(path.join(root,'docs/windows-generated-preview.txt'),path.join(destination,'README-FIRST.txt'));
 const {createHash}=require('node:crypto');
 const fileHashes={};for(const file of files)fileHashes[file]=createHash('sha256').update(await fs.readFile(path.join(root,file))).digest('hex');
 await fs.writeFile(path.join(destination,'mcp/build-info.json'),JSON.stringify({sourceCommit,workingTreePreview:process.env.PLO_MCP_PREVIEW==='1',fileHashes},null,2)+'\n');
 console.log(JSON.stringify({package:destination,sourceCommit,version:require('../package.json').version,tools:31,dependenciesIncluded:true,nativeHostIncluded:true,nodeRequired:'20+'}));
}
if(require.main===module)main().catch(error=>{console.error(error.message);process.exitCode=1;});
module.exports={main};
