import {serveStdio} from '@modelcontextprotocol/server/stdio';
import {loadConfig} from './runtime.mjs';
import {Service} from './service.mjs';
import {ResearchService} from './research-service.mjs';
import {buildServer} from './server-core.mjs';
const cli=process.argv.slice(2);
if(cli.length!==2||cli[0]!=='--config'){console.error('Usage: node mcp/server.mjs --config <mcp.local.json>');process.exit(1);}
if(process.platform!=='win32'){console.error('This MCP requires Windows. Research uses the Windows application calculation engine.');process.exit(1);}
const service=await new Service(await loadConfig(cli[1])).init();
const research=new ResearchService(service.config,service);
const handle=serveStdio(()=>buildServer(service,research),{onerror:e=>console.error(e.message)});
let closing=false;
async function close(){if(closing)return;closing=true;await Promise.all([service.close(),research.close()]);await handle.close();}
process.stdin.on('end',()=>{void close();});
for(const event of ['SIGINT','SIGTERM'])process.on(event,()=>{void close();});
