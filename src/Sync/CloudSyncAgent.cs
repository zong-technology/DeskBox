using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DeskBox.Core;
using DeskBox.Ai;
using System.Threading;

namespace DeskBox.Sync
{
    public class CloudCredential
    {
        public string BaseUrl = "", DeviceId = "", Token = "", GateToken = "", Role = "";
        public long ExpiresAt;
        public bool Valid(string role)
        {
            Uri uri;
            return Uri.TryCreate(BaseUrl, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo)
                && !string.IsNullOrWhiteSpace(DeviceId) && !string.IsNullOrEmpty(Token) && Token.Length == 64 && Token.All(Uri.IsHexDigit) && !string.IsNullOrWhiteSpace(GateToken) && !GateToken.Any(char.IsControl) && Role == role && ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    public sealed class CloudSyncAgent : IDisposable
    {
        public string AccountUrl { get; private set; }
        private readonly LocalSyncCoordinator _local;
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 15000 };
        private CloudCredential _credential;
        private readonly Dictionary<string,long> _uploadedVersions = new Dictionary<string,long>();
        private bool _busy, _disposed;
        private string CredentialPath { get { return Path.Combine(AppPaths.DataDir, "cloud-login.enc"); } }
        public bool LoggedIn { get { return _credential != null; } }
        public bool Connected { get; private set; }
        public DateTime? LastSyncedAt { get; private set; }
        public int PendingUploads { get { return _local.Sync.Records.Count(record=>{long version;return !_uploadedVersions.TryGetValue(record.EntityType+":"+record.Id,out version)||version!=record.Version;}); } }
        public string Status { get; private set; }
        public event Action Changed;
        public MobileAiRunner MobileAi;
        private bool _aiBusy;
        private CancellationTokenSource _aiCancellation;

        public CloudSyncAgent(LocalSyncCoordinator local)
        {
            _local = local; Status = "尚未登录云同步";
            AccountUrl = Json.Load<string>(Path.Combine(AppPaths.DataDir,"cloud-service-url.json"), "");
            if (File.Exists(CredentialPath)) try
            {
                var encrypted = Convert.FromBase64String(Json.Load<string>(CredentialPath, ""));
                var text = Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
                var credential = Json.FromJson<CloudCredential>(text);
                if (credential != null && credential.Valid("desktop")) { _credential = credential; AccountUrl=credential.BaseUrl; Status = "云账号已保存，等待同步"; }
                else Status = "云登录已过期，请重新授权电脑";
            } catch { Status = "云凭证无法解密，请重新授权；本地资料保留"; }
            _timer.Tick += async delegate { await SyncNow(); }; _timer.Start();
        }

        public bool Import(string text)
        {
            if (_busy||_aiBusy) { Status = "正在同步或处理手机AI，请完成或取消后再切换账号"; Raise(); return false; }
            try
            {
                var credential = Json.FromJson<CloudCredential>(text);
                if (credential == null || !credential.Valid("desktop")) throw new InvalidDataException();
                var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(Json.ToJson(credential)), null, DataProtectionScope.CurrentUser);
                var temporary=CredentialPath+".tmp";
                File.WriteAllText(temporary,Json.ToJson(Convert.ToBase64String(encrypted)),new UTF8Encoding(false));
                if(File.Exists(CredentialPath))File.Replace(temporary,CredentialPath,null);else File.Move(temporary,CredentialPath);
                Connected=false;
                _credential = credential; _uploadedVersions.Clear(); AccountUrl = credential.BaseUrl; Status = "云账号已连接，将自动同步"; Raise(); return true;
            }
            catch { Status = "导入失败：请使用网页登录生成的 Windows 电脑凭证；旧配置和本地资料保留"; Raise(); return false; }
        }
        public bool SetAccountUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate((value??"").Trim(),UriKind.Absolute,out uri) || uri.Scheme!="https" || uri.AbsolutePath!="/" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
            try { Json.Save(Path.Combine(AppPaths.DataDir,"cloud-service-url.json"),uri.GetLeftPart(UriPartial.Authority)); AccountUrl=uri.GetLeftPart(UriPartial.Authority); return true; }
            catch { Status="云服务地址保存失败，原配置保留"; Raise(); return false; }
        }

        public void Logout()
        {
            if (_busy||_aiBusy) { Status="请先完成或取消手机AI，再退出账号";Raise();return; }
            if (File.Exists(CredentialPath)) File.Delete(CredentialPath);
            _credential = null; Connected=false;_uploadedVersions.Clear(); Status = "已退出云同步，本地资料保留；可在网页撤销设备"; Raise();
        }

        public async Task SyncNow()
        {
            if(AiEnvironment.Local){Connected=false;Status="本地隔离 · 已暂停设备同步";Raise();return;}
            if (_busy || _disposed || _credential == null) return;
            _busy = true; Status = "云同步中…"; Raise();
            try
            {
                _local.PublishWindowsSnapshot();
                var response = await UploadSnapshots();
                var results = new List<CloudResult>();
                foreach (var pending in response.Pending)
                {
                    var result = _local.MergeFromCloud(pending.Record);
                    results.Add(new CloudResult { Id = pending.Id, Result = result.ToString() });
                }
                _local.PublishWindowsSnapshot(); await UploadSnapshots();
                if (results.Count > 0) await Exchange(new List<SyncRecord>(), results);
                Connected=true;LastSyncedAt=DateTime.Now;
                PollMobileAi();
                Status = "CLOUD ONLINE · " + DateTime.Now.ToString("HH:mm:ss") + " · 处理手机提交 " + results.Count;
            }
            catch (WebException ex)
            {
                Connected=false;
                var response = ex.Response as HttpWebResponse;
                Status = response != null && (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden) ? "云授权已失效，请重新登录授权；本地内容保留" : "云连接暂不可用，15秒后重试；本地内容保留";
            }
            catch { Connected=false;Status = "云同步未完成，将自动重试；本地内容保留"; }
            finally { _busy = false; Raise(); }
        }

