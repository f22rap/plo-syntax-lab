using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FlopCommands {
    // Serves only the bundled generator and accepts validated condition data on loopback.
    // No file access, script execution or arbitrary RPC is exposed to the browser.
    public sealed class LabBridge:IDisposable {
        readonly TcpListener listener;readonly string html,defaultsHtml,token=Guid.NewGuid().ToString("N");readonly Func<string,string> receive;
        volatile bool disposed;public string Origin {get;private set;}public string Url {get{return Origin+"/"+token+"/";}}
        public LabBridge(string page,Func<string,string> handler,string defaultsPage=null){html=page;defaultsHtml=defaultsPage;receive=handler;listener=new TcpListener(IPAddress.Loopback,0);listener.Start();Origin="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;Task.Run((Func<Task>)Accept);}
        async Task Accept(){while(!disposed){try{var client=await listener.AcceptTcpClientAsync().ConfigureAwait(false);Task ignored=Task.Run(()=>Handle(client));}catch(ObjectDisposedException){break;}catch(SocketException){if(disposed)break;}}}
        void Handle(TcpClient client){using(client){try{
            client.ReceiveTimeout=10000;client.SendTimeout=10000;var stream=client.GetStream();var header=new List<byte>();int b;
            while((b=stream.ReadByte())>=0){header.Add((byte)b);int n=header.Count;if(n>16384)throw new Exception("Header too large");if(n>=4&&header[n-4]==13&&header[n-3]==10&&header[n-2]==13&&header[n-1]==10)break;}
            string[] lines=Encoding.ASCII.GetString(header.ToArray()).Split(new[]{"\r\n"},StringSplitOptions.None);string[] request=lines[0].Split(' ');if(request.Length!=3){Reply(stream,400,"Bad request");return;}
            var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);for(int i=1;i<lines.Length;i++){int colon=lines[i].IndexOf(':');if(colon>0){string key=lines[i].Substring(0,colon);if(headers.ContainsKey(key)){Reply(stream,400,"Duplicate header");return;}headers[key]=lines[i].Substring(colon+1).Trim();}}
            string lengthText;int length=0;if(headers.ContainsKey("Transfer-Encoding")||(headers.TryGetValue("Content-Length",out lengthText)&&(!int.TryParse(lengthText,out length)||length<0||length>8*1024*1024))){Reply(stream,400,"Invalid length");return;}
            byte[] body=new byte[length];int offset=0;while(offset<length){int n=stream.Read(body,offset,length-offset);if(n==0)throw new EndOfStreamException();offset+=n;}
            string host;if(!headers.TryGetValue("Host",out host)||"http://"+host!=Origin){Reply(stream,403,"Forbidden");return;}
            string path=request[1].Split('?')[0];
            if(request[0]=="GET"&&path=="/"+token+"/"){Reply(stream,200,html,"text/html; charset=utf-8");return;}
            if(request[0]=="GET"&&path=="/"+token+"/defaults"&&defaultsHtml!=null){Reply(stream,200,defaultsHtml,"text/html; charset=utf-8");return;}
            string origin;if(request[0]!="POST"||path!="/"+token+"/selection"||!headers.TryGetValue("Origin",out origin)||origin!=Origin){Reply(stream,403,"Forbidden");return;}
            string contentType;if(length<1||!headers.TryGetValue("Content-Type",out contentType)||!contentType.StartsWith("application/json",StringComparison.OrdinalIgnoreCase)){Reply(stream,400,"Invalid payload");return;}
            string json=new UTF8Encoding(false,true).GetString(body);LabSelection.Read(json);
            string message=receive(json);Reply(stream,200,new JavaScriptSerializer().Serialize(new{message=message}));
        }catch(Exception ex){try{Reply(client.GetStream(),400,new JavaScriptSerializer().Serialize(new{error=ex.Message}));}catch{}}}}
        static void Reply(Stream stream,int status,string body,string type="application/json; charset=utf-8"){
            byte[] bytes=Encoding.UTF8.GetBytes(body);string header="HTTP/1.1 "+status+(status==200?" OK":" Error")+"\r\nContent-Type: "+type+"\r\nContent-Length: "+bytes.Length+"\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'self' data: blob:; script-src 'self' 'unsafe-inline' blob:; style-src 'self' 'unsafe-inline'; worker-src blob:; connect-src 'self'; frame-ancestors 'none'\r\n\r\n";
            byte[] head=Encoding.ASCII.GetBytes(header);stream.Write(head,0,head.Length);stream.Write(bytes,0,bytes.Length);
        }
        public void Dispose(){disposed=true;listener.Stop();}
    }
}
