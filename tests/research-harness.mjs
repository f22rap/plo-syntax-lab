// Linux verification only: production MCP schemas and service, same C# source
// compiled for .NET 8. Windows integration uses mcp/server.mjs and native EXEs.
import {serveStdio} from '@modelcontextprotocol/server/stdio';
import {loadConfig} from '../mcp/runtime.mjs';
import {Service} from '../mcp/service.mjs';
import {ResearchService} from '../mcp/research-service.mjs';
import {researchNative} from '../mcp/research-runtime.mjs';
import {buildServer} from '../mcp/server-core.mjs';
const config=await loadConfig(process.argv[2]),legacy=new Service(config);
const research=new ResearchService(config,legacy,{native:(q,o)=>researchNative(q,o,{command:process.env.PLO_TEST_DOTNET,args:[process.env.PLO_TEST_RESEARCH_HOST]})});
const handle=serveStdio(()=>buildServer(legacy,research),{onerror:e=>console.error(e.message)});
let closing=false;
async function close(){if(closing)return;closing=true;await Promise.all([legacy.close(),research.close()]);await handle.close();}
process.stdin.on('end',()=>{void close();});for(const s of ['SIGINT','SIGTERM'])process.on(s,()=>{void close();});