        private async Task<ExchangeResponse> UploadSnapshots()
        {
            var changed = _local.Sync.Records.Where(record => { long version; return !_uploadedVersions.TryGetValue(record.EntityType+":"+record.Id,out version) || version!=record.Version; }).ToList();
            ExchangeResponse response = null;
            for (int offset=0;offset<Math.Max(1,changed.Count);offset+=500)
            {
                var batch=changed.Skip(offset).Take(500).ToList();
                response=await Exchange(batch,new List<CloudResult>());
                foreach(var record in batch) _uploadedVersions[record.EntityType+":"+record.Id]=record.Version;
            }
            return response;
        }
        private async void PollMobileAi()
        {
            if(AiEnvironment.Local)return;
            if(_aiBusy||_disposed||MobileAi==null||_credential==null)return;_aiBusy=true;
            try
            {
                var response=await AiExchange(true,new List<MobileAiProgress>());if(response.Job==null)return;
                using(var cancellation=CancellationTokenSource.CreateLinkedTokenSource(AiEnvironment.Token))
                {
                    _aiCancellation=cancellation;var gate=new object();var state=new MobileAiProgress{Id=response.Job.id};
                    var messages=Json.FromJson<List<AiMessage>>(response.Job.messages);if(messages==null)throw new InvalidDataException();
                    var work=MobileAi.Run(state.Id,messages,cancellation.Token,update=>{lock(gate)state=new MobileAiProgress{Id=update.Id,Text=update.Text,Stage=update.Stage,Status=update.Status,Error=update.Error,Provider=update.Provider,Model=update.Model};});
                    while(!work.IsCompleted&&!_disposed)
                    {
                        await Task.Delay(650);MobileAiProgress snapshot;lock(gate)snapshot=state;
                        response=await AiExchange(false,new List<MobileAiProgress>{snapshot});if(response.Cancelled.Contains(state.Id))cancellation.Cancel();
                    }
                    if(_disposed)cancellation.Cancel();
                    var final=await work;if(!_disposed)await AiExchange(false,new List<MobileAiProgress>{final});
                }
            }
            catch{if(_aiCancellation!=null)try{_aiCancellation.Cancel();}catch{}}
            finally{_aiCancellation=null;_aiBusy=false;}
        }
        private async Task<AiExchangeResponse> AiExchange(bool claim,List<MobileAiProgress> updates)
        {
            using(var client=new CloudWebClient())
            {
                client.Encoding=Encoding.UTF8;client.Headers[HttpRequestHeader.ContentType]="application/json";client.Headers[HttpRequestHeader.Authorization]="Bearer "+_credential.Token;client.Headers["X-Device-Id"]=_credential.DeviceId;client.Headers["OAI-Sites-Authorization"]="Bearer "+_credential.GateToken;
                var operation=client.UploadStringTaskAsync(new Uri(_credential.BaseUrl.TrimEnd('/')+"/api/cloud/ai-exchange"),"POST",Json.ToJson(new{Claim=claim,Updates=updates}));if(await Task.WhenAny(operation,Task.Delay(20000))!=operation)client.CancelAsync();var text=await operation;return Json.FromJson<AiExchangeResponse>(text)??new AiExchangeResponse();
            }
        }
        private class AiExchangeResponse{public AiJob Job=null;public List<string> Cancelled=new List<string>();}
        private class AiJob{public string id="",messages="";}

        private async Task<ExchangeResponse> Exchange(List<SyncRecord> records, List<CloudResult> results)
        {
            using (var client = new CloudWebClient())
            {
                client.Encoding = Encoding.UTF8;
                client.Headers[HttpRequestHeader.ContentType] = "application/json";
                client.Headers[HttpRequestHeader.Authorization] = "Bearer " + _credential.Token;
                client.Headers["X-Device-Id"] = _credential.DeviceId;
                client.Headers["OAI-Sites-Authorization"] = "Bearer " + _credential.GateToken;
                var operation = client.UploadStringTaskAsync(new Uri(_credential.BaseUrl.TrimEnd('/') + "/api/cloud/exchange"), "POST", Json.ToJson(new { Records = records, Results = results }));
                if (await Task.WhenAny(operation, Task.Delay(20000)) != operation) client.CancelAsync();
                var output = await operation;
                var result = Json.FromJson<ExchangeResponse>(output);
                if (result == null || result.Pending == null) throw new InvalidDataException();
                return result;
            }
        }
        private void Raise() { if (_disposed) return; var changed = Changed; if (changed != null) changed(); }
        public void Dispose() { _disposed = true;if(_aiCancellation!=null)_aiCancellation.Cancel();_timer.Stop(); _timer.Dispose(); }
        private class CloudWebClient : WebClient {
            private CancellationTokenRegistration environmentRegistration;
            protected override WebRequest GetWebRequest(Uri address) { var token=AiEnvironment.Token;AiEnvironment.RequireNetwork();var request=(HttpWebRequest)base.GetWebRequest(address);request.Timeout=15000;request.ReadWriteTimeout=15000;request.AllowAutoRedirect=false;environmentRegistration.Dispose();environmentRegistration=token.Register(request.Abort);return request; }
            protected override void Dispose(bool disposing) { if(disposing)environmentRegistration.Dispose();base.Dispose(disposing); }
        }
        private class ExchangeResponse { public List<CloudPending> Pending = null; }
        private class CloudPending { public string Id = ""; public SyncRecord Record = null; }
        private class CloudResult { public string Id; public string Result; }
    }
}
